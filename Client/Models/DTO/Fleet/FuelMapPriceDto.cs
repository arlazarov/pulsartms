namespace Client.Models.DTO.Fleet;

public record FuelMapPriceDto(
  Guid Id,
  string Currency,
  decimal? CashPrice,
  decimal? IftaPrice
);
