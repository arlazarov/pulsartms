using Server.Tests.Eta;
using Server.Tests.Support;

namespace Server.Tests.Persistence;

// The forecast upsert is raw SQL; SQLite accepting it says nothing about
// PostgreSQL.
[Trait("Category", "Database")]
[Trait("Kind", "Integration")]
public sealed class EtaForecastStorePostgresTests
{
  [RequiresPostgresFact]
  public async Task KeysAreSavedWithTheRowAndOnlyALaterOneReplacesThem()
  {
    await using var fixture = await PostgresFixture.CreateAsync();
    await using var db = fixture.Connect();

    await EtaForecastStoreTests.KeysCheckAsync(db);
  }
}
