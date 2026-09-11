namespace Application.Models;

public class ListQuery
{
  public int Page { get; set; } = 1;
  public int PageSize { get; set; } = 50;
  public string Search { get; set; } = string.Empty;
}

public class ListResult<T>
{
  public int TotalCount { get; set; }
  public List<T> Items { get; set; } = [];
}
