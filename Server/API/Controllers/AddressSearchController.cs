using Application.Features.Addresses;
using Application.Features.Addresses.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Authorize(Policy = "Dispatch")]
[Route("api/address-search")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AddressSearchController : BaseController
{
  [HttpPost("suggest")]
  public Task<IActionResult> Suggest(
    SuggestAddressesRequest request,
    CancellationToken ct
  ) => HandleRequest(new SuggestAddresses(request), ct);

  [HttpPost("resolve")]
  public Task<IActionResult> Resolve(
    ResolveSuggestedAddressRequest request,
    CancellationToken ct
  ) => HandleRequest(new ResolveSuggestedAddress(request), ct);
}
