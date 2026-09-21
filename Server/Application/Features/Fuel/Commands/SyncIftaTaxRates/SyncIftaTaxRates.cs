using Application.Caching;
using Application.Features.Fuel.Interfaces;
using Application.Models;

namespace Application.Features.Fuel.Commands.SyncIftaTaxRates;

public record SyncIftaTaxRatesCommand(int Year, int Quarter)
  : IRequest<RequestResponse<int>>,
    IChecked
{
  public IEnumerable<string> Wrong()
  {
    if (Year is < 2000 or > 2100)
      yield return "Choose a year between 2000 and 2100.";
    if (Quarter is < 1 or > 4)
      yield return "Choose a quarter from 1 to 4.";
  }
}

public class SyncIftaTaxRatesHandler(
  IAppDbContext dbContext,
  IIftaApiService iftaApiService,
  ReadCache reads
) : IRequestHandler<SyncIftaTaxRatesCommand, RequestResponse<int>>
{
  public async Task<RequestResponse<int>> Handle(
    SyncIftaTaxRatesCommand request,
    CancellationToken cancellationToken
  )
  {
    var csv = await iftaApiService.GetTaxMatrixAsync(
      request.Year,
      request.Quarter,
      cancellationToken
    );

    var rates = IftaTaxMatrixParser.Parse(csv);

    await IftaTaxRateSync.SyncAsync(
      dbContext,
      rates,
      request.Year,
      request.Quarter,
      cancellationToken
    );

    var count = await dbContext.SaveChangesAsync(cancellationToken);
    if (count > 0)
      reads.Invalidate("fuel");

    return RequestResponse<int>.Ok(count);
  }
}
