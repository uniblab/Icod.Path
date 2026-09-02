param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactDirectory,

    [ValidateSet('Debug', 'Staging', 'Release')]
    [string]$Configuration = 'Release',

    [string]$ExpectedVersion = '',

    [string]$GitHubOutputPath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $PSScriptRoot 'RepositoryTools.psm1') -Force

if (-not [System.IO.Path]::IsPathRooted($ArtifactDirectory)) {
    $ArtifactDirectory = Join-Path $repositoryRoot $ArtifactDirectory
}
$ArtifactDirectory = [System.IO.Path]::GetFullPath($ArtifactDirectory)
if (-not (Test-Path -LiteralPath $ArtifactDirectory -PathType Container)) {
    throw "Artifact directory '$ArtifactDirectory' does not exist."
}

$packages = @(
    Get-ChildItem -LiteralPath $ArtifactDirectory -Filter '*.nupkg' -File |
        Where-Object { -not $_.Name.EndsWith('.symbols.nupkg', [System.StringComparison]::OrdinalIgnoreCase) } |
        Sort-Object Name
)
if (1 -ne $packages.Count) {
    throw "Expected exactly one Icod.Path .nupkg in '$ArtifactDirectory'; found $($packages.Count)."
}

$package = $packages[0]
$metadata = Get-PackageMetadata -PackagePath $package.FullName
if ('Icod.Path' -ne $metadata.Id) {
    throw "Expected PackageId 'Icod.Path'; found '$($metadata.Id)'."
}
if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion) -and $ExpectedVersion -ne $metadata.Version) {
    throw "Expected package version '$ExpectedVersion'; found '$($metadata.Version)'."
}
if ('README.md' -ne $metadata.Readme) {
    throw "Expected package readme 'README.md'; found '$($metadata.Readme)'."
}

$symbolPackagePath = Join-Path $ArtifactDirectory "Icod.Path.$($metadata.Version).snupkg"
if (-not (Test-Path -LiteralPath $symbolPackagePath -PathType Leaf)) {
    throw "Expected symbol package '$symbolPackagePath' was not produced."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $entryNames = @($archive.Entries | ForEach-Object { $_.FullName })
    foreach ($requiredEntry in @(
        'README.md',
        'LICENSE',
        'icon.png',
        'lib/net7.0/Icod.Path.dll',
        'lib/net7.0/Icod.Path.xml',
        'lib/net8.0/Icod.Path.dll',
        'lib/net8.0/Icod.Path.xml',
        'lib/net9.0/Icod.Path.dll',
        'lib/net9.0/Icod.Path.xml',
        'lib/net10.0/Icod.Path.dll',
        'lib/net10.0/Icod.Path.xml'
    )) {
        if ($requiredEntry -notin $entryNames) {
            throw "Package '$($package.Name)' is missing required entry '$requiredEntry'."
        }
    }
} finally {
    $archive.Dispose()
}

$symbols = [System.IO.Compression.ZipFile]::OpenRead($symbolPackagePath)
try {
    $symbolEntries = @($symbols.Entries | ForEach-Object { $_.FullName })
    foreach ($requiredEntry in @(
        'lib/net7.0/Icod.Path.pdb',
        'lib/net8.0/Icod.Path.pdb',
        'lib/net9.0/Icod.Path.pdb',
        'lib/net10.0/Icod.Path.pdb'
    )) {
        if ($requiredEntry -notin $symbolEntries) {
            throw "Symbol package is missing required entry '$requiredEntry'."
        }
    }
} finally {
    $symbols.Dispose()
}

if (-not [string]::IsNullOrWhiteSpace($GitHubOutputPath)) {
    'has_packages=true' >> $GitHubOutputPath
    'package_count=1' >> $GitHubOutputPath
    "package_version=$($metadata.Version)" >> $GitHubOutputPath
}

Write-Host "Exact package verification completed successfully for Icod.Path $($metadata.Version) ($Configuration)."
