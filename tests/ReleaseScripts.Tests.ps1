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
        DropUpload = $false
        FailEdit = $false
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
        'upload' {
            if ($state.FailUpload) { $global:LASTEXITCODE = 1; return }
            if (!$state.DropUpload) { $state.Release.assets += @{ name = [IO.Path]::GetFileName($args[3]) } }
        }
        'edit' { if ($state.FailEdit) { $global:LASTEXITCODE = 1 } else { $state.Release.isDraft = $false } }
        default { throw "Unexpected gh invocation: $args" }
    }
}

try {
    Run 'installer preserves user profiles and machine recovery originals' {
        $installerScript = Get-Content -Raw -LiteralPath "$projectRoot/installer/EcoMode.iss"
        Assert ($installerScript -notmatch '(?im)^\s*\[UninstallDelete\]') 'Uninstall must preserve recovery data after abnormal termination.'
        Assert ($installerScript -match 'artifacts\\standalone\\win-x64') 'Installer publish defaults must use the standalone output.'
        Assert ($installerScript -match 'CompareText\(StartupCommand, InstalledCommand\) = 0') 'Uninstall must preserve startup entries that point to another copy.'
        Assert ($installerScript -match "RegDeleteValue\(HKCU, 'Software\\Microsoft\\Windows\\CurrentVersion\\Run', 'EcoMode'\)") 'Uninstall must remove only the current user startup value for this installed copy.'
    }
    Run 'release packaging uses the source layout from any working directory and cleans failed builds' {
        $buildRoot = Join-Path $fixture 'packaging'
        $buildScripts = Join-Path $buildRoot 'scripts'
        $buildProject = Join-Path $buildRoot 'src/EcoMode/EcoMode.csproj'
        $buildInstaller = Join-Path $buildRoot 'installer/EcoMode.iss'
        $directories = @($buildScripts, (Split-Path $buildProject), (Split-Path $buildInstaller),
            "$buildRoot/src/EcoMode/Profiles", "$buildRoot/docs", "$buildRoot/elsewhere")
        New-Item -ItemType Directory -Path $directories -Force | Out-Null
        Copy-Item -LiteralPath "$projectRoot/scripts/Build-Release.ps1", "$projectRoot/scripts/Get-ReleaseVersion.ps1" -Destination $buildScripts
        foreach ($file in @($buildProject, $buildInstaller, "$buildRoot/LICENSE", "$buildRoot/README.md",
            "$buildRoot/src/EcoMode/Profiles/balanced.json", "$buildRoot/docs/screenshot.png")) {
            Set-Content -LiteralPath $file -Value 'fixture'
        }
        $compiler = Join-Path $buildRoot 'compiler.ps1'
        @'
param([Parameter(ValueFromRemainingArguments)][string[]] $CompilerArguments)
if ([IO.Path]::GetFullPath($CompilerArguments[-1]) -ne $global:EcoModeBuildTest.Installer) {
    throw 'The release script used the wrong installer path.'
}
$output = ($CompilerArguments | Where-Object { $_.StartsWith('/O') }).Substring(2)
$filename = ($CompilerArguments | Where-Object { $_.StartsWith('/F') }).Substring(2)
[IO.File]::WriteAllText((Join-Path $output ($filename + '.exe')), 'installer fixture')
$global:LASTEXITCODE = 0
'@ | Set-Content -LiteralPath $compiler
        $global:EcoModeBuildTest = @{ Project = [IO.Path]::GetFullPath($buildProject); Installer = [IO.Path]::GetFullPath($buildInstaller); FailPublish = $false }
        function global:dotnet {
            if ($args[0] -ne 'publish' -or [IO.Path]::GetFullPath($args[1]) -ne $global:EcoModeBuildTest.Project) {
                throw 'The release script used the wrong application project.'
            }
            if ($global:EcoModeBuildTest.FailPublish) { $global:LASTEXITCODE = 1; return }
            $output = $args[[Array]::IndexOf($args, '--output') + 1]
            [IO.File]::WriteAllText((Join-Path $output 'ecomode.exe'), 'application fixture')
            $global:LASTEXITCODE = 0
        }
        Push-Location "$buildRoot/elsewhere"
        try {
            & "$buildScripts/Build-Release.ps1" -Version $testVersion -IsccPath $compiler
            $releaseRoot = Join-Path $buildRoot 'artifacts/release'
            $archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $releaseRoot "$packageName.zip"))
            try {
                $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
                foreach ($expected in @('ecomode.exe', 'profiles/balanced.json', 'docs/screenshot.png', 'README.md', 'LICENSE')) {
                    Assert ($expected -in $entries) "Portable release is missing $expected."
                }
                Assert (@($entries | Where-Object { $_ -match '(^|/)(src|tests|obj|bin)/' }).Count -eq 0) 'Developer sources or build directories leaked into the package.'
            }
            finally { $archive.Dispose() }
            foreach ($line in (Get-Content -LiteralPath (Join-Path $releaseRoot "$packageName-SHA256SUMS.txt"))) {
                $hash, $name = $line -split '  ', 2
                Assert ((Get-FileHash -LiteralPath (Join-Path $releaseRoot $name) -Algorithm SHA256).Hash -eq $hash) 'A package checksum is invalid.'
            }
            $global:EcoModeBuildTest.FailPublish = $true
            Assert-Throws { & "$buildScripts/Build-Release.ps1" -Version '9.9.9' -IsccPath $compiler }
            Assert (@(Get-ChildItem "$buildRoot/artifacts" -Directory -Filter 'staging-*').Count -eq 0) 'A build left staging files behind.'
            Assert (!(Test-Path -LiteralPath "$releaseRoot/EcoMode-9.9.9-win-x64.zip")) 'Failed publishing created a release package.'
        }
        finally {
            Pop-Location
            Remove-Item -LiteralPath Function:\dotnet -ErrorAction SilentlyContinue
            Remove-Variable -Name EcoModeBuildTest -Scope Global -ErrorAction SilentlyContinue
            $global:LASTEXITCODE = 0
        }
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
    Run 'only an explicitly annotated tag opts into automatic publication' {
        $intentRoot = Join-Path $fixture 'intent'
        New-Item -ItemType Directory -Path $intentRoot | Out-Null
        git init -q $intentRoot
        git -C $intentRoot -c user.name=Fixture -c user.email=fixture@example.invalid commit --allow-empty -q -m 'Publish: true'
        if ($LASTEXITCODE -ne 0) { throw 'Could not create the tag-intent fixture.' }
        git -C $intentRoot -c user.name=Fixture -c user.email=fixture@example.invalid tag -a v1.2.3 -m "Release`n`nPublish: true"
        git -C $intentRoot -c user.name=Fixture -c user.email=fixture@example.invalid tag -a v1.2.4 -m 'Release draft'
        git -C $intentRoot tag v1.2.5
        Push-Location $intentRoot
        try {
            Assert (& "$projectRoot/scripts/Get-ReleaseIntent.ps1" -Tag v1.2.3) 'Explicit annotated intent did not enable publication.'
            Assert (!(& "$projectRoot/scripts/Get-ReleaseIntent.ps1" -Tag v1.2.4)) 'Normal tags must still create drafts.'
            Assert (!(& "$projectRoot/scripts/Get-ReleaseIntent.ps1" -Tag v1.2.5)) 'A lightweight tag inherited intent from its commit.'
        }
        finally { Pop-Location }
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
        Assert-Throws { & $publisher -Tag "v$testVersion" -Publish }
        Assert (@($global:EcoModeReleaseTest.Calls | Where-Object { $_[1] -eq 'upload' }).Count -eq 1) 'Publication continued after failure.'
        Assert ($global:EcoModeReleaseTest.Release.isDraft) 'A failed upload published the release.'
    }
    Run 'explicit publication occurs only after all assets are verified' {
        Reset-Gh $false $false
        & $publisher -Tag "v$testVersion" -Publish
        Assert (!$global:EcoModeReleaseTest.Release.isDraft) 'Verified release was not published.'
        $calls = $global:EcoModeReleaseTest.Calls
        Assert ($calls[-1][1] -eq 'edit' -and '--draft=false' -in $calls[-1]) 'Publication did not happen last.'
        Assert (@($calls | Where-Object { $_[1] -eq 'upload' }).Count -eq 3) 'Expected all packages before publication.'
    }
    Run 'incomplete asset verification prevents publication' {
        Reset-Gh $true $true
        $global:EcoModeReleaseTest.DropUpload = $true
        Assert-Throws { & $publisher -Tag "v$testVersion" -Publish }
        Assert ($global:EcoModeReleaseTest.Release.isDraft) 'Incomplete release was published.'
    }
    Run 'publication errors fail the workflow and preserve the draft' {
        Reset-Gh $true $true
        $global:EcoModeReleaseTest.FailEdit = $true
        Assert-Throws { & $publisher -Tag "v$testVersion" -Publish }
        Assert ($global:EcoModeReleaseTest.Release.isDraft) 'Failed publication lost draft state.'
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
