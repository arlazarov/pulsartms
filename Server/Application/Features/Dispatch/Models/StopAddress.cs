using System.Text.Json;
using Domain.Entities.Dispatch;

namespace Application.Features.Dispatch.Models;

public sealed record StopAddress(
  string Address,
  string City,
  string Province,
  string Country,
  string ZipCode
)
{
  public static StopAddress From(DispatchStop stop) =>
    new(stop.Address, stop.City, stop.Province, stop.Country, stop.ZipCode);

  public string Serialize() => JsonSerializer.Serialize(this);

  public void Apply(DispatchStop stop)
  {
    stop.Address = Address;
    stop.City = City;
    stop.Province = Province;
    stop.Country = Country;
    stop.ZipCode = ZipCode;
  }
}
