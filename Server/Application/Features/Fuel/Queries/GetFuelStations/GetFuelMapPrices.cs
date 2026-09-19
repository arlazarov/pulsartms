using Application.Models;

namespace Application.Features.Fuel.Queries.GetFuelStations;

public record GetFuelMapPricesQuery(DateOnly? Date = null)
  : IRequest<RequestResponse<List<FuelMapPriceDto>>>;

public record FuelMapPriceDto(
  Guid Id,
  string Currency,
  decimal? CashPrice,
  decimal? IftaPrice
);

public sealed class GetFuelMapPricesHandler(ISender sender)
  : IRequestHandler<
    GetFuelMapPricesQuery,
    RequestResponse<List<FuelMapPriceDto>>
  >
{
  public async Task<RequestResponse<List<FuelMapPriceDto>>> Handle(
    GetFuelMapPricesQuery request,
    CancellationToken ct
  )
  {
    var result = await sender.Send(new GetFuelStationsQuery(request.Date), ct);
    if (!result.Success || result.Response is null)
      return RequestResponse<List<FuelMapPriceDto>>.Fail(
        result.Errors ?? new("Fuel prices are unavailable."),
        result.StatusCode
      );

    var prices = result
      .Response.Where(station =>
        station.Latitude is >= -90 and <= 90
        && station.Longitude is >= -180 and <= 180
      )
      .Select(station => new FuelMapPriceDto(
        station.Id,
        station.CashDiscount?.Currency ?? "",
        station.CashDiscount?.DiscountPrice,
        (station.IftaDiscount ?? station.CashDiscount)?.PriceAfterIfta
      ))
      .ToList();
    return RequestResponse<List<FuelMapPriceDto>>.Ok(prices);
  }
}
