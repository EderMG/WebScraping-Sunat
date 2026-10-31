namespace SunatXmlApi;

public sealed record TokenResponse(string AccessToken, string TokenType, DateTime ExpiresAtUtc);
