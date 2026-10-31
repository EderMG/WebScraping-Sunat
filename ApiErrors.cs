using System.Text.Json;

namespace SunatXmlApi;

public static class ApiErrors
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task WriteAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            new ErrorResponse(false, new ErrorDetails(code, message)),
            JsonOptions,
            context.RequestAborted);
    }

    private sealed record ErrorResponse(bool Success, ErrorDetails Error);
    private sealed record ErrorDetails(string Code, string Message);
}
