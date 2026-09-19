using Client.Models.DTO;
using Client.Models.DTO.Addresses;
using Client.Models.DTO.Dispatch.Workspace;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class DispatchAddressSearch
{
  [Parameter, EditorRequired]
  public string Id { get; set; } = "";

  [Parameter]
  public bool Disabled { get; set; }

  [Parameter, EditorRequired]
  public Func<
    string,
    CancellationToken,
    Task<RequestResponseDTO<VerifiedDispatchAddress>>
  > Lookup { get; set; } = default!;

  [Parameter]
  public EventCallback<VerifiedDispatchAddress> Selected { get; set; }

  private string? _error;
  private bool _loading;
  private CancellationTokenSource? _request;

  protected override void OnParametersSet()
  {
    if (Disabled)
      CancelRequest();
  }

  private async Task SelectAsync(PostalAddress address)
  {
    if (Disabled || _loading)
      return;
    CancelRequest();
    var owner = _request = new CancellationTokenSource();
    var token = owner.Token;
    _loading = true;
    _error = null;
    try
    {
      // Saved addresses still pass through the route's coordinate verification.
      var query = string.Join(
        ", ",
        new[]
        {
          address.Address,
          address.City,
          address.Region,
          address.Country,
          address.PostalCode,
        }.Where(x => !string.IsNullOrWhiteSpace(x))
      );
      var result = await Lookup(query, token);
      if (!ReferenceEquals(owner, _request))
        return;
      if (result.Success && result.Response is { } verified)
        await Selected.InvokeAsync(verified);
      else
        _error =
          "Address could not be verified. Select another or edit manually.";
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    finally
    {
      if (ReferenceEquals(owner, _request))
        CancelRequest();
    }
  }

  private void CancelRequest()
  {
    var request = _request;
    _request = null;
    request?.Cancel();
    request?.Dispose();
    _loading = false;
  }

  public void Dispose() => CancelRequest();
}
