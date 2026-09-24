using Application.Features.Routing.Services.Routes;
using Domain.Entities.Messaging;

namespace Application.Features.Routing.Services.FuelPlanning;

// A fuel plan's delivery status changed in Messaging's committed
// transaction: the trucks whose hand-over reads differently now.
public sealed class FuelDeliveryObserver(PlanningSummaryCache summaries)
  : IDriverTextObserver
{
  public void Changed(Guid company, IReadOnlyCollection<DriverMessage> attempts)
  {
    foreach (var truck in attempts.Select(x => x.TruckId).Distinct())
      summaries.Committed(company, truck);
  }
}
