using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
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

  // A page of completed loads, newest load number first, is shown one
  // truck at a time: 54777's three loads under one heading, not three
  // headings interleaved with the other trucks. Trucks come in the order
  // of their newest load on the page; each load keeps its own driver.
  [Fact]
  public async Task CompletedCardsShowEachTruckOnceWithItsLoads()
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
      () => Assert.Equal(4, component.FindAll("article.dispatch-truck").Count)
    );

    var trucks = component.FindAll("article.dispatch-truck");
    Assert.Equal(
      ["Truck 54777", "Truck 11005", "Truck 11007", "Truck 11006"],
      trucks.Select(x => x.GetAttribute("aria-label"))
    );
    Assert.Equal(
      ["1408", "1397", "1396"],
      trucks[0]
        .QuerySelectorAll("section.dispatch-load")
        .Select(x =>
          System
            .Text.RegularExpressions.Regex.Match(
              x.GetAttribute("aria-label") ?? "",
              "\\d{4}"
            )
            .Value
        )
    );
    // One driver throughout: named once in the heading. Two drivers on
    // 11006's loads: no heading driver, each load names its own.
    Assert.Contains(
      "Ann",
      trucks[0].QuerySelector(".dispatch-truck__header")!.TextContent
    );
    Assert.Null(trucks[3].QuerySelector(".dispatch-truck__header-driver"));
    Assert.Contains("Maksims", trucks[3].TextContent);
    Assert.Contains("Earlier Driver", trucks[3].TextContent);
    Assert.Contains(
      "grouped by truck on this page",
      component.Find(".dispatch-board__count").TextContent
    );
  }

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
      () => Assert.NotEmpty(component.FindAll("section.dispatch-load"))
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
      .FindAll("section.dispatch-load a")
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
