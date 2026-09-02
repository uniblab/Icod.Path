# Icod.Path build and distribution tooling

This directory adapts the canonical Icod C#/.NET build-cycle contract to the `Icod.Path` library package.

## Lifecycle

| Lifecycle | Configuration | Entry point |
| --- | --- | --- |
| local `build.cmd` / `build.sh` | `Debug` | `packaging/Invoke-Build.ps1` |
| pull request | `Staging` | `.github/workflows/pull-request.yaml` |
| push to `main` | `Release` | `.github/workflows/main.yaml` |
| manual diagnostic | selected | `.github/workflows/distribution-validation.yaml` |
| `v*` tag contained in `main` | `Release` | `.github/workflows/release.yaml` |

`Icod.Path` is a library-only repository. It does not produce executable release archives.

## Target frameworks

The library and tests target:

```text
net7.0
net8.0
net9.0
net10.0
```

CI therefore installs all four SDK lines rather than assuming the template's .NET-10-only default.

## Local Debug cycle

The root build scripts run:

```text
clean -> restore -> build -> test -> pack -> validate
```

and always use `Debug` unless `packaging/Invoke-Build.ps1` is invoked directly for a diagnostic configuration.

## Package contract

`VerifyPackageArtifact.ps1` validates the exact generated package rather than rebuilding a logically equivalent package. It requires:

- exactly one `Icod.Path` `.nupkg`;
- a matching `.snupkg`;
- the expected package version when supplied;
- `README.md`, `LICENSE`, and `icon.png` in the package;
- `Icod.Path.dll` and XML documentation for net7.0, net8.0, net9.0, and net10.0; and
- portable PDB payloads for all four TFMs in the symbol package.

## Pull requests

Pull requests build and test `Staging` on Windows, Linux, and macOS. Linux produces and exact-verifies the canonical Staging package artifacts once.

## Main

Pushes to `main` are validation-only. The six-runner Release matrix covers Windows x64/ARM64, Linux x64/ARM64, and macOS x64/ARM64. Linux x64 additionally packs and exact-verifies the platform-neutral NuGet artifacts rather than making every architecture repeat identical packaging work.

## Tagged publication

A pushed `v<semver>` tag must point to a commit contained in `main`, and the tag version must exactly match `Icod.Path.csproj:PackageVersion`.

The tag workflow builds and packs the exact Release package once, validates that package and its symbol package, then publishes the same `.nupkg` to NuGet.org and GitHub Packages in parallel. GitHub Release creation waits for both registry jobs and contains the `.nupkg`, `.snupkg`, and `SHA256SUMS.txt`.

NuGet.org publication uses the `Release` environment and Trusted Publishing through `NuGet/login@v1`. GitHub Packages uses the repository `GITHUB_TOKEN` with `packages: write`.
