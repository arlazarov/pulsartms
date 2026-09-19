using Client.Models.DTO.Addresses;
using Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Client.Shared.Search.AddressAutocomplete;

public partial class AddressAutocomplete
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  [Inject]
  private TimeProvider Clock { get; set; } = default!;

  [Parameter]
  public string Id { get; set; } = "address-" + Guid.NewGuid();

  [Parameter]
  public bool Disabled { get; set; }

  [Parameter]
  public EventCallback<PostalAddress> Selected { get; set; }

  private string _query = "";
  private string? _message;
  private List<AddressSuggestion> _items = [];
  private int _active = -1;
  private bool _loading;
  private bool _disposed;
  private Guid _session = Guid.NewGuid();
  private CancellationTokenSource? _request;
  private string? ActiveId => _active < 0 ? null : Id + "-option-" + _active;

  protected override void OnParametersSet()
  {
    if (Disabled)
      Cancel();
  }

  private async Task QueryChanged(ChangeEventArgs args)
  {
    Cancel();
    _query = args.Value?.ToString() ?? "";
    if (Disabled || _query.Trim().Length < 3)
      return;
    var owner = _request = new CancellationTokenSource();
    var token = owner.Token;
    try
    {
      await Task.Delay(TimeSpan.FromMilliseconds(300), Clock, token);
      if (!Owns(owner))
        return;
      _loading = true;
      StateHasChanged();
      var result = await Api.PostAsync<
        SuggestAddressesRequest,
        AddressSuggestions
      >("api/address-search/suggest", new(_query.Trim(), _session), token);
      if (!Owns(owner))
        return;
      _items = result.Response?.Items.ToList() ?? [];
      _message =
        !result.Success
          ? "Address search is unavailable. You can enter the address manually."
        : result.Response?.ProviderUnavailable == true
          ? "New suggestions are unavailable. Saved addresses remain available."
        : _items.Count == 0 ? "No matching addresses."
        : null;
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    finally
    {
      if (Owns(owner))
      {
        _loading = false;
        _request = null;
        owner.Dispose();
      }
    }
  }

  private async Task SelectAsync(int index)
  {
    if (Disabled || index < 0 || index >= _items.Count)
      return;
    var item = _items[index];
    Cancel();
    var owner = _request = new CancellationTokenSource();
    _loading = true;
    var address = item.SavedAddress;
    if (address is null)
    {
      var result = await Api.PostAsync<
        ResolveSuggestedAddressRequest,
        PostalAddress
      >("api/address-search/resolve", new(item.Id, _session), owner.Token);
      if (!Owns(owner))
        return;
      address = result.Response;
      if (!result.Success)
        _message = "Could not load this address. Try another suggestion.";
    }
    if (!Owns(owner))
      return;
    _request = null;
    owner.Dispose();
    _loading = false;
    _session = Guid.NewGuid();
    if (address is not null)
    {
      _query = "";
      await Selected.InvokeAsync(address);
    }
  }

  private Task KeyAsync(KeyboardEventArgs args)
  {
    if (args.Key == "Escape")
    {
      Cancel();
      _session = Guid.NewGuid();
    }
    if (_items.Count == 0)
      return Task.CompletedTask;
    if (args.Key == "ArrowDown")
      _active = (_active + 1) % _items.Count;
    if (args.Key == "ArrowUp")
      _active = (_active <= 0 ? _items.Count : _active) - 1;
    return args.Key == "Enter" && _active >= 0
      ? SelectAsync(_active)
      : Task.CompletedTask;
  }

  private bool Owns(CancellationTokenSource owner) =>
    !_disposed && !Disabled && ReferenceEquals(owner, _request);

  private void Cancel()
  {
    var pending = _request;
    _request = null;
    pending?.Cancel();
    pending?.Dispose();
    _items = [];
    _active = -1;
    _message = null;
    _loading = false;
  }

  public void Dispose()
  {
    _disposed = true;
    Cancel();
  }
}
