#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Tag,
    [switch] $Publish
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$version = & "$PSScriptRoot/Get-ReleaseVersion.ps1" -Tag $Tag
$releaseDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/release'
$packageName = "EcoMode-$version-win-x64"
$files = @("$packageName.zip", "$packageName-setup.exe", "$packageName-SHA256SUMS.txt")
foreach ($file in $files) {
    if (!(Test-Path -LiteralPath (Join-Path $releaseDirectory $file) -PathType Leaf)) {
        throw "Missing release package: $file"
    }
}

$releaseJson = gh release view $Tag --json isDraft,assets 2>$null
if ($LASTEXITCODE -ne 0) {
    # Keep the release a draft until every package is attached and verified.
    $arguments = @('release', 'create', $Tag, '--verify-tag', '--draft', '--title', "EcoMode $version", '--generate-notes')
    if ($version.Contains('-')) { $arguments += '--prerelease' }
    & gh @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the draft release.' }
    $releaseJson = gh release view $Tag --json isDraft,assets
    if ($LASTEXITCODE -ne 0) { throw 'Could not read the release.' }
}

$release = ($releaseJson -join "`n") | ConvertFrom-Json
if (!$release.isDraft -and @($files | Where-Object { $_ -notin $release.assets.name }).Count -eq 0) {
    Write-Host 'All packages are already published; preserving the existing release assets.'
    return
}

foreach ($file in $files) {
    # Replace a partial set together so the checksums always match the uploaded build.
    # Completed published releases (including immutable ones) were skipped above.
    $arguments = @('release', 'upload', $Tag, (Join-Path $releaseDirectory $file))
    if ($release.isDraft -or @($release.assets).Count -gt 0) { $arguments += '--clobber' }
    & gh @arguments
    if ($LASTEXITCODE -ne 0) { throw "Could not upload release package: $file" }
}

if ($Publish) {
    $verifiedJson = gh release view $Tag --json isDraft,assets
    if ($LASTEXITCODE -ne 0) { throw 'Could not verify uploaded release assets.' }
    $verified = ($verifiedJson -join "`n") | ConvertFrom-Json
    if (@($files | Where-Object { $_ -notin $verified.assets.name }).Count -ne 0) {
        throw 'Release remains a draft because the expected asset set is incomplete.'
    }
    if ($verified.isDraft) {
        $arguments = @('release', 'edit', $Tag, '--draft=false')
        $notes = Join-Path (Split-Path $PSScriptRoot -Parent) 'CHANGELOG.md'
        if (Test-Path -LiteralPath $notes -PathType Leaf) { $arguments += @('--notes-file', $notes) }
        & gh @arguments
        if ($LASTEXITCODE -ne 0) { throw 'Could not publish the verified release.' }
    }
}
