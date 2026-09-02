param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDirectory,

    [Parameter(Mandatory = $true)]
    [string]$DestinationDirectory,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedVersion,

    [string]$GitHubOutputPath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $PSScriptRoot 'RepositoryTools.psm1') -Force

foreach ($variableName in @('SourceDirectory', 'DestinationDirectory')) {
    $value = Get-Variable -Name $variableName -ValueOnly
    if (-not [System.IO.Path]::IsPathRooted($value)) {
        $value = Join-Path $repositoryRoot $value
    }
    Set-Variable -Name $variableName -Value ([System.IO.Path]::GetFullPath($value))
}

if (-not (Test-Path -LiteralPath $SourceDirectory -PathType Container)) {
    throw "Source package directory '$SourceDirectory' does not exist."
}
if (Test-Path -LiteralPath $DestinationDirectory) {
    Remove-Item -LiteralPath $DestinationDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null

$packagePath = Join-Path $SourceDirectory "Icod.Path.$ExpectedVersion.nupkg"
$symbolPath = Join-Path $SourceDirectory "Icod.Path.$ExpectedVersion.snupkg"
foreach ($path in @($packagePath, $symbolPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Expected release artifact '$path' was not produced."
    }
    Copy-Item -LiteralPath $path -Destination (Join-Path $DestinationDirectory ([System.IO.Path]::GetFileName($path)))
}

$metadata = Get-PackageMetadata -PackagePath $packagePath
if ('Icod.Path' -ne $metadata.Id -or $ExpectedVersion -ne $metadata.Version) {
    throw "Release package metadata '$($metadata.Id) $($metadata.Version)' does not match Icod.Path $ExpectedVersion."
}

if (-not [string]::IsNullOrWhiteSpace($GitHubOutputPath)) {
    'has_packages=true' >> $GitHubOutputPath
    'package_count=1' >> $GitHubOutputPath
}

Write-Host "Selected Icod.Path $ExpectedVersion package and symbol package for release."
