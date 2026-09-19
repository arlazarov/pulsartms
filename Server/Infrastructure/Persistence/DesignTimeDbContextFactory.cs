using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory
  : IDesignTimeDbContextFactory<AppDbContext>
{
  public AppDbContext CreateDbContext(string[] args)
  {
    // Scaffolding must not load deployment credentials or start application jobs.
    var options = new DbContextOptionsBuilder<AppDbContext>()
      .UseNpgsql("Host=localhost;Database=pulsartms_design;Username=design")
      .Options;
    return new AppDbContext(options);
  }
}
