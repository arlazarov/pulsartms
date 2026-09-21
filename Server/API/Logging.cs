namespace API;

public static class Logging
{
  // The server runs in a container, so its log is whatever it writes to
  // standard output and whatever the platform collects from there. In
  // development that should be readable by a person; in production it
  // should be readable by a machine, because the thing reading it is a log
  // collector and a person only ever reads it through a search box.
  //
  // Scopes are included deliberately: without them a line says a fuel plan
  // failed, and with them it says which truck, which load and which
  // request it failed for. Two drivers being planned at the same moment
  // are otherwise indistinguishable in the log.
  public static void UseContainerLogging(this WebApplicationBuilder builder)
  {
    builder.Logging.ClearProviders();
    if (builder.Environment.IsDevelopment())
    {
      builder.Logging.AddSimpleConsole(options =>
      {
        options.IncludeScopes = true;
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
      });
      return;
    }
    builder.Logging.AddJsonConsole(options =>
    {
      options.IncludeScopes = true;
      options.UseUtcTimestamp = true;
      options.TimestampFormat = "O";
    });
  }
}
