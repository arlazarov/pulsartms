namespace Application.Features.Mileage.Models;

public sealed record OdometerSample(
  string ExternalTruckId,
  DateTimeOffset ObservedAt,
  decimal Meters
);

public sealed record OdometerPage(
  IReadOnlyList<OdometerSample> Samples,
  string Cursor,
  bool HasMore
);
