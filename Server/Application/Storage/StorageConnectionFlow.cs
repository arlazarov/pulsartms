using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Models;
using Domain.Entities.Storage;

namespace Application.Storage;

public sealed record BeginStorageConnectionCommand(string Kind, string? Name)
  : IRequest<RequestResponse<StorageConnectionStart>>;

public sealed record StorageConnectionStart(Guid Id, string AuthorizationUrl);

// The provider's redirect back. It arrives without the administrator's
// session, so it is trusted only through the single-use state it carries.
public sealed record CompleteStorageConnectionCommand(
  string? State,
  string? Code,
  string? Error
) : IRequest<RequestResponse<string>>;

// Connecting a company's own account: a pending connection holds a nonce and
// a PKCE verifier, protected, for ten minutes. The callback must return the
// same company, connection and nonce; it then exchanges the code once and
// stores only the protected refresh secret. The connection then waits for an
// administrator to pick its folder (StorageRoots).
public sealed class StorageConnectionFlow(
  IAppDbContext db,
  IEnumerable<IStorageAuthorization> authorizations,
  IStorageSecrets secrets,
  ICurrentCompany companies,
  ICurrentUser caller,
  TimeProvider clock
)
  : IRequestHandler<
    BeginStorageConnectionCommand,
    RequestResponse<StorageConnectionStart>
  >,
    IRequestHandler<CompleteStorageConnectionCommand, RequestResponse<string>>
{
  public static readonly TimeSpan PendingFor = TimeSpan.FromMinutes(10);

  private sealed record Pending(string Nonce, string Verifier);

  public async Task<RequestResponse<StorageConnectionStart>> Handle(
    BeginStorageConnectionCommand request,
    CancellationToken ct
  )
  {
    if (companies.Id is not { } company)
      return RequestResponse<StorageConnectionStart>.Fail(
        "Access denied.",
        403
      );
    var kind = StorageKinds.Find(request.Kind);
    var authorization = authorizations.FirstOrDefault(x =>
      x.Kind == kind?.Kind
    );
    if (kind is not { Implemented: true, NeedsConsent: true })
      return RequestResponse<StorageConnectionStart>.Fail(
        "This storage cannot be connected."
      );
    if (authorization is not { IsConfigured: true })
      return RequestResponse<StorageConnectionStart>.Fail(
        "The server has no client registration for this provider.",
        409
      );
    var actor = await db
      .Users.AsNoTracking()
      .Where(x => x.IdentityUserId == caller.IdentityUserId && x.IsActive)
      .Select(x => (Guid?)x.Id)
      .SingleOrDefaultAsync(ct);
    if (actor is null)
      return RequestResponse<StorageConnectionStart>.Fail(
        "Access denied.",
        403
      );
    var now = clock.GetUtcNow().UtcDateTime;
    await db
      .StorageConnections.Where(x =>
        x.State == StorageConnectionStates.Pending && x.PendingUntil < now
      )
      .ExecuteDeleteAsync(ct);
    var pending = new Pending(Random(32), Random(48));
    var connection = new StorageConnection
    {
      Id = Guid.NewGuid(),
      CompanyId = company,
      Kind = kind.Kind,
      DisplayName = Name(request.Name, kind.Name),
      State = StorageConnectionStates.Pending,
      PendingUntil = now + PendingFor,
      CreatedAt = now,
      CreatedBy = actor.Value,
      UpdatedAt = now,
      Revision = 1,
    };
    connection.ProtectedSecret = secrets.Protect(
      company,
      connection.Id,
      JsonSerializer.Serialize(pending)
    );
    db.StorageConnections.Add(connection);
    await db.SaveChangesAsync(ct);
    var challenge = Base64Url(
      SHA256.HashData(Encoding.ASCII.GetBytes(pending.Verifier))
    );
    return RequestResponse<StorageConnectionStart>.Ok(
      new(
        connection.Id,
        authorization
          .AuthorizationUrl(
            $"{company:N}.{connection.Id:N}.{pending.Nonce}",
            challenge
          )
          .ToString()
      )
    );
  }

  // The answer is only an outcome word for the redirect; details stay in
  // the connection's state and error.
  public async Task<RequestResponse<string>> Handle(
    CompleteStorageConnectionCommand request,
    CancellationToken ct
  )
  {
    var parts = request.State?.Split('.');
    if (
      parts is not { Length: 3 }
      || !Guid.TryParseExact(parts[0], "N", out var company)
      || !Guid.TryParseExact(parts[1], "N", out var id)
      || parts[2].Length is 0 or > 100
      || !await db.Companies.AnyAsync(x => x.Id == company && x.IsActive, ct)
    )
      return RequestResponse<string>.Ok("invalid");
    using var serving = companies.As(company);
    var connection = await db.StorageConnections.SingleOrDefaultAsync(
      x => x.Id == id && x.State == StorageConnectionStates.Pending,
      ct
    );
    if (connection?.ProtectedSecret is null)
      return RequestResponse<string>.Ok("invalid");
    var pending = Read(
      secrets.Unprotect(company, connection.Id, connection.ProtectedSecret)
    );
    if (
      pending is null
      || !CryptographicOperations.FixedTimeEquals(
        Encoding.ASCII.GetBytes(pending.Nonce),
        Encoding.ASCII.GetBytes(parts[2])
      )
    )
      return RequestResponse<string>.Ok("invalid");
    // The state is spent and saved before the code is exchanged: of two
    // callbacks with the same state only the one that saves first goes on.
    // It is saved as failed, so a crash from here on leaves a connection an
    // administrator can see and connect again, not a hidden pending one.
    var now = clock.GetUtcNow().UtcDateTime;
    var expired = connection.PendingUntil is not { } until || until < now;
    connection.ProtectedSecret = null;
    connection.PendingUntil = null;
    connection.State = StorageConnectionStates.Failed;
    connection.LastError = "Connecting did not finish. Connect again.";
    connection.UpdatedAt = now;
    connection.Revision++;
    if (await SaveAsync("spent", ct) is { Response: not "spent" } refused)
      return refused;
    var authorization = authorizations.FirstOrDefault(x =>
      x.Kind == connection.Kind
    );
    StorageGrant? grant = null;
    if (
      expired
      || request.Error is not null
      || string.IsNullOrEmpty(request.Code)
      || authorization is not { IsConfigured: true }
    )
      connection.LastError = "The provider did not grant access.";
    else if (
      (
        grant = await authorization.ExchangeAsync(
          request.Code,
          pending.Verifier,
          ct
        )
      )
      is null
    )
      connection.LastError = "The provider refused the authorization.";
    connection.UpdatedAt = clock.GetUtcNow().UtcDateTime;
    connection.Revision++;
    if (grant is null)
      return await SaveAsync("failed", ct);
    // Access only: files go nowhere until an administrator picks a folder.
    connection.State = StorageConnectionStates.NeedsRoot;
    connection.LastError = null;
    connection.ProtectedSecret = secrets.Protect(
      company,
      connection.Id,
      grant.Secret
    );
    return await SaveAsync("connected", ct);
  }

  private async Task<RequestResponse<string>> SaveAsync(
    string outcome,
    CancellationToken ct
  )
  {
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (Exception ex) when (db.IsWriteConflict(ex))
    {
      // Another callback with the same state got there first.
      return RequestResponse<string>.Ok("invalid");
    }
    return RequestResponse<string>.Ok(outcome);
  }

  private static Pending? Read(string? json)
  {
    if (json is null)
      return null;
    try
    {
      return JsonSerializer.Deserialize<Pending>(json);
    }
    catch (JsonException)
    {
      return null;
    }
  }

  private static string Name(string? name, string fallback)
  {
    var trimmed = name?.Trim() ?? "";
    return trimmed.Length is 0 or > 80 ? fallback : trimmed;
  }

  private static string Random(int bytes) =>
    Base64Url(RandomNumberGenerator.GetBytes(bytes));

  private static string Base64Url(byte[] bytes) =>
    Convert
      .ToBase64String(bytes)
      .TrimEnd('=')
      .Replace('+', '-')
      .Replace('/', '_');
}
