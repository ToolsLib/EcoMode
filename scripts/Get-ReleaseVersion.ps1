[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Tag
)

$ErrorActionPreference = 'Stop'
# Version tags accept an optional v prefix and SemVer prereleases (e.g. v1.2.3-rc.1).
$pattern = '^v?(?<version>(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)(?:-(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*)?)$'
$match = [regex]::Match($Tag, $pattern)
if (!$match.Success) {
    throw "Invalid release tag '$Tag'. Use vMAJOR.MINOR.PATCH or vMAJOR.MINOR.PATCH-prerelease (e.g. v1.2.3-rc.1)."
}
foreach ($component in @('major', 'minor', 'patch')) {
    if ([long]::Parse($match.Groups[$component].Value) -gt 65534) {
        throw 'Version components must be at most 65534 for Windows assembly version compatibility.'
    }
}
$match.Groups['version'].Value
