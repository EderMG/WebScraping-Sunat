namespace SunatXmlApi.Services;

public interface ISunatXmlService
{
    Task<XmlResponse> DownloadAsync(
        SunatCredentials credentials,
        XmlRequest request,
        CancellationToken cancellationToken);
}
