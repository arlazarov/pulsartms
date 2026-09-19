# C# style cleanup — September 13, 2026

## Scope

Local-only cleanup of maintained C# in Client, Server, both test projects and
tools. Existing unrelated working-copy changes were preserved. No deployment,
database migration, HOS calculation change or routing behavior change was made.

- Replaced redundant qualified type names with namespace imports. Used explicit
  aliases for conflicting Dispatch, DependencyInjection and test fixture names.
- Added pinned CSharpier 0.30.6 configuration: 80-column target, two spaces and LF.
  The editor configuration and an Architecture regression test share the target.
- Formatted 959 maintained C# files, excluding generated sources. Wrapped long
  standalone comments using Roslyn trivia without changing string values or XML.
- Kept indivisible identifiers and string literals intact. Print width is a target,
  not a promise that every physical line is at most 80 characters.
- Updated one JavaScript architecture assertion to accept C# argument/return
  line breaks while retaining the same cancellation guards and operation order.
- Documented the formatter and the SDK-Roslyn name simplification helper in the
  maintained development guide.

## Verification

- `dotnet csharpier --check --no-cache Client Server Client.Tests Server.Tests tools`:
  passed for all 959 files.
- `bash test.sh all`: 780 Client C#, 1,663 Server C# and 482 JavaScript tests passed.
- `dotnet build Client -warnaserror -p:UseSharedCompilation=false`: passed with
  zero warnings and errors.
- Strict isolated builds of CodeStyle and RouteMemoryProbe passed with zero
  warnings and errors using the managed artifact runner.
- `git diff --check`: passed.
- Restarted the local Client development server; `/fleet/map` returned HTTP 200.

The standalone historical `tools/LoadProbe` is outside the solution and already
had two compiler errors: a missing ReadCache import and an obsolete two-argument
GetDispatchBoardHandler construction. They were detected before simplification;
no substitute business dependencies were added to hide them. Its executable was
not run. This is not a claim that every historical diagnostic tool builds.

Real PostgreSQL execution, live provider behavior and browser visual checks were
not run for this source-style-only change. No production performance improvement
is claimed.
