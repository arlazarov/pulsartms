using Application.Features.Costs.Services;
using Domain.Entities.Costs;

namespace Server.Tests.Costs;

// An expense records what was spent; an attribution records the part of it a
// load bears. One purchase may be divided between loads for different
// brokers, and what is not attributed must stay visible rather than implied.
[Trait("Category", "Costs")]
[Trait("Kind", "Unit")]
public sealed class ExpenseAttributionRulesTests
{
  private static readonly Guid First = Guid.NewGuid();
  private static readonly Guid Second = Guid.NewGuid();

  private static Expense Spent(decimal amount) =>
    new() { Amount = amount, Currency = "USD" };

  [Fact]
  public void AnExpenseWithNoAttributionCarriesItsWholeAmount()
  {
    Assert.Equal(400m, ExpenseAttributionRules.Unattributed(Spent(400m), []));
  }

  [Fact]
  public void OneExpenseCanBeDividedBetweenLoads()
  {
    var expense = Spent(400m);
    List<ExpenseAttribution> attributions =
    [
      new() { DispatchId = First, Amount = 250m },
      new() { DispatchId = Second, Amount = 100m },
    ];

    Assert.Null(
      ExpenseAttributionRules.Rejection(
        expense,
        [
          .. attributions.Select(x => new AttributionShare(
            x.DispatchId,
            x.Amount
          )),
        ]
      )
    );
    Assert.Equal(
      50m,
      ExpenseAttributionRules.Unattributed(expense, attributions)
    );
  }

  [Theory]
  [InlineData(401, "exceed")]
  [InlineData(-1, "negative")]
  public void AnImpossibleShareIsRejected(decimal amount, string because)
  {
    var rejection = ExpenseAttributionRules.Rejection(
      Spent(400m),
      [new(First, amount)]
    );

    Assert.NotNull(rejection);
    Assert.Contains(because, rejection, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void ALoadCannotBearTwoSharesOfOneExpense()
  {
    var rejection = ExpenseAttributionRules.Rejection(
      Spent(400m),
      [new(First, 100m), new(First, 100m)]
    );

    Assert.NotNull(rejection);
    Assert.Contains("only one share", rejection);
  }

  [Fact]
  public void AttributingTheWholeExpenseIsAllowed()
  {
    Assert.Null(
      ExpenseAttributionRules.Rejection(Spent(400m), [new(First, 400m)])
    );
  }

  [Theory]
  [InlineData(100, 1, 1)]
  [InlineData(100, 2, 1)]
  [InlineData(0.03, 1, 1)]
  public void SplitPartsAlwaysSumToTheWhole(
    decimal amount,
    decimal first,
    decimal second
  )
  {
    var shares = ExpenseAttributionRules.Split(
      amount,
      [(First, first), (Second, second)],
      2
    );

    Assert.Equal(amount, shares.Sum(x => x.Amount));
    Assert.Equal(2, shares.Count);
  }

  [Fact]
  public void AWeightOfZeroBearsNothingAndAnEmptySplitIsEmpty()
  {
    var shares = ExpenseAttributionRules.Split(
      90m,
      [(First, 2m), (Second, 0m)],
      2
    );

    Assert.Equal(90m, Assert.Single(shares).Amount);
    Assert.Equal(First, shares[0].DispatchId);
    Assert.Empty(ExpenseAttributionRules.Split(90m, [(First, 0m)], 2));
  }

  [Fact]
  public void ASplitResultIsAcceptedByTheSumInvariant()
  {
    var expense = Spent(100m);

    var shares = ExpenseAttributionRules.Split(
      expense.Amount,
      [(First, 1m), (Second, 2m)],
      2
    );

    Assert.Null(ExpenseAttributionRules.Rejection(expense, shares));
  }

  [Theory]
  [InlineData("fuel", true)]
  [InlineData("toll", true)]
  [InlineData("settlement", false)]
  public void OnlyRecordedKindsAreAccepted(string kind, bool valid)
  {
    Assert.Equal(valid, ExpenseAttributionRules.ValidKind(kind));
  }

  [Theory]
  [InlineData("loaded-miles", true)]
  [InlineData("contract", true)]
  [InlineData("purpose-policy", false)]
  public void TheBasisIsOneOfTheRecordedFour(string basis, bool valid)
  {
    Assert.Equal(valid, ExpenseAttributionRules.ValidBasis(basis));
  }
}
