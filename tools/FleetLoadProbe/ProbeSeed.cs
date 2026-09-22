using System.Security.Claims;
using System.Text.Json;
using Application.Features.Dispatch.Models;
using Application.Features.Execution.Models;
using Application.Interfaces;
using Domain.Entities;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Entities.Fuel;
using Domain.Models.Routing;
using Infrastructure.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace FleetLoadProbe;

internal static class ProbeSeed
{
  public static readonly Guid CompanyId = new(
    "10000000-0000-4000-8000-000000000001"
  );
  public const string Email = "load-test@example.invalid";
  public const string Password = "Synthetic-local-100!";

  public static async Task CreateUserAsync(IServiceProvider services)
  {
    var email =
      Environment.GetEnvironmentVariable("PULSR_LOAD_USER_EMAIL")
      ?? throw new InvalidOperationException("Local email required.");
    var password =
      Environment.GetEnvironmentVariable("PULSR_LOAD_USER_PASSWORD")
      ?? throw new InvalidOperationException("Local password required.");
    await using var scope = services.CreateAsyncScope();
    var sp = scope.ServiceProvider;
    using var company = sp.GetRequiredService<ICurrentCompany>().As(CompanyId);
    var manager = sp.GetRequiredService<UserManager<AppUser>>();
    if (await manager.FindByEmailAsync(email) is not null)
      throw new InvalidOperationException(
        "Account exists; refusing to reset it."
      );
    var user = new AppUser { UserName = email, Email = email };
    var created = await manager.CreateAsync(user, password);
    if (!created.Succeeded)
      throw new InvalidOperationException("Local account creation failed.");
    var db = sp.GetRequiredService<AppDbContext>();
    db.Users.Add(
      new()
      {
        Id = Guid.NewGuid(),
        IdentityUserId = user.Id,
        Email = email,
        Name = "Local test",
        IsActive = true,
      }
    );
    await db.SaveChangesAsync();
    await sp.GetRequiredService<IUserRoleService>().SetAsync(user.Id, "Admin");
  }

  public static async Task RunAsync(IServiceProvider services, int count)
  {
    await using var scope = services.CreateAsyncScope();
    var sp = scope.ServiceProvider;
    using var company = sp.GetRequiredService<ICurrentCompany>().As(CompanyId);
    var db = sp.GetRequiredService<AppDbContext>();
    db.Companies.Add(
      new()
      {
        Id = CompanyId,
        Key = "load-fixture",
        Name = "SYNTHETIC LOAD TEST",
        CreatedAt = DateTime.UtcNow,
      }
    );
    await db.SaveChangesAsync();
    var users = sp.GetRequiredService<UserManager<AppUser>>();
    for (var index = 0; index < 6; index++)
    {
      var email = index == 0 ? Email : $"load-test-{index}@example.invalid";
      var user = new AppUser { UserName = email, Email = email };
      var result = await users.CreateAsync(user, Password);
      if (!result.Succeeded)
        throw new InvalidOperationException(
          "Synthetic identity creation failed."
        );
      if (index == 0)
        await users.AddClaimAsync(user, new Claim("amftms:role", "Admin"));
      db.Users.Add(
        new()
        {
          Id = Guid.NewGuid(),
          IdentityUserId = user.Id,
          Name = $"Load Test {index}",
          Email = email,
        }
      );
    }
    db.FleetPlanningSettings.Add(
      new()
      {
        Id = new("6f65ae4c-a62e-47cf-b84b-e1d5f89b908f"),
        Revision = 1,
        UpdatedAt = DateTime.UtcNow,
        SettingsJson = JsonSerializer.Serialize(
          new PlanningPreferences { UseIfta = false, CadToUsd = .73 }
        ),
      }
    );
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    for (var i = 0; i < count; i++)
    {
      var driver = new Driver
      {
        Id = Guid.NewGuid(),
        ExternalId = $"load-driver-{i}",
        Name = $"Test Driver {i + 1:000}",
        IsActive = true,
      };
      var truck = new Truck
      {
        Id = Guid.NewGuid(),
        ExternalId = $"load-truck-{i}",
        UnitNumber = $"TEST-{i + 1:000}",
        IsActive = true,
        Driver = driver,
        Trailer = new()
        {
          Id = Guid.NewGuid(),
          ExternalId = $"load-trailer-{i}",
          UnitNumber = $"TEST-T{i + 1:000}",
          IsActive = true,
        },
      };
      db.Trucks.Add(truck);
      for (var j = 0; j < 2; j++)
      {
        var load = new Dispatch
        {
          Id = Guid.NewGuid(),
          Truck = truck,
          Driver = driver,
          Trailer = truck.Trailer,
          TruckNumber = truck.UnitNumber,
          DriverName = driver.Name,
          TrailerNumber = truck.Trailer.UnitNumber,
          LoadNumber = 100000 + i * 2 + j,
          OrderNumber = $"SYNTHETIC-{i}-{j}",
          CustomerName = "Test Broker",
          Status = j == 0 ? "in_transit" : "assigned",
          ShipDate = today.AddDays(j * 5),
          DeliveryDate = today.AddDays(j * 5 + 4),
          Currency = "USD",
          Price = 6000,
        };
        var latitude = 40m + i * .0001m;
        var start = j == 0 ? -121m : -76m;
        var end = j == 0 ? -76.5m : -82m;
        load.Stops = new[] { start, (start + end) / 2, end }
          .Select(
            (longitude, stop) =>
              new DispatchStop
              {
                Id = Guid.NewGuid(),
                DispatchId = load.Id,
                Sequence = stop + 1,
                Job = stop == 0 ? "Pick Up" : "Drop Off",
                StateAfter = stop == 2 ? "Empty" : "Loaded",
                Name = $"Test Facility {i}-{j}-{stop}",
                Address = $"{100 + stop} Synthetic Road",
                City = "Test City",
                Province = "NE",
                Country = "US",
                ZipCode = "68001",
                Latitude = latitude,
                Longitude = longitude,
                AddressVerifiedAt = DateTime.UtcNow,
                ScheduledDate = today.AddDays(j * 5 + stop * 2),
                ScheduledTime = new(12, 0),
                AppointmentTimeZoneId = "America/Chicago",
                DriverId = driver.Id,
                DriverName = driver.Name,
                TruckId = truck.Id,
                TrailerId = truck.Trailer.Id,
                ManualCompletedAt =
                  j == 0 && stop == 0 ? DateTime.UtcNow.AddHours(-1) : null,
                ManualCompletionRevision = j == 0 && stop == 0 ? 1 : 0,
              }
          )
          .ToList();
        foreach (var stop in load.Stops)
          stop.SourceAddressJson = StopAddress.From(stop).Serialize();
        db.Dispatches.Add(load);
        db.ExecutionLegs.Add(
          new()
          {
            Id = Guid.NewGuid(),
            TruckId = truck.Id,
            DriverId = driver.Id,
            TrailerId = truck.Trailer.Id,
            Revision = 1,
            Status = j == 0 ? "active" : "planned",
            StartedAt = j == 0 ? DateTime.UtcNow.AddHours(-1) : null,
            RecordedAt = DateTime.UtcNow,
            Trip = new() { Id = Guid.NewGuid(), Name = load.OrderNumber },
            Stops = ExecutionStopRows.Capture(load.Stops),
            Loads =
            [
              new()
              {
                Id = Guid.NewGuid(),
                DispatchId = load.Id,
                StartVisitId = load.Stops[0].Id,
                EndVisitId = load.Stops[^1].Id,
              },
            ],
          }
        );
      }
    }
    for (var i = 0; i < 250; i++)
    {
      db.FuelStations.Add(
        new FuelStation
        {
          Id = Guid.NewGuid(),
          ExternalId = $"synthetic-fuel-{i}",
          Name = $"TEST Fuel {i:000}",
          Address = "Synthetic station",
          City = "Test City",
          Region = "NE",
          Country = "US",
          Latitude = 40.005m,
          Longitude = -121m + i * .21m,
          BusinessStatus = "OPERATIONAL",
          StatusCheckedAt = DateTime.UtcNow,
          FuelDiscounts =
          [
            new()
            {
              Id = Guid.NewGuid(),
              Currency = "USD",
              Product = "Diesel",
              RetailPrice = 4m,
              DiscountPrice = 3.2m + i % 7 * .04m,
              Savings = .8m - i % 7 * .04m,
              EffectiveFrom = today.AddDays(-1),
              EffectiveTo = today.AddDays(14),
            },
          ],
        }
      );
    }
    await db.SaveChangesAsync();
  }
}
