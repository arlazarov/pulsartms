using API;
using API.Controllers;
using Application.Features.Eta.Interfaces;
using Application.Features.Fleet.Interfaces;
using Application.Features.Routing.Background;
using Application.Features.Routing.Interfaces;
using FleetLoadProbe;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;

if (args.Length != 3 || args[0] is not ("seed" or "serve" or "drop" or "user"))
  throw new ArgumentException(
    "Expected seed|serve|drop|user SCHEMA FLEET_SIZE."
  );
var count = int.Parse(args[2]);
if (count is not (10 or 50 or 100))
  throw new ArgumentException("Choose 10, 50 or 100 trucks.");
await using var database = new ProbeDatabase(args[1]);
if (args[0] == "drop")
{
  await database.DropAsync();
  return;
}
if (args[0] == "seed")
  await database.CreateAsync();
else
  await database.VerifyAsync();

var builder = WebApplication.CreateBuilder(
  new WebApplicationOptions { Args = [], EnvironmentName = "LoadProbe" }
);
builder.Configuration.Sources.Clear();
builder.Configuration.AddInMemoryCollection(
  new Dictionary<string, string?>
  {
    ["Database:ApplyMigrations"] = "false",
    ["BackgroundOperations:Enabled"] = "false",
    ["DispatchImport:Provider"] = "",
    ["Gmail:BackgroundMaintenanceEnabled"] = "false",
    ["Synchronization:Enabled"] = "true",
    ["Synchronization:PlanningConcurrency"] = "2",
    ["RouteRecalculationBudget:Enabled"] = "false",
  }
);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.UseUrls(
  Environment.GetEnvironmentVariable("PULSR_LOAD_URL")
    ?? "http://127.0.0.1:5086"
);
builder.AddApplicationServices();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddRequestLimits();
builder
  .Services.AddControllers()
  .AddApplicationPart(typeof(FleetController).Assembly);
builder.Services.RemoveAll<IHostedService>();
builder.Services.RemoveAll<DbContextOptions<AppDbContext>>();
builder.Services.RemoveAll<AppDbContext>();
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(database.Source));
builder.Services.AddSingleton(new SyntheticProviders(count));
builder.Services.AddSingleton<IRoutingProvider>(sp =>
  sp.GetRequiredService<SyntheticProviders>()
);
builder.Services.AddSingleton<IRouteAlternativesProvider>(sp =>
  sp.GetRequiredService<SyntheticProviders>()
);
builder.Services.AddSingleton<IDriverHosProvider>(sp =>
  sp.GetRequiredService<SyntheticProviders>()
);
builder.Services.AddSingleton<IHosHistoryProvider>(sp =>
  sp.GetRequiredService<SyntheticProviders>()
);
builder.Services.AddSingleton<
  IHttpMessageHandlerBuilderFilter,
  NoExternalHttp
>();
builder.Services.AddSingleton<ProbeControl>();
await using var app = builder.Build();
if (args[0] == "seed")
{
  await ProbeSeed.RunAsync(app.Services, count);
  Console.WriteLine("Synthetic fleet seeded in isolated fixture schema.");
  return;
}
if (args[0] == "user")
{
  await ProbeSeed.CreateUserAsync(app.Services);
  Console.WriteLine("Local fixture account is ready.");
  return;
}
using var databaseTiming = new DatabaseTiming();
app.UseAuthorization();
app.Use(
  async (context, next) =>
  {
    var label = context.Request.Headers["X-Probe-Measure"].ToString();
    if (
      label is "planning" or "board" or "refresh" or "fuel"
      && (
        await context
          .RequestServices.GetRequiredService<IAuthorizationService>()
          .AuthorizeAsync(context.User, null, "Admin")
      ).Succeeded
    )
    {
      using var measurement = databaseTiming.Begin(label);
      await next(context);
    }
    else
      await next(context);
  }
);
app.UseRateLimiter();
app.UseOperationalCompression();
app.MapControllers();
app.MapGet("/probe/health", () => Results.Ok()).AllowAnonymous();
app.MapProbeControls();
app.MapGet("/probe/database", () => databaseTiming.Snapshot())
  .RequireAuthorization("Admin");
await app.Services.GetRequiredService<ProbeControl>().LoadAsync();
using var workers = new CancellationTokenSource();
var planning = Task.WhenAll(
  app.Services.GetRequiredService<IPlanningRefreshOperation>()
    .RunAsync(workers.Token),
  app.Services.GetRequiredService<IPlanningSummaryOperation>()
    .RunAsync(workers.Token)
);
try
{
  await app.RunAsync();
}
finally
{
  await workers.CancelAsync();
  try
  {
    await planning;
  }
  catch (OperationCanceledException) { }
}
