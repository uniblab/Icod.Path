# Icod.Path

`Icod.Path` is a standalone .NET library for deterministic pathname normalization, physical canonicalization, and no-follow pathname-indirection inspection across POSIX and Windows path models.

The library is command-neutral. It can be consumed by utility suites, applications, services, build tools, or other libraries that need canonical-path behavior without depending on a command-line implementation.

## What the library provides

- `PathPlatformSemantics` models POSIX and Windows separators, roots, volume identity, and pathname comparison rules independently of the host operating system.
- `PathLexicalNormalizer` converts input into absolute lexical form without observing the filesystem.
- `CanonicalPathResolver` performs ordered physical resolution, missing-component handling, pathname-indirection traversal, relative-path calculation, and component-aware containment checks.
- `ICanonicalPathFileSystemProvider` separates canonical-path policy from filesystem observation and permits deterministic or synthetic providers in tests and specialized hosts.
- `IPathIndirectionInspector` and `SystemPathIndirectionInspector` characterize a terminal pathname object without silently dereferencing it.
- `CanonicalPathResult`, `RelativePathResult`, `PathContainmentResult`, and related models return structured success or failure information instead of writing diagnostics or inventing a successful path after an error.

## Canonicalization model

Lexical normalization and physical resolution are deliberately separate operations. Lexical normalization applies the selected pathname grammar without touching the filesystem. Physical resolution processes pathname components in filesystem order so supported pathname indirection is expanded before a following `..`, matching actual traversal semantics rather than merely simplifying text.

Missing components are controlled by `MissingPathComponentPolicy`:

- `RequireExisting` requires every component to exist.
- `AllowFinalComponent` permits only the final component to be absent.
- `AllowMissingSuffix` permits a missing suffix after the last existing directory.

A failed operation returns a structured `CanonicalPathFailure`. Unresolved input is never reported as a successful canonical pathname.

## Symbolic links and Windows reparse points

Windows reparse points are not treated as synonyms for symbolic links. The system inspector preserves the raw reparse tag, Microsoft and name-surrogate bits, physical attributes, decoded targets where supported, mounted-volume identity where available, and recall/offline indicators.

The model distinguishes POSIX and Windows symbolic links, directory junctions, mounted volumes, other name-surrogate reparse points, Cloud Files placeholders, opaque reparse points, and unknown host indirection. The resolver follows only mechanisms whose targets can be characterized safely as pathnames.

## Basic use

```csharp
using Icod.Path;

var resolver = new CanonicalPathResolver();
var result = await resolver.ResolvePhysicalAsync( inputPath );

if ( result.Succeeded ) {
    Console.WriteLine( result.Path );
} else {
    Console.Error.WriteLine( result.Failure?.Message );
}
```

Use `NormalizeLexically` when filesystem observation is not desired, `InspectLinkAsync` for no-follow terminal-object inspection, `GetRelativePath` for component-aware relative paths, and `EvaluateContainment` for root/candidate containment checks.

## Platform profile

The same pathname grammar can be selected independently of the current host for deterministic tests and tooling. POSIX paths use `/`, a single root, and ordinal case-sensitive comparison. Windows paths recognize drive roots, UNC roots, current-volume rooted paths, and extended path prefixes and use ordinal case-insensitive root and component comparison.

System-backed physical observation uses the current host filesystem. On Windows, reparse-point characterization uses no-follow handles and native reparse metadata; on POSIX hosts, symbolic-link targets are observed without resolving the entire link chain in one step.

## Build and test

`Icod.Path` targets .NET 10.0 and uses C# 13.

```text
dotnet build Icod.Path.sln
dotnet test Icod.Path.sln
```

The repository contains the library project at the root and its test project under `tests/Path.Tests`.

## Detailed contract

See [`src/README.md`](src/README.md) for the canonical-path, pathname-indirection, and platform contract implemented by the source tree.

## License

`Icod.Path` is licensed under the GNU Lesser General Public License, version 3.0 or later. See [`LICENSE`](LICENSE).
