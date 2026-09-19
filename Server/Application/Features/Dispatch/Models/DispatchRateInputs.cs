namespace Application.Features.Dispatch.Models;

public sealed record DispatchRateInputs(
  Guid DispatchId,
  decimal? Price,
  decimal? LoadedMiles,
  string Currency
);
