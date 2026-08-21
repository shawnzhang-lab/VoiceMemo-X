[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Split-Path -Parent $PSScriptRoot)).Path.TrimEnd('\')
$outputRoot = Join-Path $projectRoot 'publish\github-source'
$packageFolderName = 'voice-memory-demo'
$stageRoot = Join-Path $outputRoot $packageFolderName
$verifyRoot = Join-Path $outputRoot '_verify'

function Assert-SafeOutputPath {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $expectedParent = [IO.Path]::GetFullPath($outputRoot).TrimEnd('\')
    if (-not $fullPath.StartsWith($expectedParent + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理非发布目录：$fullPath"
    }
}

. (Join-Path $PSScriptRoot 'PublicReleaseFiles.ps1')

$checkArgs = @{}
if ($SkipBuild) { $checkArgs.SkipBuild = $true }
& (Join-Path $PSScriptRoot 'Test-PublicRelease.ps1') @checkArgs

$inventory = Get-PublicReleaseInventory -ProjectRoot $projectRoot
if ($inventory.MissingRequiredFiles.Count -gt 0) {
    throw "缺少公开文件：$($inventory.MissingRequiredFiles -join ', ')"
}

$projectXml = [xml](Get-Content -LiteralPath (Join-Path $projectRoot 'src\VoiceMemoryDemo.App\VoiceMemoryDemo.App.csproj') -Raw)
$version = [string]$projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw '无法从项目文件读取版本号。' }

$zipPath = Join-Path $outputRoot "VoiceMemo-X-$version-github-source.zip"
$manifestPath = Join-Path $outputRoot "VoiceMemo-X-$version-github-source.manifest.json"
$checksumPath = "$zipPath.sha256"

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
foreach ($path in @($stageRoot, $verifyRoot)) {
    Assert-SafeOutputPath -Path $path
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}
foreach ($path in @($zipPath, $manifestPath, $checksumPath)) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
}
New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null

$manifestFiles = [System.Collections.Generic.List[object]]::new()
foreach ($file in $inventory.Files) {
    $relative = $file.FullName.Substring($projectRoot.Length).TrimStart('\')
    $destination = Join-Path $stageRoot $relative
    $destinationParent = Split-Path -Parent $destination
    New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination -Force

    $sourceHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    $copiedHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
    if ($sourceHash -ne $copiedHash) { throw "复制后哈希不一致：$relative" }
    $manifestFiles.Add([ordered]@{
        path = $relative.Replace('\', '/')
        bytes = $file.Length
        sha256 = $sourceHash.ToLowerInvariant()
    })
}

$manifest = [ordered]@{
    packageType = 'github-source'
    product = 'VoiceMemo X / 速说速记X'
    version = $version
    generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    sourceOnly = $true
    modelWeightsIncluded = $false
    binariesIncluded = $false
    fileCount = $manifestFiles.Count
    files = $manifestFiles
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8

Compress-Archive -LiteralPath $stageRoot -DestinationPath $zipPath -CompressionLevel Optimal
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$zipHash  $([IO.Path]::GetFileName($zipPath))" | Set-Content -LiteralPath $checksumPath -Encoding ascii

New-Item -ItemType Directory -Path $verifyRoot -Force | Out-Null
Expand-Archive -LiteralPath $zipPath -DestinationPath $verifyRoot -Force
$verifiedRoot = Join-Path $verifyRoot $packageFolderName
if (-not (Test-Path -LiteralPath $verifiedRoot -PathType Container)) {
    throw 'ZIP 验证失败：缺少仓库根目录。'
}

$verifiedFiles = @(Get-ChildItem -LiteralPath $verifiedRoot -Recurse -File)
if ($verifiedFiles.Count -ne $manifestFiles.Count) {
    throw "ZIP 文件数不一致：预期 $($manifestFiles.Count)，实际 $($verifiedFiles.Count)"
}
foreach ($entry in $manifestFiles) {
    $verifiedPath = Join-Path $verifiedRoot ($entry.path.Replace('/', '\'))
    if (-not (Test-Path -LiteralPath $verifiedPath -PathType Leaf)) { throw "ZIP 缺少文件：$($entry.path)" }
    $verifiedHash = (Get-FileHash -LiteralPath $verifiedPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($verifiedHash -ne $entry.sha256) { throw "ZIP 文件哈希不一致：$($entry.path)" }
}

Assert-SafeOutputPath -Path $verifyRoot
Remove-Item -LiteralPath $verifyRoot -Recurse -Force

Write-Host "GITHUB_SOURCE_PACKAGE_PASS" -ForegroundColor Green
Write-Host "Folder:   $stageRoot"
Write-Host "ZIP:      $zipPath"
Write-Host "Manifest: $manifestPath"
Write-Host "SHA-256:  $zipHash"
