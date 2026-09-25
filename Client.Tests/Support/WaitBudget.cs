using System.Runtime.CompilerServices;
using Bunit;

namespace Client.Tests.Support;

// How long a waited-for render may take before a test fails. bUnit's one
// second was met in a quiet run and missed when the gate ran both test
// assemblies and the scripts at once: under four parallel runs eleven
// tests failed only by timing out. The assertions are unchanged; a correct
// render is simply given longer to arrive, and a wrong one still fails.
internal static class WaitBudget
{
  [ModuleInitializer]
  internal static void Apply() =>
    BunitContext.DefaultWaitTimeout = TimeSpan.FromSeconds(5);
}
