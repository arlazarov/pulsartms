using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using AngleSharp.Dom;
using Bunit;
using Client.Pages.Dispatch;
using Client.Services;
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
          component.Find(".dispatch-table__status").TextContent
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

    // Completed loads are read in the Table alone (the owner, September
    // 27): it is the view drawn, and no live planning is asked for.
    Assert.Equal("true", View(component, "Table").GetAttribute("aria-pressed"));
    var query = requests.Last(uri => uri.AbsolutePath == "/api/dispatch").Query;
    Assert.Contains("status=completed", query);
    Assert.Contains("search=Historic", query);
    Assert.Empty(
      component.FindAll(".dispatch-planning, .driver-hours, .fuel-recalculate")
    );
    Assert.DoesNotContain(
      requests,
      uri =>
        uri.AbsolutePath.Contains("/planning", StringComparison.Ordinal)
        && !uri.AbsolutePath.EndsWith("/previews", StringComparison.Ordinal)
    );
    // Papers are for active loads: choosing them reads Active again, and
    // the scope is no longer there to reach.
    await View(component, "Papers").ClickAsync(new MouseEventArgs());
    Assert.Equal(
      "true",
      component.Find("#dispatch-active").GetAttribute("aria-pressed")
    );
    Assert.Contains(
      "is-unavailable",
      component.Find(".dispatch-board__scope").ClassName
    );
    Assert.Contains(
      "search=Historic",
      requests.Last(uri => uri.AbsolutePath == "/api/dispatch/board").Query
    );
    Assert.Empty(component.FindAll(".dispatch-load__phase"));
  }

  // A search from Cards or Papers finds a load by its displayed number and
  // also finds completed ones, listed apart and labelled, while the view
  // stays Active; clearing the search reads Active alone again (the owner,
  // September 27).
  [Theory]
  [InlineData("Cards")]
  [InlineData("Papers")]
  public async Task ASearchFromCardsOrPapersAlsoFindsCompletedLoads(string view)
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
            ? Archive(1, 1408)
            : Auxiliary(request.RequestUri)
        );
      }
    );
    var component = context.Render<DispatchList>();
    component.WaitForAssertion(
      () =>
        Assert.Contains(
          requests,
          uri => uri.AbsolutePath == "/api/dispatch/board"
        )
    );
    if (view == "Papers")
      await View(component, "Papers").ClickAsync(new MouseEventArgs());
    Assert.DoesNotContain(requests, uri => uri.AbsolutePath == "/api/dispatch");

    await Search(component, clock, "AMF1408");
    component.WaitForAssertion(
      () => Assert.NotNull(component.Find(".dispatch-history"))
    );

    var history = requests
      .Last(uri => uri.AbsolutePath == "/api/dispatch")
      .Query;
    Assert.Contains("status=completed", history);
    Assert.Contains("search=AMF1408", history);
    Assert.Contains("pageSize=12", history);
    Assert.Contains(
      "search=AMF1408",
      requests.Last(uri => uri.AbsolutePath == "/api/dispatch/board").Query
    );
    Assert.Equal(
      "Completed",
      component.Find(".dispatch-history__tag").TextContent
    );
    Assert.StartsWith(
      "/dispatch/",
      component.Find("a.dispatch-history__open").GetAttribute("href")
    );
    Assert.Equal("true", View(component, view).GetAttribute("aria-pressed"));
    Assert.Equal(
      "true",
      component.Find("#dispatch-active").GetAttribute("aria-pressed")
    );

    var asked = requests.Count(uri => uri.AbsolutePath == "/api/dispatch");
    await Search(component, clock, "");
    component.WaitForAssertion(
      () => Assert.Empty(component.FindAll(".dispatch-history"))
    );
    Assert.Equal(
      asked,
      requests.Count(uri => uri.AbsolutePath == "/api/dispatch")
    );
  }

  // A history search that fails says so, with a retry, and is never shown
  // as "no completed loads" (root's review, September 27).
  [Fact]
  public async Task AFailedHistorySearchOffersARetryNotAnEmptyAnswer()
  {
    var fail = true;
    var clock = new FakeTimeProvider(
      new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero)
    );
    using var context = Context(
      clock,
      (request, _) =>
        Task.FromResult(
          request.RequestUri!.AbsolutePath != "/api/dispatch"
            ? Auxiliary(request.RequestUri)
          : fail ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
          : Archive(1, 1408)
        )
    );
    var component = context.Render<DispatchList>();
    component.WaitForAssertion(
      () => Assert.NotNull(component.Find("#dispatch-search"))
    );

    await Search(component, clock, "AMF1408");
    component.WaitForAssertion(
      () => Assert.NotNull(component.Find(".dispatch-history__error"))
    );
    Assert.Empty(component.FindAll(".dispatch-history__open"));

    fail = false;
    await component.InvokeAsync(
      () =>
        component
          .Find(".dispatch-history__error button")
          .ClickAsync(new MouseEventArgs())
    );
    component.WaitForAssertion(
      () => Assert.NotNull(component.Find(".dispatch-history__open"))
    );
    Assert.Empty(component.FindAll(".dispatch-history__error"));
  }

  // An answer for a search the dispatcher has since cleared or replaced is
  // dropped: history held back for AMF1408 does not appear after the search
  // is cleared, nor over the answer for AMF1409.
  [Fact]
  public async Task ALateHistoryAnswerForAnOlderSearchIsDropped()
  {
    var held = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var clock = new FakeTimeProvider(
      new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero)
    );
    using var context = Context(
      clock,
      (request, _) =>
        request.RequestUri!.AbsolutePath != "/api/dispatch"
          ? Task.FromResult(Auxiliary(request.RequestUri))
        : request.RequestUri.Query.Contains("AMF1408") ? held.Task
        : Task.FromResult(Archive(1, 1409))
    );
    var component = context.Render<DispatchList>();
    component.WaitForAssertion(
      () => Assert.NotNull(component.Find("#dispatch-search"))
    );

    await Search(component, clock, "AMF1408");
    await Search(component, clock, "");
    held.SetResult(Archive(1, 1408));
    await component.InvokeAsync(async () => await Task.Yield());
    Assert.Empty(component.FindAll(".dispatch-history"));

    var late = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    held = late;
    await Search(component, clock, "AMF1408");
    await Search(component, clock, "AMF1409");
    component.WaitForAssertion(
      () => Assert.NotNull(component.Find(".dispatch-history__open"))
    );
    late.SetResult(Archive(1, 1408));
    await component.InvokeAsync(async () => await Task.Yield());
    Assert.Contains(
      "1409",
      Assert.Single(component.FindAll(".dispatch-history__open")).TextContent
    );
  }

  // Every load the search finds can be reached, a page of 12 at a time
  // (the owner, September 27): AMF10 finds 1014 on the first page and 1030
  // on the second, asked for with the same search.
  [Fact]
  public async Task HistoryPagesThroughEveryMatch()
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
          request.RequestUri!.AbsolutePath != "/api/dispatch"
            ? Auxiliary(request.RequestUri)
          : request.RequestUri.Query.Contains("page=2") ? Archive(2, 1030)
          : Archive(1, 1014)
        );
      }
    );
    var component = context.Render<DispatchList>();
    component.WaitForAssertion(
      () => Assert.NotNull(component.Find("#dispatch-search"))
    );

    await Search(component, clock, "AMF10");
    component.WaitForAssertion(
      () =>
        Assert.Contains(
          "1014",
          component.Find(".dispatch-history__open").TextContent
        )
    );
    var pages = component.Find(".dispatch-history .dispatch-page__pagination");
    Assert.Contains("Page 1 of 2", pages.TextContent);

    await component.InvokeAsync(
      () =>
        component
          .FindAll(".dispatch-history .dispatch-page__pagination button")
          .Single(button => button.TextContent == "Next")
          .ClickAsync(new MouseEventArgs())
    );
    component.WaitForAssertion(
      () =>
        Assert.Contains(
          "1030",
          component.Find(".dispatch-history__open").TextContent
        )
    );
    var second = requests
      .Last(uri => uri.AbsolutePath == "/api/dispatch")
      .Query;
    Assert.Contains("page=2", second);
    Assert.Contains("search=AMF10", second);
    Assert.Contains("status=completed", second);
    Assert.Contains(
      "Page 2 of 2",
      component.Find(".dispatch-history .dispatch-page__pagination").TextContent
    );
  }

  private static async Task Search(
    IRenderedComponent<DispatchList> component,
    FakeTimeProvider clock,
    string text
  )
  {
    var input = component
      .Find("#dispatch-search")
      .InputAsync(new ChangeEventArgs { Value = text });
    await component.InvokeAsync(
      () => clock.Advance(TimeSpan.FromMilliseconds(300))
    );
    await input;
  }

  // Completed loads are read in the Table alone, newest day first (the
  // owner, September 27); Cards read Active again.
  [Fact]
  public async Task CompletedReadsAsTheTableAndActiveKeepsTheCards()
  {
    using var context = Context(
      new FakeTimeProvider(),
      (request, _) =>
        Task.FromResult(
          request.RequestUri!.AbsolutePath == "/api/dispatch"
            ? Page(
              ("54777", 1408, "Ann"),
              ("11005", 1407, "James"),
              ("11007", 1400, "Kim"),
              ("11006", 1398, "Maksims"),
              ("54777", 1397, "Ann"),
              ("54777", 1396, "Ann"),
              ("11006", 1395, "Earlier Driver")
            )
            : Auxiliary(request.RequestUri)
        )
    );
    var component = context.Render<DispatchList>();
    await component.InvokeAsync(
      () => component.Find("#dispatch-completed").ClickAsync(new())
    );
    component.WaitForAssertion(
      () => Assert.NotEmpty(component.FindAll("tr.dispatch-table__row"))
    );

    Assert.Empty(component.FindAll("article.dispatch-truck"));
    Assert.False(View(component, "Cards").HasAttribute("disabled"));
    Assert.Equal(
      "true",
      component
        .FindAll(".dispatch-view button")
        .Single(button => button.TextContent == "Table")
        .GetAttribute("aria-pressed")
    );
    Assert.Contains(
      "Newest pickup days first",
      component.Find(".dispatch-board__count").TextContent
    );

    // Cards read Active again.
    await View(component, "Cards").ClickAsync(new MouseEventArgs());
    Assert.Equal("true", View(component, "Cards").GetAttribute("aria-pressed"));
    Assert.Equal(
      "true",
      component.Find("#dispatch-active").GetAttribute("aria-pressed")
    );
  }

  private static IElement View(
    IRenderedComponent<DispatchList> component,
    string name
  ) =>
    component
      .FindAll(".dispatch-view button")
      .Single(button => button.TextContent == name);

  // The list's place lives in its address: opened there (a return from a
  // load, browser Back or a reload), it reads that scope, search and page
  // and nothing else, and each load it shows opens with the way back to
  // exactly that address.
  [Fact]
  public void TheListReopensAtItsAddressAndLoadsCarryIt()
  {
    const string place = "/dispatch?scope=completed&q=Historic&page=2";
    var requests = new ConcurrentQueue<Uri>();
    using var context = Context(
      new FakeTimeProvider(),
      (request, _) =>
      {
        requests.Enqueue(request.RequestUri!);
        return Task.FromResult(
          request.RequestUri!.AbsolutePath == "/api/dispatch"
            ? Archive(2)
            : Auxiliary(request.RequestUri)
        );
      }
    );
    context.Services.GetRequiredService<NavigationManager>().NavigateTo(place);

    var component = context.Render<DispatchList>();

    component.WaitForAssertion(
      () => Assert.NotEmpty(component.FindAll("tr.dispatch-table__row"))
    );
    var read = Assert.Single(
      requests,
      uri =>
        uri.AbsolutePath.StartsWith("/api/dispatch", StringComparison.Ordinal)
        && uri.AbsolutePath != "/api/dispatch/previews"
        && !uri.AbsolutePath.Contains("/planning", StringComparison.Ordinal)
    );
    Assert.Equal("/api/dispatch", read.AbsolutePath);
    Assert.Contains("status=completed", read.Query);
    Assert.Contains("page=2", read.Query);
    Assert.Contains("search=Historic", read.Query);
    Assert.Equal(
      "true",
      component.Find("#dispatch-completed").GetAttribute("aria-pressed")
    );
    var loads = component
      .FindAll("tr.dispatch-table__row a")
      .Select(link => link.GetAttribute("href") ?? "")
      .Where(href => href.StartsWith("/dispatch/", StringComparison.Ordinal))
      .ToList();
    Assert.NotEmpty(loads);
    Assert.All(
      loads,
      href =>
        Assert.EndsWith(
          $"{ReturnNavigation.Parameter}={Uri.EscapeDataString(place)}",
          href
        )
    );
    Assert.Contains(
      context.ReturnPlace.Invocations,
      call => call.Identifier == "reflect" && Equals(call.Arguments[0], place)
    );
  }

  // The page's parameters can be set again with the same address (the
  // layout renders again, or the list is returned to). That reads the list
  // again where it is; it must not fall back to page 1.
  [Fact]
  public void SettingTheSameAddressAgainKeepsThePage()
  {
    const string place = "/dispatch?scope=completed&q=Historic&page=2";
    var requests = new ConcurrentQueue<Uri>();
    using var context = Context(
      new FakeTimeProvider(),
      (request, _) =>
      {
        requests.Enqueue(request.RequestUri!);
        return Task.FromResult(
          request.RequestUri!.AbsolutePath == "/api/dispatch"
            ? Archive(2)
            : Auxiliary(request.RequestUri)
        );
      }
    );
    context.Services.GetRequiredService<NavigationManager>().NavigateTo(place);
    var component = context.Render<DispatchList>();
    component.WaitForAssertion(
      () => Assert.NotEmpty(component.FindAll("tr.dispatch-table__row"))
    );

    component.Render();

    component.WaitForAssertion(
      () =>
        Assert.Equal(
          2,
          requests.Count(uri => uri.AbsolutePath == "/api/dispatch")
        )
    );
    Assert.All(
      requests.Where(uri => uri.AbsolutePath == "/api/dispatch"),
      uri => Assert.Contains("page=2", uri.Query)
    );
  }

  private static HttpResponseMessage Page(
    params (string Truck, int Number, string Driver)[] loads
  )
  {
    var ids = loads
      .Select(x => x.Truck)
      .Distinct()
      .ToDictionary(x => x, _ => Guid.NewGuid());
    var items = loads
      .Select(x =>
      {
        var load = DispatchFinancialViewsTests.Load(x.Number);
        load.Status = "completed";
        load.Completed = true;
        load.TruckId = ids[x.Truck];
        load.TruckNumber = x.Truck;
        load.DriverName = x.Driver;
        return load;
      })
      .ToArray();
    return new(HttpStatusCode.OK)
    {
      Content = JsonContent.Create(
        new
        {
          success = true,
          response = new
          {
            items,
            page = 1,
            pageSize = 12,
            totalCount = items.Length,
            totalPages = 1,
            hasPreviousPage = false,
            hasNextPage = false,
          },
        }
      ),
    };
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
          component.Find(".dispatch-table__open strong").TextContent
        )
    );
    Assert.DoesNotContain(
      "1111",
      component.Find(".dispatch-table__open strong").TextContent
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
    load.Completed = true;
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
