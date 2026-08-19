# Icod.Path

`Icod.Path` is the neutral canonical-path and pathname-indirection foundation shared by the Icod utility suites. Completion Gate E2 introduced lexical normalization, physical resolution, missing-component policy, loop detection, relative paths, containment checks, and platform-aware root and volume semantics. Completion Gate E3R adds authoritative no-follow characterization of POSIX symbolic links and Windows reparse points.

The project is intentionally independent of individual commands and of `Icod.CoreUtils.Shared`. The current Shared incubation project references this neutral layer so traversal and metadata consumers use the same physical-object classification as `readlink`, `realpath`, and Patch rather than maintaining a second Windows reparse model.

The public resolver returns structured success or failure results. It never reports unresolved input as a successful canonical path. Physical resolution processes pathname components in filesystem order so eligible pathname indirection is resolved before a following `..`, matching filesystem traversal semantics rather than merely applying lexical simplification.

Windows reparse points are not treated as synonyms for symbolic links. The shared inspector preserves the raw tag, Microsoft and name-surrogate bits, physical attributes, decoded targets where supported, mounted-volume identity where available, and recall/offline indicators. It distinguishes Windows symbolic links, directory junctions, mounted volumes, unknown name surrogates, Cloud Files placeholders, and opaque reparse points.

See [`src/README.md`](src/README.md) for the contract and platform profile.
