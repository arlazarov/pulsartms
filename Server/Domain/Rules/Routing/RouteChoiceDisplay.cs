using Domain.Models.Routing;

namespace Domain.Rules.Routing;

public static class RouteChoiceDisplay
{
  // Only the transport projection is simplified; the durable draft remains the
  // save authority.
  public static RouteChoiceDisplayPreview Create(RouteChoicePreview preview)
  {
    return new(
      preview.Id,
      preview.DispatchId,
      preview.TruckId,
      preview.LoadNumber,
      preview.Revision,
      preview.ExpiresAt,
      [.. preview.Stops],
      [.. preview.ViaPoints],
      preview
        .Options.Select(option => new RouteChoiceDisplayOption(
          option.Number,
          new(
            option.Route.CalculatedAt,
            option.Route.Miles,
            option.Route.Seconds,
            option
              .Route.Legs.Select(leg =>
                leg with
                {
                  Points = DisplayRouteGeometry.Simplify(leg.Points),
                }
              )
              .ToList(),
            [.. option.Route.Warnings]
          ),
          option.DifferenceMiles,
          option.DifferenceSeconds
        ))
        .ToList(),
      preview.SavedRoute is { } saved ? new(saved.Miles, saved.Seconds) : null
    )
    {
      OriginUpdatedAt = preview.OriginUpdatedAt,
      ExecutionLegId = preview.ExecutionLegId,
    };
  }
}
