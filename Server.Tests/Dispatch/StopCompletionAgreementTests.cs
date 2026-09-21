using System.Reflection;
using Application.Features.Dispatch.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Models.Routing;
using StopEntity = Domain.Entities.Dispatch.DispatchStop;

namespace Server.Tests.Dispatch;

// One stop with one set of facts must report one completion state. Readers
// differ in which facts they carry, not in the rule they apply.
[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class StopCompletionAgreementTests
{
  public static TheoryData<bool, bool?, bool, bool> Facts()
  {
    var data = new TheoryData<bool, bool?, bool, bool>();
    foreach (var awaitingHandoff in new[] { false, true })
    foreach (var completionOverride in new bool?[] { null, false, true })
    foreach (var executionCompleted in new[] { false, true })
    foreach (var hasActual in new[] { false, true })
      data.Add(
        awaitingHandoff,
        completionOverride,
        executionCompleted,
        hasActual
      );
    return data;
  }

  [Theory]
  [MemberData(nameof(Facts))]
  public void EveryWorkStopReportsTheSharedCompletionRule(
    bool awaitingHandoff,
    bool? completionOverride,
    bool executionCompleted,
    bool hasActual
  )
  {
    var expected = StopCompletion.IsCompleted(
      awaitingHandoff,
      completionOverride,
      executionCompleted,
      hasActual
    );
    var actual = DateTime.UtcNow;

    var entity = new StopEntity
    {
      AwaitingHandoff = awaitingHandoff,
      CompletionOverride = completionOverride,
      ExecutionCompleted = executionCompleted,
      DepartedAt = hasActual ? actual : null,
    };
    var response = new DispatchStopResponse
    {
      AwaitingHandoff = awaitingHandoff,
      CompletionOverride = completionOverride,
      ExecutionCompleted = executionCompleted,
      DepartedAt = hasActual ? actual : null,
    };
    var work = Work(
      awaitingHandoff,
      completionOverride,
      executionCompleted,
      hasActual ? actual : null
    );

    Assert.Equal(expected, ((IWorkStopFacts)entity).IsCompleted);
    Assert.Equal(expected, ((IWorkStopFacts)response).IsCompleted);
    Assert.Equal(expected, ((IWorkStopFacts)work).IsCompleted);
  }

  [Fact]
  public void EveryWorkStopImplementationIsCoveredByTheAgreementCheck()
  {
    var covered = new[]
    {
      typeof(StopEntity),
      typeof(DispatchStopResponse),
      typeof(RouteWorkStop),
    };
    var implementations = new[]
    {
      typeof(IWorkStopFacts).Assembly,
      typeof(DispatchStopResponse).Assembly,
    }
      .SelectMany(assembly => assembly.GetTypes())
      .Where(type =>
        type is { IsClass: true, IsAbstract: false }
        && type.IsAssignableTo(typeof(IWorkStopFacts))
      )
      .ToArray();

    Assert.Equal(
      covered.Select(x => x.FullName).OrderBy(x => x),
      implementations.Select(x => x.FullName).OrderBy(x => x)
    );
  }

  [Fact]
  public void TheResponseCarriesEveryCompletionFactTheRuleReads()
  {
    var parameters = typeof(StopCompletion)
      .GetMethod(nameof(StopCompletion.IsCompleted))!
      .GetParameters()
      .Select(x => x.Name)
      .Where(x => x != "hasActual");

    foreach (var fact in parameters)
      Assert.NotNull(
        typeof(DispatchStopResponse).GetProperty(
          char.ToUpperInvariant(fact![0]) + fact[1..],
          BindingFlags.Public | BindingFlags.Instance
        )
      );
  }

  private static RouteWorkStop Work(
    bool awaitingHandoff,
    bool? completionOverride,
    bool executionCompleted,
    DateTime? departedAt
  ) =>
    new(
      Guid.NewGuid(),
      null,
      "",
      0,
      "Drop Off",
      null,
      null,
      "Empty",
      0,
      awaitingHandoff,
      executionCompleted,
      null,
      null,
      null,
      null,
      departedAt,
      null,
      completionOverride,
      0,
      "",
      "",
      "",
      "",
      "",
      "",
      null,
      null,
      null,
      null,
      ""
    );
}
