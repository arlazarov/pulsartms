using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.Dispatch.Workspace;
using Client.Pages.Dispatch;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components.Web;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchStopCorrectionTests
{
  [Fact]
  public async Task LaterDriverChangeSavesExactStopWithoutOpeningTransfer()
  {
    var workspace = Workspace();
    var first = workspace.Stops[0];
    first.ExecutionLegId = Guid.NewGuid();
    first.DriverId = Guid.NewGuid();
    var next = new DispatchWorkspaceStop
    {
      Id = Guid.NewGuid(),
      Sequence = 2,
      CanCorrect = true,
      ExecutionLegId = first.ExecutionLegId,
      TruckId = first.TruckId,
      DriverId = first.DriverId,
    };
    workspace.Stops.Add(next);
    var driver = Guid.NewGuid();
    StopCorrectionRequest? written = null;
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
          return Resources(driver);
        Assert.EndsWith(
          $"/stops/{next.Id}/correction",
          request.RequestUri!.AbsolutePath
        );
        written =
          await request.Content!.ReadFromJsonAsync<StopCorrectionRequest>(ct);
        return MileageComponentResponses.Ok(workspace);
      }
    );
    var cut = context.Render<DispatchStopCorrection>(p =>
      p.Add(x => x.Workspace, workspace).Add(x => x.Stop, next)
    );
    cut.WaitForAssertion(
      () =>
        Assert.False(cut.Find("#correction-driver").HasAttribute("disabled"))
    );
    await cut.Find("#correction-driver").InputAsync("Driver");
    await cut.FindAll("[role=option]")
      .Single(x => x.TextContent.Trim() == "Driver")
      .ClickAsync(new());
    Assert.Equal("stop", cut.Find("#correction-scope").GetAttribute("value"));
    Assert.True(cut.Instance.CanSubmit);
    await cut.Find(".btn--primary").ClickAsync(new());
    Assert.NotNull(written);
    Assert.Equal("stop", written.AssignmentScope);
    Assert.True(written.ChangeDriver);
    Assert.False(written.ChangeTruck);
    Assert.Equal(driver, written.DriverId);
    Assert.Equal(first.DriverId, next.DriverId);
  }

  [Fact]
  public async Task CompletedStopIsDirectlyEditableAndCancelReleasesDraftWithoutWriting()
  {
    var calls = new List<HttpMethod>();
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        calls.Add(request.Method);
        return Task.FromResult(Resources());
      }
    );
    var workspace = Workspace();
    workspace.Load.Stops =
    [
      new()
      {
        Id = workspace.Stops[0].Id,
        ExecutionCompleted = true,
        IsCompleted = true,
      },
    ];
    var states = new List<bool>();
    var cut = context.Render<DispatchStopCorrection>(p =>
      p.Add(x => x.Workspace, workspace)
        .Add(x => x.Stop, workspace.Stops[0])
        .Add(x => x.DraftChanged, value => states.Add(value))
    );
    cut.WaitForAssertion(() => Assert.Equal(3, calls.Count));
    cut.WaitForAssertion(
      () => Assert.False(cut.Find("#correction-truck").HasAttribute("disabled"))
    );
    Assert.Equal(
      "completed",
      cut.Find("#correction-status").GetAttribute("value")
    );
    Assert.Equal(5, cut.FindAll(".stop-correction__label svg").Count);
    Assert.Empty(cut.FindAll(".btn--primary"));
    Assert.Empty(
      cut.FindAll("input[type=checkbox], #correction-reason, #correction-time")
    );
    await cut.InvokeAsync(() => cut.Find("#correction-status").Click());
    await cut.FindAll("button")
      .Single(x => x.TextContent.Trim() == "Cancel")
      .ClickAsync(new MouseEventArgs());
    Assert.Equal([true, false], states);
    Assert.All(calls, method => Assert.Equal(HttpMethod.Get, method));
    Assert.Equal(
      "completed",
      cut.Find("#correction-status").GetAttribute("value")
    );
  }

  [Fact]
  public async Task CompletionWithoutTimeSendsOpenedVersionAndStableRetryIdentity()
  {
    var writes = new List<StopCorrectionRequest>();
    var workspace = Workspace();
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
          return Resources();
        Assert.Equal(
          $"/api/dispatch/{workspace.Load.Id}/stops/{workspace.Stops[0].Id}/correction",
          request.RequestUri!.AbsolutePath
        );
        writes.Add(
          (await request.Content!.ReadFromJsonAsync<StopCorrectionRequest>(ct))!
        );
        return new HttpResponseMessage(
          writes.Count == 1
            ? HttpStatusCode.ServiceUnavailable
            : HttpStatusCode.OK
        )
        {
          Content = JsonContent.Create(
            new RequestResponseDTO<DispatchWorkspaceResponse>
            {
              Success = writes.Count > 1,
              Response = writes.Count > 1 ? workspace : null,
            }
          ),
        };
      }
    );
    var changed = 0;
    var cut = context.Render<DispatchStopCorrection>(p =>
      p.Add(x => x.Workspace, workspace)
        .Add(x => x.Stop, workspace.Stops[0])
        .Add(x => x.Changed, () => changed++)
    );
    await cut.InvokeAsync(() => cut.Find("#correction-status").Click());
    cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".btn--primary")));
    await cut.Find(".btn--primary").ClickAsync(new MouseEventArgs());
    Assert.Contains("Retry save", cut.Markup);
    Assert.True(cut.Find("fieldset").HasAttribute("disabled"));
    await cut.Find(".btn--primary").ClickAsync(new MouseEventArgs());
    Assert.Equal(2, writes.Count);
    Assert.Equal(writes[0].IdempotencyKey, writes[1].IdempotencyKey);
    Assert.Equal(7, writes[0].ExpectedRevision);
    Assert.Equal(workspace.SourceFingerprint, writes[0].SourceFingerprint);
    Assert.Null(writes[0].CompletedAt);
    Assert.False(writes[0].ChangeAssignment);
    Assert.True(string.IsNullOrEmpty(writes[0].Reason));
    Assert.Equal(1, changed);
  }

  [Fact]
  public async Task OnlyChangedResourcesAreAppliedAcrossChosenScope()
  {
    var workspace = Workspace();
    var truck = Guid.NewGuid();
    StopCorrectionRequest? write = null;
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
          return Resources(truck);
        write = await request.Content!.ReadFromJsonAsync<StopCorrectionRequest>(
          ct
        );
        return new(HttpStatusCode.OK)
        {
          Content = JsonContent.Create(
            new RequestResponseDTO<DispatchWorkspaceResponse>
            {
              Success = true,
              Response = workspace,
            }
          ),
        };
      }
    );
    var cut = context.Render<DispatchStopCorrection>(p =>
      p.Add(x => x.Workspace, workspace).Add(x => x.Stop, workspace.Stops[0])
    );
    cut.WaitForAssertion(
      () => Assert.False(cut.Find("#correction-truck").HasAttribute("disabled"))
    );
    Assert.Empty(cut.FindAll("#correction-scope"));
    await cut.Find("#correction-truck").InputAsync("11005");
    cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[role=option]")));
    await cut.Find("[role=option]").ClickAsync(new());
    cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#correction-scope")));
    cut.Find("#correction-scope").Change("all");
    Assert.Contains("Other resources stay unchanged", cut.Markup);
    await cut.Find(".btn--primary").ClickAsync(new MouseEventArgs());
    Assert.NotNull(write);
    Assert.True(write.ChangeTruck);
    Assert.False(write.ChangeTrailer);
    Assert.False(write.ChangeDriver);
    Assert.False(write.ChangeCoDriver);
    Assert.Equal("keep", write.Completion);
    Assert.Equal("all", write.AssignmentScope);
    Assert.Equal(truck, write.TruckId);
  }

  [Fact]
  public async Task PartialAssignmentRangeCannotSilentlyChangeEarlierStops()
  {
    var workspace = Workspace();
    var truck = Guid.NewGuid();
    workspace.Stops.Add(
      new()
      {
        Id = Guid.NewGuid(),
        Sequence = 2,
        CanCorrect = true,
      }
    );
    using var context = new ClientComponentContext(
      (_, _) => Task.FromResult(Resources(truck))
    );
    var cut = context.Render<DispatchStopCorrection>(p =>
      p.Add(x => x.Workspace, workspace).Add(x => x.Stop, workspace.Stops[1])
    );
    cut.WaitForAssertion(
      () => Assert.False(cut.Find("#correction-truck").HasAttribute("disabled"))
    );
    await cut.Find("#correction-truck").InputAsync("11005");
    cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[role=option]")));
    await cut.Find("[role=option]").ClickAsync(new());
    cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#correction-scope")));
    cut.Find("#correction-scope").Change("onward");
    Assert.Contains(
      "full assignment section",
      cut.Find("[role=alert]").TextContent
    );
    Assert.True(cut.Find(".btn--primary").HasAttribute("disabled"));
    cut.Find("#correction-scope").Change("all");
    Assert.Empty(cut.FindAll("[role=alert]"));
    Assert.False(cut.Find(".btn--primary").HasAttribute("disabled"));
  }

  [Fact]
  public void ResourceFailureDoesNotDisableStatusOrLoseCurrentResourceLabels()
  {
    var workspace = Workspace();
    using var context = new ClientComponentContext(
      (_, _) =>
        Task.FromResult(
          new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        )
    );
    var cut = context.Render<DispatchStopCorrection>(p =>
      p.Add(x => x.Workspace, workspace).Add(x => x.Stop, workspace.Stops[0])
    );
    cut.WaitForAssertion(
      () => Assert.Contains("Status can still be changed", cut.Markup)
    );
    Assert.Contains(
      "54777",
      cut.Find("#correction-truck").GetAttribute("value")
    );
    cut.Find("#correction-status").Click();
    Assert.Equal(
      "true",
      cut.Find("#correction-status").GetAttribute("aria-pressed")
    );
    Assert.False(cut.Find(".btn--primary").HasAttribute("disabled"));
    cut.Find("#correction-status").Click();
    Assert.Equal(
      "false",
      cut.Find("#correction-status").GetAttribute("aria-pressed")
    );
    Assert.Empty(cut.FindAll(".btn--primary"));
  }

  [Fact]
  public async Task ConfirmedTransferShowsResourcesImmediatelyAndDoesNotChangeConfirmation()
  {
    var workspace = Workspace();
    workspace.Stops[0].Transfer = new()
    {
      Kind = "drop_hook",
      Action = "drop",
      Status = "confirmed",
    };
    var trailer = Guid.NewGuid();
    StopCorrectionRequest? write = null;
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
          return Resources(trailer);
        write = await request.Content!.ReadFromJsonAsync<StopCorrectionRequest>(
          ct
        );
        return new(HttpStatusCode.OK)
        {
          Content = JsonContent.Create(
            new RequestResponseDTO<DispatchWorkspaceResponse>
            {
              Success = true,
              Response = workspace,
            }
          ),
        };
      }
    );
    var cut = context.Render<DispatchStopCorrection>(p =>
      p.Add(x => x.Workspace, workspace).Add(x => x.Stop, workspace.Stops[0])
    );
    cut.WaitForAssertion(
      () =>
        Assert.False(cut.Find("#correction-trailer").HasAttribute("disabled"))
    );
    Assert.Equal(4, cut.FindAll("select, input[role=combobox]").Count);
    Assert.Empty(cut.FindAll("#correction-status"));
    Assert.DoesNotContain("Manage transfer", cut.Markup);
    await cut.Find("#correction-trailer").InputAsync("11005");
    await cut.Find("[role=option]").ClickAsync(new());
    Assert.Equal(
      "current",
      cut.Find("#correction-scope").GetAttribute("value")
    );
    Assert.Contains("both linked Drop and Hook", cut.Markup);
    await cut.Find(".btn--primary").ClickAsync(new MouseEventArgs());
    Assert.NotNull(write);
    Assert.Equal("keep", write.Completion);
    Assert.Equal("current", write.AssignmentScope);
    Assert.True(write.ChangeTrailer);
    Assert.False(write.ChangeTruck);
    Assert.False(write.ChangeDriver);
  }

  private static HttpResponseMessage Resources(Guid? truck = null) =>
    new(HttpStatusCode.OK)
    {
      Content = JsonContent.Create(
        new
        {
          success = true,
          response = new
          {
            items = truck is { } id
              ? new[]
              {
                new
                {
                  id,
                  unitNumber = "11005",
                  name = "Driver",
                  isActive = true,
                },
              }
              : [],
            totalCount = truck.HasValue ? 1 : 0,
          },
        }
      ),
    };

  private static DispatchWorkspaceResponse Workspace() =>
    new()
    {
      Revision = 7,
      SourceFingerprint = new string('A', 64),
      Load = new() { Id = Guid.NewGuid(), Status = "completed" },
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Name = "Completed facility",
          CanCorrect = true,
          TruckId = Guid.NewGuid(),
          TruckNumber = "54777",
        },
      ],
    };
}
