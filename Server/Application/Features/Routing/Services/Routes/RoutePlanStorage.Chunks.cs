using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Domain.Entities.Dispatch;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.Routes;

public static partial class RoutePlanStorage
{
  private static readonly JsonSerializerOptions StateOptions =
    CreateStateOptions();

  public static string SerializeState(RoutePlan plan) =>
    JsonSerializer.Serialize(plan, StateOptions);

  private static JsonSerializerOptions CreateStateOptions()
  {
    var resolver = new DefaultJsonTypeInfoResolver();
    resolver.Modifiers.Add(info =>
    {
      if (info.Type != typeof(TruckRoute) && info.Type != typeof(RouteLeg))
        return;
      foreach (var property in info.Properties.Where(x => x.Name == "points"))
        property.ShouldSerialize = (_, _) => false;
    });
    return new(RoutingJson.Options) { TypeInfoResolver = resolver };
  }

  public static RoutePlan? Read(DispatchRoutePlan entity)
  {
    var plan = SavedRouteReader.Plan(entity.PlanJson);
    if (plan is null || entity.GeometryManifestJson is null)
      return plan;
    var manifest = Manifest(entity.GeometryManifestJson);
    var chunks = entity.GeometryChunks.ToDictionary(
      x => x.Key,
      x => RouteChunkPacker.Decode(x.CoordinatesJson)
    );
    Restore(plan.Route, manifest.Route, manifest.Measures, chunks);
    if (plan.ReferenceRoute is not null && manifest.Reference is not null)
      Restore(
        plan.ReferenceRoute,
        manifest.Reference,
        manifest.ReferenceMeasures!,
        chunks
      );
    else if (plan.ReferenceRoute is not null || manifest.Reference is not null)
      throw new InvalidOperationException("Route reference manifest mismatch.");
    Captures.Add(
      plan,
      new(entity.Id, entity.GeometryManifestJson, Fingerprint(plan))
    );
    return plan;
  }

  public static async Task<DispatchRoutePlan?> LoadAsync(
    IAppDbContext db,
    DispatchRoutePlan? entity,
    CancellationToken ct
  )
  {
    if (entity?.GeometryManifestJson is null)
      return entity;
    var manifest = Manifest(entity.GeometryManifestJson);
    var keys = manifest
      .Route.Concat(manifest.Reference ?? [])
      .SelectMany(x => x)
      .Select(x => x.Key)
      .Distinct()
      .ToArray();
    entity.GeometryChunks = await db
      .RouteGeometryChunks.AsNoTracking()
      .Where(x => x.RoutePlanId == entity.Id && keys.Contains(x.Key))
      .ToListAsync(ct);
    return entity;
  }

  public static async Task PrepareAsync(
    IAppDbContext db,
    DispatchRoutePlan entity,
    RoutePlan plan,
    CancellationToken ct
  )
  {
    if (plan.GeometryOmitted)
      throw new RoutePlanningException(
        "Cannot save an omitted route geometry."
      );
    PreserveMovement(entity, plan);
    var fingerprint = Fingerprint(plan);
    if (
      Captures.TryGetValue(plan, out var captured)
      && captured.Id == entity.Id
      && captured.Manifest == entity.GeometryManifestJson
      && captured.Fingerprint.AsSpan().SequenceEqual(fingerprint)
    )
    {
      SaveMovement(db, entity, plan);
      entity.PlanJson = SerializeState(plan);
      return;
    }
    var packer = new RouteChunkPacker(
      entity.GeometryChunks.Select(x => (x.Key, x.CoordinatesJson))
    );
    var manifest = new RouteChunkManifest(
      1,
      plan.Route.Legs.Select(x => packer.Pack(x.Points)).ToList(),
      plan.ReferenceRoute?.Legs.Select(x => packer.Pack(x.Points)).ToList(),
      plan.Route.Legs.Select(x => new RouteChunkMeasure(x.Miles, x.Seconds))
        .ToList(),
      plan.ReferenceRoute?.Legs.Select(x => new RouteChunkMeasure(
          x.Miles,
          x.Seconds
        ))
        .ToList()
    );
    var addedKeys = packer.Added.Keys.ToArray();
    var existingKeys =
      addedKeys.Length == 0
        ? []
        : await db
          .RouteGeometryChunks.AsNoTracking()
          .Where(x => x.RoutePlanId == entity.Id && addedKeys.Contains(x.Key))
          .Select(x => x.Key)
          .ToListAsync(ct);
    foreach (var (key, json) in packer.Added)
    {
      var chunk = new RouteGeometryChunk
      {
        CompanyId = entity.CompanyId,
        RoutePlanId = entity.Id,
        Key = key,
        CoordinatesJson = json,
      };
      if (!existingKeys.Contains(key))
        db.RouteGeometryChunks.Add(chunk);
      entity.GeometryChunks.Add(chunk);
    }
    var jsonManifest = JsonSerializer.Serialize(manifest, RoutingJson.Options);
    RecordChange(db, entity, plan, manifest, jsonManifest);
    entity.GeometryManifestJson = jsonManifest;
    SaveMovement(db, entity, plan);
    entity.PlanJson = SerializeState(plan);
    Captures.Remove(plan);
    Captures.Add(
      plan,
      new(entity.Id, entity.GeometryManifestJson, fingerprint)
    );
  }

  private static RouteChunkManifest Manifest(string json)
  {
    var manifest = JsonSerializer.Deserialize<RouteChunkManifest>(
      json,
      RoutingJson.Options
    );
    if (manifest is null || manifest.Format != 1)
      throw new InvalidOperationException("Unsupported route chunk format.");
    return manifest;
  }

  private static void Restore(
    TruckRoute route,
    List<List<RouteChunkRange>> ranges,
    List<RouteChunkMeasure> measures,
    Dictionary<string, RoutePoint[]> chunks
  )
  {
    if (route.Legs.Count != ranges.Count || measures?.Count != ranges.Count)
      throw new InvalidOperationException("Route leg manifest mismatch.");
    for (var i = 0; i < ranges.Count; i++)
    {
      if (
        route.Legs[i].Miles != measures[i].Miles
        || route.Legs[i].Seconds != measures[i].Seconds
      )
        throw new InvalidOperationException(
          "Route measures do not match their geometry."
        );
      var points = new List<RoutePoint>();
      foreach (var range in ranges[i])
      {
        if (
          !chunks.TryGetValue(range.Key, out var chunk)
          || range.Start < 0
          || range.Count <= 0
          || range.Start > chunk.Length - range.Count
        )
          throw new InvalidOperationException("Missing route chunk range.");
        for (var at = range.Start; at < range.Start + range.Count; at++)
          points.Add(chunk[at]);
      }
      route.Legs[i] = route.Legs[i] with { Points = points };
    }
  }
}
