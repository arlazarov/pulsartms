namespace Application.Features.Dispatch.Models;

public sealed record DispatchSettingsState(string LoadNumberPrefix, long Revision, DateTime? UpdatedAt);
