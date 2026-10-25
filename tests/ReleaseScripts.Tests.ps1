#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('EcoMode-release-tests-' + [guid]::NewGuid().ToString('N'))
$testVersion = '1.2.3-rc.1'
$packageName = "EcoMode-$testVersion-win-x64"
$files = @("$packageName.zip", "$packageName-setup.exe", "$packageName-SHA256SUMS.txt")
$script:passed = 0

function Assert($condition, [string] $message) {
    if (!$condition) { throw $message }
}

function Assert-Throws([scriptblock] $action) {
    $failed = $false
    try { & $action } catch { $failed = $true }
    Assert $failed 'Expected this operation to fail.'
}

function Run([string] $name, [scriptblock] $action) {
    & $action
    $script:passed++
    Write-Host "PASS: $name"
}

function Reset-Gh([bool] $exists, [bool] $draft, [string[]] $assets = @()) {
    $global:EcoModeReleaseTest = @{
        Exists = $exists
        Release = @{ isDraft = $draft; assets = @($assets | ForEach-Object { @{ name = $_ } }) }
        Calls = [Collections.Generic.List[object]]::new()
        FailUpload = $false
    }
}

# The publisher is tested without authentication or any GitHub requests.
function global:gh {
    $state = $global:EcoModeReleaseTest
    $state.Calls.Add(@($args))
    $global:LASTEXITCODE = 0
    switch ($args[1]) {
        'view' {
            if (!$state.Exists) { $global:LASTEXITCODE = 1; return }
            $state.Release | ConvertTo-Json -Depth 6 -Compress
        }
        'create' { $state.Exists = $true; $state.Release.isDraft = $true }
        'upload' { if ($state.FailUpload) { $global:LASTEXITCODE = 1 } }
        default { throw "Unexpected gh invocation: $args" }
    }
}

try {
    Run 'installer preserves user profiles and machine recovery originals' {
        $installerScript = Get-Content -Raw -LiteralPath "$projectRoot/ecomode.iss"
        Assert ($installerScript -notmatch '(?im)^\s*\[UninstallDelete\]') 'Uninstall must preserve recovery data after abnormal termination.'
        Assert ($installerScript -match 'net10\.0-windows') 'Installer publish defaults must match .NET 10.'
    }
    $fixtureScripts = Join-Path $fixture 'scripts'
    $fixturePackages = Join-Path $fixture 'artifacts/release'
    New-Item -ItemType Directory -Path $fixtureScripts, $fixturePackages -Force | Out-Null
    Copy-Item -LiteralPath "$projectRoot/scripts/Get-ReleaseVersion.ps1", "$projectRoot/scripts/Publish-Release.ps1" -Destination $fixtureScripts
    foreach ($file in $files) { Set-Content -LiteralPath (Join-Path $fixturePackages $file) -Value 'test package' }
    $publisher = Join-Path $fixtureScripts 'Publish-Release.ps1'

    Run 'stable and prerelease version tags are normalized' {
        foreach ($tag in @('v1.2.3', '1.2.3', 'v0.1.0-beta.2', 'v1.2.3-0')) {
            Assert ((& "$fixtureScripts/Get-ReleaseVersion.ps1" -Tag $tag) -eq $tag.TrimStart('v')) "Invalid normalization: $tag"
        }
    }
    Run 'malformed, unsafe, and unsupported version tags fail validation' {
        foreach ($tag in @('v1.2', 'v01.2.3', 'v1.2.3-01', 'v1.2.3;exit', 'v1.2.3$(exit)', 'v65535.2.3', 'v1.2.3+metadata')) {
            Assert-Throws { & "$fixtureScripts/Get-ReleaseVersion.ps1" -Tag $tag }
        }
    }
    Run 'missing release creates a verified draft prerelease and uploads packages' {
        Reset-Gh $false $false
        & $publisher -Tag "v$testVersion"
        $creates = @($global:EcoModeReleaseTest.Calls | Where-Object { $_[1] -eq 'create' })
        Assert ($creates.Count -eq 1 -and '--draft' -in $creates[0] -and '--verify-tag' -in $creates[0] -and '--prerelease' -in $creates[0]) 'Tag build must prepare a draft for the existing tag.'
        Assert (@($global:EcoModeReleaseTest.Calls | Where-Object { $_[1] -eq 'upload' }).Count -eq 3) 'Expected three release packages.'
    }
    Run 'draft release reruns replace all packages' {
        Reset-Gh $true $true $files
        & $publisher -Tag "v$testVersion"
        $uploads = @($global:EcoModeReleaseTest.Calls | Where-Object { $_[1] -eq 'upload' })
        Assert ($uploads.Count -eq 3 -and @($uploads | Where-Object { '--clobber' -notin $_ }).Count -eq 0) 'Draft rerun should replace the complete package set.'
    }
    Run 'published release without packages receives the build' {
        Reset-Gh $true $false
        & $publisher -Tag "v$testVersion"
        Assert (@($global:EcoModeReleaseTest.Calls | Where-Object { $_[1] -eq 'upload' }).Count -eq 3) 'Published release did not receive packages.'
        Assert (@($global:EcoModeReleaseTest.Calls | Where-Object { $_[1] -eq 'create' }).Count -eq 0) 'Existing release must not be recreated.'
    }
    Run 'already published complete packages are preserved' {
        Reset-Gh $true $false $files
        & $publisher -Tag "v$testVersion"
        Assert (@($global:EcoModeReleaseTest.Calls | Where-Object { $_[1] -eq 'upload' }).Count -eq 0) 'Complete published assets must be preserved.'
    }
    Run 'partial published uploads are replaced together with matching checksums' {
        Reset-Gh $true $false @($files[0])
        & $publisher -Tag "v$testVersion"
        $uploads = @($global:EcoModeReleaseTest.Calls | Where-Object { $_[1] -eq 'upload' })
        Assert ($uploads.Count -eq 3 -and @($uploads | Where-Object { '--clobber' -notin $_ }).Count -eq 0) 'Partial rerun must publish one consistent build.'
    }
    Run 'upload failures stop publication' {
        Reset-Gh $true $true
        $global:EcoModeReleaseTest.FailUpload = $true
        Assert-Throws { & $publisher -Tag "v$testVersion" }
        Assert (@($global:EcoModeReleaseTest.Calls | Where-Object { $_[1] -eq 'upload' }).Count -eq 1) 'Publication continued after failure.'
    }
    Run 'missing packages fail before any GitHub operation' {
        Reset-Gh $true $true
        Remove-Item -LiteralPath (Join-Path $fixturePackages $files[0])
        Assert-Throws { & $publisher -Tag "v$testVersion" }
        Assert ($global:EcoModeReleaseTest.Calls.Count -eq 0) 'Publisher contacted GitHub with missing packages.'
    }
    Write-Host "PASS: $script:passed release script checks."
}
finally {
    Remove-Item -LiteralPath Function:\gh -ErrorAction SilentlyContinue
    Remove-Variable -Name EcoModeReleaseTest -Scope Global -ErrorAction SilentlyContinue
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (!$resolvedFixture.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing cleanup outside the test temporary directory: $resolvedFixture"
    }
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}

# Expected failure tests set the mocked CLI's native exit status to 1. Clear it
# only after every assertion and cleanup succeeds; GitHub's pwsh wrapper exits
# with LASTEXITCODE even when the PowerShell script completed successfully.
$global:LASTEXITCODE = 0
