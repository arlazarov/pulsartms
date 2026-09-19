using System.Net;
using System.Net.Http.Json;
using AngleSharp.Dom;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.Dispatch;
using Client.Pages.Dispatch;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components.Web;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchActivityPanelTests
{
  [Fact]
  public async Task AddsLoadNoteOnlyOnSubmitUsingTheServerRevision()
  {
    var load = Guid.NewGuid();
    var drafts = new List<bool>();
    AddDispatchActivityUpdate? body = null;
    var reads = 0;
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
        {
          reads++;
          return Ok(Page(load, 7));
        }
        Assert.Equal(
          $"/api/dispatch/{load}/activity",
          request.RequestUri!.AbsolutePath
        );
        body =
          await request.Content!.ReadFromJsonAsync<AddDispatchActivityUpdate>(
            ct
          );
        return Ok(Item(load));
      }
    );
    var cut = context.Render<DispatchActivityPanel>(p =>
      p.Add(x => x.DispatchId, load)
        .Add(x => x.DraftChanged, value => drafts.Add(value))
    );
    cut.WaitForElement("textarea");
    await cut.Find("textarea").InputAsync("Driver called. All OK.");
    Assert.Equal("Notes", cut.Find("h2").TextContent);
    Assert.Empty(cut.FindAll("select, input[type=checkbox]"));
    Assert.DoesNotContain("What happened?", cut.Markup);
    Assert.DoesNotContain("Link to stop", cut.Markup);
    Assert.DoesNotContain("Dispatch activity", cut.Markup);
    Assert.DoesNotContain("Refresh", cut.Markup);
    Assert.Null(body);
    await cut.Find("form").SubmitAsync(EventArgs.Empty);
    Assert.Equal(7, body!.ExpectedRevision);
    Assert.NotEqual(Guid.Empty, body.OperationId);
    Assert.Equal("note", body.Kind);
    Assert.Null(body.StopId);
    Assert.Null(body.DriverId);
    Assert.False(body.NeedsAttention);
    Assert.Equal("", cut.Find("textarea").GetAttribute("value") ?? "");
    Assert.Equal(2, reads);
    Assert.Equal([true, false], drafts);
  }

  [Fact]
  public async Task ConflictKeepsDraftAndRefreshDoesNotWrite()
  {
    var load = Guid.NewGuid();
    var writes = 0;
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        if (request.Method == HttpMethod.Get)
          return Task.FromResult(Ok(Page(load, 1)));
        writes++;
        return Task.FromResult(
          new HttpResponseMessage(HttpStatusCode.Conflict)
        );
      }
    );
    var cut = context.Render<DispatchActivityPanel>(p =>
      p.Add(x => x.DispatchId, load)
    );
    cut.WaitForElement("textarea").Input("Keep this note");
    await cut.Find("form").SubmitAsync(EventArgs.Empty);
    Assert.Contains("Reload notes", cut.Find("[role=alert]").TextContent);
    Assert.Contains("Keep this note", cut.Markup);
    await Button(cut, "Reload notes").ClickAsync(new MouseEventArgs());
    Assert.Equal(1, writes);
    Assert.Contains("Keep this note", cut.Markup);
  }

  [Fact]
  public async Task UncertainSaveRetriesSameOperationWithoutDiscardingDraft()
  {
    var load = Guid.NewGuid();
    var writes = new List<AddDispatchActivityUpdate>();
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
          return Ok(Page(load));
        writes.Add(
          (
            await request.Content!.ReadFromJsonAsync<AddDispatchActivityUpdate>(
              ct
            )
          )!
        );
        return writes.Count == 1
          ? new HttpResponseMessage(HttpStatusCode.GatewayTimeout)
          : Ok(Item(load));
      }
    );
    var cut = context.Render<DispatchActivityPanel>(p =>
      p.Add(x => x.DispatchId, load)
    );
    cut.WaitForElement("textarea").Input("Retain until confirmed");
    await cut.Find("form").SubmitAsync(EventArgs.Empty);
    Assert.True(cut.Find("fieldset").HasAttribute("disabled"));
    Assert.NotNull(Button(cut, "Retry note"));
    await cut.Find("form").SubmitAsync(EventArgs.Empty);
    Assert.Equal(2, writes.Count);
    Assert.Equal(writes[0], writes[1]);
    Assert.False(cut.Find("fieldset").HasAttribute("disabled"));
  }

  [Fact]
  public async Task OldReadCannotPopulateAnotherLoad()
  {
    var oldLoad = Guid.NewGuid();
    var newLoad = Guid.NewGuid();
    var pending = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    using var context = new ClientComponentContext(
      (request, _) =>
        request.RequestUri!.AbsolutePath.Contains(oldLoad.ToString())
          ? pending.Task
          : Task.FromResult(
            Ok(Page(newLoad, items: [Item(newLoad, "New note")]))
          )
    );
    var cut = context.Render<DispatchActivityPanel>(p =>
      p.Add(x => x.DispatchId, oldLoad)
    );
    cut.Render(p => p.Add(x => x.DispatchId, newLoad));
    cut.WaitForAssertion(() => Assert.Contains("New note", cut.Markup));
    pending.SetResult(Ok(Page(oldLoad, items: [Item(oldLoad, "Stale note")])));
    await cut.InvokeAsync(() => Task.CompletedTask);
    Assert.DoesNotContain("Stale note", cut.Markup);
  }

  [Fact]
  public async Task PinnedIssueResolvesWithoutEditingTheLoadAndEscapesText()
  {
    var load = Guid.NewGuid();
    var issue = Item(load, "<script>private note</script>") with
    {
      NeedsAttention = true,
      Revision = 3,
    };
    ResolveDispatchActivityUpdate? body = null;
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
          return Ok(Page(load, items: [], issues: [issue]));
        Assert.Equal(
          $"/api/dispatch/{load}/activity/{issue.Id}/resolve",
          request.RequestUri!.AbsolutePath
        );
        body =
          await request.Content!.ReadFromJsonAsync<ResolveDispatchActivityUpdate>(
            ct
          );
        return Ok(issue with { ResolvedAt = DateTime.UtcNow });
      }
    );
    var cut = context.Render<DispatchActivityPanel>(p =>
      p.Add(x => x.DispatchId, load)
    );
    cut.WaitForElement(".dispatch-activity__issues");
    Assert.Equal("Notes", cut.Find("h2").TextContent);
    Assert.Empty(cut.FindAll("script"));
    await Button(cut, "Resolve").ClickAsync(new MouseEventArgs());
    Assert.Equal(3, body!.ExpectedRevision);
    Assert.NotEqual(Guid.Empty, body.OperationId);
  }

  private static IElement Button(
    IRenderedComponent<DispatchActivityPanel> cut,
    string text
  ) => cut.FindAll("button").Single(x => x.TextContent.Trim() == text);

  private static HttpResponseMessage Ok<T>(T body) =>
    new(HttpStatusCode.OK)
    {
      Content = JsonContent.Create(
        new RequestResponseDTO<T> { Success = true, Response = body }
      ),
    };

  private static DispatchActivityPage Page(
    Guid load,
    long revision = 0,
    IReadOnlyList<DispatchActivityItem>? items = null,
    IReadOnlyList<DispatchActivityItem>? issues = null
  ) =>
    new(
      load,
      revision,
      items ?? [],
      null,
      issues?.Count ?? 0,
      issues ?? [],
      null
    );

  private static DispatchActivityItem Item(Guid load, string text = "A note") =>
    new(
      Guid.NewGuid(),
      load,
      1,
      1,
      "note",
      text,
      null,
      null,
      null,
      null,
      Guid.NewGuid(),
      "Dispatcher",
      DateTime.UtcNow,
      false,
      null,
      null,
      null
    );
}
