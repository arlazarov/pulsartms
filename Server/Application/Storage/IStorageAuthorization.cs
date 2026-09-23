namespace Application.Storage;

// The consent flow for a storage kind that connects to a company's own
// account. The server's client registration comes from deployment secrets;
// when it is missing the kind cannot be connected and says so. Consent only
// grants access: an administrator then picks where files go (see
// IStorageRootPicker), and nothing is created in the account before that.
public interface IStorageAuthorization
{
  string Kind { get; }
  bool IsConfigured { get; }

  Uri AuthorizationUrl(string state, string codeChallenge);

  // Null when the provider refused the code.
  Task<StorageGrant?> ExchangeAsync(
    string code,
    string codeVerifier,
    CancellationToken ct
  );
}

// Secret is what the adapter needs later (a refresh token); it is protected
// before it is stored.
public sealed record StorageGrant(string Secret);

// Choosing the folder files go into, in the provider's own picker so the
// administrator sees their drives, including shared ones. The picker needs a
// short-lived access token in the administrator's browser; the server then
// verifies the chosen folder can hold new files before using it.
public interface IStorageRootPicker
{
  string Kind { get; }
  bool IsConfigured { get; }

  Task<StoragePickerSession?> SessionAsync(
    StorageTarget target,
    CancellationToken ct
  );

  // Null when the folder is not one this connection can add files to.
  Task<StorageRoot?> VerifyAsync(
    StorageTarget target,
    string folderId,
    CancellationToken ct
  );
}

public sealed record StoragePickerSession(
  string AccessToken,
  string ClientId,
  string ApiKey,
  string AppId
);

public sealed record StorageRoot(string Id, string Name, string? DriveId);
