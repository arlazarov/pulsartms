using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Client.Models.DTO.Execution;
using Client.Pages.Dispatch;
using Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class SwitchWorkflowTests
{
  [Fact]
  public async Task CancelNeedsExplicitConfirmationAndIsSingleWhileBusy()
  {
    var operation = SwitchComponentResponses.Operation(Guid.NewGuid());
    var pending = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var writes = new List<CancelSwitchRequest>();
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        Assert.Equal(
          $"/api/execution/switches/{operation.Id}/cancel",
          request.RequestUri!.AbsolutePath
        );
        writes.Add(
          (await request.Content!.ReadFromJsonAsync<CancelSwitchRequest>(ct))!
        );
        return await pending.Task;
      }
    );
    var component = context.Render<SwitchOperationCard>(p =>
      p.Add(x => x.Operation, operation with { CanCancel = false })
    );
    Assert.DoesNotContain("Cancel planned Switch", component.Markup);
    component.Render(p => p.Add(x => x.Operation, operation));
    await Button(component, "Cancel planned Switch").ClickAsync(new());
    Assert.Empty(writes);
    var saving = Button(component, "Confirm Cancel planned Switch")
      .ClickAsync(new());
    component.WaitForAssertion(() => Assert.Single(writes));
    Assert.True(Button(component, "Saving…").HasAttribute("disabled"));
    await Button(component, "Saving…").ClickAsync(new());
    Assert.Single(writes);
    Assert.Equal(operation.Revision, writes[0].Revision);
    Assert.NotEqual(Guid.Empty, writes[0].IdempotencyKey);
    pending.SetResult(
      MileageComponentResponses.Ok(
        new SwitchResult(operation.Id, "cancelled", operation.Revision + 1)
      )
    );
    await saving;
  }

  [Fact]
  public async Task WorkspaceLoadsOnlyOnOpenAndIgnoresPreviousLoadResponse()
  {
    var first = Guid.NewGuid();
    var second = Guid.NewGuid();
    var pending = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var reads = 0;
    CancellationToken oldToken = default;
    using var context = new ClientComponentContext(
      (request, ct) =>
      {
        reads++;
        Assert.Equal(HttpMethod.Get, request.Method);
        if (request.RequestUri!.AbsolutePath.Contains(first.ToString()))
        {
          oldToken = ct;
          return pending.Task;
        }
        return Task.FromResult(
          MileageComponentResponses.Ok(
            SwitchComponentResponses.Workspace(second)
          )
        );
      }
    );
    var auth = context.AddAuthorization();
    auth.SetAuthorized("Dispatcher");
    auth.SetRoles("Dispatch");
    var component = context.Render<DispatchSwitchSection>(p =>
      p.Add(x => x.DispatchId, first)
    );
    Assert.Equal(0, reads);
    var opening = Button(component, "Manage Switch").ClickAsync(new());
    component.WaitForAssertion(() => Assert.Equal(1, reads));
    component.Render(p => p.Add(x => x.DispatchId, second));
    Assert.True(oldToken.IsCancellationRequested);
    await Button(component, "Manage Switch").ClickAsync(new());
    pending.SetResult(
      MileageComponentResponses.Ok(SwitchComponentResponses.Workspace(first))
    );
    await opening;
    Assert.Equal(2, reads);
    await Button(component, "Plan a Switch").ClickAsync(new());
    Assert.Equal(
      second,
      component
        .FindComponent<SwitchPlanEditor>()
        .Instance.Workspace.Loads.Single()
        .DispatchId
    );
  }

  [Fact]
  public async Task PreviewDoesNotSaveAndUncertainPlanRetainsExactRetry()
  {
    var load = Guid.NewGuid();
    var workspace = SwitchComponentResponses.Workspace(load);
    var source = workspace.Loads.Single();
    var plans = new List<PlanSwitchRequest>();
    PlanSwitchRequest? previewed = null;
    var saved = false;
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        Assert.Equal(HttpMethod.Post, request.Method);
        var body = (
          await request.Content!.ReadFromJsonAsync<PlanSwitchRequest>(ct)
        )!;
        if (request.RequestUri!.AbsolutePath.EndsWith("/preview"))
        {
          previewed = body;
          var change = Assert.Single(body.Loads);
          return MileageComponentResponses.Ok(
            new SwitchPreview(
              true,
              [],
              [
                new(
                  load,
                  change.Outgoing,
                  change.Incoming,
                  source.Visits.Take(1).ToArray(),
                  source.Visits.Skip(3).ToArray()
                ),
              ]
            )
          );
        }
        Assert.Equal(
          "/api/execution/switches",
          request.RequestUri.AbsolutePath
        );
        plans.Add(body);
        return plans.Count == 1
          ? MileageComponentResponses.Error<SwitchResult>(
            HttpStatusCode.ServiceUnavailable,
            "Unknown result."
          )
          : MileageComponentResponses.Ok(
            new SwitchResult(Guid.NewGuid(), "planned", 1)
          );
      }
    );
    var component = context.Render<SwitchPlanEditor>(p =>
      p.Add(x => x.DispatchId, load)
        .Add(x => x.Workspace, workspace)
        .Add(x => x.Saved, _ => saved = true)
    );
    component
      .Find("select[id$='-site-visit']")
      .Change(source.Visits[1].Id.ToString());
    component
      .Find("select[id$='-release']")
      .Change(source.Visits[1].Id.ToString());
    component
      .Find("select[id$='-receive']")
      .Change(source.Visits[2].Id.ToString());
    component
      .Find("select[id$='-in-truck']")
      .Change(workspace.Trucks[1].Id.ToString());
    await Button(component, "Preview Switch").ClickAsync(new());
    Assert.NotNull(previewed);
    Assert.Empty(plans);
    await Button(component, "Plan Switch").ClickAsync(new());
    Assert.False(saved);
    Assert.Contains("Unknown result.", component.Markup);
    Assert.True(Button(component, "Back to edit").HasAttribute("disabled"));
    await Button(component, "Retry same plan").ClickAsync(new());
    Assert.True(saved);
    Assert.Equal(2, plans.Count);
    Assert.Equal(
      JsonSerializer.Serialize(previewed),
      JsonSerializer.Serialize(plans[0])
    );
    Assert.Equal(
      JsonSerializer.Serialize(plans[0]),
      JsonSerializer.Serialize(plans[1])
    );
    Assert.Null(plans[0].PlannedAt);
    Assert.Null(plans[0].Loads[0].PlannedReceiveAt);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task DropValidatesOptionalTimeAndKeepsParticipantScopedRetry(
    bool knownTime
  )
  {
    var operation = SwitchComponentResponses.Operation(Guid.NewGuid());
    var participant = operation.Participants.Single();
    var writes = new List<SwitchParticipantAction>();
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        Assert.Equal(
          $"/api/execution/switches/{operation.Id}/participants/"
            + $"{participant.Id}/release",
          request.RequestUri!.AbsolutePath
        );
        writes.Add(
          (
            await request.Content!.ReadFromJsonAsync<SwitchParticipantAction>(
              ct
            )
          )!
        );
        return MileageComponentResponses.Error<SwitchResult>(
          HttpStatusCode.ServiceUnavailable,
          "Retry safely."
        );
      }
    );
    context.Services.AddSingleton<TimeProvider>(
      new FakeTimeProvider(
        new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero)
      )
    );
    var component = context.Render<SwitchOperationCard>(p =>
      p.Add(x => x.Operation, operation)
    );
    Assert.DoesNotContain("Record Hook", component.Markup);
    await Button(component, "Record Drop").ClickAsync(new());
    Assert.Empty(writes);
    if (knownTime)
    {
      await component.Find("input[type='date']").ChangeAsync("2026-09-12");
      await Button(component, "Confirm Drop").ClickAsync(new());
      Assert.Empty(writes);
      Assert.Contains("Enter a valid actual time", component.Markup);
      await component.Find("input[type='text']").ChangeAsync("02:00 PM");
    }
    await Button(component, "Confirm Drop").ClickAsync(new());
    await Button(component, "Confirm Drop").ClickAsync(new());
    Assert.Equal(2, writes.Count);
    Assert.Equal(writes[0], writes[1]);
    Assert.Equal(operation.Revision, writes[0].OperationRevision);
    Assert.Equal(participant.Revision, writes[0].ParticipantRevision);
    Assert.Equal(participant.OutgoingRevision, writes[0].LegRevision);
    Assert.Equal(participant.Id, writes[0].ParticipantId);
    Assert.NotEqual(Guid.Empty, writes[0].IdempotencyKey);
    if (knownTime)
    {
      var local = new DateTime(2026, 9, 12, 14, 0, 0);
      Assert.Equal(
        new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)),
        writes[0].OccurredAt
      );
    }
    else
      Assert.Null(writes[0].OccurredAt);
  }

  private static IElement Button<T>(
    IRenderedComponent<T> component,
    string text
  )
    where T : Microsoft.AspNetCore.Components.IComponent =>
    component.FindAll("button").Single(x => x.TextContent.Trim() == text);
}
