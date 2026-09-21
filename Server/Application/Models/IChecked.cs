namespace Application.Models;

// A request that can say what is wrong with its own shape: an identifier
// that was never chosen, a page size nobody could want, a field longer
// than the column that holds it.
//
// Only shape. Whether the load exists, whether the truck is free, whether
// the revision is still current - those are questions about the database,
// and they belong in the handler that has the database open. The point of
// asking about shape separately is that it can be answered before any of
// that: before a transaction, before a lock, before a paid request to a
// provider.
public interface IChecked
{
  // Every reason this request is malformed, in words the dispatcher can
  // read. Nothing means the shape is fine.
  IEnumerable<string> Wrong();
}
