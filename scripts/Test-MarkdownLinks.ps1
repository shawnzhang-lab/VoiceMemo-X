[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Split-Path -Parent $PSScriptRoot)).Path.TrimEnd('\')
. (Join-Path $PSScriptRoot 'PublicReleaseFiles.ps1')
$inventory = Get-PublicReleaseInventory -ProjectRoot $projectRoot
$failures = [System.Collections.Generic.List[string]]::new()

function Test-LocalTarget {
    param(
        [Parameter(Mandatory)][System.IO.FileInfo]$SourceFile,
        [Parameter(Mandatory)][string]$RawTarget
    )

    $target = $RawTarget.Trim().Trim('<', '>')
    if ([string]::IsNullOrWhiteSpace($target) -or $target.StartsWith('#')) { return }
    if ($target -match '^(?i)(https?|mailto|tel|data|file):') { return }

    $target = ($target -split '#', 2)[0]
    $target = ($target -split '\?', 2)[0]
    if ([string]::IsNullOrWhiteSpace($target)) { return }
    $target = [Uri]::UnescapeDataString($target).Replace('/', '\')

    $baseDirectory = Split-Path -Parent $SourceFile.FullName
    $resolved = [IO.Path]::GetFullPath((Join-Path $baseDirectory $target))
    if (-not $resolved.StartsWith($projectRoot + '\', [StringComparison]::OrdinalIgnoreCase) -and
        -not [string]::Equals($resolved, $projectRoot, [StringComparison]::OrdinalIgnoreCase)) {
        $failures.Add("链接逃出仓库：$($SourceFile.FullName.Substring($projectRoot.Length).TrimStart('\')) -> $RawTarget")
        return
    }
    if (-not (Test-Path -LiteralPath $resolved)) {
        $failures.Add("本地链接不存在：$($SourceFile.FullName.Substring($projectRoot.Length).TrimStart('\')) -> $RawTarget")
    }
}

foreach ($file in $inventory.Files | Where-Object Extension -eq '.md') {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($match in [regex]::Matches($content, '!?(?:\[[^\]]*\])\((?<target>[^)\s]+)(?:\s+["''][^"'']*["''])?\)')) {
        Test-LocalTarget -SourceFile $file -RawTarget $match.Groups['target'].Value
    }
    foreach ($match in [regex]::Matches($content, '(?i)(?:href|src)\s*=\s*["''](?<target>[^"'']+)["'']')) {
        Test-LocalTarget -SourceFile $file -RawTarget $match.Groups['target'].Value
    }
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Error $failure -ErrorAction Continue }
    throw "Markdown 本地链接检查失败：$($failures.Count) 项。"
}

Write-Host 'MARKDOWN_LINK_CHECK_PASS' -ForegroundColor Green

