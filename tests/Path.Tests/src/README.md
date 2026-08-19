# Canonical-path tests

The test suite covers lexical normalization, drive and UNC roots, volume comparison, ordered physical traversal, relative and absolute symbolic-link targets, symbolic links followed by `..`, raw final-link inspection, dangling links, missing-component policies, non-directory components, loop and expansion-limit detection, relative path calculation, containment, cancellation, and host filesystem behavior.

`SyntheticCanonicalPathFileSystemProvider` keeps cross-platform semantics deterministic and permits Windows path behavior to be tested on Unix runners and POSIX behavior on Windows runners.
