using Client.Models.DTO.Dispatch;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class DispatchDetails : IDisposable
{
  [Parameter] public Guid Id { get; set; }
  [Inject] private ApiService Api { get; set; } = default!;
  private DispatchResponse? _item;
  private bool _loading;
  private string? _error;
  private CancellationTokenSource? _request;
  protected override Task OnParametersSetAsync() => LoadAsync();

  private async Task LoadAsync()
  {
    _request?.Cancel();
    using var request = new CancellationTokenSource();
    _request = request;
    _loading = true;
    _error = null;
    var result = await Api.GetAsync<DispatchResponse>($"api/dispatch/{Id}", request.Token);
    if (request.IsCancellationRequested) return;
    if (result.Success) _item = result.Response;
    else _error = result.ErrorMessage;
    _loading = false;
    if (ReferenceEquals(_request, request)) _request = null;
  }

  private static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
  private static string Commodity(string? value) => Value(value);
  private static string Date(DateOnly? date) => date?.ToString("yyyy-MM-dd") ?? "—";
  private static string Timestamp(DateTime? date) => date?.ToString("yyyy-MM-dd hh:mm tt 'UTC'", System.Globalization.CultureInfo.InvariantCulture) ?? "—";
  public void Dispose() { _request?.Cancel(); GC.SuppressFinalize(this); }
}
