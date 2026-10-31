namespace SunatXmlApi;

public sealed class ApiExceptionMiddleware(
    RequestDelegate next,
    ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ApiException exception)
        {
            logger.LogError(exception,
                "Error {Code}, HTTP {StatusCode}. TraceId: {TraceId}",
                exception.Code, exception.StatusCode, context.TraceIdentifier);
            await ApiErrors.WriteAsync(
                context,
                exception.StatusCode,
                exception.Code,
                exception.Message);
        }
        catch (BadHttpRequestException exception)
        {
            logger.LogError(exception, "Solicitud inválida. TraceId: {TraceId}", context.TraceIdentifier);
            await ApiErrors.WriteAsync(
                context,
                StatusCodes.Status400BadRequest,
                "SOLICITUD_INVALIDA",
                "La solicitud no tiene un formato válido.");
        }
        catch (OperationCanceledException exception) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Solicitud cancelada. TraceId: {TraceId}", context.TraceIdentifier);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Error no controlado al procesar la solicitud.");
            await ApiErrors.WriteAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "ERROR_INTERNO",
                "Ocurrió un error interno al procesar la solicitud.");
        }
    }
}
