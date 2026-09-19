using System.Text.Json;
using Application;
using Application.Features.Eta.Options;
using Application.Features.Routing.Commands;
using Application.Features.Routing.Options;
using Application.Features.Routing.Services.Routes;
using Application.Features.Synchronization.Options;
using Infrastructure;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

internal static class AssignmentRoutePreparation
{
  public static async Task RunAsync()
  {
    var configuration = new ConfigurationBuilder()
      .SetBasePath(Path.GetFullPath("Server/API"))
      .AddJsonFile("appsettings.json")
      .AddJsonFile("appsettings.Development.json")
      .AddUserSecrets("pulsartms-api-local")
      .AddEnvironmentVariables()
      .Build();
    var services = new ServiceCollection();
    services.AddSingleton<IConfiguration>(configuration);
    services.AddLogging();
    services.AddApplication();
    services.AddInfrastructure(configuration);
    Bind<SynchronizationOptions>("Synchronization");
    Bind<FuelRegionOptions>("FuelRegions");
    Bind<RouteRecalculationBudgetOptions>("RouteRecalculationBudget");
    Bind<RoutePreparationOptions>("RoutePreparation");
    Bind<EtaPlanningOptions>("EtaPlanning");
    await using var provider = services.BuildServiceProvider();
    await using var scope = provider.CreateAsyncScope();
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    var id = Guid.Parse("360b616c-aae1-4a9f-acf2-58f82f72eb34");
    var plans =
      scope.ServiceProvider.GetRequiredService<RoutePlanningService>();
    var load = await plans.LoadAsync(id, timeout.Token);
    if (
      load.LoadNumber != 1376
      || load.TruckId != Guid.Parse("341731b5-9440-43a3-adf1-ed7668c4d155")
      || load.PlanningAssignmentRevision != 2
      || load.Stops.Length != 2
      || load.Stops[0].Id != Guid.Parse("5dc8564b-f6b1-4dac-8f75-7ac2bc83cefa")
      || load.Stops.Any(s => s.IsCompleted)
    )
      throw new InvalidOperationException(
        "Restored assignment changed; route preparation not started."
      );
    // Resolve the normal scoped worker command without starting any hosted
    // workers or migrations.
    var result = await scope
      .ServiceProvider.GetRequiredService<ISender>()
      .Send(new PrepareDispatchPlanningCommand(id), timeout.Token);
    var plan = result.Response?.State?.Plan;
    Console.WriteLine(
      JsonSerializer.Serialize(
        new
        {
          result.Success,
          result.Response?.Message,
          dispatchId = id,
          plan?.Version,
          plan?.FromCurrentPosition,
          plan?.InputsChanged,
          stops = plan?.Stops.Select(s => new
          {
            s.Id,
            s.Sequence,
            s.Job,
          }),
          legs = plan?.Route.Legs.Count,
          points = plan?.Route.Legs.Sum(l => l.Points.Count),
          nextStop = plan?.Tracking.NextStopId,
        }
      )
    );
    if (
      !result.Success
      || result.Response?.Message is not null
      || plan is null
      || plan.InputsChanged
    )
      throw new InvalidOperationException(
        "Route preparation did not complete; inspect the planning response."
      );

    void Bind<T>(string section)
      where T : class =>
      services
        .AddOptions<T>()
        .Bind(configuration.GetSection(section))
        .ValidateDataAnnotations();
  }
}
