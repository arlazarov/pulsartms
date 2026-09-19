using System.Globalization;
using Client.Models.DTO.Execution;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class SwitchOperationCard : IDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  [Inject]
  private TimeProvider Clock { get; set; } = default!;

  [Parameter, EditorRequired]
  public SwitchDetails Operation { get; set; } = default!;

  [Parameter]
  public IReadOnlyList<SwitchResourceOption> Drivers { get; set; } = [];

  [Parameter]
  public bool Locked { get; set; }

  [Parameter]
  public EventCallback<SwitchResult> Updated { get; set; }

  [Parameter]
  public EventCallback<bool> BusyChanged { get; set; }

  [Parameter]
  public EventCallback<Guid?> EditorChanged { get; set; }

  [Parameter]
  public EventCallback<ExecutionSourceApplyResult> SourceUpdated { get; set; }

  private readonly CancellationTokenSource _lifetime = new();
  private SwitchDetails? _opened;
  private SwitchParticipantDetails? _participant;
  private SwitchParticipantAction? _request;
  private Guid? _reviewParticipant;
  private MileageTimeDraft _occurred = new();
  private string? _kind;
  private string? _error;
  private Guid _key;
  private bool _saving;
  private bool _disposed;
  private string FieldId => $"switch-actual-{Operation.Id}";
  private string ActionLabel =>
    _kind == "cancel"
      ? "Cancel planned Switch"
      : EventLabel(_participant!, _kind == "receive");

  private async Task BeginAsync(string kind, SwitchParticipantDetails? row)
  {
    if (Locked || _saving || _kind is not null || _reviewParticipant.HasValue)
      return;
    _opened = Operation;
    _participant = row;
    _kind = kind;
    _key = Guid.NewGuid();
    _request = null;
    _occurred = new();
    _error = null;
    await EditorChanged.InvokeAsync(Operation.Id);
  }

  private async Task SaveAsync()
  {
    if (_saving || _disposed || _opened is null || _kind is null)
      return;
    _error = null;
    if (_kind != "cancel" && _request is null)
    {
      if (
        !_occurred.TryRead(out var actual)
        || actual > Clock.GetUtcNow()
        || actual is { Year: < 2000 }
      )
      {
        _error = "Enter a valid actual time, or leave both fields blank.";
        return;
      }
      var row = _participant!;
      _request = new(
        _key,
        _opened.Id,
        row.Id,
        _opened.Revision,
        row.Revision,
        _kind == "receive" ? row.IncomingRevision : row.OutgoingRevision,
        actual
      );
    }
    _saving = true;
    await BusyChanged.InvokeAsync(true);
    var prefix = $"api/execution/switches/{_opened.Id}";
    var result =
      _kind == "cancel"
        ? await Api.PostAsync<CancelSwitchRequest, SwitchResult>(
          $"{prefix}/cancel",
          new(_key, _opened.Revision),
          _lifetime.Token
        )
        : await Api.PostAsync<SwitchParticipantAction, SwitchResult>(
          $"{prefix}/participants/{_participant!.Id}/{_kind}",
          _request!,
          _lifetime.Token
        );
    if (_disposed)
      return;
    _saving = false;
    await BusyChanged.InvokeAsync(false);
    if (!result.Success || result.Response is null)
    {
      _error = result.ErrorMessage;
      return;
    }
    await EditorChanged.InvokeAsync(null);
    await Updated.InvokeAsync(result.Response);
  }

  private async Task CloseAsync()
  {
    if (_saving)
      return;
    _opened = null;
    _kind = null;
    _request = null;
    _error = null;
    await EditorChanged.InvokeAsync(null);
  }

  private async Task ReviewEditingAsync(Guid participant, bool opened)
  {
    _reviewParticipant = opened ? participant : null;
    await EditorChanged.InvokeAsync(opened ? Operation.Id : null);
  }

  private async Task SourceAcceptedAsync(ExecutionSourceApplyResult result)
  {
    _reviewParticipant = null;
    await EditorChanged.InvokeAsync(null);
    await SourceUpdated.InvokeAsync(result);
  }

  private static string EventLabel(
    SwitchParticipantDetails row,
    bool receive
  ) =>
    row.TransferKind == "drop_hook"
      ? receive
        ? "Hook"
        : "Drop"
      : receive
        ? "Receive"
        : "Release";

  private static string Time(DateTime? value) =>
    value is { } utc
      ? DateTime
        .SpecifyKind(utc, DateTimeKind.Utc)
        .ToLocalTime()
        .ToString("MMM d · hh:mm tt", CultureInfo.InvariantCulture)
      : "Not recorded";

  private static string EventTime(DateTime? value, bool confirmed) =>
    value.HasValue ? Time(value)
    : confirmed ? "Confirmed · time not recorded"
    : "Not recorded";

  private string Assignment(SwitchAssignmentOption option)
  {
    var parts = new List<string>
    {
      $"Truck {Value(option.Truck)}",
      $"Driver {Value(option.Driver)}",
      $"Trailer {Value(option.Trailer)}",
    };
    if (option.Resources.CoDriverId is { } coDriver)
      parts.Add(
        "Co-driver "
          + (
            Drivers.FirstOrDefault(x => x.Id == coDriver)?.Name
            ?? "Not available"
          )
      );
    return string.Join(" · ", parts);
  }

  private static string Value(string value) =>
    string.IsNullOrWhiteSpace(value) ? "Not recorded" : value;

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _lifetime.Dispose();
  }
}
