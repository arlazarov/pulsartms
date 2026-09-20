using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Client.Services;
using Client.Shared.Trucks.TruckCamera;
using Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Client.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Component")]
public sealed class TruckCameraComponentTests
{
  // Asking Samsara for a picture takes the better part of a minute. The
  // dialog used to stand empty for all of it, which read as a camera that
  // had failed to open. The road a minute ago answers most of what the
  // dispatcher opened this for, so it is shown while the new one is taken.
  [Fact]
  public async Task TheLastSnapshotShowsWhileTheNewOneIsBeingTaken()
  {
    var truck = Guid.NewGuid();
    var capture = Guid.NewGuid();
    var known = new DateTimeOffset(2026, 9, 20, 2, 40, 0, TimeSpan.Zero);
    var started = new TaskCompletionSource<bool>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var polls = 0;
    using var context = new BunitContext();
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    using var transport = new StubHttpMessageHandler(
      async (request, _) =>
      {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Post)
        {
          // Held open: the component is stuck with only the old picture.
          await started.Task;
          return Ok(capture);
        }
        if (path.EndsWith($"/camera/{capture}", StringComparison.Ordinal))
        {
          polls++;
          return Ok(
            new
            {
              status = "complete",
              url = "https://fixture.invalid/new.jpg",
              capturedAt = DateTimeOffset.UtcNow,
            }
          );
        }
        return Ok(
          new
          {
            status = "complete",
            url = "https://fixture.invalid/known.jpg",
            capturedAt = known,
          }
        );
      }
    );
    using var client = new HttpClient(transport)
    {
      BaseAddress = new("https://fixture.invalid/"),
    };
    context.Services.AddSingleton(client);
    context.Services.AddSingleton(new ApiService(client));
    var component = context.Render<TruckCamera>(p =>
      p.Add(x => x.TruckId, truck)
        .Add(x => x.TruckNumber, "11007")
        .Add(x => x.ShowTrigger, false)
    );

    var opening = component.InvokeAsync(component.Instance.OpenAsync);
    component.WaitForAssertion(() =>
    {
      Assert.Equal(
        "https://fixture.invalid/known.jpg",
        component.Find("dialog.truck-camera img").GetAttribute("src")
      );
      Assert.Contains(
        "last one",
        component.Find("dialog.truck-camera p[role='status']").TextContent
      );
    });
    Assert.Equal(0, polls);

    started.SetResult(true);
    await opening;
    component.WaitForAssertion(() =>
    {
      Assert.Equal(
        "https://fixture.invalid/new.jpg",
        component.Find("dialog.truck-camera img").GetAttribute("src")
      );
      // The fresh picture arrived, so nothing is being waited on.
      Assert.Empty(
        component.Find("dialog.truck-camera p[role='status']").TextContent
      );
    });
  }

  // Nothing to fall back on is the one case that must still read as empty
  // rather than as a picture that failed to load.
  [Fact]
  public async Task NoPreviousSnapshotLeavesTheDialogEmptyWithoutClaimingOne()
  {
    using var context = new BunitContext();
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    using var transport = new StubHttpMessageHandler(
      (request, _) =>
        Task.FromResult(
          request.Method == HttpMethod.Post
            ? Ok(Guid.NewGuid())
            : Ok(new { status = "pending", url = (string?)null })
        )
    );
    using var client = new HttpClient(transport)
    {
      BaseAddress = new("https://fixture.invalid/"),
    };
    context.Services.AddSingleton(client);
    context.Services.AddSingleton(new ApiService(client));
    var component = context.Render<TruckCamera>(p =>
      p.Add(x => x.TruckId, Guid.NewGuid()).Add(x => x.ShowTrigger, false)
    );
    _ = component.InvokeAsync(component.Instance.OpenAsync);
    component.WaitForAssertion(
      () => Assert.NotNull(component.Find("dialog.truck-camera"))
    );
    Assert.Empty(component.FindAll("dialog.truck-camera img"));
    Assert.Empty(
      component.Find("dialog.truck-camera p[role='status']").TextContent
    );
  }

  private static HttpResponseMessage Ok(object response) =>
    new(HttpStatusCode.OK)
    {
      Content = JsonContent.Create(new { success = true, response }),
    };
}
