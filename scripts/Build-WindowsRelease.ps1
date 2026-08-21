[CmdletBinding()]
param(
    [switch]$AcknowledgeModelLicenseReview
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot 'src\VoiceMemoryDemo.App\VoiceMemoryDemo.App.csproj'
$projectXml = [xml](Get-Content -LiteralPath $projectPath -Raw)
$version = [string]$projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw '无法从项目文件读取版本号。' }
$publishDirectory = Join-Path $projectRoot 'publish\速说速记X-win-x64'
$releaseDirectory = Join-Path $projectRoot 'publish\release-assets'
$presentModels = @()
$embeddingModelRoot = Join-Path $projectRoot 'Models\multilingual-e5-small'
if (Test-Path -LiteralPath $embeddingModelRoot) {
    $presentModels += Get-ChildItem -LiteralPath $embeddingModelRoot -Recurse -File |
        Where-Object { $_.Name -notin @('model.onnx', 'LOCAL_MODEL_INFO.md', 'MODEL_CARD.md') }
}
foreach ($relative in @(
    'Models\speaker-router\silero_vad.onnx',
    'Models\speaker-router\3dspeaker_speech_campplus_sv_zh_en_16k-common_advanced.onnx',
    'Models\speaker-router\sherpa-onnx-pyannote-segmentation-3-0\model.onnx')) {
    $path = Join-Path $projectRoot $relative
    if (Test-Path -LiteralPath $path) { $presentModels += Get-Item -LiteralPath $path }
}
if ($presentModels.Count -gt 0 -and -not $AcknowledgeModelLicenseReview) {
    throw '检测到将进入发布包的模型权重。逐项确认来源、许可证与再分发权后，使用 -AcknowledgeModelLicenseReview 继续。'
}
$releaseFlavor = if ($presentModels.Count -gt 0) { 'win-x64' } else { 'win-x64-lite' }
$zipPath = Join-Path $releaseDirectory "VoiceMemo-X-$version-$releaseFlavor.zip"
$checksumPath = "$zipPath.sha256"

& (Join-Path $PSScriptRoot 'Test-PublicRelease.ps1')
& (Join-Path $projectRoot 'publish-win-x64.ps1')

New-Item -ItemType Directory -Force -Path $releaseDirectory | Out-Null
foreach ($relative in @('LICENSE', 'NOTICE', 'PRIVACY.md', 'THIRD_PARTY_NOTICES.md', 'ASSET_LICENSE.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $relative) -Destination $publishDirectory -Force
}

$publishedModelRoot = Join-Path $publishDirectory 'Models'
if (Test-Path -LiteralPath $publishedModelRoot) {
    $modelInventory = Get-ChildItem -LiteralPath $publishedModelRoot -Recurse -File | ForEach-Object {
        [ordered]@{
            path = $_.FullName.Substring($publishDirectory.Length).TrimStart('\').Replace('\', '/')
            bytes = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $inventoryJson = $modelInventory | ConvertTo-Json -Depth 4
    [IO.File]::WriteAllText(
        (Join-Path $publishDirectory 'model-inventory.json'),
        ($inventoryJson + [Environment]::NewLine),
        [Text.UTF8Encoding]::new($false))
}

$dependencyInventoryPath = Join-Path $publishDirectory 'dependency-inventory.json'
$dependencyJson = & dotnet list $projectPath package --include-transitive --format json
if ($LASTEXITCODE -ne 0) { throw '无法生成 NuGet 依赖清单。' }
[IO.File]::WriteAllText(
    $dependencyInventoryPath,
    (($dependencyJson -join [Environment]::NewLine) + [Environment]::NewLine),
    [Text.UTF8Encoding]::new($false))

$forbiddenPackagedNames = @('settings.dat', 'diagnostics.log', 'speaker-router.jsonl')
$forbiddenPackagedExtensions = @('.db', '.db-shm', '.db-wal', '.wav', '.mp3', '.m4a', '.flac', '.pfx', '.p12', '.key')
$forbiddenPackaged = @(Get-ChildItem -LiteralPath $publishDirectory -Recurse -File | Where-Object {
    $_.Name -in $forbiddenPackagedNames -or $_.Extension.ToLowerInvariant() -in $forbiddenPackagedExtensions
})
if ($forbiddenPackaged.Count -gt 0) {
    throw "发布目录含用户数据或密钥类文件：$($forbiddenPackaged.FullName -join ', ')"
}

$releaseManifest = Get-ChildItem -LiteralPath $publishDirectory -Recurse -File | ForEach-Object {
    [ordered]@{
        path = $_.FullName.Substring($publishDirectory.Length).TrimStart('\').Replace('\', '/')
        bytes = $_.Length
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
[IO.File]::WriteAllText(
    (Join-Path $publishDirectory 'release-manifest.json'),
    (($releaseManifest | ConvertTo-Json -Depth 4) + [Environment]::NewLine),
    [Text.UTF8Encoding]::new($false))

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumText = "$hash  $(Split-Path -Leaf $zipPath)`n"
[IO.File]::WriteAllText($checksumPath, $checksumText, [Text.UTF8Encoding]::new($false))

Write-Host "发布包：$zipPath"
Write-Host "SHA-256：$hash"
Write-Host '脚本只生成本地文件，不上传 GitHub。'
