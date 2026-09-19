using Microsoft.EntityFrameworkCore.Storage;

namespace Application.Features.Routing.Interfaces;

public interface IPlanningPublicationScope
{
  // Owns a fresh transaction protecting work/history membership and stored
  // truck/fleet settings and saved roads until commit/disposal. Reads share the
  // scoped IAppDbContext. Automatic exchange-rate writes join this scope;
  // other external observations retain separate policies.
  // Null truck identity protects global changes or unresolved source membership.
  // Provider calls and calculation must finish before entering this scope.
  Task<IDbContextTransaction> BeginAsync(Guid? truckId, CancellationToken ct);
}
