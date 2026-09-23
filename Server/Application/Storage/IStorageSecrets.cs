namespace Application.Storage;

// Protects a connection's secret for storage in its row, bound to the
// company and connection so a secret copied to another row cannot be read.
public interface IStorageSecrets
{
  string Protect(Guid company, Guid connection, string secret);

  // Null when the value cannot be read for this company and connection.
  string? Unprotect(Guid company, Guid connection, string protectedValue);
}
