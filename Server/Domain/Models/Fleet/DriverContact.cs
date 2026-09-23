namespace Domain.Models.Fleet;

// Usable says the value is a complete address a message could go to; a
// source phone written without its country is shown but not usable.
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

// A field taken from the source follows later imports again; a local field
// left empty is cleared here and stays cleared through imports.
public sealed record DriverContactUpdate(
  long Revision,
  string? Phone,
  bool PhoneFromSource,
  string? Email,
  bool EmailFromSource,
  string? WhatsAppPhone
);
