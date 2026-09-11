using System.Text.Json.Serialization;
using Client.Models.DTO;
using Client.Models.DTO.Planning;

namespace Client.Services;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
  PropertyNameCaseInsensitive = true, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(RequestResponseDTO<AutomaticPlanningResult>))]
[JsonSerializable(typeof(RequestResponseDTO<List<AutomaticPlanningResult>>))]
[JsonSerializable(typeof(RequestResponseDTO<FuelPlanEditPreview>))]
internal partial class PlanningJsonContext : JsonSerializerContext;
