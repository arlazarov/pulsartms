namespace Client.Models.DTO;

public class ApiResponse<T>
{
  public bool Success { get; set; }
  public T? Response { get; set; }
}
