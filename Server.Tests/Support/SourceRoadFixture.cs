using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Support;

internal sealed class SourceRoadFixture : IAsyncDisposable
{
  private readonly string path = Path.Combine(
    Path.GetTempPath(),
    $"pulsr-source-road-{Guid.NewGuid():N}.db"
  );
  public ManualTimeProvider Time { get; } = new();
  public DateTime Now => Time.GetUtcNow().UtcDateTime;
  public AppDbContext Db { get; private set; } = null!;
  public SourceRoadStore Store => new(Db);

  public AppDbContext Connect() =>
    new(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite($"Data Source={path};Pooling=False")
        .Options
    );

  public static async Task<SourceRoadFixture> CreateAsync()
  {
    var fixture = new SourceRoadFixture();
    fixture.Db = fixture.Connect();
    await fixture.Db.Database.EnsureCreatedAsync();
    return fixture;
  }

  public async ValueTask DisposeAsync()
  {
    await Db.DisposeAsync();
    File.Delete(path);
  }
}
