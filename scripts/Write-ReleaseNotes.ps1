[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ProjectDirectory,
    [Parameter(Mandatory = $true)][string]$Tag,
    [Parameter(Mandatory = $true)][string]$OutputFile
)

$ErrorActionPreference = 'Stop'
if ($Tag -cnotmatch '^v\d+\.\d+\.\d+$') {
    throw "Release tag must use vMAJOR.MINOR.PATCH format, but got '$Tag'."
}

$projectRoot = (Resolve-Path -LiteralPath $ProjectDirectory).Path
$escapedTag = [regex]::Escape($Tag)
$headingPattern = "(?m)^###[ \t]+$escapedTag[ \t]*\r?$"
$sectionPattern = "(?ms)^###[ \t]+$escapedTag[ \t]*\r?\n(?<body>.*?)(?=^#{1,3}[ \t]|\z)"

function Read-ReleaseSection {
    param([string]$FileName)
    $content = Get-Content -LiteralPath (Join-Path $projectRoot $FileName) -Raw -Encoding UTF8
    if ([regex]::Matches($content, $headingPattern).Count -ne 1) {
        throw "$FileName must contain exactly one '### $Tag' release section."
    }
    $section = [regex]::Match($content, $sectionPattern)
    if (-not $section.Success -or [string]::IsNullOrWhiteSpace($section.Groups['body'].Value)) {
        throw "$FileName has no release notes for '$Tag'."
    }
    return $section.Groups['body'].Value.Trim()
}

$chinese = Read-ReleaseSection -FileName 'README.md'
$english = Read-ReleaseSection -FileName 'README.en.md'
# Keep generated notes outside the checkout so publish metadata sees a clean tree.
$body = "## 简体中文`n`n$chinese`n`n## English`n`n$english`n"
[System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($OutputFile), $body, [System.Text.UTF8Encoding]::new($false))
Write-Output "Prepared bilingual release notes for $Tag."
