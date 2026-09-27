using Server.Tests.Dispatch;
using Server.Tests.Support;

namespace Server.Tests.Persistence;

// The source-ahead rules' SQL on the engine they run on (CW2, CW3).
[Trait("Category", "Database")]
[Trait("Kind", "Integration")]
public sealed class SourceAheadAuditPostgresTests
{
  [RequiresPostgresFact]
  public async Task AClosedSourceWithOpenExecutionIsReviewedOnly()
  {
    await using var postgres = await PostgresFixture.CreateAsync();
    await using var f = await SourceAheadAuditTests.Fixture.ForAsync(
      null,
      postgres.Connect()
    );
    await SourceAheadAuditTests.ClosedSourceAsync(f);
  }

  [RequiresPostgresFact]
  public async Task AnOpenSourceReviewIsReportedUntilItIsDecided()
  {
    await using var postgres = await PostgresFixture.CreateAsync();
    await using var f = await SourceAheadAuditTests.Fixture.ForAsync(
      null,
      postgres.Connect()
    );
    await SourceAheadAuditTests.SourceReviewAsync(f);
  }
}
