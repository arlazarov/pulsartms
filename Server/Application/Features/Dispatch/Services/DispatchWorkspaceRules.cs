using Application.Features.Dispatch.Models;

namespace Application.Features.Dispatch.Services;

public static class DispatchWorkspaceRules
{
  public static string? Validate(
    DispatchWorkspaceResponse current,
    UpdateDispatchWorkspaceRequest request
  )
  {
    var fields = ValidateFields(request.Metadata, request.Stops);
    if (fields is not null)
      return fields;
    var existing = current.Stops.ToDictionary(x => x.Id);
    var segments = current
      .Stops.Where(x => x.CanMove)
      .GroupBy(x => x.SegmentKey)
      .ToDictionary(x => x.Key, x => x.First());
    var submitted = request.Stops.ToDictionary(x => x.Id);
    foreach (var old in current.Stops)
    {
      if (!submitted.TryGetValue(old.Id, out var update))
      {
        if (!old.CanRemove)
          return "A recorded or transfer stop cannot be removed.";
        continue;
      }
      if (
        old.SegmentKey != update.SegmentKey
        || old.ExecutionLegId != update.ExecutionLegId
      )
        return "Stops cannot move across an assignment or recorded boundary.";
      if (
        !old.CanEdit
        && DispatchWorkspaceData.EditableHash(old)
          != DispatchWorkspaceData.EditableHash(update)
      )
        return old.LockReason ?? "This stop is locked.";
      if (old.Job != update.Job)
        return "Changing an existing cargo operation requires reconciliation.";
    }
    var order = current.Stops.Select(x => x.SegmentKey).Distinct().ToArray();
    var ranks = order
      .Select((key, rank) => (key, rank))
      .ToDictionary(x => x.key, x => x.rank);
    var lastRank = -1;
    foreach (var stop in request.Stops)
    {
      if (!ranks.TryGetValue(stop.SegmentKey, out var rank) || rank < lastRank)
        return "Stops cannot move across an assignment or recorded boundary.";
      lastRank = rank;
      if (!existing.ContainsKey(stop.Id))
      {
        if (
          !segments.TryGetValue(stop.SegmentKey, out var anchor)
          || stop.ExecutionLegId != anchor.ExecutionLegId
        )
          return "Add a stop within an editable future segment.";
        if (stop.Job is not ("Pick Up" or "Drop Off" or "Delivery"))
          return "New stops must be a pickup or delivery.";
      }
      if (
        !existing.TryGetValue(stop.Id, out var previous)
        || DispatchWorkspaceData.EditableHash(previous)
          != DispatchWorkspaceData.EditableHash(stop)
      )
      {
        var error = ValidateStop(stop);
        if (error is not null)
          return error;
      }
    }
    foreach (var segment in segments.Keys)
      if (!request.Stops.Any(x => x.SegmentKey == segment))
        return "Keep at least one stop in each existing future segment.";
    var firstPickup = request.Stops.FindIndex(x =>
      x.Job is "Pick Up" or "Pickup"
    );
    if (
      firstPickup > 0
      && request
        .Stops.Take(firstPickup)
        .Any(x => x.Job is "Drop Off" or "Delivery")
      && current.Stops.First().Job is "Pick Up" or "Pickup"
    )
      return "A delivery cannot move before the load's first pickup.";
    return null;
  }

  public static string? ValidateNew(
    DispatchWorkspaceMetadata metadata,
    List<DispatchWorkspaceStop> stops
  )
  {
    var fields = ValidateFields(metadata, stops);
    if (fields is not null)
      return fields;
    if (
      stops.Count < 2
      || stops[0].Job != "Pick Up"
      || stops[^1].Job is not ("Drop Off" or "Delivery")
      || stops.Any(x => x.Job is not ("Pick Up" or "Drop Off" or "Delivery"))
    )
      return "Start with a pickup and finish with a delivery.";
    foreach (var stop in stops)
    {
      if (
        stop.TruckId.HasValue
        || stop.DriverId.HasValue
        || stop.CoDriverId.HasValue
        || stop.TrailerId.HasValue
        || stop.ExecutionLegId.HasValue
        || stop.Transfer is not null
        || stop.TruckNumber.Length > 0
        || stop.DriverName.Length > 0
        || stop.CoDriverName.Length > 0
        || stop.TrailerNumber.Length > 0
      )
        return "Assign resources after creating the load.";
      var error = ValidateStop(stop);
      if (error is not null)
        return error;
    }
    return null;
  }

  private static string? ValidateFields(
    DispatchWorkspaceMetadata metadata,
    List<DispatchWorkspaceStop> stops
  )
  {
    if (metadata is null || stops is null)
      return "Provide the load fields and stops.";
    if (HasNullText(metadata) || stops.Any(x => x is null || HasNullText(x)))
      return "Text fields cannot be null.";
    if (stops.Count is < 1 or > 49)
      return "A load must contain between 1 and 49 stops.";
    if (
      stops.Any(x => x is null || x.Id == Guid.Empty)
      || stops.Select(x => x.Id).Distinct().Count() != stops.Count
    )
      return "Every stop must have its own stable identity.";
    var billingError =
      DispatchBillingRules.TermsError(metadata.PaymentTerms)
      ?? DispatchBillingRules.AdjustmentsError(metadata);
    if (billingError is not null)
      return billingError;
    if (
      metadata.OrderNumber.Length > 100
      || metadata.CustomerName.Length > 200
      || metadata.BrokerCompany.Length > 200
      || metadata.BrokerContact.Length > 200
      || metadata.BrokerPhone.Length > 100
      || metadata.BrokerEmail.Length > 254
      || metadata.BrokerReference.Length > 200
      || metadata.BillTo.Length > 2000
      || metadata.LoadInstructions.Length > 10000
      || !FitsAmount(metadata.Price)
      || metadata.Currency.Length > 10
    )
      return "Some load fields exceed their supported length or range.";
    return null;
  }

  private static bool HasNullText(object value) =>
    value
      .GetType()
      .GetProperties()
      .Any(x =>
        x.PropertyType == typeof(string)
        && x.Name is not ("LockReason")
        && x.GetValue(value) is null
      );

  public static string? ValidateStop(DispatchWorkspaceStop stop)
  {
    if (
      string.IsNullOrWhiteSpace(stop.Name)
      || string.IsNullOrWhiteSpace(stop.Address)
      || stop.Name.Length > 300
      || stop.Address.Length > 500
      || stop.City.Length > 150
      || stop.Province.Length > 100
      || stop.Country.Length > 100
      || stop.ZipCode.Length > 50
      || stop.StopNo.Length > 100
      || stop.AppointmentReference.Length > 100
      || stop.ContactName.Length > 200
      || stop.ContactPhone.Length > 100
      || stop.ContactEmail.Length > 254
      || stop.Notes.Length > 10000
      || stop.Commodity.Length > 10000
      || stop.WeightUnit.Length > 20
      || stop.Temperature.Length > 50
      || stop.TemperatureUnit.Length > 20
    )
      return "Provide a facility and address within the supported limits.";
    if (
      stop.Latitude is null or < -90 or > 90
      || stop.Longitude is null or < -180 or > 180
    )
      return "Confirm valid coordinates for the stop before saving.";
    if (
      decimal.Round(stop.Latitude.Value, 7) != stop.Latitude.Value
      || decimal.Round(stop.Longitude.Value, 7) != stop.Longitude.Value
    )
      return "Use coordinates with at most seven decimal places.";
    if (
      !FitsAmount(stop.Weight)
      || !FitsAmount(stop.Pieces)
      || !FitsAmount(stop.Pallets)
    )
      return "Cargo quantities require up to 16 digits and 2 decimal places.";
    if (stop.TimeZoneId.Length > 100)
      return "Choose a supported appointment time zone.";
    TimeZoneInfo? zone = null;
    if (stop.TimeZoneId.Length > 0)
    {
      try
      {
        zone = TimeZoneInfo.FindSystemTimeZoneById(stop.TimeZoneId);
      }
      catch (TimeZoneNotFoundException)
      {
        return "Choose a supported appointment time zone.";
      }
      catch (InvalidTimeZoneException)
      {
        return "Choose a supported appointment time zone.";
      }
    }
    if (stop.AppointmentMode == "unscheduled")
      return
        stop.ScheduledDate is not null
        || stop.ScheduledTime is not null
        || stop.ScheduledDate2 is not null
        || stop.ScheduledTime2 is not null
        ? "Unscheduled stops cannot contain appointment values."
        : null;
    if (
      stop.AppointmentMode is not ("at" or "window")
      || stop.ScheduledDate is null
    )
      return "Provide the appointment start date.";
    if (
      stop.AppointmentMode == "at"
      && (stop.ScheduledDate2 is not null || stop.ScheduledTime2 is not null)
    )
      return "A single appointment cannot contain an end date or time.";
    if (
      stop.AppointmentMode == "window"
      && (
        stop.ScheduledDate2 is null
        || stop.ScheduledTime is null
        || stop.ScheduledTime2 is null
        || stop.ScheduledDate2.Value.ToDateTime(stop.ScheduledTime2.Value)
          < stop.ScheduledDate.Value.ToDateTime(stop.ScheduledTime.Value)
      )
    )
      return "Provide a window end that is not before its start.";
    if (
      zone is not null
      && new[]
      {
        (stop.ScheduledDate, stop.ScheduledTime),
        (stop.ScheduledDate2, stop.ScheduledTime2),
      }.Any(x =>
        x.Item1.HasValue
        && x.Item2.HasValue
        && zone.IsInvalidTime(x.Item1.Value.ToDateTime(x.Item2.Value))
      )
    )
      return "The appointment falls within a daylight-saving clock gap.";
    return null;
  }

  private static bool FitsAmount(decimal? value) =>
    value is null
    || value is >= 0 and <= 9999999999999999.99m
      && decimal.Round(value.Value, 2) == value.Value;
}
