using System.Security.Cryptography;
using System.Text.Json;
using Application.Features.Integrations.Interfaces;
using Application.Features.Integrations.Models;
using Domain.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Infrastructure.Persistence;

public sealed class IntegrationCredentialStore(
  IServiceScopeFactory scopes,
  IDataProtectionProvider protection,
  TimeProvider time
) : IIntegrationCredentialStore
{
  private const int MaximumFieldLength = 8_192;
  private const int MaximumJsonLength = 32_768;
  private const int MaximumProtectedLength = 65_536;
  private const string Purpose = "AMFTMS.IntegrationCredentials.v1";

  public async Task<StoredIntegrationCredentials> ReadAsync(
    string provider,
    CancellationToken ct
  )
  {
    RequireProvider(provider);
    await using var scope = scopes.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var row = await db
      .IntegrationCredentialSettings.AsNoTracking()
      .SingleOrDefaultAsync(value => value.Provider == provider, ct);
    if (row is null)
      return new(0, null, null);
    if (row.Revision <= 0)
      throw Unavailable();
    return new(
      row.Revision,
      row.UpdatedAt,
      row.ProtectedValues is null
        ? null
        : Unprotect(provider, row.ProtectedValues)
    );
  }

  public async Task<bool> TryWriteAsync(
    string provider,
    long expectedRevision,
    IntegrationCredentialValues? values,
    CancellationToken ct
  )
  {
    RequireProvider(provider);
    if (expectedRevision < 0 || expectedRevision == long.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(expectedRevision));
    if (values is not null)
      ValidateFields(provider, values);
    await using var scope = scopes.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var row = await db.IntegrationCredentialSettings.SingleOrDefaultAsync(
      value => value.Provider == provider,
      ct
    );
    var inserting = row is null;
    if ((row?.Revision ?? 0) != expectedRevision)
      return false;
    if (row is null)
    {
      row = new() { Provider = provider };
      db.IntegrationCredentialSettings.Add(row);
    }
    // Keep a revisioned tombstone on restore so an old first-save request
    // cannot overwrite it.
    row.ProtectedValues = values is null ? null : Protect(provider, values);
    row.Revision = expectedRevision + 1;
    row.UpdatedAt = time.GetUtcNow().UtcDateTime;
    try
    {
      await db.SaveChangesAsync(ct);
      return true;
    }
    catch (DbUpdateConcurrencyException)
    {
      return false;
    }
    catch (DbUpdateException exception)
      when (inserting && IsConcurrentInsert(exception))
    {
      return false;
    }
  }

  private string Protect(string provider, IntegrationCredentialValues values)
  {
    var fields = IntegrationProviderCatalog
      .Fields(provider)
      .ToDictionary(
        field => field,
        field => values.Get(field)!,
        StringComparer.Ordinal
      );
    var json = JsonSerializer.Serialize(fields);
    if (json.Length > MaximumJsonLength)
      throw new ArgumentException(
        "Integration credentials are too large.",
        nameof(values)
      );
    var result = protection.CreateProtector(Purpose, provider).Protect(json);
    if (result.Length > MaximumProtectedLength)
      throw new ArgumentException(
        "Integration credentials are too large.",
        nameof(values)
      );
    return result;
  }

  private IntegrationCredentialValues Unprotect(
    string provider,
    string encrypted
  )
  {
    if (encrypted.Length is 0 or > MaximumProtectedLength)
      throw Unavailable();
    try
    {
      var json = protection
        .CreateProtector(Purpose, provider)
        .Unprotect(encrypted);
      if (json.Length > MaximumJsonLength)
        throw Unavailable();
      using var document = JsonDocument.Parse(json, new() { MaxDepth = 4 });
      if (document.RootElement.ValueKind != JsonValueKind.Object)
        throw Unavailable();
      var fields = new Dictionary<string, string>(StringComparer.Ordinal);
      foreach (var field in document.RootElement.EnumerateObject())
      {
        if (
          field.Value.ValueKind != JsonValueKind.String
          || !fields.TryAdd(field.Name, field.Value.GetString()!)
        )
          throw Unavailable();
      }
      var values = new IntegrationCredentialValues(fields);
      ValidateFields(provider, values);
      return values;
    }
    catch (Exception exception)
      when (exception
          is CryptographicException
            or JsonException
            or ArgumentException
      )
    {
      throw Unavailable();
    }
  }

  private static void RequireProvider(string provider)
  {
    if (!IntegrationProviderCatalog.Contains(provider))
      throw new ArgumentException("Unsupported integration.", nameof(provider));
  }

  private static void ValidateFields(
    string provider,
    IntegrationCredentialValues values
  )
  {
    var allowed = IntegrationProviderCatalog.Fields(provider);
    if (
      values.FieldNames.Count != allowed.Count
      || values.FieldNames.Any(field =>
        !allowed.Contains(field, StringComparer.Ordinal)
      )
      || allowed.Any(field =>
        values.Get(field) is not { } value
        || string.IsNullOrWhiteSpace(value)
        || value.Length > MaximumFieldLength
        || value.Any(char.IsControl)
      )
    )
      throw new ArgumentException(
        "Integration credential fields are invalid.",
        nameof(values)
      );
  }

  private static bool IsConcurrentInsert(DbUpdateException exception) =>
    exception.InnerException switch
    {
      PostgresException
      {
        SqlState: PostgresErrorCodes.UniqueViolation,
        ConstraintName: "PK_IntegrationCredentialSettings"
      } => true,
      SqliteException { SqliteExtendedErrorCode: 1555 } => true,
      _ => false,
    };

  private static InvalidOperationException Unavailable() =>
    new("Saved integration credentials are unavailable.");
}
