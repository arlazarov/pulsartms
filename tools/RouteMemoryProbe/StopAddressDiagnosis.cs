using System.Text.Json;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Services.Addresses;
using Domain.Entities.Dispatch;
using Infrastructure.Integrations.Google.Places;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Npgsql;

internal static class StopAddressDiagnosis
{
  public static async Task RunAsync(
    NpgsqlConnection connection,
    IConfiguration configuration,
    Guid dispatchId,
    bool verify
  )
  {
    var stops = new List<(string Owner, DispatchStop Stop)>();
    await using (var transaction = await connection.BeginTransactionAsync())
    {
      await using (
        var settings = new NpgsqlCommand(
          "SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = '10s'",
          connection,
          transaction
        )
      )
        await settings.ExecuteNonQueryAsync();
      await using var command = new NpgsqlCommand(
        """
        SELECT 'source', jsonb_build_object(
          'id',s."Id",'sequence',s."Sequence",'job',s."Job",
          'address',s."Address",'city',s."City",'province',s."Province",
          'country',s."Country",'zipCode',s."ZipCode",
          'latitude',s."Latitude",'longitude',s."Longitude",
          'sourceAddressJson',s."SourceAddressJson",
          'addressVerifiedAt',s."AddressVerifiedAt",
          'addressRetryAfter',s."AddressRetryAfter")::text
        FROM "DispatchStops" s WHERE s."DispatchId"=@id
        UNION ALL
        SELECT 'leg:' || e."Status" || ':' || e."Id"::text, s::text
        FROM "LoadExecutionLegs" l
        JOIN "ExecutionLegs" e ON e."Id"=l."ExecutionLegId",
          jsonb_array_elements(e."StopsJson"::jsonb) s
        WHERE l."DispatchId"=@id AND octet_length(e."StopsJson")<1048576
        LIMIT 100
        """,
        connection,
        transaction
      );
      command.Parameters.AddWithValue("id", dispatchId);
      await using var rows = await command.ExecuteReaderAsync();
      var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
      while (await rows.ReadAsync())
        stops.Add(
          (
            rows.GetString(0),
            JsonSerializer.Deserialize<DispatchStop>(rows.GetString(1), json)!
          )
        );
      await rows.CloseAsync();
      await transaction.RollbackAsync();
    }
    using var cache = new MemoryCache(new MemoryCacheOptions());
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
    var geocoder = new GoogleAddressGeocoder(http, configuration, cache);
    foreach (var (owner, stop) in stops)
    {
      string? outcome = null;
      if (verify && owner == "source")
      {
        try
        {
          var resolved = await geocoder.ResolveAsync(
            StopLocation.Address(stop),
            default
          );
          outcome = $"Resolved: {resolved.Point}";
        }
        catch (RoutePlanningException ex)
        {
          outcome = ex.Message;
        }
      }
      Console.WriteLine(
        JsonSerializer.Serialize(
          new
          {
            owner,
            stop.Id,
            stop.Sequence,
            stop.Job,
            address = StopLocation.Address(stop),
            stop.Latitude,
            stop.Longitude,
            stop.AddressVerifiedAt,
            stop.AddressRetryAfter,
            hasProvenance = !string.IsNullOrWhiteSpace(stop.SourceAddressJson),
            reliable = StopLocation.ReliablePoint(stop, DateTime.UtcNow)
              is not null,
            outcome,
          }
        )
      );
    }
  }
}
