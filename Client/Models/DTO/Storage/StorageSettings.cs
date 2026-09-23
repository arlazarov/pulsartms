namespace Client.Models.DTO.Storage;

public sealed record StorageKindView(
  string Kind,
  string Name,
  bool Implemented,
  bool Available,
  string? Unavailable
);

public sealed record StorageConnectionView(
  Guid Id,
  string Kind,
  string DisplayName,
  string State,
  bool IsDefault,
  string? RootName,
  string? LastError,
  long Revision,
  DateTime CreatedAt
);

public sealed record StorageSettings(
  IReadOnlyList<StorageKindView> Kinds,
  IReadOnlyList<StorageConnectionView> Connections
);

public sealed record StorageConnectRequest(string Kind, string? Name);

public sealed record StorageConnectionStart(Guid Id, string AuthorizationUrl);

public sealed record StorageRevisionRequest(long ExpectedRevision);

public sealed record StoragePickerSession(
  string AccessToken,
  string ClientId,
  string ApiKey,
  string AppId
);

public sealed record StorageRootRequest(string FolderId, long ExpectedRevision);

public sealed record StorageLayoutView(
  string LoadsFolder,
  string LoadTemplate,
  string CancelledSuffix,
  string InboxFolder,
  long Revision,
  string Example,
  string CancelledExample
);

public sealed record StorageLayoutUpdate(
  string LoadsFolder,
  string LoadTemplate,
  string CancelledSuffix,
  string InboxFolder,
  long ExpectedRevision
);
