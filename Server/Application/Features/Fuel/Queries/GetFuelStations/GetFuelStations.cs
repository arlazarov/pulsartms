using Application.Caching;
using Application.Features.Fuel.Models;
using Application.Models;
using Domain.Entities.Fuel;

namespace Application.Features.Fuel.Queries.GetFuelStations;

public record GetFuelStationsQuery(
  DateOnly? Date = null,
  bool CompareDays = false
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

  // The same comparison one day back: yesterday against the day asked for.
  public FuelPriceComparisonDto? CashPreviousComparison { get; init; }
  public FuelPriceComparisonDto? IftaPreviousComparison { get; init; }
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
    var items = await DayAsync(date, cancellationToken);
    if (!request.CompareDays)
      return RequestResponse<List<FuelStationDto>>.Ok(items);

    // Whether to fuel now or wait is read off both sides of the day asked
    // for, so each station carries the step into it and the step out of it.
    // A day nobody has priced is an empty list, and the step to it is null.
    var yesterday = await NeighbourAsync(date, -1, cancellationToken);
    var tomorrow = await NeighbourAsync(date, 1, cancellationToken);
    items = items
      .Select(station =>
      {
        yesterday.TryGetValue(station.Id, out var before);
        tomorrow.TryGetValue(station.Id, out var after);
        return station with
        {
          CashComparison = FuelPriceComparisonDto.Create(
            date,
            station.CashDiscount,
            after?.CashDiscount
          ),
          IftaComparison = FuelPriceComparisonDto.Create(
            date,
            Ifta(station),
            Ifta(after)
          ),
          CashPreviousComparison = FuelPriceComparisonDto.Create(
            date.AddDays(-1),
            before?.CashDiscount,
            station.CashDiscount
          ),
          IftaPreviousComparison = FuelPriceComparisonDto.Create(
            date.AddDays(-1),
            Ifta(before),
            Ifta(station)
          ),
        };
      })
      .ToList();
    return RequestResponse<List<FuelStationDto>>.Ok(items);
  }

  private static FuelDiscountDto? Ifta(FuelStationDto? station) =>
    station?.IftaDiscount ?? station?.CashDiscount;

  private Task<List<FuelStationDto>> DayAsync(
    DateOnly date,
    CancellationToken cancellationToken
  ) =>
    reads.GetAsync(
      "fuel",
      date.ToString("O"),
      () => LoadAsync(date, cancellationToken),
      ct: cancellationToken
    );

  private async Task<Dictionary<Guid, FuelStationDto>> NeighbourAsync(
    DateOnly date,
    int days,
    CancellationToken cancellationToken
  )
  {
    // The calendar ends somewhere; past its end there is no day to compare.
    if (days < 0 ? date == DateOnly.MinValue : date == DateOnly.MaxValue)
      return [];
    var stations = await DayAsync(date.AddDays(days), cancellationToken);
    return stations.ToDictionary(station => station.Id);
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
        // A station the place provider reports as shut is not somewhere a
        // driver can buy fuel, so it is not offered - for planning or on the
        // map. Temporary closures are excluded the same way and return on
        // their own once the status is asked again and answers otherwise.
        //
        // An empty status is kept. It means nobody has asked, not that the
        // station is shut, and dropping the unasked would empty the map.
        && x.BusinessStatus != FuelStationStatus.ClosedPermanently
        && x.BusinessStatus != FuelStationStatus.ClosedTemporarily
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
