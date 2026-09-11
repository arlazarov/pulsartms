namespace Application.Features.Synchronization.Interfaces;

public interface IBackgroundOperation
{
  Task RunAsync(CancellationToken cancellationToken);
}

public interface IFleetSynchronizationOperation : IBackgroundOperation;
public interface IPlanningRefreshOperation : IBackgroundOperation;
public interface IEtaRefreshOperation : IBackgroundOperation;
public interface ITruckHistoryOperation : IBackgroundOperation;
public interface IBaseRouteOperation : IBackgroundOperation;
public interface IGmailWatchOperation : IBackgroundOperation;
