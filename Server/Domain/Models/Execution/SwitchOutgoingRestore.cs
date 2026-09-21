using System.Text.Json;
using Domain.Entities.Execution;

namespace Domain.Models.Execution;

public sealed record SwitchOutgoingRestore(
  string StopsJson,
  string SourceSignature,
  string Status,
  DateTime? StartedAt,
  DateTime? CompletedAt,
  Guid? EndSwitchId,
  Guid StartVisitId,
  Guid EndVisitId,
  long RouteChoiceRevision,
  long PlannedRevision
)
{
  public string Serialize() => JsonSerializer.Serialize(this);

  public static SwitchOutgoingRestore? Read(SwitchParticipant participant)
  {
    try
    {
      return JsonSerializer.Deserialize<SwitchOutgoingRestore>(
        participant.OutgoingRestoreJson
      );
    }
    catch (JsonException)
    {
      return null;
    }
  }
}
