[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'sdk-version-policy.ps1')
$version = Get-SdkProjectReleaseVersion
$preview = $version.Contains('-preview.', [StringComparison]::Ordinal)
$environment = if ($preview) { 'preview' } else { 'stable' }
$branch = if ($preview) { 'refs/heads/develop' } else { 'refs/heads/main' }
$source = git -C $PSScriptRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $source -cnotmatch '\A[0-9a-f]{40}\z' -or $env:GITHUB_SHA -cne $source -or
    $env:GITHUB_REF -cne $branch -or $env:GITHUB_REPOSITORY -ine 'mirrorpulse/adapter-sdk') {
    throw 'The Adapter SDK release must match the canonical repository, source SHA and channel branch.'
}
if ($env:MP_SDK_PUBLISH -ceq 'true' -and $env:MP_SDK_CONFIRM -cne 'PUBLISH') {
    throw 'Explicit SDK publication requires PUBLISH confirmation.'
}
@("version=$version", "environment=$environment", "source=$source") |
    Out-File -LiteralPath $env:GITHUB_OUTPUT -Append -Encoding utf8
