using System.Globalization;
using Client.Models.DTO.Planning;
using Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Client.Shared.Fuel.FuelSendPlan;

// What a dispatcher hands a driver for this shift. Opening it or copying
// the message records nothing. Send via WhatsApp and Mark as sent both act
// on the plan version that was shown and are refused if it moved in
// between. What WhatsApp reports is shown as it said it: accepted is not
// delivered, and an attempt without an answer is sent again only on
// purpose.
public partial class FuelSendPlan : IDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  [Inject]
  private IJSRuntime JS { get; set; } = default!;

  [Parameter, EditorRequired]
  public Guid DispatchId { get; set; }

  [Parameter]
  public Guid? ExecutionLegId { get; set; }

  [Parameter]
  public string TruckNumber { get; set; } = "";

  [Parameter]
  public EventCallback Closed { get; set; }

  [Parameter]
  public EventCallback Sent { get; set; }

  private FuelIssuePreview? _preview;
  private bool _loading,
    _saving,
    _editingContact,
    _disposed;
  private string? _error,
    _status;
  private readonly CancellationTokenSource _lifetime = new();

  private string Url =>
    $"api/dispatch/{DispatchId}/planning/fuel/issue"
    + (ExecutionLegId is { } leg ? $"?executionLegId={leg}" : "");

  protected override Task OnInitializedAsync() => LoadAsync();

  private async Task LoadAsync()
  {
    _loading = true;
    _error = null;
    _status = null;
    var result = await Api.GetAsync<FuelIssuePreview>(Url, _lifetime.Token);
    if (_disposed)
      return;
    _loading = false;
    if (!result.Success || result.Response is null)
    {
      _error = result.ErrorMessage ?? "The fuel plan could not be opened.";
      return;
    }
    _preview = result.Response;
  }

  private async Task CopyAsync()
  {
    if (_preview is null)
      return;
    try
    {
      await JS.InvokeVoidAsync("navigator.clipboard.writeText", _preview.Message);
      _status = "Message copied. It is not marked as sent.";
    }
    catch (JSException)
    {
      _status = "Could not copy. Please try again.";
    }
  }

  private async Task MarkSentAsync()
  {
    if (_preview is not { } preview || _saving || _disposed)
      return;
    _saving = true;
    _error = null;
    _status = null;
    var result = await Api.PostAsync<FuelIssueSentRequest, FuelIssuePreview>(
      $"api/dispatch/{DispatchId}/planning/fuel/issue/sent",
      new(
        preview.PlanCalculatedAt,
        preview.Lines.Select(x => x.VisitKey).ToList()
      )
      {
        ExecutionLegId = preview.ExecutionLegId,
        AssignmentRevision = preview.AssignmentRevision,
      },
      _lifetime.Token
    );
    if (_disposed)
      return;
    _saving = false;
    if (!result.Success || result.Response is null)
    {
      _error = result.ErrorMessage ?? "The plan could not be marked as sent.";
      return;
    }
    _preview = result.Response;
    _status = "Marked as sent.";
    await Sent.InvokeAsync();
  }

  private async Task SendAsync(bool again)
  {
    if (_preview is not { } preview || _saving || _disposed)
      return;
    _saving = true;
    _error = null;
    _status = null;
    var result = await Api.PostAsync<FuelIssueSendRequest, FuelIssuePreview>(
      $"api/dispatch/{DispatchId}/planning/fuel/issue/whatsapp",
      new(
        new(
          preview.PlanCalculatedAt,
          preview.Lines.Select(x => x.VisitKey).ToList()
        )
        {
          ExecutionLegId = preview.ExecutionLegId,
          AssignmentRevision = preview.AssignmentRevision,
        },
        again
      ),
      _lifetime.Token
    );
    if (_disposed)
      return;
    _saving = false;
    if (!result.Success || result.Response is null)
    {
      _error = result.ErrorMessage ?? "The plan could not be sent.";
      return;
    }
    _preview = result.Response;
    if (_preview.LastMessage?.Status is "accepted" or "sent")
      await Sent.InvokeAsync();
  }

  private async Task ContactSavedAsync()
  {
    _editingContact = false;
    await LoadAsync();
  }

  private bool CanSend(FuelIssuePreview preview) =>
    !_saving
    && preview.Recipient?.State == "ready"
    && preview.Lines.Count > 0
    && (preview.LastMessage is { NeedsConfirmation: true } || !AllSent(preview));

  private static string? LineLabel(FuelIssueLine line) =>
    line.Changed ? "Changed since sent"
    : line.Delivery == "failed" ? "Not delivered"
    : !line.Sent ? null
    : line.Delivery switch
    {
      "delivered" => "Delivered",
      "read" => "Read",
      _ => "Sent",
    };

  private static string LineClass(FuelIssueLine line) =>
    line.Changed || line.Delivery == "failed"
      ? "fleet-fuel-visit__sent fleet-fuel-visit__sent--changed"
      : "fleet-fuel-visit__sent";

  private static string DeliveryClass(FuelIssueMessageState last) =>
    last.NeedsConfirmation || last.Status is "failed" or "rejected"
      ? "fuel-send-plan__delivery fuel-send-plan__delivery--warn"
      : "fuel-send-plan__delivery";

  private static string RecipientText(FuelIssuePreview preview) =>
    preview.Recipient switch
    {
      null or { State: "notConfigured" } =>
        "WhatsApp is not set up. An administrator adds it in Settings.",
      { State: "noDriver" } =>
        "No driver is assigned to this truck's current work.",
      { State: "noNumber" } r => $"{r.DriverName} has no WhatsApp number.",
      { State: "outsideWindow" } r => $"{r.DriverName} has not written to "
        + "the WhatsApp number in the last 24 hours, so WhatsApp would not "
        + "deliver this message. Ask the driver to send any message first.",
      { } r => $"Sends to {r.DriverName} at {r.WhatsAppPhone}."
        + (r.WindowEndsAt is { } ends
          ? " WhatsApp accepts messages to them until "
            + ends
              .ToLocalTime()
              .ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture)
            + "."
          : ""),
    };

  private static string DeliveryText(FuelIssueMessageState last) =>
    last.NeedsConfirmation
      ? "WhatsApp did not answer the last attempt, so it may have been "
        + "delivered. Check with the driver before sending again."
      : last.Status switch
      {
        "accepted" => "WhatsApp accepted the message. Not delivered yet.",
        "sent" => "WhatsApp sent the message. Not delivered yet.",
        "delivered" => "Delivered to the driver's phone.",
        "read" => "Read by the driver.",
        "failed" => "WhatsApp could not deliver the message" + Code(last) + ".",
        "rejected" => "WhatsApp refused the message" + Code(last) + ".",
        "withdrawn" =>
          "Not sent: the plan changed while it was being sent.",
        _ => "Sending…",
      };

  private static string Code(FuelIssueMessageState last) =>
    last.ErrorCode is { } code
      ? $" (code {code.ToString(CultureInfo.InvariantCulture)})"
      : "";

  private Task CloseAsync() => Closed.InvokeAsync();

  private static bool AllSent(FuelIssuePreview preview) =>
    preview.Lines.All(x => x.Sent && !x.Changed);

  private static string StateText(FuelIssuePreview preview) =>
    preview.IssueState switch
    {
      "ready" => preview.HorizonEndsAt is { } until
        ? "Driver on duty. This shift runs to about "
          + until
            .ToLocalTime()
            .ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture)
          + ", including a two-hour station allowance."
        : "Driver on duty.",
      "awaitingDuty" =>
        "Awaiting duty: the driver is off duty or resting. Hold the plan "
          + "until they are back on duty.",
      _ => "Driver hours unknown: the current shift cannot be confirmed.",
    };

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _lifetime.Dispose();
  }
}
