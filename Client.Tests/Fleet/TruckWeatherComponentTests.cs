using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Services;
using Client.Shared.ActionIcon;
using Client.Shared.Trucks.TruckWeather;
using Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Component")]
public sealed class TruckWeatherComponentTests
{
  [Theory]
  [InlineData("CLEAR", true, "sun")]
  [InlineData("MOSTLY_CLEAR", false, "moon")]
  [InlineData("PARTLY_CLOUDY", true, "cloud")]
  [InlineData("FOG", false, "cloud")]
  [InlineData("LIGHT_RAIN", true, "rain")]
  [InlineData("RAIN_AND_SNOW", true, "snow")]
  [InlineData("SLEET", false, "snow")]
  [InlineData("SCATTERED_THUNDERSTORMS", true, "thunder")]
  [InlineData(" thunderstorm ", false, "thunder")]
  [InlineData("UNKNOWN", true, "temperature")]
  [InlineData(null, true, "temperature")]
  public void ConditionSelectsSemanticIconWithoutChangingReadingOrRequests(
    string? condition,
    bool daytime,
    string icon
  )
  {
    using var context = new BunitContext();
    var clock = new FakeTimeProvider();
    context.Services.AddSingleton<TimeProvider>(clock);
    var calls = 0;
    using var transport = new StubHttpMessageHandler(
      (_, _) =>
      {
        calls++;
        return Task.FromResult(
          new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = JsonContent.Create(
              new
              {
                success = true,
                response = new
                {
                  celsius = 24,
                  condition,
                  description = "Fixture weather",
                  isDaytime = daytime,
                  updatedAt = clock.GetUtcNow(),
                },
              }
            ),
          }
        );
      }
    );
    using var client = new HttpClient(transport)
    {
      BaseAddress = new("https://fixture.invalid/"),
    };
    context.Services.AddSingleton(client);
    var truck = Guid.NewGuid();
    var component = context.Render<TruckWeather>(p =>
      p.Add(x => x.TruckId, truck).Add(x => x.Current, true)
    );
    component.WaitForAssertion(() =>
    {
      Assert.Equal(icon, component.FindComponent<ActionIcon>().Instance.Kind);
      var weather = component.Find(".truck-weather");
      Assert.Contains($"truck-weather--{icon}", weather.ClassList);
      Assert.Equal("Outside temperature", weather.GetAttribute("aria-label"));
      Assert.Contains(
        "Fixture weather · Google Weather",
        weather.GetAttribute("title")
      );
      Assert.Equal("Temp", weather.QuerySelector("small > span")!.TextContent);
      Assert.Equal("24 °C", weather.QuerySelector("strong")!.TextContent);
      Assert.Equal(
        "true",
        weather.QuerySelector("svg")!.GetAttribute("aria-hidden")
      );
    });
    component.Render(p =>
      p.Add(x => x.TruckId, truck).Add(x => x.Current, true)
    );
    Assert.Equal(1, calls);
  }

  [Fact]
  public async Task HiddenWeatherWaitsForVisibilityWithoutRenewingFreshReads()
  {
    using var context = new BunitContext();
    var clock = new FakeTimeProvider();
    context.Services.AddSingleton<TimeProvider>(clock);
    await using var visibility = new PageVisibility();
    using var owner = new CancellationTokenSource();
    visibility.VisibilityChanged(false);
    var calls = 0;
    using var transport = new StubHttpMessageHandler(
      (_, _) =>
      {
        calls++;
        return Task.FromResult(
          new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = JsonContent.Create(
              new
              {
                success = true,
                response = new
                {
                  celsius = 24,
                  condition = "CLEAR",
                  description = "Clear",
                  isDaytime = true,
                  updatedAt = clock.GetUtcNow(),
                },
              }
            ),
          }
        );
      }
    );
    using var client = new HttpClient(transport)
    {
      BaseAddress = new("https://fixture.invalid/"),
    };
    context.Services.AddSingleton(client);
    var component = context.Render<TruckWeather>(p =>
      p.Add(x => x.TruckId, Guid.NewGuid())
        .Add(x => x.Current, true)
        .Add(x => x.Visibility, visibility)
        .Add(x => x.OwnerCancellation, owner.Token)
    );
    await component.InvokeAsync(() => clock.Advance(TimeSpan.FromHours(1)));
    Assert.Equal(0, calls);
    await component.InvokeAsync(() => visibility.VisibilityChanged(true));
    component.WaitForAssertion(() => Assert.Equal(1, calls));
    await component.InvokeAsync(() => visibility.VisibilityChanged(false));
    await component.InvokeAsync(() => clock.Advance(TimeSpan.FromMinutes(5)));
    await component.InvokeAsync(() => visibility.VisibilityChanged(true));
    Assert.Equal(1, calls);
    await component.InvokeAsync(() => visibility.VisibilityChanged(false));
    await component.InvokeAsync(() => clock.Advance(TimeSpan.FromMinutes(6)));
    Assert.Equal(1, calls);
    await component.InvokeAsync(() => visibility.VisibilityChanged(true));
    component.WaitForAssertion(() => Assert.Equal(2, calls));

    await component.InvokeAsync(() => owner.Cancel());
    await visibility.DisposeAsync();
    await component.InvokeAsync(() => clock.Advance(TimeSpan.FromHours(1)));
    Assert.Equal(2, calls);
  }

  [Fact]
  public async Task ColdGpsRecoversPromptlyThenWeatherUsesTenMinuteCadence()
  {
    using var context = new BunitContext();
    var clock = new FakeTimeProvider();
    context.Services.AddSingleton<TimeProvider>(clock);
    var calls = 0;
    using var transport = new StubHttpMessageHandler(
      (_, _) =>
        Task.FromResult(
          new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = JsonContent.Create(
              new
              {
                success = true,
                response = ++calls == 1
                  ? null
                  : new
                  {
                    celsius = 24,
                    condition = "CLEAR",
                    description = "Clear",
                    isDaytime = true,
                    updatedAt = clock.GetUtcNow(),
                  },
              }
            ),
          }
        )
    );
    using var client = new HttpClient(transport)
    {
      BaseAddress = new Uri("https://fixture.invalid/"),
    };
    context.Services.AddSingleton(client);
    var component = context.Render<TruckWeather>(p =>
      p.Add(x => x.TruckId, Guid.NewGuid()).Add(x => x.Current, true)
    );
    Assert.Equal(1, calls);
    await component.InvokeAsync(() => clock.Advance(TimeSpan.FromSeconds(15)));
    component.WaitForAssertion(() => Assert.Contains("24", component.Markup));
    Assert.Equal(2, calls);
    await component.InvokeAsync(() => clock.Advance(TimeSpan.FromMinutes(9)));
    Assert.Equal(2, calls);
    await component.InvokeAsync(() => clock.Advance(TimeSpan.FromMinutes(1)));
    component.WaitForAssertion(() => Assert.Equal(3, calls));
  }

  [Fact]
  public async Task SelectionAndDisposalCancelPendingWeatherWithoutLateUpdates()
  {
    using var context = new BunitContext();
    var clock = new FakeTimeProvider();
    context.Services.AddSingleton<TimeProvider>(clock);
    var tokens = new List<CancellationToken>();
    var replies = new List<TaskCompletionSource<HttpResponseMessage>>();
    using var transport = new StubHttpMessageHandler(
      (_, ct) =>
      {
        tokens.Add(ct);
        var reply = new TaskCompletionSource<HttpResponseMessage>(
          TaskCreationOptions.RunContinuationsAsynchronously
        );
        replies.Add(reply);
        return reply.Task;
      }
    );
    using var client = new HttpClient(transport)
    {
      BaseAddress = new Uri("https://fixture.invalid/"),
    };
    context.Services.AddSingleton(client);
    var component = context.Render<TruckWeather>(p =>
      p.Add(x => x.TruckId, Guid.NewGuid()).Add(x => x.Current, true)
    );
    component.Render(p => p.Add(x => x.TruckId, Guid.NewGuid()));
    Assert.True(tokens[0].IsCancellationRequested);
    replies[0]
      .SetResult(
        new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = JsonContent.Create(
            new
            {
              success = true,
              response = new
              {
                celsius = 30,
                condition = "CLEAR",
                description = "Clear",
                isDaytime = true,
                updatedAt = clock.GetUtcNow(),
              },
            }
          ),
        }
      );
    await component.InvokeAsync(() => Task.CompletedTask);
    Assert.DoesNotContain("30", component.Markup);
    component.Instance.Dispose();
    Assert.True(tokens[1].IsCancellationRequested);
    replies[1].SetCanceled();
    clock.Advance(TimeSpan.FromHours(1));
    Assert.Equal(2, tokens.Count);
  }

  [Fact]
  public void HistoricalDateDoesNotFetchCurrentWeather()
  {
    using var context = new BunitContext();
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider());
    using var transport = new StubHttpMessageHandler(
      (_, _) =>
        throw new InvalidOperationException("No weather request expected.")
    );
    using var client = new HttpClient(transport);
    context.Services.AddSingleton(client);
    var component = context.Render<TruckWeather>(p =>
      p.Add(x => x.TruckId, Guid.NewGuid()).Add(x => x.Current, false)
    );
    Assert.Contains("—", component.Markup);
    Assert.Contains("Weather unavailable", component.Markup);
    Assert.Contains(
      "truck-weather--temperature",
      component.Find(".truck-weather").ClassList
    );
  }
}
