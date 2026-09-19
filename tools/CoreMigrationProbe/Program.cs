using System.Text.RegularExpressions;
using CoreMigrationProbe;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

if (args.Length > 0 && !args.SequenceEqual(["--create-isolated-fixture"]))
  throw new ArgumentException("Unknown migration verification mode.");
await using var fixture = args.SequenceEqual(["--create-isolated-fixture"])
  ? await MigrationFixture.CreateAsync()
  : null;
var connection =
  fixture?.ConnectionString
  ?? Environment.GetEnvironmentVariable("PULSR_MIGRATION_TEST_CONNECTION");
var settings = new NpgsqlConnectionStringBuilder(connection ?? "");
if (
  !Regex.IsMatch(
    settings.Database ?? "",
    "^pulsr_core_fixture_[a-f0-9]{32}$",
    RegexOptions.CultureInvariant
  )
)
  throw new InvalidOperationException(
    "A dedicated fixture database is required."
  );
var options = new DbContextOptionsBuilder<AppDbContext>()
  .UseNpgsql(settings.ConnectionString)
  .Options;
await using var db = new AppDbContext(options);
var tables = await db
  .Database.SqlQueryRaw<int>(
    """
    SELECT count(*)::integer AS "Value" FROM information_schema.tables
    WHERE table_schema = 'public'
    """
  )
  .SingleAsync();
if (tables != 0)
  throw new InvalidOperationException("The fixture must initially be empty.");
await CleanTransitionProbe.VerifyAsync(db);
