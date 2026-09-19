using System.Net;
using System.Net.Http.Json;
using AngleSharp.Dom;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.Dispatch;
using Client.Pages.Dispatch;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchDocumentsTests
{
  [Fact]
  public async Task SelectionUploadsAndFailurePreservesTheRetryKey()
  {
    var load = Guid.NewGuid();
    var drafts = new List<bool>();
    var writes = new List<UploadDispatchDocumentRequest>();
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
          return Ok(new List<DispatchDocumentInfo>());
        Assert.Equal(
          $"/api/dispatch/{load}/documents",
          request.RequestUri!.AbsolutePath
        );
        var update = (
          await request.Content!.ReadFromJsonAsync<UploadDispatchDocumentRequest>(
            ct
          )
        )!;
        writes.Add(update);
        return writes.Count == 1
          ? new HttpResponseMessage(HttpStatusCode.GatewayTimeout)
          : Ok(Info(update.IdempotencyKey, "rate.pdf"));
      }
    );
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var cut = context.Render<DispatchDocuments>(p =>
      p.Add(x => x.DispatchId, load)
        .Add(x => x.DraftChanged, value => drafts.Add(value))
        .Add(x => x.CanEdit, true)
    );
    cut.WaitForAssertion(
      () => Assert.False(cut.Find("input[type=file]").HasAttribute("disabled"))
    );
    cut.FindComponent<InputFile>()
      .UploadFiles(
        InputFileContent.CreateFromText(
          "%PDF-1.7 fixture",
          "rate.pdf",
          contentType: "application/pdf"
        )
      );
    cut.WaitForAssertion(() => Assert.NotNull(Button(cut, "Retry upload")));
    Assert.Single(writes);
    Assert.Contains("rate.pdf", cut.Markup);
    Assert.NotNull(Button(cut, "Retry upload"));
    await Button(cut, "Retry upload").ClickAsync(new MouseEventArgs());
    Assert.Equal(2, writes.Count);
    Assert.Equal(writes[0].IdempotencyKey, writes[1].IdempotencyKey);
    Assert.Equal(writes[0].Content, writes[1].Content);
    Assert.Single(cut.FindAll("li"));
    Assert.Empty(cut.FindAll(".dispatch-documents__upload .btn--primary"));
    Assert.Equal([true, false], drafts);
  }

  [Fact]
  public async Task ReturningToSameLoadRejectsThePreviousGenerationsRead()
  {
    var first = Guid.NewGuid();
    var second = Guid.NewGuid();
    var firstReads = 0;
    var pending = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        if (request.RequestUri!.AbsolutePath.Contains(first.ToString()))
        {
          firstReads++;
          if (firstReads == 1)
            return pending.Task;
          return Task.FromResult(
            Ok(
              new List<DispatchDocumentInfo>
              {
                Info(Guid.NewGuid(), "current.pdf"),
              }
            )
          );
        }
        return Task.FromResult(Ok(new List<DispatchDocumentInfo>()));
      }
    );
    var cut = context.Render<DispatchDocuments>(p =>
      p.Add(x => x.DispatchId, first)
    );
    cut.Render(p => p.Add(x => x.DispatchId, second));
    cut.Render(p => p.Add(x => x.DispatchId, first));
    cut.WaitForAssertion(() => Assert.Contains("current.pdf", cut.Markup));
    pending.SetResult(
      Ok(new List<DispatchDocumentInfo> { Info(Guid.NewGuid(), "stale.pdf") })
    );
    await cut.InvokeAsync(() => Task.CompletedTask);
    Assert.DoesNotContain("stale.pdf", cut.Markup);
    Assert.Contains("current.pdf", cut.Markup);
  }

  [Fact]
  public async Task SelectionChangeDuringUploadDoesNotLockOrPolluteTheNewLoad()
  {
    var first = Guid.NewGuid();
    var second = Guid.NewGuid();
    var pending = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var started = new TaskCompletionSource<UploadDispatchDocumentRequest>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    CancellationToken uploadCancellation = default;
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
          return Ok(new List<DispatchDocumentInfo>());
        var upload = (
          await request.Content!.ReadFromJsonAsync<UploadDispatchDocumentRequest>(
            ct
          )
        )!;
        uploadCancellation = ct;
        started.SetResult(upload);
        return await pending.Task;
      }
    );
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var cut = context.Render<DispatchDocuments>(p =>
      p.Add(x => x.DispatchId, first).Add(x => x.CanEdit, true)
    );
    cut.WaitForAssertion(
      () => Assert.False(cut.Find("input[type=file]").HasAttribute("disabled"))
    );
    var uploading = cut.InvokeAsync(
      () =>
        cut.FindComponent<InputFile>()
          .Instance.OnChange.InvokeAsync(
            new InputFileChangeEventArgs([new ReadableBrowserFile()])
          )
    );
    var upload = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.False(uploading.IsCompleted);
    cut.Render(p => p.Add(x => x.DispatchId, second));
    cut.WaitForAssertion(
      () => Assert.False(cut.Find("input[type=file]").HasAttribute("disabled"))
    );
    Assert.True(uploadCancellation.IsCancellationRequested);
    pending.SetResult(Ok(Info(upload.IdempotencyKey, "old.pdf")));
    await uploading.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.DoesNotContain("old.pdf", cut.Markup);
    Assert.False(cut.Find("input[type=file]").HasAttribute("disabled"));
    Assert.Empty(cut.FindAll("li"));
  }

  [Fact]
  public async Task BatchPausesAtFailureAndRetriesBeforeTheNextFile()
  {
    var writes = new List<UploadDispatchDocumentRequest>();
    var drafts = new List<bool>();
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
          return Ok(new List<DispatchDocumentInfo>());
        var upload = (
          await request.Content!.ReadFromJsonAsync<UploadDispatchDocumentRequest>(
            ct
          )
        )!;
        writes.Add(upload);
        return writes.Count == 1
          ? new HttpResponseMessage(HttpStatusCode.GatewayTimeout)
          : Ok(Info(upload.IdempotencyKey, upload.FileName));
      }
    );
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var cut = context.Render<DispatchDocuments>(p =>
      p.Add(x => x.DispatchId, Guid.NewGuid())
        .Add(x => x.CanEdit, true)
        .Add(x => x.DraftChanged, value => drafts.Add(value))
    );
    cut.WaitForAssertion(
      () => Assert.False(cut.Find("input[type=file]").HasAttribute("disabled"))
    );
    Assert.True(cut.Find("input[type=file]").HasAttribute("multiple"));
    await cut.Find("select").ChangeAsync("pod");
    cut.FindComponent<InputFile>()
      .UploadFiles(File("first.pdf"), File("second.pdf"));
    cut.WaitForAssertion(() => Assert.NotNull(Button(cut, "Retry upload")));
    Assert.Single(writes);
    Assert.Contains("Paused", cut.Markup);
    Assert.True(cut.Find("input[type=file]").HasAttribute("disabled"));
    await Button(cut, "Retry upload").ClickAsync(new MouseEventArgs());
    Assert.Equal(3, writes.Count);
    Assert.Equal(writes[0].IdempotencyKey, writes[1].IdempotencyKey);
    Assert.Equal(writes[0].Content, writes[1].Content);
    Assert.NotEqual(writes[1].IdempotencyKey, writes[2].IdempotencyKey);
    Assert.Equal(
      ["first.pdf", "first.pdf", "second.pdf"],
      writes.Select(x => x.FileName)
    );
    Assert.All(writes, write => Assert.Equal("pod", write.Kind));
    Assert.Equal(2, cut.FindAll("li").Count);
    Assert.Contains("2 documents uploaded.", cut.Markup);
    Assert.Equal([true, false], drafts);
  }

  [Fact]
  public async Task DiscardRemainingFilesKeepsAlreadyUploadedDocuments()
  {
    var writes = 0;
    var drafts = new List<bool>();
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
          return Ok(new List<DispatchDocumentInfo>());
        var upload = (
          await request.Content!.ReadFromJsonAsync<UploadDispatchDocumentRequest>(
            ct
          )
        )!;
        writes++;
        return writes == 1
          ? Ok(Info(upload.IdempotencyKey, upload.FileName))
          : new HttpResponseMessage(HttpStatusCode.GatewayTimeout);
      }
    );
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var cut = context.Render<DispatchDocuments>(p =>
      p.Add(x => x.DispatchId, Guid.NewGuid())
        .Add(x => x.CanEdit, true)
        .Add(x => x.DraftChanged, value => drafts.Add(value))
    );
    cut.WaitForAssertion(
      () => Assert.False(cut.Find("input[type=file]").HasAttribute("disabled"))
    );
    cut.FindComponent<InputFile>()
      .UploadFiles(File("saved.pdf"), File("failed.pdf"), File("queued.pdf"));
    cut.WaitForAssertion(() => Assert.NotNull(Button(cut, "Retry upload")));
    Assert.Equal(2, writes);
    await Button(cut, "Discard remaining files")
      .ClickAsync(new MouseEventArgs());
    Assert.Single(cut.FindAll("li"));
    Assert.Contains("saved.pdf", cut.Markup);
    Assert.DoesNotContain("queued.pdf", cut.Markup);
    Assert.DoesNotContain("Retry upload", cut.Markup);
    Assert.False(cut.Find("input[type=file]").HasAttribute("disabled"));
    Assert.Equal([true, false], drafts);
    Assert.Equal(2, writes);
  }

  [Fact]
  public void OversizedSelectionDoesNotStartAnUpload()
  {
    var writes = 0;
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        if (request.Method != HttpMethod.Get)
          writes++;
        return Task.FromResult(Ok(new List<DispatchDocumentInfo>()));
      }
    );
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var cut = context.Render<DispatchDocuments>(p =>
      p.Add(x => x.DispatchId, Guid.NewGuid()).Add(x => x.CanEdit, true)
    );
    cut.WaitForAssertion(
      () => Assert.False(cut.Find("input[type=file]").HasAttribute("disabled"))
    );
    cut.FindComponent<InputFile>()
      .UploadFiles(
        InputFileContent.CreateFromBinary(
          new byte[5 * 1024 * 1024 + 1],
          "large.pdf",
          contentType: "application/pdf"
        )
      );
    cut.WaitForAssertion(() => Assert.Contains("up to 5 MB each", cut.Markup));
    Assert.Contains("Choose non-empty files", cut.Markup);
    Assert.Equal(0, writes);
    Assert.False(cut.Find("input[type=file]").HasAttribute("disabled"));
  }

  [Fact]
  public async Task BrowserReadFailureKeepsRetryAndDiscardAvailable()
  {
    var writes = 0;
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        if (request.Method != HttpMethod.Get)
          writes++;
        return Task.FromResult(Ok(new List<DispatchDocumentInfo>()));
      }
    );
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var cut = context.Render<DispatchDocuments>(p =>
      p.Add(x => x.DispatchId, Guid.NewGuid()).Add(x => x.CanEdit, true)
    );
    cut.WaitForAssertion(
      () => Assert.False(cut.Find("input[type=file]").HasAttribute("disabled"))
    );
    await cut.InvokeAsync(
      () =>
        cut.FindComponent<InputFile>()
          .Instance.OnChange.InvokeAsync(
            new InputFileChangeEventArgs([new UnreadableBrowserFile()])
          )
    );
    Assert.Contains("could not be read", cut.Find("[role=alert]").TextContent);
    Assert.False(Button(cut, "Retry upload").HasAttribute("disabled"));
    await Button(cut, "Discard remaining files").ClickAsync(new());
    Assert.False(cut.Find("input[type=file]").HasAttribute("disabled"));
    Assert.Equal(0, writes);
  }

  [Fact]
  public void ReadOnlyViewDoesNotOfferUploadAndEscapesFileNames()
  {
    using var context = new ClientComponentContext(
      (_, _) =>
        Task.FromResult(
          Ok(
            new List<DispatchDocumentInfo>
            {
              Info(Guid.NewGuid(), "<script>bad.pdf</script>"),
            }
          )
        )
    );
    var cut = context.Render<DispatchDocuments>(p =>
      p.Add(x => x.DispatchId, Guid.NewGuid())
    );
    cut.WaitForElement("li");
    Assert.Empty(cut.FindAll("input[type=file]"));
    Assert.Empty(cut.FindAll("script"));
  }

  private static IElement Button(
    IRenderedComponent<DispatchDocuments> cut,
    string text
  ) => cut.FindAll("button").Single(x => x.TextContent.Trim() == text);

  private static DispatchDocumentInfo Info(Guid id, string name) =>
    new(id, "rc", name, "application/pdf", 20, DateTime.UtcNow, "Dispatcher");

  private static InputFileContent File(string name) =>
    InputFileContent.CreateFromText(
      "%PDF-1.7 fixture",
      name,
      contentType: "application/pdf"
    );

  private static HttpResponseMessage Ok<T>(T body) =>
    new(HttpStatusCode.OK)
    {
      Content = JsonContent.Create(
        new RequestResponseDTO<T> { Success = true, Response = body }
      ),
    };

  private sealed class ReadableBrowserFile : IBrowserFile
  {
    private static readonly byte[] Content = "%PDF-1.7 fixture"u8.ToArray();

    public string Name => "old.pdf";
    public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
    public long Size => Content.Length;
    public string ContentType => "application/pdf";

    public Stream OpenReadStream(
      long maxAllowedSize = 512000,
      CancellationToken cancellationToken = default
    )
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (Size > maxAllowedSize)
        throw new IOException("File exceeds the requested size limit.");
      return new MemoryStream(Content, writable: false);
    }
  }

  private sealed class UnreadableBrowserFile : IBrowserFile
  {
    public string Name => "unreadable.pdf";
    public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
    public long Size => 20;
    public string ContentType => "application/pdf";

    public Stream OpenReadStream(
      long maxAllowedSize = 512000,
      CancellationToken cancellationToken = default
    ) => throw new JSException("Synthetic browser read failure.");
  }
}
