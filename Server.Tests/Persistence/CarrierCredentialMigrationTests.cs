using Domain.Entities;
using Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Server.Tests.Persistence;

[Trait("Category", "Database")]
[Trait("Kind", "Integration")]
public sealed class CarrierCredentialMigrationTests
{
  [RequiresPostgresFact]
  public async Task ExistingBundlesKeepTheirOwnerAndAllowAnotherCarrier()
  {
    await using var fixture = await PostgresFixture.CreateAsync();
    var db = fixture.Connect();
    await db.Database.ExecuteSqlRawAsync(
      """
      DROP TABLE "IntegrationCredentialSettings";
      CREATE TABLE "IntegrationCredentialSettings" (
        "Provider" varchar(32)
          CONSTRAINT "PK_IntegrationCredentialSettings" PRIMARY KEY,
        "ProtectedValues" varchar(65536),
        "Revision" bigint NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL
      );
      INSERT INTO "IntegrationCredentialSettings"
        ("Provider", "ProtectedValues", "Revision", "UpdatedAt")
      VALUES ('torque', 'retained-ciphertext', 7, now());
      """
    );
    var migration = new IsolateCarrierIntegrationCredentials();
    var generator = db.GetService<IMigrationsSqlGenerator>();
    var commands = generator.Generate(migration.UpOperations);
    await using (var transaction = await db.Database.BeginTransactionAsync())
    {
      foreach (var command in commands)
        await db.Database.ExecuteSqlRawAsync(command.CommandText);
      await transaction.CommitAsync();
    }
    var original = await db.IntegrationCredentialSettings.SingleAsync();
    Assert.Equal(Company.Amf, original.CompanyId);
    Assert.Equal("retained-ciphertext", original.ProtectedValues);
    Assert.Equal(7, original.Revision);
    db.IntegrationCredentialSettings.Add(
      new()
      {
        CompanyId = Guid.NewGuid(),
        Provider = "torque",
        Revision = 1,
        UpdatedAt = DateTime.UtcNow,
      }
    );
    await db.SaveChangesAsync();
    Assert.Equal(
      2,
      await db.IntegrationCredentialSettings.IgnoreQueryFilters().CountAsync()
    );
    Assert.Single(await db.IntegrationCredentialSettings.ToListAsync());
  }
}
