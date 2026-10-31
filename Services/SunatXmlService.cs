using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using SunatXmlApi.Configuration;

namespace SunatXmlApi.Services;

public sealed class SunatXmlService(
    IHttpClientFactory httpClientFactory,
    IOptions<SunatOptions> options,
    ILogger<SunatXmlService> logger) : ISunatXmlService
{
    private static readonly string[] FileNameProperties =
        ["nombreArchivo", "nomArchivo", "fileName", "filename"];
    private static readonly string[] FileContentProperties =
        ["valorArchivo", "valArchivo", "archivo", "contenido", "content", "data"];

    public async Task<XmlResponse> DownloadAsync(
        SunatCredentials credentials,
        XmlRequest request,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.LoginUrl))
        {
            throw new ApiException(
                StatusCodes.Status503ServiceUnavailable,
                "SUNAT_NO_CONFIGURADO",
                "La integración con SUNAT no está configurada.");
        }

        try
        {
            var sunatToken = await GetSunatTokenAsync(settings, credentials, cancellationToken);
            return await FetchXmlAsync(settings, sunatToken, request, cancellationToken);
        }
        catch (ApiException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falló la consulta de XML en SUNAT.");
            throw new ApiException(
                StatusCodes.Status502BadGateway,
                "ERROR_PROVEEDOR",
                "No fue posible completar la consulta con SUNAT.");
        }
    }

    private static async Task<string> GetSunatTokenAsync(
        SunatOptions settings,
        SunatCredentials credentials,
        CancellationToken cancellationToken)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = false
        });

        var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(settings.LoginUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 30_000
        });

        await page.Locator("#txtRuc").FillAsync(credentials.RucEmisor);
        await page.Locator("#txtUsuario").FillAsync(credentials.UsuarioSol);
        await page.Locator("#txtContrasena").FillAsync(credentials.ClaveSol);
        await page.Locator("#btnAceptar").ClickAsync();
        cancellationToken.ThrowIfCancellationRequested();

        if (await page.Locator("#txtRuc").IsVisibleAsync())
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                "CREDENCIALES_SOL_INVALIDAS",
                "SUNAT rechazó las credenciales SOL enviadas.");
        }

        await page.GotoAsync(settings.MenuUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 30_000
        });

        await page.WaitForFunctionAsync(
            "() => Boolean(sessionStorage.getItem('SUNAT.token') || localStorage.getItem('SUNAT.token'))",
            null,
            new PageWaitForFunctionOptions { Timeout = 30_000 });

        var token = await page.EvaluateAsync<string?>(
            "() => sessionStorage.getItem('SUNAT.token') || localStorage.getItem('SUNAT.token')");

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ApiException(
                StatusCodes.Status502BadGateway,
                "TOKEN_SUNAT_NO_DISPONIBLE",
                "SUNAT no devolvió un token de sesión para realizar la consulta.");
        }

        return token;
    }

    private async Task<XmlResponse> FetchXmlAsync(
        SunatOptions settings,
        string token,
        XmlRequest request,
        CancellationToken cancellationToken)
    {

        var tipoServSunat = Uri.EscapeDataString(request.TipoServicio) == "01" ? "02" : "";
        var requestUrl = settings.ConsultationUrlTemplate
            .Replace("{ruc}", Uri.EscapeDataString(request.RucComprobante), StringComparison.Ordinal)
            .Replace("{tipo}", Uri.EscapeDataString(request.Tipo), StringComparison.Ordinal)
            .Replace("{serie}", Uri.EscapeDataString(request.Serie), StringComparison.Ordinal)
            .Replace("{numero}", Uri.EscapeDataString(request.Numero), StringComparison.Ordinal)
            .Replace("{tipoServicio}", tipoServSunat, StringComparison.Ordinal);

        using var message = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("Mozilla", "5.0"));
        message.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("(Windows NT 10.0; Win64; x64)"));
        message.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("AppleWebKit", "537.36"));
        message.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("Chrome", "142.0.0.0"));
        message.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("Safari", "537.36"));

        using var response = await httpClientFactory.CreateClient().SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                "COMPROBANTE_NO_ENCONTRADO",
                "SUNAT no encontró el comprobante solicitado.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new ApiException(
                StatusCodes.Status502BadGateway,
                "ERROR_PROVEEDOR",
                "SUNAT no pudo procesar la consulta del comprobante.");
        }

        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var defaultFileName =
            $"{request.RucComprobante}-{request.Tipo}-{request.Serie}-{request.Numero}.xml";

        if (response.Content.Headers.ContentType?.MediaType?.Contains(
                "json",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            return ParseJsonResponse(content, defaultFileName);
        }

        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            ?? defaultFileName;
        return new XmlResponse(Path.GetFileName(fileName), Convert.ToBase64String(content));
    }

    private static XmlResponse ParseJsonResponse(byte[] content, string defaultFileName)
    {
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        var fileName = FindString(root, FileNameProperties) ?? defaultFileName;
        var fileContent = FindString(root, FileContentProperties);

        if (string.IsNullOrWhiteSpace(fileContent))
        {
            throw new ApiException(
                StatusCodes.Status502BadGateway,
                "RESPUESTA_SUNAT_INVALIDA",
                "SUNAT devolvió una respuesta sin contenido XML.");
        }

        var base64 = fileContent.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            ? fileContent[(fileContent.IndexOf(',') + 1)..]
            : fileContent;

        try
        {
            _ = Convert.FromBase64String(base64);
            return new XmlResponse(Path.GetFileName(fileName), base64);
        }
        catch (FormatException)
        {
            if (!fileContent.TrimStart().StartsWith('<'))
            {
                throw new ApiException(
                    StatusCodes.Status502BadGateway,
                    "RESPUESTA_SUNAT_INVALIDA",
                    "SUNAT devolvió contenido XML en un formato no reconocido.");
            }

            return new XmlResponse(
                Path.GetFileName(fileName),
                Convert.ToBase64String(Encoding.UTF8.GetBytes(fileContent)));
        }
    }

    private static string? FindString(JsonElement element, IReadOnlyCollection<string> propertyNames)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (propertyNames.Contains(property.Name, StringComparer.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                var nested = FindString(property.Value, propertyNames);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindString(item, propertyNames);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }
}
