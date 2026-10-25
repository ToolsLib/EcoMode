#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Version,
    [string] $IsccPath
)

$ErrorActionPreference = 'Stop'
$Version = & "$PSScriptRoot/Get-ReleaseVersion.ps1" -Tag $Version
$numericVersion = ($Version -split '-', 2)[0] + '.0'
$projectRoot = Split-Path $PSScriptRoot -Parent
$artifactRoot = Join-Path $projectRoot 'artifacts'
$releaseDirectory = Join-Path $artifactRoot 'release'
$stagingDirectory = Join-Path $artifactRoot ('staging-' + [guid]::NewGuid().ToString('N'))
$publishDirectory = Join-Path $stagingDirectory 'publish'
$packageName = "EcoMode-$Version-win-x64"

if (!$IsccPath) {
    $IsccPath = @("$env:ProgramFiles\Inno Setup 7\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (!$IsccPath -or !(Test-Path -LiteralPath $IsccPath -PathType Leaf)) {
    throw "Inno Setup compiler not found at '$IsccPath'. Supply -IsccPath with the location of ISCC.exe."
}
New-Item -ItemType Directory -Path $releaseDirectory, $publishDirectory -Force | Out-Null

try {
    dotnet publish "$projectRoot/ecomode.csproj" --configuration Release --runtime win-x64 --self-contained true `
        -p:PublishProfile=Standalone `
        --output $publishDirectory "-p:Version=$Version" "-p:AssemblyVersion=$numericVersion" "-p:FileVersion=$numericVersion"
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

    Copy-Item -LiteralPath "$projectRoot/LICENSE", "$projectRoot/README.md" -Destination $publishDirectory
    Copy-Item -LiteralPath "$projectRoot/profiles", "$projectRoot/docs" -Destination $publishDirectory -Recurse

    $zipPath = Join-Path $releaseDirectory "$packageName.zip"
    Compress-Archive -Path "$publishDirectory/*" -DestinationPath $zipPath -Force

    & $IsccPath "/DMyAppVer=$Version" "/DMyAppNumericVer=$numericVersion" "/DPublishDir=$publishDirectory" `
        "/O$releaseDirectory" "/F$packageName-setup" "$projectRoot/ecomode.iss"
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }

    $installerPath = Join-Path $releaseDirectory "$packageName-setup.exe"
    $checksums = foreach ($file in @($zipPath, $installerPath)) {
        $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $([IO.Path]::GetFileName($file))"
    }
    $checksumPath = Join-Path $releaseDirectory "$packageName-SHA256SUMS.txt"
    $checksums | Set-Content -LiteralPath $checksumPath -Encoding utf8NoBOM
    Write-Host "Release packages: $zipPath, $installerPath, $checksumPath"
}
finally {
    # Check the absolute cleanup target before recursively deleting this build's staging files.
    $resolvedStaging = [IO.Path]::GetFullPath($stagingDirectory)
    $resolvedRoot = [IO.Path]::GetFullPath($artifactRoot) + [IO.Path]::DirectorySeparatorChar
    if (!$resolvedStaging.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove staging directory outside artifacts: $resolvedStaging"
    }
    Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
}
