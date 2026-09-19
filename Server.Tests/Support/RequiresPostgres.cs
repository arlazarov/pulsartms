namespace Server.Tests.Support;

// A machine with no recorded development database skips these; a machine that
// has one and cannot reach it fails them. Absent infrastructure and broken
// infrastructure are different answers, and a silent pass would report
// neither. The skip reason names what is missing, so a suite that quietly
// stopped exercising the engine says so in its own output.
public sealed class RequiresPostgresFactAttribute : FactAttribute
{
  public RequiresPostgresFactAttribute()
  {
    if (!PostgresFixture.IsRecorded)
      Skip = RequiresPostgres.Reason;
  }
}

public sealed class RequiresPostgresTheoryAttribute : TheoryAttribute
{
  public RequiresPostgresTheoryAttribute()
  {
    if (!PostgresFixture.IsRecorded)
      Skip = RequiresPostgres.Reason;
  }
}

internal static class RequiresPostgres
{
  public const string Reason =
    "No development Postgres is recorded in "
    + "~/.local/share/pulsartms/development/databases.json, so behaviour that "
    + "only the real engine has was not exercised.";
}
