namespace Application.Features.Fuel.Interfaces;

public interface IIftaApiService
{
  Task<string> GetTaxMatrixAsync(
    int year,
    int quarter,
    CancellationToken cancellationToken = default
  );
}
