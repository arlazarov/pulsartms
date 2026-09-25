using Domain.Models.Eta;

namespace Application.Features.Eta.Interfaces;

public interface IHosHistoryProvider
{
  Task<HosHistory?> GetAsync(string driverId, CancellationToken ct);

  // What the forecasts last read, if it is still usable. Never calls the
  // provider: a page showing it must not add provider work per viewer.
  HosHistory? Peek(string driverId);
}
