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
public sealed class DispatchBoardPollingTests
{
  [Theory]
  [InlineData("Table")]
  [InlineData("Papers")]
  public async Task PollingRetainsViewParametersAndCannotOverwriteANewerView(string view)
  {
    var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
    var requests = new ConcurrentQueue<Uri>();
    var pollStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var pendingPoll = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var context = new ClientComponentContext((request, _) =>
    {
      if (request.RequestUri!.AbsolutePath != "/api/dispatch/board") return Task.FromResult(Auxiliary(request.RequestUri));
      requests.Enqueue(request.RequestUri);
      if (requests.Count == 3) { pollStarted.SetResult(); return pendingPoll.Task; }
      return Task.FromResult(Board(requests.Count));
    });
    context.Services.AddSingleton<TimeProvider>(clock);
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var component = context.Render<DispatchList>();
    component.WaitForAssertion(() => Assert.Single(requests));
    await component.FindAll(".dispatch-view button").Single(x => x.TextContent == view).ClickAsync(new MouseEventArgs());
    Assert.Equal(2, requests.Count);
    await component.InvokeAsync(() => clock.Advance(TimeSpan.FromSeconds(61)));
    await pollStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(requests.ElementAt(1).Query, requests.ElementAt(2).Query);
    Assert.Contains("includePlanned=true", requests.ElementAt(2).Query);
    await component.FindAll(".dispatch-view button").Single(x => x.TextContent == "Cards").ClickAsync(new MouseEventArgs());
    pendingPoll.SetResult(Board(99));
    component.WaitForAssertion(() => Assert.StartsWith("4 trucks", component.Find(".dispatch-board__count").TextContent));
    await component.InvokeAsync(() => clock.Advance(TimeSpan.FromSeconds(10)));
    Assert.DoesNotContain("99 trucks", component.Markup);
  }

  [Fact]
  public async Task SupersededSearchRequestIsDisposedWithoutBreakingTheDebouncedReplacement()
  {
    var clock = new FakeTimeProvider();
    var requests = new ConcurrentQueue<Uri>();
    var pendingSearch = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var context = new ClientComponentContext((request, ct) =>
    {
      if (request.RequestUri!.AbsolutePath != "/api/dispatch/board") return Task.FromResult(Auxiliary(request.RequestUri));
      requests.Enqueue(request.RequestUri);
      return requests.Count == 2 ? pendingSearch.Task.WaitAsync(ct) : Task.FromResult(Board(requests.Count));
    });
    context.Services.AddSingleton<TimeProvider>(clock);
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var component = context.Render<DispatchList>();
    component.WaitForAssertion(() => Assert.Single(requests));
    var firstInput = component.Find("#dispatch-search").InputAsync(new ChangeEventArgs { Value = "547" });
    await component.InvokeAsync(() => clock.Advance(TimeSpan.FromMilliseconds(300)));
    component.WaitForAssertion(() => Assert.Equal(2, requests.Count));
    var nextInput = component.Find("#dispatch-search").InputAsync(new ChangeEventArgs { Value = "54777" });
    await firstInput;
    await component.InvokeAsync(() => clock.Advance(TimeSpan.FromMilliseconds(300)));
    await nextInput;
    Assert.Contains("search=54777", requests.Last().Query);
    Assert.Equal(3, requests.Count);
    Assert.Empty(component.FindAll("[role=alert]"));
  }

  [Fact]
  public async Task TelemetryPollsRequestPositionsWithoutLocationHistory()
  {
    var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
    var locations = new ConcurrentQueue<Uri>();
    using var context = new ClientComponentContext((request, _) =>
    {
      if (request.RequestUri!.AbsolutePath == "/api/fleet/locations") locations.Enqueue(request.RequestUri);
      return Task.FromResult(request.RequestUri.AbsolutePath == "/api/dispatch/board" ? Board(1) : Auxiliary(request.RequestUri));
    });
    context.Services.AddSingleton<TimeProvider>(clock);
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var component = context.Render<DispatchList>();
    component.WaitForAssertion(() => Assert.Single(locations));
    await component.InvokeAsync(() => clock.Advance(TimeSpan.FromSeconds(10)));
    component.WaitForAssertion(() => Assert.Equal(2, locations.Count));
    Assert.All(locations, uri => Assert.Equal("?points=false&wait=25", uri.Query));
  }

  private static HttpResponseMessage Board(int count) => new(HttpStatusCode.OK)
  {
    Content = JsonContent.Create(new { success = true, response = new { items = Array.Empty<object>(), page = 1, totalCount = count } })
  };

  private static HttpResponseMessage Auxiliary(Uri uri) => new(HttpStatusCode.OK)
  {
    Content = uri.AbsolutePath == "/api/fleet/locations"
      ? JsonContent.Create(new { success = true, response = new { trucks = Array.Empty<object>(), points = Array.Empty<object>() } })
      : JsonContent.Create(new { success = true, response = Array.Empty<object>() })
  };
}
