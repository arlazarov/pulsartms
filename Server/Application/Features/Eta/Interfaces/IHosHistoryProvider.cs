using Application.Features.Eta.Models;

namespace Application.Features.Eta.Interfaces;

public interface IHosHistoryProvider
{
  Task<HosHistory?> GetAsync(string driverId, CancellationToken ct);
}
