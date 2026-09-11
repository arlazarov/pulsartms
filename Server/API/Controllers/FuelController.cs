using Application.Features.Fuel.Commands;
using Application.Features.Fuel.Commands.ImportFuelDiscounts;
using Application.Features.Fuel.Commands.SyncIftaTaxRates;
using Application.Features.Fuel.Queries.GetFuelStations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace API.Controllers;

[Route("api/[controller]")]
public class FuelController : BaseController
{
  [HttpGet("stations")]
  public async Task<IActionResult> GetStations(
    [FromQuery] DateOnly? date,
    CancellationToken cancellationToken
  ) =>
    await HandleRequest(
      new GetFuelStationsQuery(date ?? DateOnly.FromDateTime(DateTime.Today), IncludeNextDay: true),
      cancellationToken
    );

  [HttpPost("gmail-watch/start")]
  [Authorize(Policy = "Admin")]
  public async Task<IActionResult> StartGmailWatch(CancellationToken cancellationToken) =>
    await HandleRequest(new StartGmailWatchCommand(), cancellationToken);

  [AllowAnonymous]
  [HttpPost("gmail-notifications")]
  [RequestSizeLimit(16384)]
  public Task<IActionResult> GmailNotifications(CancellationToken cancellationToken) =>
    HandleRequest(new ReceiveGmailNotificationCommand(Request.Headers.Authorization.ToString(), Request.Body), cancellationToken);

  [Authorize(Policy = "Admin")]
  [HttpPost("import")]
  public Task<IActionResult> Import(CancellationToken cancellationToken) =>
    HandleRequest(new ImportFuelDiscountsCommand(), cancellationToken);

  [HttpPost("ifta/sync")]
  [Authorize(Policy = "Admin")]
  public async Task<IActionResult> SyncIfta(
    int year,
    int quarter,
    CancellationToken cancellationToken
  ) => await HandleRequest(new SyncIftaTaxRatesCommand(year, quarter), cancellationToken);

}
