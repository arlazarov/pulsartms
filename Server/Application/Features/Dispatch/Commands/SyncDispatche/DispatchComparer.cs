using Application.Features.Dispatch.Models;
using Domain.Entities.Dispatch;

namespace Application.Features.Dispatch.Commands.SyncDispatche;

public static class DispatchComparer
{
  public static bool StopsChanged(
    IReadOnlyCollection<DispatchStop> current,
    IReadOnlyCollection<ExternalDispatchStop> source
  )
  {
    if (current.Count != source.Count)
      return true;

    var currentStops = current.OrderBy(x => x.Sequence).ToList();
    var sourceStops = source.OrderBy(x => x.Sequence).ToList();

    for (var i = 0; i < currentStops.Count; i++)
    {
      if (!StopEquals(currentStops[i], sourceStops[i]))
        return true;
    }

    return false;
  }

  private static bool StopEquals(DispatchStop current, ExternalDispatchStop source)
  {
    return current.Sequence == source.Sequence
      && current.Job == source.Job
      && current.Name == source.Name
      && current.Address == source.Address
      && current.City == source.City
      && current.Province == source.Province
      && current.Country == source.Country
      && current.ZipCode == source.ZipCode
      && current.Latitude == source.Latitude
      && current.Longitude == source.Longitude
      && current.DriverName == source.DriverName
      && current.CoDriverName == source.CoDriverName
      && current.CarrierName == source.CarrierName
      && current.TruckNumber == source.TruckNumber
      && current.TrailerNumber == source.TrailerNumber
      && current.Commodity == source.Commodity
      && current.Notes == source.Notes
      && current.StopNo == source.StopNo
      && current.Weight == source.Weight
      && current.WeightUnit == source.WeightUnit
      && current.Pieces == source.Pieces
      && current.Pallets == source.Pallets
      && current.Temperature == source.Temperature
      && current.TemperatureUnit == source.TemperatureUnit
      && current.ScheduledDate == source.ScheduledDate
      && current.ScheduledTime == source.ScheduledTime
      && current.ScheduledDate2 == source.ScheduledDate2
      && current.ScheduledTime2 == source.ScheduledTime2
      && current.IsWindow == source.IsWindow
      && current.ArrivedAt == source.ArrivedAt
      && current.PickedUpAt == source.PickedUpAt
      && current.DeliveredAt == source.DeliveredAt
      && current.DepartedAt == source.DepartedAt;
  }
}
