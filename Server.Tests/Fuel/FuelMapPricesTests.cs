using Application.Features.Fuel.Queries.GetFuelStations;
using Application.Models;
using MediatR;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelMapPricesTests
{
  [Fact]
  public async Task OverviewReusesSelectedDayQuotesWithoutLocationsDetailsOrTomorrow()
  {
    var date = new DateOnly(2026, 9, 12);
    var cash = new FuelDiscountDto(
      "USD",
      "Diesel",
      4m,
      3m,
      1m,
      date,
      date,
      null,
      "US gal"
    );
    var ifta = cash with { DiscountPrice = 3.1m, PriceAfterIfta = 2.5m };
    var station = new FuelStationDto(
      Guid.NewGuid(),
      "external",
      "Station",
      "Address",
      "City",
      "TX",
      "",
      "US",
      40,
      -100,
      [cash, ifta]
    )
    {
      CashDiscount = cash,
      IftaDiscount = ifta,
    };
    var sender = new Sender(
      RequestResponse<List<FuelStationDto>>.Ok(
        [
          station,
          station with
          {
            Id = Guid.NewGuid(),
            IftaDiscount = null,
          },
          station with
          {
            Id = Guid.NewGuid(),
            CashDiscount = null,
            IftaDiscount = null,
          },
          station with
          {
            Id = Guid.NewGuid(),
            Latitude = null,
          },
          station with
          {
            Id = Guid.NewGuid(),
            Longitude = 181,
          },
        ]
      )
    );
    using var cancellation = new CancellationTokenSource();
    var result = await new GetFuelMapPricesHandler(sender).Handle(
      new(date),
      cancellation.Token
    );

    Assert.Equal(
      new GetFuelStationsQuery(date, IncludeNextDay: false),
      sender.Query
    );
    Assert.Equal(cancellation.Token, sender.Cancellation);
    Assert.True(result.Success);
    Assert.Equal(3, result.Response!.Count);
    Assert.Equal(
      new FuelMapPriceDto(station.Id, "USD", 3m, 2.5m),
      result.Response[0]
    );
    Assert.Null(result.Response[1].IftaPrice);
    Assert.Null(result.Response[2].CashPrice);
    Assert.Equal("", result.Response[2].Currency);
    Assert.Equal(
      new[] { "CashPrice", "Currency", "Id", "IftaPrice" },
      typeof(FuelMapPriceDto)
        .GetProperties()
        .Select(property => property.Name)
        .Order()
    );
  }

  [Fact]
  public async Task FailedStationReadDoesNotPublishAnEmptySuccessfulOverview()
  {
    var sender = new Sender(
      RequestResponse<List<FuelStationDto>>.Fail("Unavailable", 503)
    );
    var result = await new GetFuelMapPricesHandler(sender).Handle(
      new(new(2026, 9, 12)),
      default
    );
    Assert.False(result.Success);
    Assert.Equal(503, result.StatusCode);
    Assert.Null(result.Response);
  }

  private sealed class Sender(RequestResponse<List<FuelStationDto>> result)
    : ISender
  {
    public GetFuelStationsQuery? Query { get; private set; }
    public CancellationToken Cancellation { get; private set; }

    public Task<TResponse> Send<TResponse>(
      IRequest<TResponse> request,
      CancellationToken ct = default
    )
    {
      Query = Assert.IsType<GetFuelStationsQuery>(request);
      Cancellation = ct;
      return Task.FromResult((TResponse)(object)result);
    }

    public Task Send<TRequest>(TRequest request, CancellationToken ct = default)
      where TRequest : IRequest => throw new NotSupportedException();

    public Task<object?> Send(object request, CancellationToken ct = default) =>
      throw new NotSupportedException();

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
      IStreamRequest<TResponse> request,
      CancellationToken ct = default
    ) => throw new NotSupportedException();

    public IAsyncEnumerable<object?> CreateStream(
      object request,
      CancellationToken ct = default
    ) => throw new NotSupportedException();
  }
}
