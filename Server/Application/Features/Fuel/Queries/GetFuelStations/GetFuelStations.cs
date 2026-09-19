using Application.Caching;
using Application.Features.Fuel.Models;
using Application.Models;

namespace Application.Features.Fuel.Queries.GetFuelStations;

public record GetFuelStationsQuery(
  DateOnly? Date = null,
  bool IncludeNextDay = false
) : IRequest<RequestResponse<List<FuelStationDto>>>;

public record FuelStationDto(
  Guid Id,
  string ExternalId,
  string Name,
  string Address,
  string City,
  string Region,
  string PostalCode,
  string Country,
  decimal? Latitude,
  decimal? Longitude,
  List<FuelDiscountDto> Discounts
)
{
  public FuelDiscountDto? CashDiscount { get; init; }
  public FuelDiscountDto? IftaDiscount { get; init; }
  public FuelPriceComparisonDto? CashComparison { get; init; }
  public FuelPriceComparisonDto? IftaComparison { get; init; }
}

public record FuelDiscountDto(
  string Currency,
  string Product,
  decimal RetailPrice,
  decimal DiscountPrice,
  decimal Savings,
  DateOnly EffectiveFrom,
  DateOnly EffectiveTo,
  decimal? PriceAfterIfta,
  string Unit = ""
);

public class GetFuelStationsHandler(
  IAppDbContext dbContext,
  ReadCache reads,
  TimeProvider clock
) : IRequestHandler<GetFuelStationsQuery, RequestResponse<List<FuelStationDto>>>
{
  public async Task<RequestResponse<List<FuelStationDto>>> Handle(
    GetFuelStationsQuery request,
    CancellationToken cancellationToken
  )
  {
    var date =
      request.Date ?? FuelPricingDate.FromUtc(clock.GetUtcNow().UtcDateTime);
    var items = await reads.GetAsync(
      "fuel",
      date.ToString("O"),
      () => LoadAsync(date, cancellationToken),
      ct: cancellationToken
    );
    if (request.IncludeNextDay && date < DateOnly.MaxValue)
    {
      var nextDate = date.AddDays(1);
      var next = await reads.GetAsync(
        "fuel",
        nextDate.ToString("O"),
        () => LoadAsync(nextDate, cancellationToken),
        ct: cancellationToken
      );
      var byId = next.ToDictionary(station => station.Id);
      items = items
        .Select(station =>
          byId.TryGetValue(station.Id, out var tomorrow)
            ? station with
            {
              CashComparison = FuelPriceComparisonDto.Create(
                date,
                station.CashDiscount,
                tomorrow.CashDiscount
              ),
              IftaComparison = FuelPriceComparisonDto.Create(
                date,
                station.IftaDiscount ?? station.CashDiscount,
                tomorrow.IftaDiscount ?? tomorrow.CashDiscount
              ),
            }
            : station
        )
        .ToList();
    }
    return RequestResponse<List<FuelStationDto>>.Ok(items);
  }

  private async Task<List<FuelStationDto>> LoadAsync(
    DateOnly date,
    CancellationToken cancellationToken
  )
  {
    var quarterStart = new DateOnly(date.Year, (date.Month - 1) / 3 * 3 + 1, 1);
    var previousQuarterStart = quarterStart.AddMonths(-3);
    var iftaRates = await dbContext
      .IftaTaxRates.AsNoTracking()
      .Where(x =>
        x.EffectiveFrom <= date
        && x.EffectiveTo >= previousQuarterStart
        && x.FuelType == "Diesel"
      )
      .OrderByDescending(x => x.EffectiveFrom)
      .ToListAsync(cancellationToken);

    var stations = await dbContext
      .FuelStations.AsNoTracking()
      .Where(x =>
        x.Latitude.HasValue
        && x.Longitude.HasValue
        && x.FuelDiscounts.Any(d =>
          d.EffectiveFrom <= date && d.EffectiveTo >= date
        )
      )
      .OrderBy(x => x.Name)
      .Select(x => new FuelStationDto(
        x.Id,
        x.ExternalId,
        x.Name,
        x.Address,
        x.City,
        x.Region,
        x.PostalCode,
        x.Country,
        x.Latitude,
        x.Longitude,
        x.FuelDiscounts.Where(d =>
            d.EffectiveFrom <= date && d.EffectiveTo >= date
          )
          .Select(d => new FuelDiscountDto(
            d.Currency,
            d.Product,
            d.RetailPrice,
            d.DiscountPrice,
            d.Savings,
            d.EffectiveFrom,
            d.EffectiveTo,
            null,
            ""
          ))
          .ToList()
      ))
      .ToListAsync(cancellationToken);

    var items = FuelPriceCalculator
      .ApplyIfta(stations, iftaRates)
      .Select(station => FuelDisplayPrices.Select(station, date))
      .ToList();

    return items;
  }
}
