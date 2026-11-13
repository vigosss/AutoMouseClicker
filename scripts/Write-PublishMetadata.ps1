[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ProjectDirectory,
    [Parameter(Mandatory = $true)][string]$OutputFile,
    [Parameter(Mandatory = $true)][string]$DependencyManifest,
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$TargetFramework,
    [string]$RuntimeIdentifier = '',
    [string]$SelfContained = 'false',
    [string]$NoBuild = 'false'
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath $ProjectDirectory).Path
$outputPath = [System.IO.Path]::GetFullPath($OutputFile)
$publishRoot = [System.IO.Path]::GetDirectoryName($outputPath)
if (-not (Test-Path -LiteralPath (Join-Path $publishRoot 'Ming-AutoClicker.exe') -PathType Leaf)) {
    throw 'The publish directory must contain Ming-AutoClicker.exe before metadata is generated.'
}
$dependencyData = Get-Content -LiteralPath $DependencyManifest -Raw -Encoding UTF8 | ConvertFrom-Json

function Read-RepositoryGit {
    param([string[]]$Arguments)
    # The exception is scoped to this invocation and this repository only.
    # Git expects forward slashes in safe.directory on Windows.
    # An unavailable Git identity leaves source metadata unknown, not guessed.
    $ErrorActionPreference = 'Continue'
    $safeDirectory = $projectRoot.Replace('\', '/')
    $result = & git -c "safe.directory=$safeDirectory" -C $projectRoot @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    return ($result -join "`n")
}

$commit = $null
$tag = $null
$dirty = $null
if ((Get-Command git -ErrorAction SilentlyContinue) -and (Test-Path -LiteralPath (Join-Path $projectRoot '.git'))) {
    $commit = Read-RepositoryGit -Arguments @('rev-parse', '--verify', 'HEAD')
    if ($commit) {
        $status = Read-RepositoryGit -Arguments @('status', '--porcelain', '--untracked-files=normal')
        if ($null -ne $status) { $dirty = -not [string]::IsNullOrWhiteSpace($status) }
        # rev-parse --quiet does not write an expected "no tag" diagnostic to stderr.
        if ($env:GITHUB_REF -and $env:GITHUB_REF.StartsWith('refs/tags/')) {
            $candidate = Read-RepositoryGit -Arguments @('rev-parse', '--verify', '--quiet', ($env:GITHUB_REF + '^{commit}'))
            if ($candidate -eq $commit) { $tag = $env:GITHUB_REF.Substring(10) }
        }
        if (-not $tag) {
            $tag = Read-RepositoryGit -Arguments @('tag', '--points-at', 'HEAD')
            if ($tag) { $tag = ($tag -split "`n" | Sort-Object | Select-Object -First 1) }
        }
        if (-not $tag) { $tag = $null }
    }
}

$repository = 'https://github.com/vigosss/AutoMouseClicker'
$buildReused = [bool]::Parse($NoBuild)
if ($buildReused) {
    # A previous binary may not correspond to the current checkout.
    $commit = $null
    $tag = $null
}
$sourceArchive = $null
if ($commit -and $dirty -eq $false -and
    $env:GITHUB_ACTIONS -eq 'true' -and
    $env:GITHUB_REPOSITORY -eq 'vigosss/AutoMouseClicker' -and
    $env:GITHUB_SHA -eq $commit) {
    # A clean local commit alone does not prove that it exists on GitHub.
    $sourceArchive = "$repository/archive/$commit.zip"
}

$packages = @($dependencyData.libraries.PSObject.Properties |
    Where-Object { $_.Value.type -in @('package', 'runtimepack') } |
    Sort-Object Name |
    ForEach-Object {
        $separator = $_.Name.LastIndexOf('/')
        [ordered]@{ name = $_.Name.Substring(0, $separator); version = $_.Name.Substring($separator + 1); type = $_.Value.type }
    })

$nativeAssets = @()
foreach ($target in $dependencyData.targets.PSObject.Properties) {
    if ($target.Name -ne $dependencyData.runtimeTarget.name) { continue }
    foreach ($library in $target.Value.PSObject.Properties) {
        foreach ($asset in $library.Value.native.PSObject.Properties) {
            $nativeAssets += [ordered]@{ package = $library.Name; path = $asset.Name }
        }
        foreach ($asset in $library.Value.runtimeTargets.PSObject.Properties) {
            if ($asset.Value.assetType -eq 'native' -and $asset.Value.rid -eq $RuntimeIdentifier) {
                $nativeAssets += [ordered]@{ package = $library.Name; path = $asset.Name }
            }
        }
    }
}

$metadata = [ordered]@{
    schemaVersion = 1
    application = 'Ming-AutoClicker'
    version = $Version
    author = 'M-ing'
    license = 'GPL-3.0-only'
    repositoryUrl = $repository
    sourceCommit = $commit
    sourceTag = $tag
    workingTreeDirty = $dirty
    sourceArchiveUrl = $sourceArchive
    buildReused = $buildReused
    buildUtc = [DateTime]::UtcNow.ToString('o')
    targetFramework = $TargetFramework
    runtimeIdentifier = $RuntimeIdentifier
    selfContained = [bool]::Parse($SelfContained)
    packages = $packages
    nativeAssets = @($nativeAssets | Sort-Object { $_.package }, { $_.path })
}
[System.IO.File]::WriteAllText($outputPath, ($metadata | ConvertTo-Json -Depth 6) + "`n", [System.Text.UTF8Encoding]::new($false))
Write-Output "Published BUILD_INFO.json (commit=$commit; workingTreeDirty=$dirty)."
