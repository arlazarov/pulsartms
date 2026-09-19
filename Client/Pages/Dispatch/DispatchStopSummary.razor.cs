using System.Globalization;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Dispatch.Workspace;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class DispatchStopSummary
{
  [Parameter, EditorRequired]
  public DispatchWorkspaceStop Stop { get; set; } = default!;

  [Parameter]
  public DispatchStopResponse? Recorded { get; set; }

  private IEnumerable<(string Label, string? Value)> Facts
  {
    get
    {
      (string Label, string? Value)[] facts =
      [
        (DispatchWorkspaceStopDisplay.ReferenceLabel(Stop), Stop.StopNo),
        ("Appt #", Stop.AppointmentReference),
        ("Contact", Stop.ContactName),
        ("Phone", Stop.ContactPhone),
        ("Email", Stop.ContactEmail),
      ];
      foreach (
        var fact in facts.Where(f => !string.IsNullOrWhiteSpace(f.Value))
      )
        yield return fact;
      if (Recorded is { } recorded && recorded.Id == Stop.Id)
      {
        (string Label, DateTime? Value)[] events =
        [
          ("Arrived", recorded.ArrivedAt),
          ("Picked up", recorded.PickedUpAt),
          ("Delivered", recorded.DeliveredAt),
          ("Departed", recorded.DepartedAt),
          ("Manually completed", recorded.ManualCompletedAt),
        ];
        foreach (var item in events)
          if (item.Value is { } actual)
            yield return (
              item.Label,
              DispatchWorkspaceStopDisplay.Timestamp(actual)
            );
      }
      if (!DispatchWorkspaceStopDisplay.HasCargo(Stop))
        yield break;
      if (!string.IsNullOrWhiteSpace(Stop.Commodity))
        yield return ("Commodity", Stop.Commodity);
      if (Stop.Weight is { } weight)
        yield return ("Weight", $"{Number(weight)} {Stop.WeightUnit}".Trim());
      if (Stop.Pieces is { } pieces)
        yield return ("Pieces", Number(pieces));
      if (Stop.Pallets is { } pallets)
        yield return ("Pallets", Number(pallets));
      if (!string.IsNullOrWhiteSpace(Stop.Temperature))
        yield return (
          "Temperature",
          $"{Stop.Temperature} {Stop.TemperatureUnit}".Trim()
        );
    }
  }

  private static string Number(decimal value) =>
    value.ToString("0.##", CultureInfo.InvariantCulture);

  private static string FactIcon(string label) => label switch
  {
    "Contact" or "Phone" or "Email" => "contact",
    "Commodity" or "Weight" or "Pieces" or "Pallets" => "cargo",
    "Temperature" => "temperature",
    "Arrived" or "Picked up" or "Delivered" or "Departed" or "Manually completed" => "check",
    _ => "reference",
  };
}
