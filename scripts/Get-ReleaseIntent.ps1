#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string] $Tag)

$ErrorActionPreference = 'Stop'
& "$PSScriptRoot/Get-ReleaseVersion.ps1" -Tag $Tag | Out-Null
$reference = "refs/tags/$Tag"
$type = git cat-file -t $reference
if ($LASTEXITCODE -ne 0) { throw "Release tag not found: $Tag" }
# A lightweight tag's commit message must never opt a release into publication.
if ($type -ne 'tag') { return $false }
$annotation = git for-each-ref --format='%(contents)' $reference
if ($LASTEXITCODE -ne 0) { throw "Could not inspect release tag: $Tag" }
return @($annotation | Where-Object { $_ -ceq 'Publish: true' }).Count -gt 0
