using Application.Features.Routing.Services.FuelPlanning;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class CompanyFuelMemoryTests
{
  [Theory]
  [InlineData("fuel-prices:same-date-profile-generation")]
  [InlineData("fuel-calendar:same-dates-generation")]
  public async Task SignaturesAreNotReusedAcrossCompanies(string key)
  {
    var companies = new TestCompany();
    using var memory = new FuelPlanMemory(companies);
    await memory.PricesAsync(key, () => Task.FromResult<string?>("A"), default);
    using (companies.As(Guid.NewGuid()))
      Assert.Equal(
        "B",
        await memory.PricesAsync(
          key,
          () => Task.FromResult<string?>("B"),
          default
        )
      );
    Assert.Equal(
      "A",
      await memory.PricesAsync(
        key,
        () => throw new InvalidOperationException(),
        default
      )
    );
  }
}
