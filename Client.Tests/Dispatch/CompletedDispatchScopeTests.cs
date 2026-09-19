using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Pages.Dispatch;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class CompletedDispatchScopeTests
{
  [Fact]
  public async Task CompletedSearchPaginationAndViewsKeepScopeAndNeverRequestLivePlanning()
  {
    var requests = new ConcurrentQueue<Uri>();
    var clock = new FakeTimeProvider(
      new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero)
    );
    using var context = Context(
      clock,
      (request, _) =>
      {
        requests.Enqueue(request.RequestUri!);
        return Task.FromResult(
          request.RequestUri!.AbsolutePath == "/api/dispatch"
            ? Archive(request.RequestUri.Query.Contains("page=2") ? 2 : 1)
            : Auxiliary(request.RequestUri)
        );
      }
    );
    var component = context.Render<DispatchList>();
    component.WaitForAssertion(
      () =>
        Assert.Single(
          requests,
          uri => uri.AbsolutePath == "/api/dispatch/board"
        )
    );

    await component.InvokeAsync(
      () => component.Find("#dispatch-completed").ClickAsync(new())
    );
    component.WaitForAssertion(
      () =>
        Assert.Equal(
          "Completed",
          component.Find(".dispatch-load__phase").TextContent
        )
    );
    Assert.Contains(
      "24 completed loads",
      component.Find(".dispatch-board__count").TextContent
    );
    Assert.Empty(
      component.FindAll(
        ".dispatch-truck__equipment, .dispatch-planning, .driver-hours, .fuel-recalculate"
      )
    );
    Assert.Contains("Historic Driver", component.Markup);
    Assert.DoesNotContain("Live Driver", component.Markup);
    var beforeClock = requests.Count;
    await component.InvokeAsync(() => clock.Advance(TimeSpan.FromMinutes(5)));
    await component.InvokeAsync(async () => await Task.Yield());
    Assert.Equal(beforeClock, requests.Count);

    var input = component
      .Find("#dispatch-search")
      .InputAsync(new ChangeEventArgs { Value = "Historic" });
    await component.InvokeAsync(
      () => clock.Advance(TimeSpan.FromMilliseconds(300))
    );
    await input;
    await component
      .FindAll(".dispatch-page__pagination button")
      .Single(button => button.TextContent == "Next")
      .ClickAsync(new MouseEventArgs());
    Assert.Contains(
      "page=2",
      requests.Last(uri => uri.AbsolutePath == "/api/dispatch").Query
    );
    Assert.Contains(
      "search=Historic",
      requests.Last(uri => uri.AbsolutePath == "/api/dispatch").Query
    );

    foreach (var view in new[] { "Table", "Papers", "Cards" })
    {
      await component
        .FindAll(".dispatch-view button")
        .Single(button => button.TextContent == view)
        .ClickAsync(new MouseEventArgs());
      var query = requests
        .Last(uri => uri.AbsolutePath == "/api/dispatch")
        .Query;
      Assert.Contains("status=completed", query);
      Assert.Contains("page=1", query);
      Assert.Contains("search=Historic", query);
      Assert.Empty(
        component.FindAll(
          ".dispatch-planning, .driver-hours, .fuel-recalculate"
        )
      );
      if (view == "Papers")
      {
        Assert.Single(component.FindAll(".dispatch-paper-column"));
        Assert.Contains(
          "Completed loads",
          component.Find(".dispatch-paper-column__heading").TextContent
        );
      }
    }
    Assert.DoesNotContain(
      requests,
      uri =>
        uri.AbsolutePath.Contains("/planning", StringComparison.Ordinal)
        && !uri.AbsolutePath.EndsWith("/previews", StringComparison.Ordinal)
    );
    await component.InvokeAsync(
      () => component.Find("#dispatch-active").ClickAsync(new())
    );
    Assert.Contains(
      "search=Historic",
      requests.Last(uri => uri.AbsolutePath == "/api/dispatch/board").Query
    );
    Assert.Empty(component.FindAll(".dispatch-load__phase"));
  }

  [Fact]
  public async Task LateCompletedResponseCannotReplaceNewerActiveScope()
  {
    var clock = new FakeTimeProvider();
    var started = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var pending = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    using var context = Context(
      clock,
      (request, _) =>
      {
        if (request.RequestUri!.AbsolutePath != "/api/dispatch")
          return Task.FromResult(Auxiliary(request.RequestUri));
        started.TrySetResult();
        return pending.Task;
      }
    );
    var component = context.Render<DispatchList>();
    var archive = component.InvokeAsync(
      () => component.Find("#dispatch-completed").ClickAsync(new())
    );
    await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await component.InvokeAsync(
      () => component.Find("#dispatch-active").ClickAsync(new())
    );
    pending.SetResult(Archive(1));
    await archive;
    component.WaitForAssertion(
      () =>
        Assert.StartsWith(
          "1 truck",
          component.Find(".dispatch-board__count").TextContent
        )
    );
    Assert.Empty(component.FindAll(".dispatch-load"));
    Assert.Equal(
      "true",
      component.Find("#dispatch-active").GetAttribute("aria-pressed")
    );
  }

  [Fact]
  public async Task ChangedSearchClearsArchiveAndIgnoresLateOlderPage()
  {
    var clock = new FakeTimeProvider();
    var pageStarted = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var page = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    using var context = Context(
      clock,
      (request, _) =>
      {
        var uri = request.RequestUri!;
        if (uri.AbsolutePath != "/api/dispatch")
          return Task.FromResult(Auxiliary(uri));
        if (uri.Query.Contains("page=2"))
        {
          pageStarted.TrySetResult();
          return page.Task;
        }
        return Task.FromResult(
          Archive(1, uri.Query.Contains("search=new") ? 9000 : 1375)
        );
      }
    );
    var component = context.Render<DispatchList>();
    await component.InvokeAsync(
      () => component.Find("#dispatch-completed").ClickAsync(new())
    );
    var next = component
      .FindAll(".dispatch-page__pagination button")
      .Single(button => button.TextContent == "Next")
      .ClickAsync(new MouseEventArgs());
    await pageStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var input = component
      .Find("#dispatch-search")
      .InputAsync(new ChangeEventArgs { Value = "new" });
    await component.InvokeAsync(
      () => clock.Advance(TimeSpan.FromMilliseconds(300))
    );
    await input;
    page.SetResult(Archive(2, 1111));
    await next;
    component.WaitForAssertion(
      () =>
        Assert.Contains(
          "9000",
          component.Find(".dispatch-load__number").TextContent
        )
    );
    Assert.DoesNotContain(
      "1111",
      component.Find(".dispatch-load__number").TextContent
    );
  }

  private static ClientComponentContext Context(
    FakeTimeProvider clock,
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send
  )
  {
    var context = new ClientComponentContext(send);
    context.Services.AddSingleton<TimeProvider>(clock);
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var popup = context.JSInterop.SetupModule("./js/generated/shared/popup.js");
    popup.SetupVoid("lockScroll").SetVoidResult();
    popup.SetupVoid("unlockScroll").SetVoidResult();
    return context;
  }

  private static HttpResponseMessage Archive(int page, int number = 1375)
  {
    var load = DispatchFinancialViewsTests.Load(number);
    load.Status = "completed";
    return new(HttpStatusCode.OK)
    {
      Content = JsonContent.Create(
        new
        {
          success = true,
          response = new
          {
            items = new[] { load },
            page,
            pageSize = 12,
            totalCount = 24,
            totalPages = 2,
            hasPreviousPage = page > 1,
            hasNextPage = page < 2,
          },
        }
      ),
    };
  }

  private static HttpResponseMessage Auxiliary(Uri uri) =>
    new(HttpStatusCode.OK)
    {
      Content =
        uri.AbsolutePath == "/api/dispatch/board"
          ? JsonContent.Create(
            new
            {
              success = true,
              response = new
              {
                items = Array.Empty<object>(),
                page = 1,
                totalCount = 1,
              },
            }
          )
        : uri.AbsolutePath == "/api/fleet/locations"
          ? JsonContent.Create(
            new
            {
              success = true,
              response = new
              {
                trucks = Array.Empty<object>(),
                points = Array.Empty<object>(),
              },
            }
          )
        : JsonContent.Create(
          new { success = true, response = Array.Empty<object>() }
        ),
    };
}
