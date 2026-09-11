using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Server.Tests.Identity;

[Trait("Category", "Identity")]
public class SessionPersistenceTests
{
  [Fact]
  public void ProtectedTokensSurviveServiceProviderReplacement()
  {
    using var connection = new SqliteConnection("Data Source=:memory:");
    connection.Open();
    ServiceProvider Create()
    {
      var services = new ServiceCollection();
      services.AddLogging();
      services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
      services.AddDataProtection().SetApplicationName("AMFTMS").PersistKeysToDbContext<AppDbContext>();
      return services.BuildServiceProvider();
    }
    string token;
    using (var first = Create())
    {
      using var scope = first.CreateScope();
      scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
      token = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("session-test").Protect("session");
    }
    using var second = Create();
    Assert.Equal("session", second.GetRequiredService<IDataProtectionProvider>().CreateProtector("session-test").Unprotect(token));
  }
}
