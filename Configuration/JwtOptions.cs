namespace SunatXmlApi.Configuration;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = "SunatXmlApi";
    public string Audience { get; init; } = "SunatXmlApiClient";
    public string SigningKey { get; init; } = string.Empty;
    public int ExpirationMinutes { get; init; } = 60;
}
