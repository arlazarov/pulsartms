namespace Client.Models.DTO.Fleet;

public sealed record DriverContactValue(
  string? Value,
  string? Source,
  bool IsLocal,
  bool Usable
);

public sealed record DriverContactState(
  Guid DriverId,
  string Name,
  long Revision,
  DriverContactValue Phone,
  DriverContactValue Email,
  string? WhatsAppPhone,
  DateTime? ChangedAt
);

public sealed record DriverContactUpdate(
  long Revision,
  string? Phone,
  bool PhoneFromSource,
  string? Email,
  bool EmailFromSource,
  string? WhatsAppPhone
);
