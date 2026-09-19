using System.Text;
using System.Text.Json;
using API.Controllers;
using Application.Features.Fuel.Commands;
using Application.Features.Fuel.Commands.ImportFuelDiscounts;
using Application.Features.Fuel.Interfaces;
using Application.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Server.Tests.Identity;

[Trait("Category", "Identity")]
public class GmailNotificationTests
{
  [Fact]
  public void ManualFuelOperationsRequireAdminButPushAuthenticatesItsOwnSender()
  {
    foreach (var name in new[] { "Import", "StartGmailWatch", "SyncIfta" })
    {
      var method = typeof(FuelController).GetMethod(name)!;
      var policy = Assert.Single(
        method
          .GetCustomAttributes(typeof(AuthorizeAttribute), true)
          .Cast<AuthorizeAttribute>()
      );
      Assert.Equal("Admin", policy.Policy);
    }
    Assert.NotEmpty(
      typeof(FuelController)
        .GetMethod("GmailNotifications")!
        .GetCustomAttributes(typeof(AllowAnonymousAttribute), true)
    );
  }

  [Theory]
  [InlineData(false, true, "fuel@example.com", "123", 503)]
  [InlineData(true, false, "fuel@example.com", "123", 401)]
  [InlineData(true, true, "other@example.com", "123", 400)]
  [InlineData(true, true, "fuel@example.com", "0", 400)]
  [InlineData(true, true, "fuel@example.com", "123", 200)]
  public async Task NotificationValidatesBeforeImport(
    bool configured,
    bool authorized,
    string email,
    string history,
    int expected
  )
  {
    var data = Convert.ToBase64String(
      Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(
          new { emailAddress = email, historyId = history }
        )
      )
    );
    await Check(
      configured,
      authorized,
      JsonSerializer.Serialize(new { message = new { data } }),
      expected
    );
  }

  [Theory]
  [InlineData("{")]
  [InlineData("{}")]
  [InlineData("{\"message\":{\"data\":\"not base64\"}}")]
  public Task MalformedNotificationsDoNotImport(string json) =>
    Check(true, true, json, 400);

  private static async Task Check(
    bool configured,
    bool authorized,
    string json,
    int expected
  )
  {
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddMediatR(options =>
      options.RegisterServicesFromAssemblyContaining<ReceiveGmailNotificationCommand>()
    );
    services.AddSingleton<IGmailPushValidator>(
      new Validator(configured, authorized)
    );
    var import = new ImportHandler();
    services.AddSingleton<
      IRequestHandler<ImportFuelDiscountsCommand, RequestResponse<int>>
    >(import);
    using var provider = services.BuildServiceProvider();
    using var body = new MemoryStream(Encoding.UTF8.GetBytes(json));
    var result = await provider
      .GetRequiredService<ISender>()
      .Send(new ReceiveGmailNotificationCommand("Bearer test", body));
    Assert.Equal(expected, result.StatusCode);
    Assert.Equal(expected == 200 ? 1 : 0, import.Calls);
  }

  private sealed class Validator(bool configured, bool authorized)
    : IGmailPushValidator
  {
    public bool IsConfigured => configured;

    public Task<bool> ValidateAsync(string authorization) =>
      Task.FromResult(authorized);

    public bool IsExpectedMailbox(string? email) => email == "fuel@example.com";
  }

  private sealed class ImportHandler
    : IRequestHandler<ImportFuelDiscountsCommand, RequestResponse<int>>
  {
    public int Calls { get; private set; }

    public Task<RequestResponse<int>> Handle(
      ImportFuelDiscountsCommand request,
      CancellationToken cancellationToken
    )
    {
      Calls++;
      return Task.FromResult(RequestResponse<int>.Ok(1));
    }
  }
}
