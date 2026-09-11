namespace Client.Models.DTO;

public class PaginatedListDTO<T>
{
  public List<T> Items { get; set; } = [];

  public int Page { get; set; }

  public int PageSize { get; set; }

  public int TotalCount { get; set; }

  public int TotalPages { get; set; }

  public bool HasPreviousPage { get; set; }

  public bool HasNextPage { get; set; }
}
