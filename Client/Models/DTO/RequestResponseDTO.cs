namespace Client.Models.DTO;

public class RequestResponseDTO<T>
{
  public bool Success { get; set; }

  public T? Response { get; set; }

  public List<string> Errors { get; set; } = [];

  [System.Text.Json.Serialization.JsonIgnore]
  public System.Net.HttpStatusCode? HttpStatusCode { get; set; }

  public string ErrorMessage => string.Join(Environment.NewLine, Errors);
}
