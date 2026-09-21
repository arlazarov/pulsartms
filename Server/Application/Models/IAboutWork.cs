namespace Application.Models;

// A request that can say which truck's work it is about, so every line
// logged while it runs says so too.
//
// Without this a log of a busy morning reads as a list of things that
// happened to nobody: two fuel plans failing at the same second look
// identical, and the question a dispatcher actually asks - "why did 11006
// lose its stops" - cannot be answered from it. It is a marker a request
// opts into rather than something read off it by reflection, because a
// property that is found by its name is a property that can be renamed
// into silence.
public interface IAboutWork
{
  Guid? Load => null;
  Guid? Truck => null;
}
