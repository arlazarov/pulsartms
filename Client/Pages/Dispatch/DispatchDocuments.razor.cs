using System.Globalization;
using Client.Models.DTO.Dispatch;
using Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace Client.Pages.Dispatch;

public partial class DispatchDocuments : IAsyncDisposable
{
  [Parameter]
  public Guid DispatchId { get; set; }

  [Parameter]
  public bool CanEdit { get; set; }

  [Parameter]
  public EventCallback<bool> DraftChanged { get; set; }

  [Inject]
  private ApiService Api { get; set; } = default!;

  [Inject]
  private IJSRuntime JS { get; set; } = default!;

  private const int MaximumBytes = 5 * 1024 * 1024;
  private const int MaximumFiles = 50;
  private readonly CancellationTokenSource _lifetime = new();
  private CancellationTokenSource _selection = new();
  private List<DispatchDocumentInfo> _documents = [];
  private readonly Queue<IBrowserFile> _files = new();
  private UploadDispatchDocumentRequest? _pending;
  private IJSObjectReference? _module;
  private Guid _identity;
  private Guid _fileKey = Guid.NewGuid();
  private string _kind = "rc";
  private string _batchKind = "rc";
  private string? _error;
  private string? _notice;
  private int _batchCount;
  private int _completed;
  private bool _loading;
  private bool _busy;
  private bool _uploadFailed;
  private bool _disposed;
  private bool _selecting;
  private bool _reportedDraft;

  private bool UploadDisabled =>
    _busy || _loading || _files.Count > 0 || !CanEdit;

  private string? PendingName =>
    _pending?.FileName ?? (_files.TryPeek(out var file) ? file.Name : null);

  protected override async Task OnParametersSetAsync()
  {
    if (_identity == DispatchId)
      return;
    _selection.Cancel();
    _selection.Dispose();
    _selection = new();
    _identity = DispatchId;
    _documents = [];
    _busy = false;
    _loading = false;
    _selecting = false;
    _notice = null;
    ClearFile();
    await PublishDraftAsync();
    await LoadAsync();
  }

  private async Task LoadAsync()
  {
    if (_disposed || _busy || _loading || DispatchId == Guid.Empty)
      return;
    var owner = _selection;
    var identity = DispatchId;
    _loading = true;
    _error = null;
    var result = await Api.GetAsync<List<DispatchDocumentInfo>>(
      $"api/dispatch/{identity}/documents",
      owner.Token
    );
    if (!Current(owner))
      return;
    _loading = false;
    if (result.Success && result.Response is not null)
      _documents = result.Response;
    else
      _error = "Documents could not be loaded. Refresh documents to retry.";
  }

  private async Task SelectFileAsync(InputFileChangeEventArgs args)
  {
    if (UploadDisabled)
      return;
    _error = null;
    _notice = null;
    try
    {
      var files = args.GetMultipleFiles(MaximumFiles);
      if (files.Any(file => file.Size is <= 0 or > MaximumBytes))
      {
        _error = "Choose non-empty files up to 5 MB each.";
        _fileKey = Guid.NewGuid();
        return;
      }
      foreach (var file in files)
        _files.Enqueue(file);
      _batchCount = files.Count;
      _batchKind = _kind;
    }
    catch (InvalidOperationException)
    {
      _error = "Choose up to 50 files at a time.";
      _fileKey = Guid.NewGuid();
      return;
    }
    await UploadAsync();
  }

  private async Task UploadAsync()
  {
    if (_busy || _loading || _files.Count == 0 || !CanEdit)
      return;
    var owner = _selection;
    var identity = DispatchId;
    _busy = true;
    _uploadFailed = false;
    _error = null;
    await PublishDraftAsync();
    try
    {
      while (Current(owner) && CanEdit && _files.Count > 0)
      {
        if (_pending is null && !await ReadFileAsync(owner))
          return;
        if (!Current(owner))
          return;
        var pending = _pending!;
        StateHasChanged();
        var result = await Api.PostAsync<
          UploadDispatchDocumentRequest,
          DispatchDocumentInfo
        >($"api/dispatch/{identity}/documents", pending, owner.Token);
        if (!Current(owner))
          return;
        if (
          !result.Success
          || result.Response is null
          || result.Response.Id != pending.IdempotencyKey
        )
        {
          _uploadFailed = true;
          _error = "Upload could not be confirmed. Retry the same file.";
          return;
        }
        _documents.RemoveAll(x => x.Id == result.Response.Id);
        _documents.Insert(0, result.Response);
        _pending = null;
        _files.Dequeue();
        _completed++;
        if (_files.Count > 0)
          StateHasChanged();
      }
      if (Current(owner) && _files.Count == 0)
      {
        _notice =
          _completed == 1
            ? "Document uploaded."
            : $"{_completed} documents uploaded.";
        ClearFile();
      }
    }
    finally
    {
      if (Current(owner))
      {
        _busy = false;
        _selecting = false;
        await PublishDraftAsync();
      }
    }
  }

  private async Task<bool> ReadFileAsync(CancellationTokenSource owner)
  {
    _selecting = true;
    StateHasChanged();
    try
    {
      var file = _files.Peek();
      using var memory = new MemoryStream();
      await using var stream = file.OpenReadStream(MaximumBytes, owner.Token);
      await stream.CopyToAsync(memory, owner.Token);
      if (!Current(owner))
        return false;
      _pending = new(Guid.NewGuid(), _batchKind, file.Name, memory.ToArray());
      _selecting = false;
      return true;
    }
    catch (OperationCanceledException) when (owner.IsCancellationRequested) { }
    catch (Exception ex)
      when (ex is IOException or InvalidOperationException or JSException)
    {
      if (Current(owner))
      {
        _uploadFailed = true;
        _error =
          "The file could not be read. " + "Retry or discard remaining files.";
      }
    }
    return false;
  }

  private async Task DownloadAsync(DispatchDocumentInfo document)
  {
    if (_busy || _loading)
      return;
    var owner = _selection;
    var identity = DispatchId;
    _busy = true;
    _error = null;
    try
    {
      var result = await Api.GetAsync<DispatchDocumentDownload>(
        $"api/dispatch/{identity}/documents/{document.Id}",
        owner.Token
      );
      if (!Current(owner))
        return;
      if (!result.Success || result.Response is null)
      {
        _error = "The file could not be downloaded. Please retry.";
        return;
      }
      var module = _module ??= await JS.InvokeAsync<IJSObjectReference>(
        "import",
        _lifetime.Token,
        "./js/generated/dispatch/documents.js"
      );
      if (!Current(owner))
        return;
      await module.InvokeVoidAsync(
        "download",
        owner.Token,
        result.Response.FileName,
        result.Response.Content
      );
    }
    catch (OperationCanceledException)
      when (owner.IsCancellationRequested || _lifetime.IsCancellationRequested)
    { }
    catch (JSException)
    {
      if (Current(owner))
        _error = "The browser could not download this file. Please retry.";
    }
    finally
    {
      if (Current(owner))
        _busy = false;
    }
  }

  private void ClearFile()
  {
    _pending = null;
    _files.Clear();
    _batchCount = 0;
    _completed = 0;
    _fileKey = Guid.NewGuid();
    _uploadFailed = false;
  }

  private Task ClearSelectionAsync()
  {
    if (_busy)
      return Task.CompletedTask;
    ClearFile();
    _error = null;
    _notice = "Remaining files discarded. Uploaded documents stay attached.";
    return PublishDraftAsync();
  }

  private async Task PublishDraftAsync()
  {
    if (_disposed)
      return;
    var draft = _pending is not null || _selecting || _files.Count > 0;
    if (_reportedDraft == draft)
      return;
    _reportedDraft = draft;
    await DraftChanged.InvokeAsync(draft);
  }

  private bool Current(CancellationTokenSource owner) =>
    !_disposed && owner == _selection && !owner.IsCancellationRequested;

  private static string Time(DateTime value) =>
    DateTime
      .SpecifyKind(value, DateTimeKind.Utc)
      .ToLocalTime()
      .ToString("MMM d · hh:mm tt", CultureInfo.InvariantCulture);

  private static string Kind(string kind) =>
    kind == "other" ? "Other" : kind.ToUpperInvariant();

  public async ValueTask DisposeAsync()
  {
    _disposed = true;
    _selection.Cancel();
    _lifetime.Cancel();
    _pending = null;
    _files.Clear();
    if (_module is not null)
    {
      try
      {
        await _module.DisposeAsync();
      }
      catch (JSDisconnectedException) { }
    }
    _lifetime.Dispose();
    _selection.Dispose();
  }
}
