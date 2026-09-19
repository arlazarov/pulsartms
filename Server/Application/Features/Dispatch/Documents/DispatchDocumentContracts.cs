namespace Application.Features.Dispatch.Documents;

public sealed record DispatchDocumentInfo(
  Guid Id,
  string Kind,
  string FileName,
  string ContentType,
  int Length,
  DateTime RecordedAt,
  string ActorName
);

public sealed record UploadDispatchDocumentRequest(
  Guid IdempotencyKey,
  string Kind,
  string FileName,
  byte[] Content
);

public sealed record DispatchDocumentDownload(
  string FileName,
  string ContentType,
  byte[] Content
);
