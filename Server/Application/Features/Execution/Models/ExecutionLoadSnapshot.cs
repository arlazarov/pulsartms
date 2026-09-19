using System.Collections.Immutable;
using Application.Features.Routing.Models;

namespace Application.Features.Execution.Models;

public sealed record ExecutionLoadSnapshot(
  RouteWorkSnapshot Work,
  ExecutionLoadDetails Details
);

public sealed record ExecutionLoadDetails(
  string OrderNumber,
  DateOnly? OrderDate,
  DateOnly? InvoiceDate,
  string CustomerName,
  DateTime LastSyncedAt,
  string DriverName,
  string TrailerNumber,
  ImmutableDictionary<Guid, ExecutionStopDetails> Stops
);

public sealed record ExecutionStopDetails(
  string DriverName,
  string CoDriverName,
  string TrailerNumber,
  string StopNo,
  decimal? Weight,
  string WeightUnit,
  decimal? Pieces,
  decimal? Pallets,
  string Temperature,
  string TemperatureUnit,
  DateTime? OperationRecordedAt,
  string? ManualCompletedByName,
  DateTime? ManualCompletionRecordedAt
);
