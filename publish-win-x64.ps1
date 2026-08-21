$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'src\VoiceMemoryDemo.App\VoiceMemoryDemo.App.csproj'
$publishRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'publish'))
$publishPath = [IO.Path]::GetFullPath((Join-Path $publishRoot '速说速记X-win-x64'))

if (-not $publishPath.StartsWith($publishRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "发布目录越界：$publishPath"
}
if (Test-Path -LiteralPath $publishPath) {
    Remove-Item -LiteralPath $publishPath -Recurse -Force
}

dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishPath `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

Write-Host "发布完成：$publishPath\速说速记X.exe"
