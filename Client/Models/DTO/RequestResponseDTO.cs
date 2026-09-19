namespace Client.Models.DTO;

public class RequestResponseDTO<T>
{
  public bool Success { get; set; }

  public T? Response { get; set; }

  public List<string> Errors { get; set; } = [];

  [System.Text.Json.Serialization.JsonIgnore]
  public System.Net.HttpStatusCode? HttpStatusCode { get; set; }

  [System.Text.Json.Serialization.JsonIgnore]
  public string? ETag { get; set; }

  // A 304 reply: the caller's copy tagged with the sent validator is still current and no body was read.
  [System.Text.Json.Serialization.JsonIgnore]
  public bool NotModified { get; set; }

  public string ErrorMessage => string.Join(Environment.NewLine, Errors);
}
