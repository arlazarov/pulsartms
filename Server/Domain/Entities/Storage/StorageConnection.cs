namespace Domain.Entities.Storage;

// Where a company keeps files: the PulsR-managed store or one of its own
// accounts (Google Drive and later others). A company may have several; one
// is the default for new files, and every stored file names the connection
// that holds it.
public sealed class StorageConnection : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public string Kind { get; set; } = "";
  public string DisplayName { get; set; } = "";
  public string State { get; set; } = StorageConnectionStates.Pending;
  public bool IsDefault { get; set; }

  // Provider location for this company's files, such as the Drive folder an
  // administrator picked, and its name as the provider showed it.
  public string? Root { get; set; }
  public string? RootName { get; set; }

  // Tokens and connection-flow secrets, protected by the server; never
  // returned by any read.
  public string? ProtectedSecret { get; set; }
  public DateTime? PendingUntil { get; set; }
  public string? LastError { get; set; }
  public DateTime CreatedAt { get; set; }
  public Guid CreatedBy { get; set; }
  public DateTime UpdatedAt { get; set; }
  public long Revision { get; set; }
}

public static class StorageConnectionStates
{
  public const string Pending = "pending";

  // Access granted; an administrator still has to pick where files go.
  public const string NeedsRoot = "needs-root";
  public const string Connected = "connected";
  public const string Failed = "failed";
  public const string Disconnected = "disconnected";
}
