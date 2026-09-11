using Application.Features.Synchronization.Models;

namespace Application.Features.Synchronization.Interfaces;

public interface ISynchronizationStatusProvider
{
  SynchronizationStatus Status { get; }
}
