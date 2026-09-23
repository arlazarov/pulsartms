namespace Application.Storage;

// Every kind of storage a company can choose, and what is actually
// implemented. A kind that is listed but not implemented can be shown, never
// connected.
public static class StorageKinds
{
  public const string Managed = "managed";
  public const string GoogleDrive = "google-drive";
  public const string Dropbox = "dropbox";
  public const string OneDrive = "onedrive";

  public static IReadOnlyList<StorageKind> All { get; } =
    [
      new(Managed, "PulsR storage", Implemented: true, NeedsConsent: false),
      new(GoogleDrive, "Google Drive", Implemented: true, NeedsConsent: true),
      new(Dropbox, "Dropbox", Implemented: false, NeedsConsent: true),
      new(OneDrive, "OneDrive", Implemented: false, NeedsConsent: true),
    ];

  public static StorageKind? Find(string? kind) =>
    All.FirstOrDefault(x => x.Kind == kind);
}

public sealed record StorageKind(
  string Kind,
  string Name,
  bool Implemented,
  bool NeedsConsent
);
