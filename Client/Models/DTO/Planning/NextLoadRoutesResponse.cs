namespace Client.Models.DTO.Planning;

public sealed record NextLoadLabels(Guid Id, IReadOnlyList<string> Names)
{
  public Guid? ExecutionLegId { get; init; }
}

public sealed record NextLoadRoutesResponse(
  string Revision,
  bool Unchanged,
  IReadOnlyList<NextLoadRoute>? Routes,
  IReadOnlyList<NextLoadLabels>? Labels = null
);
