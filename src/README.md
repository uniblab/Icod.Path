# Canonical-path and pathname-indirection model

The `Icod.Path` namespace separates pathname grammar from physical filesystem observation.

- `PathPlatformSemantics` describes POSIX and Windows separators, root syntax, volume identity, and comparison rules independently of the host operating system.
- `PathLexicalNormalizer` creates absolute lexical paths without observing the filesystem and rejects invalid or unresolved drive-relative forms.
- `IPathIndirectionInspector` characterizes one terminal physical object without dereferencing it or opening file content.
- `SystemPathIndirectionInspector` reads POSIX link targets and, on Windows, combines no-follow handle information, `FSCTL_GET_REPARSE_POINT`, and volume-mount APIs.
- `PathIndirectionInfo` preserves the exact link/reparse classification, raw tag, target spellings, physical attributes, mounted-volume GUID path, and recall/offline indicators.
- `ICanonicalPathFileSystemProvider` supplies one no-follow observation per pathname component.
- `CanonicalPathResolver` performs ordered physical resolution, loop and expansion-limit checks, missing-component policy, terminal-object inspection, relative-path computation, and containment evaluation.
- `CanonicalPathResult`, `RelativePathResult`, and `PathContainmentResult` carry structured failures; no failure path is returned as a successful canonical result.

## Missing components

`RequireExisting` requires every component. `AllowFinalComponent` permits only the final unresolved component. `AllowMissingSuffix` permits the first missing component and the remaining lexical suffix. The result records the number of unresolved suffix components.

## Link and reparse behavior

A reparse point is a tagged Windows extension mechanism, not necessarily a link. `PathIndirectionKind` therefore distinguishes:

- POSIX and Windows symbolic links;
- Windows directory junctions;
- Windows mounted volumes;
- other name-surrogate tags whose provider-specific target is not decoded;
- Cloud Files placeholders;
- opaque non-name-surrogate reparse points; and
- unknown host indirection.

The resolver follows only characterized mechanisms whose targets can safely be expanded as pathnames: POSIX links, Windows symbolic links, junctions, and mounted volumes. Unknown name surrogates remain observable physical objects and produce a controlled unsupported result when pathname resolution would require following them. Recognized non-name-surrogate points, including Cloud Files placeholders and opaque filter-managed objects, remain the same physical file or directory and are never relabeled as links. Reparse points whose tag cannot be characterized are quarantined rather than silently traversed merely because `Directory.Exists` succeeds.

The source-compatible `FollowSymbolicLinks` option retains its historical name, but its enabled behavior applies to all eligible pathname indirection. The final object may be retained for no-follow inspection. Unsupported terminal reparse points can be retained only when the caller explicitly permits that physical-object result.

## Windows observation safety

Windows characterization opens the terminal object with `FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS`, obtains the tag without normal reparse processing, and reads reparse data only for the Microsoft symbolic-link and mount-point formats. Unknown provider data is never guessed or decoded. Cloud and offline/recall attributes are reported without opening file content, reducing the risk that metadata inspection hydrates remote data.

The mount-point tag is shared by directory junctions and mounted volumes. `GetVolumeNameForVolumeMountPointW` is used to distinguish a mount-manager volume mount from a junction; the volume GUID pathname is retained when available.

## Platform profile

POSIX paths use `/`, a single root, and ordinal case-sensitive comparison. Windows paths recognize drive roots, UNC roots, current-volume rooted paths, and extended path prefixes, and use ordinal case-insensitive root and component comparison. Drive-relative input is resolved only when its drive matches the supplied base path; otherwise it is rejected rather than guessed.
