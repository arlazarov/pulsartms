using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;

internal static class ImportBackup
{
  public static async Task CreateAsync(string connection, string path)
  {
    if (!Path.IsPathFullyQualified(path) || File.Exists(path))
      throw new InvalidOperationException("Use a new absolute backup path.");
    var settings = new NpgsqlConnectionStringBuilder(connection);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var start = new ProcessStartInfo("pg_dump")
    {
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
    };
    start.ArgumentList.Add("--format=custom");
    start.ArgumentList.Add("--no-password");
    start.ArgumentList.Add("--file=" + path);
    start.Environment["PGHOST"] = settings.Host;
    start.Environment["PGPORT"] = settings.Port.ToString();
    start.Environment["PGDATABASE"] = settings.Database;
    start.Environment["PGUSER"] = settings.Username;
    start.Environment["PGPASSWORD"] = settings.Password;
    start.Environment["PGSSLMODE"] = settings
      .SslMode.ToString()
      .ToLowerInvariant();
    using var process = Process.Start(start)!;
    var output = process.StandardOutput.ReadToEndAsync();
    var error = process.StandardError.ReadToEndAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    try
    {
      await process.WaitForExitAsync(timeout.Token);
    }
    catch
    {
      process.Kill(entireProcessTree: true);
      throw;
    }
    await Task.WhenAll(output, error);
    if (process.ExitCode != 0 || new FileInfo(path).Length == 0)
      throw new InvalidOperationException("Backup failed.");
    if (!OperatingSystem.IsWindows())
      File.SetUnixFileMode(
        path,
        UnixFileMode.UserRead | UnixFileMode.UserWrite
      );
    await using var file = File.OpenRead(path);
    var hash = Convert.ToHexString(await SHA256.HashDataAsync(file));
    Console.WriteLine(
      JsonSerializer.Serialize(
        new
        {
          backup = path,
          bytes = file.Length,
          sha256 = hash,
        }
      )
    );
  }
}
