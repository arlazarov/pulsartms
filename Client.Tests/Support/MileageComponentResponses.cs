using System.Net;
using System.Net.Http.Json;
using Client.Models.DTO;
using Client.Models.DTO.Mileage;

namespace Client.Tests.Support;

internal static class MileageComponentResponses
{
  public static HttpResponseMessage Fleet(string path, Guid truckId) =>
    path.EndsWith("/drivers", StringComparison.Ordinal)
      ? Ok(
        new MileageFleetList<MileageDriverOption>(
          1,
          [new(Guid.NewGuid(), "Historical driver", true)]
        )
      )
      : Ok(
        new MileageFleetList<MileageUnitOption>(
          1,
          [new(truckId, "54777", true)]
        )
      );

  public static HttpResponseMessage Ok<T>(T value) =>
    new(HttpStatusCode.OK)
    {
      Content = JsonContent.Create(
        new RequestResponseDTO<T> { Success = true, Response = value }
      ),
    };

  public static HttpResponseMessage Error<T>(
    HttpStatusCode status,
    string message
  ) =>
    new(status)
    {
      Content = JsonContent.Create(
        new RequestResponseDTO<T> { Success = false, Errors = [message] }
      ),
    };

  public static MileagePolicyState Policy(
    long revision = 7,
    string home = "unallocated"
  ) => new(revision, "previous", home, "unallocated", "unallocated", null);

  public static MileageMovementRow Row(
    Guid dispatchId,
    string purpose = "pickup-approach",
    bool editable = false
  ) =>
    new(
      editable ? Guid.NewGuid() : null,
      4,
      Guid.NewGuid(),
      null,
      null,
      null,
      purpose,
      "empty",
      Guid.NewGuid(),
      dispatchId,
      null,
      dispatchId,
      "next",
      "pickup-approach",
      7,
      false,
      editable ? "recorded-movement" : "saved-planned-route",
      20,
      null,
      null,
      null,
      editable
    )
    {
      PreviousLoadNumber = 1377,
      NextLoadNumber = 1383,
      AllocatedLoadNumber = 1383,
    };

  public static DispatchMileageBreakdownState Breakdown(
    Guid id,
    MileageMovementRow row,
    decimal total = 120
  ) =>
    new(
      id,
      1383,
      new(total - 20, 20, 10, 0, total, 0),
      new(null, null, null, null, null, 1),
      [row],
      false
    );
}
