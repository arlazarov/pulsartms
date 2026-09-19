using Application.Features.Dispatch.Models;

namespace Application.Features.Dispatch.Services;

public static class DispatchSourceAssignments
{
  public static string Fingerprint(
    ExternalDispatch source,
    Func<string, Guid?> truck,
    Func<string, Guid?> driver,
    Func<string, Guid?> trailer
  )
  {
    return Fingerprint(Capture(source, truck, driver, trailer));
  }

  public static DispatchAssignmentProposal Capture(
    ExternalDispatch source,
    Func<string, Guid?> truck,
    Func<string, Guid?> driver,
    Func<string, Guid?> trailer
  ) =>
    new()
    {
      Header = new(
        source.TruckNumber,
        source.DriverName,
        "",
        source.TrailerNumber,
        truck(source.TruckNumber),
        driver(source.DriverName),
        null,
        trailer(source.TrailerNumber)
      ),
      Visits = source
        .Stops.OrderBy(x => x.Sequence)
        .Select(x => new DispatchAssignmentVisit(
          x.Sequence,
          x.Job,
          x.Name,
          new(
            x.TruckNumber,
            x.DriverName,
            x.CoDriverName,
            x.TrailerNumber,
            truck(x.TruckNumber),
            driver(x.DriverName),
            driver(x.CoDriverName),
            trailer(x.TrailerNumber)
          )
        ))
        .ToArray(),
    };

  public static string Fingerprint(DispatchAssignmentProposal proposal)
  {
    var assignments = proposal.Visits.Select(x => x.Resources).ToArray();
    var uniform = assignments.Distinct().Count() <= 1;
    return DispatchWorkspaceData.Hash(
      new
      {
        proposal.Header,
        // Uniform visit additions change topology, not resource assignment.
        Visits = uniform ? assignments.Take(1) : assignments,
        Positions = uniform
          ? []
          : proposal.Visits.Select(x => x.Sequence).ToArray(),
      }
    );
  }
}
