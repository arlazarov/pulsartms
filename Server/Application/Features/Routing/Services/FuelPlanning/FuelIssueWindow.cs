using Domain.Models.Fleet;
using Domain.Models.Routing;
using Domain.Policies;
using Domain.Rules.Routing;
using Microsoft.Extensions.Options;

namespace Application.Features.Routing.Services.FuelPlanning;

// The hand-over line of a fuel plan with the carrier's buffer and hours
// freshness: drawn when the plan is prepared (FuelIssueRecords), checked
// when a prepared summary is read against the hours read then.
public sealed class FuelIssueWindow(
  IOptions<FuelIssueOptions> options,
  TimeProvider time
)
{
  private TimeSpan Buffer => TimeSpan.FromHours(options.Value.ShiftBufferHours);
  private TimeSpan Freshness =>
    TimeSpan.FromMinutes(options.Value.HosFreshMinutes);

  public void Apply(FuelPlan plan, DriverHosClocks? hos) =>
    FuelIssueHorizon.Apply(plan, hos, time.GetUtcNow(), Buffer, Freshness);

  public bool Holds(FuelPlan plan, DriverHosClocks? hos) =>
    FuelIssueHorizon.Holds(plan, hos, time.GetUtcNow(), Buffer, Freshness);
}
