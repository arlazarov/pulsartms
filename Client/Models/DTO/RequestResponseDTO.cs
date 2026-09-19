using System.Net;
using System.Text.Json.Serialization;

namespace Client.Models.DTO;

public class RequestResponseDTO<T>
{
  public bool Success { get; set; }

  public T? Response { get; set; }

  public List<string> Errors { get; set; } = [];

  [JsonIgnore]
  public HttpStatusCode? HttpStatusCode { get; set; }

  public string ErrorMessage => string.Join(Environment.NewLine, Errors);
}
