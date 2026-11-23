[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AssetDirectory,
    [switch]$CheckDestination
)

$ErrorActionPreference = 'Stop'
$manifest = Get-Content -LiteralPath (Join-Path $AssetDirectory 'sdk-release.json') -Raw | ConvertFrom-Json
& (Join-Path $PSScriptRoot 'verify-sdk-assets.ps1') -AssetDirectory $AssetDirectory -SourceSha $manifest.sourceSha
$source = git -C $PSScriptRoot rev-parse HEAD
$preview = $manifest.version.Contains('-preview.', [StringComparison]::Ordinal)
$branch = if ($preview) { 'refs/heads/develop' } else { 'refs/heads/main' }
if ($LASTEXITCODE -ne 0 -or $source -cne $manifest.sourceSha -or $env:GITHUB_REF -cne $branch -or
    $env:GITHUB_REPOSITORY -ine $manifest.repository) {
    throw 'SDK release provenance differs from the checked-out source or release channel.'
}

$remote = "https://github.com/$($manifest.repository).git"
& git ls-remote --exit-code --refs $remote "refs/tags/$($manifest.tag)" | Out-Null
switch ($LASTEXITCODE) {
    0 { throw 'The SDK tag already exists.' }
    2 { }
    default { throw 'Could not verify that the SDK tag is available.' }
}
if ($CheckDestination) { return }

& gh api "repos/$($manifest.repository)/git/refs" -f "ref=refs/tags/$($manifest.tag)" -f "sha=$source" --silent
if ($LASTEXITCODE -ne 0) { throw 'SDK tag creation failed.' }
$assets = @(Get-ChildItem -LiteralPath $AssetDirectory -File | ForEach-Object FullName)
$channelOptions = if ($preview) { @('--prerelease', '--latest=false') } else { @('--latest') }
& gh release create $manifest.tag @assets --repo $manifest.repository --verify-tag `
    --title "MirrorPulse Adapter SDK $($manifest.version)" --generate-notes @channelOptions
if ($LASTEXITCODE -ne 0) { throw 'SDK release creation failed. Recover the reserved tag from the original candidate.' }
