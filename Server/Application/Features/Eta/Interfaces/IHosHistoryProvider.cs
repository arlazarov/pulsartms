using Domain.Models.Eta;

namespace Application.Features.Eta.Interfaces;

public interface IHosHistoryProvider
{
  Task<HosHistory?> GetAsync(string driverId, CancellationToken ct);
}
