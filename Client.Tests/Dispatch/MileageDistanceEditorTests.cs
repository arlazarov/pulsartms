using System.Net;
using System.Net.Http.Json;
using AngleSharp.Dom;
using Bunit;
using Client.Models;
using Client.Models.DTO.Mileage;
using Client.Pages.Dispatch;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class MileageDistanceEditorTests
{
  private static readonly DateTimeOffset Now = new(
    2026,
    9,
    15,
    16,
    0,
    0,
    TimeSpan.Zero
  );

  [Fact]
  public async Task PlannedKilometersUseMilesContractWithoutActualEvidence()
  {
    var row = MileageComponentResponses.Row(Guid.NewGuid(), "home", true);
    MovementDistanceUpdate? saved = null;
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        Assert.Equal(HttpMethod.Put, request.Method);
        saved =
          await request.Content!.ReadFromJsonAsync<MovementDistanceUpdate>(ct);
        return MileageComponentResponses.Ok(row);
      }
    );
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
    var host = context.Render<CascadingValue<DisplayUnits>>(p =>
      p.Add(x => x.Value, new DisplayUnits(Distance: "kilometers"))
        .AddChildContent<MileageDistanceEditor>(p =>
          p.Add(x => x.Movement, row)
        )
    );
    var component = host.FindComponent<MileageDistanceEditor>();
    Fill(component, "16.093");
    await component.Find("form").SubmitAsync(EventArgs.Empty);
    Assert.NotNull(saved);
    Assert.Equal(10m, saved.Miles);
    Assert.Equal("planned", saved.Basis);
    Assert.Equal("manual-estimate", saved.Source);
    Assert.Equal(row.Revision, saved.Revision);
    Assert.Equal("Reference 42", saved.SourceReference);
    Assert.Null(saved.StartedAt);
    Assert.Null(saved.EndedAt);
    Assert.Contains("Distance (km)", component.Markup);
  }

  [Fact]
  public async Task ActualEvidenceRequiresTimesAndRetainsDraftAfterConflict()
  {
    var row = MileageComponentResponses.Row(Guid.NewGuid(), "home", true);
    var writes = 0;
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        writes++;
        return Task.FromResult(
          MileageComponentResponses.Error<MileageMovementRow>(
            HttpStatusCode.Conflict,
            "Movement changed. Reload."
          )
        );
      }
    );
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
    var component = context.Render<MileageDistanceEditor>(p =>
      p.Add(x => x.Movement, row)
    );
    Fill(component, "15");
    component.Find("select").Change("manual-distance-record");
    await component.Find("form").SubmitAsync(EventArgs.Empty);
    Assert.Equal(0, writes);
    Assert.Contains("Actual start must precede", component.Markup);
    component.Find("input[id$='-start-date']").Change("2026-09-12");
    component.Find("input[id$='-start-time']").Change("01:00 PM");
    component.Find("input[id$='-end-date']").Change("2026-09-12");
    component.Find("input[id$='-end-time']").Change("02:00 PM");
    await component.Find("form").SubmitAsync(EventArgs.Empty);
    Assert.Equal(1, writes);
    Assert.Contains("Movement changed. Reload.", component.Markup);
    Assert.Equal(
      "Reference 42",
      component.Find("input[id$='-reference']").GetAttribute("value")
    );
    Assert.False(component.Find("fieldset").HasAttribute("disabled"));
  }

  [Fact]
  public async Task ActualEvidencePreservesExactConfirmedBoundaries()
  {
    var start = Now.UtcDateTime.AddHours(-4).AddSeconds(12.345);
    var end = Now.UtcDateTime.AddHours(-2).AddSeconds(56.789);
    var row = MileageComponentResponses.Row(Guid.NewGuid(), "home", true) with
    {
      StartedAt = start,
      EndedAt = end,
    };
    MovementDistanceUpdate? saved = null;
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        saved =
          await request.Content!.ReadFromJsonAsync<MovementDistanceUpdate>(ct);
        return MileageComponentResponses.Ok(row);
      }
    );
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
    var component = context.Render<MileageDistanceEditor>(p =>
      p.Add(x => x.Movement, row)
    );
    Fill(component, "20");
    component.Find("select").Change("manual-odometer");
    Assert.True(
      component.Find("input[id$='-start-time']").HasAttribute("disabled")
    );
    await component.Find("form").SubmitAsync(EventArgs.Empty);
    Assert.NotNull(saved);
    Assert.Equal("actual", saved.Basis);
    Assert.Equal("manual-odometer", saved.Source);
    Assert.Equal(start, saved.StartedAt!.Value.UtcDateTime);
    Assert.Equal(end, saved.EndedAt!.Value.UtcDateTime);
  }

  private static void Fill(
    IRenderedComponent<MileageDistanceEditor> component,
    string value
  )
  {
    component.Find("input[id$='-value']").Change(value);
    component.Find("input[id$='-reference']").Change("Reference 42");
    component.Find("textarea").Change("Dispatcher recorded evidence.");
  }
}
