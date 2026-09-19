using System.Net;
using System.Net.Http.Json;
using AngleSharp.Dom;
using Bunit;
using Client.Models.DTO.Execution;
using Client.Pages.Dispatch;
using Client.Tests.Support;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class SwitchSourceReviewTests
{
  [Fact]
  public async Task SourceReadIsLazyAndCancelledWhenLoadChanges()
  {
    var first = Review(Guid.NewGuid(), Guid.NewGuid());
    var second = Review(Guid.NewGuid(), Guid.NewGuid());
    var pending = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var reads = 0;
    CancellationToken oldToken = default;
    using var context = new ClientComponentContext(
      (request, ct) =>
      {
        Assert.Equal(HttpMethod.Get, request.Method);
        reads++;
        if (
          request.RequestUri!.AbsolutePath.Contains(first.DispatchId.ToString())
        )
        {
          oldToken = ct;
          return pending.Task;
        }
        return Task.FromResult(MileageComponentResponses.Ok(second));
      }
    );
    var component = context.Render<SwitchSourceReview>(p =>
      p.Add(x => x.DispatchId, first.DispatchId)
        .Add(x => x.ExecutionLegId, first.ExecutionLegId)
        .Add(x => x.Reason, "Source changed")
    );
    Assert.Equal(0, reads);
    var opening = Button(component, "Review source changes").ClickAsync(new());
    component.WaitForAssertion(() => Assert.Equal(1, reads));
    component.Render(p =>
      p.Add(x => x.DispatchId, second.DispatchId)
        .Add(x => x.ExecutionLegId, second.ExecutionLegId)
    );
    Assert.True(oldToken.IsCancellationRequested);
    await Button(component, "Review source changes").ClickAsync(new());
    pending.SetResult(MileageComponentResponses.Ok(first));
    await opening;
    Assert.Contains(second.Changes[0].After.Address, component.Markup);
    Assert.DoesNotContain(first.Changes[0].After.Address, component.Markup);
    await Button(component, "Cancel review").ClickAsync(new());
    Assert.DoesNotContain("Accept shown changes", component.Markup);
    Assert.Equal(2, reads);
  }

  [Fact]
  public async Task AcceptanceUsesPreviewRevisionAndKeepsUncertainRetry()
  {
    var review = Review(Guid.NewGuid(), Guid.NewGuid());
    var writes = new List<AcceptExecutionSourceChangesRequest>();
    var accepted = false;
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        Assert.Equal(
          $"/api/dispatch/{review.DispatchId}/execution/source-review",
          request.RequestUri!.AbsolutePath
        );
        if (request.Method == HttpMethod.Get)
        {
          Assert.Contains(
            review.ExecutionLegId.ToString(),
            request.RequestUri.Query
          );
          return MileageComponentResponses.Ok(review);
        }
        Assert.Equal(HttpMethod.Post, request.Method);
        writes.Add(
          (
            await request.Content!.ReadFromJsonAsync<AcceptExecutionSourceChangesRequest>(
              ct
            )
          )!
        );
        return writes.Count == 1
          ? MileageComponentResponses.Error<ExecutionSourceApplyResult>(
            HttpStatusCode.ServiceUnavailable,
            "Unknown result"
          )
          : MileageComponentResponses.Ok(
            new ExecutionSourceApplyResult(
              review.DispatchId,
              review.ExecutionLegId,
              review.AssignmentRevision + 1
            )
          );
      }
    );
    var component = context.Render<SwitchSourceReview>(p =>
      p.Add(x => x.DispatchId, review.DispatchId)
        .Add(x => x.ExecutionLegId, review.ExecutionLegId)
        .Add(x => x.Accepted, _ => accepted = true)
    );
    await Button(component, "Review source changes").ClickAsync(new());
    Assert.Empty(writes);
    Assert.Contains("11:00 AM", component.Markup);
    Assert.Contains("02:00 PM", component.Markup);
    await Button(component, "Accept shown changes").ClickAsync(new());
    Assert.False(accepted);
    await Button(component, "Retry same acceptance").ClickAsync(new());
    Assert.True(accepted);
    Assert.Equal(2, writes.Count);
    Assert.Equal(writes[0], writes[1]);
    Assert.Equal(review.AssignmentRevision, writes[0].ExpectedRevision);
    Assert.Equal(review.SourceSignature, writes[0].SourceSignature);
    Assert.Equal(review.ExecutionLegId, writes[0].ExecutionLegId);
    Assert.NotEqual(Guid.Empty, writes[0].IdempotencyKey);
    Assert.DoesNotContain("Retry same acceptance", component.Markup);
    Assert.Contains("Source changes accepted", component.Markup);
  }

  [Fact]
  public async Task ConflictBlocksStaleAcceptanceAndBusyWriteIsSingle()
  {
    var review = Review(Guid.NewGuid(), Guid.NewGuid());
    var pending = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var writes = 0;
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        if (request.Method == HttpMethod.Get)
          return Task.FromResult(MileageComponentResponses.Ok(review));
        writes++;
        return pending.Task;
      }
    );
    var component = context.Render<SwitchSourceReview>(p =>
      p.Add(x => x.DispatchId, review.DispatchId)
        .Add(x => x.ExecutionLegId, review.ExecutionLegId)
    );
    await Button(component, "Review source changes").ClickAsync(new());
    var saving = Button(component, "Accept shown changes").ClickAsync(new());
    component.WaitForAssertion(() => Assert.Equal(1, writes));
    Assert.True(Button(component, "Cancel review").HasAttribute("disabled"));
    await Button(component, "Saving…").ClickAsync(new());
    Assert.Equal(1, writes);
    pending.SetResult(
      MileageComponentResponses.Error<ExecutionSourceApplyResult>(
        HttpStatusCode.Conflict,
        "A newer source must be reviewed."
      )
    );
    await saving;
    Assert.True(
      Button(component, "Retry same acceptance").HasAttribute("disabled")
    );
    await Button(component, "Retry same acceptance").ClickAsync(new());
    Assert.Equal(1, writes);
    await Button(component, "Cancel review").ClickAsync(new());
    Assert.Contains("Review source changes", component.Markup);
  }

  [Fact]
  public async Task UnsafePreviewCannotBeAccepted()
  {
    var review = Review(Guid.NewGuid(), Guid.NewGuid()) with
    {
      CanApply = false,
      Problems = ["Transfer boundaries changed."],
    };
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        Assert.Equal(HttpMethod.Get, request.Method);
        return Task.FromResult(MileageComponentResponses.Ok(review));
      }
    );
    var component = context.Render<SwitchSourceReview>(p =>
      p.Add(x => x.DispatchId, review.DispatchId)
        .Add(x => x.ExecutionLegId, review.ExecutionLegId)
    );
    await Button(component, "Review source changes").ClickAsync(new());
    Assert.Contains("Transfer boundaries changed", component.Markup);
    Assert.DoesNotContain("Accept shown changes", component.Markup);
  }

  private static ExecutionSourceReview Review(Guid load, Guid leg)
  {
    var before = SwitchComponentResponses.Workspace(load).Loads[0].Visits[
      0
    ] with
    {
      ScheduledDate = new(2026, 9, 15),
      ScheduledTime = new(11, 0),
    };
    return new(
      load,
      leg,
      12,
      new string('b', 64),
      true,
      [],
      [
        new(
          before.Id,
          before,
          before with
          {
            Address = $"Updated address {load}",
            ScheduledTime = new(14, 0),
          }
        ),
      ]
    );
  }

  private static IElement Button<T>(
    IRenderedComponent<T> component,
    string text
  )
    where T : Microsoft.AspNetCore.Components.IComponent =>
    component.FindAll("button").Single(x => x.TextContent.Trim() == text);
}
