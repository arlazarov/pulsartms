using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.Dispatch;
using Client.Shared.Dispatch.StopCompletionEditor;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components.Web;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class StopCompletionEditorTests
{
  [Fact]
  public async Task OpeningAndCancellingDoesNotWriteAndMalformedTimeStaysLocal()
  {
    var calls = 0;
    using var context = new ClientComponentContext(
      (_, _) =>
      {
        calls++;
        throw new InvalidOperationException();
      }
    );
    var stop = Stop();
    var cut = context.Render<StopCompletionEditor>(p =>
      p.Add(x => x.DispatchId, Guid.NewGuid()).Add(x => x.Stop, stop)
    );
    await cut.Find("button").ClickAsync(new MouseEventArgs());
    cut.Find("input[type=text]").Change("not a time");
    await cut.Find(".btn--primary").ClickAsync(new MouseEventArgs());
    Assert.Contains("02:00 PM", cut.Find("[role=alert]").TextContent);
    await cut.FindAll("button")
      .Single(b => b.TextContent == "Cancel")
      .ClickAsync(new MouseEventArgs());
    Assert.Empty(cut.FindAll("input"));
    Assert.Equal(0, calls);
  }

  [Fact]
  public async Task SaveUsesExactIdentityAndRevisionThenUndoRequiresAnotherConfirmation()
  {
    var writes = new List<StopCompletionUpdate>();
    var stop = Stop();
    var dispatchId = Guid.NewGuid();
    var changed = 0;
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal(
          $"/api/dispatch/{dispatchId}/stops/{stop.Id}/completion",
          request.RequestUri!.AbsolutePath
        );
        var body =
          await request.Content!.ReadFromJsonAsync<StopCompletionUpdate>(ct);
        writes.Add(body!);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = JsonContent.Create(
            new RequestResponseDTO<StopCompletionState>
            {
              Success = true,
              Response = new(
                body!.CompletedAt?.UtcDateTime,
                Guid.NewGuid(),
                "Operator",
                DateTime.UtcNow,
                writes.Count
              ),
            }
          ),
        };
      }
    );
    var cut = context.Render<StopCompletionEditor>(p =>
      p.Add(x => x.DispatchId, dispatchId)
        .Add(x => x.Stop, stop)
        .Add(x => x.Changed, () => changed++)
    );
    await cut.Find("button").ClickAsync(new MouseEventArgs());
    await cut.Find(".btn--primary").ClickAsync(new MouseEventArgs());
    Assert.Single(writes);
    Assert.Equal(0, writes[0].Revision);
    Assert.Equal(stop.CompletionIdentity, writes[0].CompletionIdentity);
    Assert.True(stop.IsCompleted);
    Assert.Contains("by Operator", cut.Markup);
    Assert.Equal(1, changed);
    await cut.Find("button").ClickAsync(new MouseEventArgs());
    Assert.Single(writes);
    Assert.Contains("Confirm undo", cut.Markup);
    await cut.Find(".btn--primary").ClickAsync(new MouseEventArgs());
    Assert.Null(writes[1].CompletedAt);
    Assert.Equal(1, writes[1].Revision);
    Assert.False(stop.IsCompleted);
    Assert.Equal(2, changed);
  }

  [Fact]
  public async Task ConflictRetainsDraftWithoutShowingDetailedServerErrors()
  {
    using var context = new ClientComponentContext(
      (_, _) =>
        Task.FromResult(
          new HttpResponseMessage(HttpStatusCode.Conflict)
          {
            Content = JsonContent.Create(
              new RequestResponseDTO<StopCompletionState>
              {
                Success = false,
                Errors = ["private technical diagnostic"],
              }
            ),
          }
        )
    );
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var stop = Stop();
    var cut = context.Render<StopCompletionEditor>(p =>
      p.Add(x => x.DispatchId, Guid.NewGuid()).Add(x => x.Stop, stop)
    );
    await cut.Find("button").ClickAsync(new MouseEventArgs());
    await cut.Find(".btn--primary").ClickAsync(new MouseEventArgs());
    Assert.Contains("Close and reopen", cut.Find("[role=alert]").TextContent);
    Assert.DoesNotContain("private technical", cut.Markup);
    Assert.NotEmpty(cut.FindAll("input"));
    Assert.False(stop.IsCompleted);
  }

  [Theory]
  [InlineData(HttpStatusCode.Forbidden, "permission")]
  [InlineData(HttpStatusCode.Unauthorized, "sign in again")]
  [InlineData(HttpStatusCode.InternalServerError, "Please try again")]
  public async Task AccessAndServerFailuresDoNotBlameTheActualTime(
    HttpStatusCode status,
    string expected
  )
  {
    using var context = new ClientComponentContext(
      (_, _) => Task.FromResult(new HttpResponseMessage(status))
    );
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var cut = context.Render<StopCompletionEditor>(p =>
      p.Add(x => x.DispatchId, Guid.NewGuid()).Add(x => x.Stop, Stop())
    );
    await cut.Find("button").ClickAsync(new MouseEventArgs());
    await cut.Find(".btn--primary").ClickAsync(new MouseEventArgs());
    var message = cut.Find("[role=alert]").TextContent;
    Assert.Contains(expected, message);
    Assert.DoesNotContain("actual time", message);
    Assert.NotEmpty(cut.FindAll("input"));
  }

  [Fact]
  public async Task AChangedSelectionRejectsTheOldInFlightReply()
  {
    var pending = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    using var context = new ClientComponentContext((_, _) => pending.Task);
    var original = Stop();
    var next = Stop();
    var dispatchId = Guid.NewGuid();
    var cut = context.Render<StopCompletionEditor>(p =>
      p.Add(x => x.DispatchId, dispatchId).Add(x => x.Stop, original)
    );
    await cut.Find("button").ClickAsync(new MouseEventArgs());
    var save = cut.Find(".btn--primary").ClickAsync(new MouseEventArgs());
    cut.Render(p => p.Add(x => x.Stop, next));
    Assert.True(cut.Find("button").HasAttribute("disabled"));
    pending.SetResult(
      new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = JsonContent.Create(
          new RequestResponseDTO<StopCompletionState>
          {
            Success = true,
            Response = new(
              DateTime.UtcNow,
              Guid.NewGuid(),
              "Operator",
              DateTime.UtcNow,
              1
            ),
          }
        ),
      }
    );
    await save;
    Assert.False(next.IsCompleted);
    Assert.Empty(cut.FindAll("input"));
  }

  [Fact]
  public async Task LateReplyCannotReplaceANewerManualRevision()
  {
    var pending = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    using var context = new ClientComponentContext((_, _) => pending.Task);
    var stop = Stop();
    var cut = context.Render<StopCompletionEditor>(p =>
      p.Add(x => x.DispatchId, Guid.NewGuid()).Add(x => x.Stop, stop)
    );
    await cut.Find("button").ClickAsync(new MouseEventArgs());
    var save = cut.Find(".btn--primary").ClickAsync(new MouseEventArgs());
    stop.ManualCompletionRevision = 2;
    pending.SetResult(
      new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = JsonContent.Create(
          new RequestResponseDTO<StopCompletionState>
          {
            Success = true,
            Response = new(
              DateTime.UtcNow,
              Guid.NewGuid(),
              "Operator",
              DateTime.UtcNow,
              1
            ),
          }
        ),
      }
    );
    await save;
    Assert.Equal(2, stop.ManualCompletionRevision);
    Assert.False(stop.IsCompleted);
  }

  private static DispatchStopResponse Stop() =>
    new()
    {
      Id = Guid.NewGuid(),
      Sequence = 1,
      Job = "Pick Up",
      CompletionIdentity = new string('A', 64),
    };
}
