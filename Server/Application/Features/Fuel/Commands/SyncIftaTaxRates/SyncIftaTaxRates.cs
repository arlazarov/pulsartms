using Application.Caching;
using Application.Features.Fuel.Interfaces;
using Application.Models;

namespace Application.Features.Fuel.Commands.SyncIftaTaxRates;

public record SyncIftaTaxRatesCommand(int Year, int Quarter) : IRequest<RequestResponse<int>>;

public class SyncIftaTaxRatesValidator : AbstractValidator<SyncIftaTaxRatesCommand>
{
  public SyncIftaTaxRatesValidator()
  {
    RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
    RuleFor(x => x.Quarter).InclusiveBetween(1, 4);
  }
}

public class SyncIftaTaxRatesHandler(IAppDbContext dbContext, IIftaApiService iftaApiService, ReadCache reads)
  : IRequestHandler<SyncIftaTaxRatesCommand, RequestResponse<int>>
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
    if (count > 0) reads.Invalidate("fuel");

    return RequestResponse<int>.Ok(count);
  }
}
