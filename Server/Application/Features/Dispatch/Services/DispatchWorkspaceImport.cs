using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Models;
using Domain.Entities.Dispatch;
using Domain.Models.Execution;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Dispatch.Services;

public static class DispatchWorkspaceImport
{
  public static void RestoreCommercial(
    DispatchEntity load,
    DispatchWorkspace workspace
  )
  {
    if (
      load.Status == "completed"
      && load.Stops.Any(x => x.CompletionOverride == false)
    )
      load.Status = "in_transit";
    if (!workspace.OwnsCommercial)
      return;
    var baseline = DispatchWorkspaceData.Read<DispatchWorkspaceMetadata>(
      workspace.SourceCommercialJson
    );
    var local = DispatchWorkspaceData.Read<DispatchWorkspaceMetadata>(
      workspace.MetadataJson
    );
    if (local.OrderNumber == baseline.OrderNumber)
      local.OrderNumber = load.OrderNumber;
    if (local.CustomerName == baseline.CustomerName)
      local.CustomerName = load.CustomerName;
    if (local.Price == baseline.Price && local.Currency == baseline.Currency)
    {
      local.Price = load.Price;
      local.Currency = load.Currency;
    }
    workspace.SourceCommercialJson = DispatchWorkspaceData.Write(
      DispatchWorkspaceData.Commercial(load)
    );
    DispatchWorkspaceData.ApplyCommercial(load, local);
    workspace.MetadataJson = DispatchWorkspaceData.Write(local);
  }

  public static void MergeStops(
    DispatchEntity load,
    DispatchWorkspace workspace,
    IReadOnlyCollection<ExternalDispatchStop> source,
    DateTime now
  )
  {
    var baseline = DispatchWorkspaceData.Read<List<DispatchStop>>(
      workspace.SourceStopsJson
    );
    var matches = DispatchStopMatcher.Match(baseline, source);
    var sourceChanged =
      matches.Count != source.Count || matches.Count != baseline.Count;
    var actualConflict = false;
    foreach (var incoming in source)
    {
      if (!matches.TryGetValue(incoming, out var original))
        continue;
      var candidate = ExecutionSnapshots.Copy(original);
      DispatchMapper.UpdateStop(candidate, incoming, null, null, null, null);
      var assignmentChanged =
        original.TruckNumber != incoming.TruckNumber
        || original.TrailerNumber != incoming.TrailerNumber
        || original.DriverName != incoming.DriverName
        || original.CoDriverName != incoming.CoDriverName;
      sourceChanged |=
        original.Sequence != incoming.Sequence
        || VisitKey(original) != VisitKey(candidate)
        || assignmentChanged;
      var target = load.Stops.SingleOrDefault(x => x.Id == original.Id);
      if (target is null)
        continue;
      MergeContent(target, original, candidate);
      var times = new[]
      {
        incoming.ArrivedAt,
        incoming.PickedUpAt,
        incoming.DeliveredAt,
        incoming.DepartedAt,
      }
        .Where(x => x.HasValue)
        .Select(x => x!.Value)
        .ToArray();
      if (times.Length == 0)
        continue;
      if (
        assignmentChanged
        || VisitKey(target) != VisitKey(original)
        || VisitKey(candidate) != VisitKey(original)
        || times.Any(x => x > now)
        || !times.SequenceEqual(times.Order())
      )
      {
        actualConflict = true;
        continue;
      }
      var merged = new[]
      {
        target.ArrivedAt ?? incoming.ArrivedAt,
        target.PickedUpAt ?? incoming.PickedUpAt,
        target.DeliveredAt ?? incoming.DeliveredAt,
        target.DepartedAt ?? incoming.DepartedAt,
      }
        .Where(x => x.HasValue)
        .Select(x => x!.Value)
        .ToArray();
      if (!merged.SequenceEqual(merged.Order()))
      {
        actualConflict = true;
        continue;
      }
      if (
        Conflicts(target.ArrivedAt, incoming.ArrivedAt)
        || Conflicts(target.PickedUpAt, incoming.PickedUpAt)
        || Conflicts(target.DeliveredAt, incoming.DeliveredAt)
        || Conflicts(target.DepartedAt, incoming.DepartedAt)
      )
      {
        actualConflict = true;
        continue;
      }
      target.ArrivedAt ??= incoming.ArrivedAt;
      target.PickedUpAt ??= incoming.PickedUpAt;
      target.DeliveredAt ??= incoming.DeliveredAt;
      target.DepartedAt ??= incoming.DepartedAt;
    }
    workspace.SourceStopsJson = ExecutionSnapshots.Write(baseline);
    workspace.SourceReviewReason =
      actualConflict
        ? "Imported actuals conflict with locally edited visits. "
          + "Review the source."
      : sourceChanged
        ? "The source itinerary changed. Local visits and order were retained."
      : null;
  }

  private static bool Conflicts(DateTime? saved, DateTime? incoming) =>
    saved.HasValue && incoming.HasValue && saved != incoming;

  private static void MergeContent(
    DispatchStop target,
    DispatchStop baseline,
    DispatchStop incoming
  )
  {
    var appointment = StopAppointment.From(incoming);
    if (StopAppointment.From(target) == StopAppointment.From(baseline))
      appointment.ApplyTo(target);
    appointment.ApplyTo(baseline);
    if (target.Notes == baseline.Notes)
      target.Notes = incoming.Notes;
    if (target.StopNo == baseline.StopNo)
      target.StopNo = incoming.StopNo;
    if (Cargo(target) == Cargo(baseline))
      CopyCargo(target, incoming);
    if (
      target.Temperature == baseline.Temperature
      && target.TemperatureUnit == baseline.TemperatureUnit
    )
    {
      target.Temperature = incoming.Temperature;
      target.TemperatureUnit = incoming.TemperatureUnit;
    }
    baseline.Notes = incoming.Notes;
    baseline.StopNo = incoming.StopNo;
    CopyCargo(baseline, incoming);
    baseline.Temperature = incoming.Temperature;
    baseline.TemperatureUnit = incoming.TemperatureUnit;
  }

  private static (string, decimal?, string, decimal?, decimal?) Cargo(
    DispatchStop stop
  ) =>
    (stop.Commodity, stop.Weight, stop.WeightUnit, stop.Pieces, stop.Pallets);

  private static void CopyCargo(DispatchStop target, DispatchStop source)
  {
    target.Commodity = source.Commodity;
    target.Weight = source.Weight;
    target.WeightUnit = source.WeightUnit;
    target.Pieces = source.Pieces;
    target.Pallets = source.Pallets;
  }

  private static string VisitKey(DispatchStop stop) =>
    DispatchWorkspaceData.Hash(
      new
      {
        stop.Job,
        stop.Name,
        stop.Address,
        stop.City,
        stop.Province,
        stop.Country,
        stop.ZipCode,
        stop.Latitude,
        stop.Longitude,
      }
    );
}
