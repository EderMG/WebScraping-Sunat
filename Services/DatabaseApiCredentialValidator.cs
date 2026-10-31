using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SunatXmlApi.Data;

namespace SunatXmlApi.Services;

public sealed class DatabaseApiCredentialValidator(
    ScrapingDbContext dbContext) : IApiCredentialValidator
{
    private const string CaseSensitiveCollation = "Latin1_General_100_BIN2";

    public async ValueTask<bool> IsValidAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var usuario = await dbContext.Usuarios
            .AsNoTracking()
            .Where(entity =>
                EF.Functions.Collate(entity.Usuario, CaseSensitiveCollation) == username)
            .Select(entity => new { entity.Usuario, entity.Password })
            .FirstOrDefaultAsync(cancellationToken);

        return usuario is not null &&
               FixedTimeEquals(username, usuario.Usuario) &&
               FixedTimeEquals(password, usuario.Password);
    }

    private static bool FixedTimeEquals(string value, string expected)
    {
        var valueBytes = Encoding.UTF8.GetBytes(value);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return valueBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(valueBytes, expectedBytes);
    }
}
