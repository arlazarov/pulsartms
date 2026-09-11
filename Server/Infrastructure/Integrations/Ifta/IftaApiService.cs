using Application.Features.Fuel.Interfaces;

namespace Infrastructure.Integrations.Ifta;

public class IftaApiService(HttpClient httpClient) : IIftaApiService
{
  public async Task<string> GetTaxMatrixAsync(
    int year,
    int quarter,
    CancellationToken cancellationToken = default
  )
  {
    if (quarter is < 1 or > 4)
    {
      throw new ArgumentOutOfRangeException(nameof(quarter));
    }

    var url = $"https://www.iftach.org/taxmatrix/charts/{quarter}Q{year}.csv";

    return await httpClient.GetStringAsync(url, cancellationToken);
  }
}
