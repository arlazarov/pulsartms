using System.Security.Cryptography;
using Application.Storage;
using Microsoft.AspNetCore.DataProtection;

namespace Infrastructure.Storage;

public sealed class StorageSecrets(IDataProtectionProvider protection)
  : IStorageSecrets
{
  private const string Purpose = "AMFTMS.StorageConnections.v1";

  public string Protect(Guid company, Guid connection, string secret) =>
    Protector(company, connection).Protect(secret);

  public string? Unprotect(Guid company, Guid connection, string protectedValue)
  {
    try
    {
      return Protector(company, connection).Unprotect(protectedValue);
    }
    catch (CryptographicException)
    {
      return null;
    }
  }

  private IDataProtector Protector(Guid company, Guid connection) =>
    protection.CreateProtector(
      Purpose,
      company.ToString("N"),
      connection.ToString("N")
    );
}
