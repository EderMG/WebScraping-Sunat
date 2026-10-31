namespace SunatXmlApi.Services;

public interface IApiCredentialValidator
{
    ValueTask<bool> IsValidAsync(string username, string password, CancellationToken cancellationToken);
}
