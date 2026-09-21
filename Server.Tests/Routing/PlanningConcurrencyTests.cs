using System.Threading.Channels;
using Application.Features.Routing.Background;
using Application.Features.Routing.Commands;
using Application.Models;
using Domain.Models.Routing;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Server.Tests.Support;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class PlanningConcurrencyTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task MissingOrCompletedLoadsFinishWithoutProviderWork(
    bool exists
  )
  {
    await using var fixture = await PlanningRefreshFixture.CreateAsync();
    var id = Guid.NewGuid();
    if (exists)
    {
      fixture.Db.Dispatches.Add(new() { Id = id, Status = "completed" });
      await fixture.Db.SaveChangesAsync();
    }
    await fixture.Store.RequestAsync(
      new(id, null),
      "one",
      fixture.Now,
      default
    );
    using var cancellation = new CancellationTokenSource(
      TimeSpan.FromSeconds(5)
    );
    var running = fixture
      .Services.GetRequiredService<PlanningRefreshOperation>()
      .RunAsync(cancellation.Token);
    try
    {
      while (
        await fixture.Db.PlanningRefreshRequests.AnyAsync(
          x => x.CompletedVersion < x.RequestedVersion,
          cancellation.Token
        )
      )
        await Task.Delay(10, cancellation.Token);
    }
    finally
    {
      cancellation.Cancel();
      await running.WaitAsync(TimeSpan.FromSeconds(5));
    }
  }

  [Fact]
  public async Task SlowWorkDoesNotBlockAnotherSlotAndRepeatedDemandDoesNotDuplicateWork()
  {
    var sender = new Sender();
    await using var fixture = await PlanningRefreshFixture.CreateAsync(
      services => services.AddSingleton<ISender>(sender)
    );
    var operation =
      fixture.Services.GetRequiredService<PlanningRefreshOperation>();
    var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
    for (var i = 0; i < ids.Length; i++)
    {
      fixture.Db.Dispatches.Add(
        new()
        {
          Id = ids[i],
          Status = "assigned",
          LoadNumber = i + 1,
        }
      );
      await fixture.Store.RequestAsync(
        new(ids[i], null),
        "one",
        fixture.Now.AddSeconds(i - 3),
        default
      );
    }
    await fixture.Db.SaveChangesAsync();
    using var cancellation = new CancellationTokenSource();
    var running = operation.RunAsync(cancellation.Token);
    try
    {
      var first = await sender
        .Started.Reader.ReadAsync()
        .AsTask()
        .WaitAsync(TimeSpan.FromSeconds(5));
      var second = await sender
        .Started.Reader.ReadAsync()
        .AsTask()
        .WaitAsync(TimeSpan.FromSeconds(5));
      Assert.NotEqual(first.Id, second.Id);
      Assert.False(sender.Started.Reader.TryRead(out _));
      foreach (var id in ids)
        await fixture.Store.RequestAsync(
          new(id, null),
          "one",
          fixture.Now,
          default
        );
      Assert.All(
        await fixture.Db.PlanningRefreshRequests.ToListAsync(),
        row => Assert.Equal(1, row.RequestedVersion)
      );
      second.Done.SetResult();
      var third = await sender
        .Started.Reader.ReadAsync()
        .AsTask()
        .WaitAsync(TimeSpan.FromSeconds(5));
      Assert.Equal(ids[2], third.Id);
      Assert.False(first.Done.Task.IsCompleted);
    }
    finally
    {
      cancellation.Cancel();
      await running.WaitAsync(TimeSpan.FromSeconds(5));
    }
  }

  private sealed class Sender : ISender
  {
    public Channel<(Guid Id, TaskCompletionSource Done)> Started { get; } =
      Channel.CreateUnbounded<(Guid, TaskCompletionSource)>();

    public async Task<TResponse> Send<TResponse>(
      IRequest<TResponse> request,
      CancellationToken ct = default
    )
    {
      var command = Assert.IsType<PrepareDispatchPlanningCommand>(request);
      var done = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously
      );
      await Started.Writer.WriteAsync((command.DispatchId, done), ct);
      await done.Task.WaitAsync(ct);
      return (TResponse)
        (object)
          RequestResponse<AutomaticPlanningResult>.Ok(
            new(Guid.NewGuid(), command.DispatchId, 1, null, null)
          );
    }

    public Task Send<TRequest>(TRequest request, CancellationToken ct = default)
      where TRequest : IRequest => throw new NotSupportedException();

    public Task<object?> Send(object request, CancellationToken ct = default) =>
      throw new NotSupportedException();

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
      IStreamRequest<TResponse> request,
      CancellationToken ct = default
    ) => throw new NotSupportedException();

    public IAsyncEnumerable<object?> CreateStream(
      object request,
      CancellationToken ct = default
    ) => throw new NotSupportedException();
  }
}
