[CmdletBinding()]
param(
    [string]$PackagePath,
    [switch]$Build
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Split-Path -Parent $PSScriptRoot)).Path.TrimEnd('\')
$outputRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'publish\github-source')).TrimEnd('\')
$projectXml = [xml](Get-Content -LiteralPath (Join-Path $projectRoot 'src\VoiceMemoryDemo.App\VoiceMemoryDemo.App.csproj') -Raw)
$version = [string]$projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    $PackagePath = Join-Path $outputRoot "VoiceMemo-X-$version-github-source.zip"
}
$PackagePath = [IO.Path]::GetFullPath($PackagePath)
if (-not $PackagePath.StartsWith($outputRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "只允许检查项目的 GitHub 源码包目录：$PackagePath"
}
if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) { throw "源码包不存在：$PackagePath" }

$manifestPath = [IO.Path]::ChangeExtension($PackagePath, '.manifest.json')
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "manifest 不存在：$manifestPath" }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.packageType -ne 'github-source' -or -not $manifest.sourceOnly -or $manifest.modelWeightsIncluded -or $manifest.binariesIncluded) {
    throw 'manifest 的源码发布边界不正确。'
}

$verifyRoot = Join-Path $outputRoot '_package-test'
$verifyFull = [IO.Path]::GetFullPath($verifyRoot).TrimEnd('\')
if (-not $verifyFull.StartsWith($outputRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "拒绝使用非发布验证目录：$verifyFull"
}
if (Test-Path -LiteralPath $verifyFull) { Remove-Item -LiteralPath $verifyFull -Recurse -Force }
New-Item -ItemType Directory -Path $verifyFull -Force | Out-Null

try {
    Expand-Archive -LiteralPath $PackagePath -DestinationPath $verifyFull -Force
    $repoRoot = Join-Path $verifyFull 'voice-memory-demo'
    if (-not (Test-Path -LiteralPath $repoRoot -PathType Container)) { throw 'ZIP 缺少 voice-memory-demo 根目录。' }

    $actualFiles = @(Get-ChildItem -LiteralPath $repoRoot -Recurse -File)
    if ($actualFiles.Count -ne [int]$manifest.fileCount) {
        throw "文件数不一致：manifest=$($manifest.fileCount)，ZIP=$($actualFiles.Count)"
    }
    foreach ($entry in $manifest.files) {
        $path = Join-Path $repoRoot ([string]$entry.path).Replace('/', '\')
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "ZIP 缺少文件：$($entry.path)" }
        $item = Get-Item -LiteralPath $path
        if ($item.Length -ne [long]$entry.bytes) { throw "文件长度不一致：$($entry.path)" }
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne [string]$entry.sha256) { throw "文件哈希不一致：$($entry.path)" }
    }

    $forbidden = @($actualFiles | Where-Object {
        $_.Extension.ToLowerInvariant() -in @('.onnx', '.wav', '.mp3', '.mp4', '.zip', '.db', '.key', '.pfx', '.p12', '.pem') -or
        $_.Name -in @('settings.dat', 'diagnostics.log', 'speaker-router.jsonl')
    })
    if ($forbidden.Count -gt 0) { throw "源码包包含禁止文件：$($forbidden.Name -join ', ')" }

    if ($Build) {
        & dotnet build (Join-Path $repoRoot 'VoiceMemoryDemo.sln') -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw "源码包干净构建失败，退出码 $LASTEXITCODE" }
    }

    Write-Host "GITHUB_SOURCE_PACKAGE_VERIFY_PASS files=$($manifest.fileCount) build=$Build" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $verifyFull) { Remove-Item -LiteralPath $verifyFull -Recurse -Force }
}

