using System.Text.Json.Serialization;
using Application.Features.Auth.Queries;
using Application.Features.Dispatch.Models;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;
using Application.Features.Fleet.Queries;
using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Queries.GetFuelStations;
using Application.Features.Integrations.Models;
using Application.Features.Routing.Models;
using Application.Features.Synchronization.Models;
using Application.Features.Users.Models;
using Application.Models;

namespace API;

// Source-generated metadata for every API response shape; reflection remains the fallback
// for anything not listed. ApiJsonContextTests keeps this list complete.
[JsonSerializable(typeof(RequestResponse<object>))]
[JsonSerializable(typeof(RequestResponse<bool>))]
[JsonSerializable(typeof(RequestResponse<int>))]
[JsonSerializable(typeof(RequestResponse<Guid>))]
[JsonSerializable(typeof(RequestResponse<AutomaticPlanningResult>))]
[JsonSerializable(typeof(RequestResponse<List<AutomaticPlanningResult>>))]
[JsonSerializable(typeof(RequestResponse<CameraImage>))]
[JsonSerializable(typeof(RequestResponse<CurrentUserDto>))]
[JsonSerializable(typeof(RequestResponse<DispatchResponse>))]
[JsonSerializable(typeof(RequestResponse<List<DispatchResponse>>))]
[JsonSerializable(typeof(RequestResponse<PaginatedList<DispatchResponse>>))]
[JsonSerializable(typeof(RequestResponse<DispatchSettingsState>))]
[JsonSerializable(typeof(RequestResponse<FleetLocationsResponse>))]
[JsonSerializable(typeof(RequestResponse<FuelPlan>))]
[JsonSerializable(typeof(RequestResponse<FuelPlanEditPreview>))]
[JsonSerializable(typeof(RequestResponse<List<FuelStationDto>>))]
[JsonSerializable(typeof(RequestResponse<GmailWatchResult>))]
[JsonSerializable(typeof(RequestResponse<IReadOnlyDictionary<string, RequestTiming>>))]
[JsonSerializable(typeof(RequestResponse<IntegrationConnectionState>))]
[JsonSerializable(typeof(RequestResponse<IReadOnlyList<IntegrationConnectionState>>))]
[JsonSerializable(typeof(RequestResponse<IReadOnlyList<VehicleLocationPoint>>))]
[JsonSerializable(typeof(RequestResponse<ListResult<DriverDto>>))]
[JsonSerializable(typeof(RequestResponse<ListResult<TrailerDto>>))]
[JsonSerializable(typeof(RequestResponse<ListResult<TruckDto>>))]
[JsonSerializable(typeof(RequestResponse<NextLoadRoutesResponse>))]
[JsonSerializable(typeof(RequestResponse<PaginatedList<TruckDispatchBoardResponse>>))]
[JsonSerializable(typeof(RequestResponse<PaginatedList<UserDto>>))]
[JsonSerializable(typeof(RequestResponse<PlanningSettingsState>))]
[JsonSerializable(typeof(RequestResponse<RoutePlan>))]
[JsonSerializable(typeof(RequestResponse<RoutePlanningState>))]
[JsonSerializable(typeof(RequestResponse<SynchronizationStatus>))]
[JsonSerializable(typeof(RequestResponse<TruckRoute>))]
[JsonSerializable(typeof(RequestResponse<TruckRouteProfile>))]
[JsonSerializable(typeof(RequestResponse<UserDto>))]
// Unwrapped bodies returned by BaseController.HandleUnwrappedRequest.
[JsonSerializable(typeof(CurrentUserDto))]
[JsonSerializable(typeof(SynchronizationStatus))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, RequestTiming>))]
public partial class ApiJsonContext : JsonSerializerContext;
