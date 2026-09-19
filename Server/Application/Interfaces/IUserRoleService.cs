namespace Application.Interfaces;

public interface IUserRoleService
{
  Task<string?> GetAsync(string identityId, CancellationToken ct = default);
  Task<Dictionary<Guid, string>> GetAsync(
    IReadOnlyCollection<Guid> ids,
    CancellationToken ct = default
  );
  Task SetAsync(string identityId, string role, CancellationToken ct = default);
}
