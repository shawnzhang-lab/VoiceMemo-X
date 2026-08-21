$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'src\VoiceMemoryDemo.App\VoiceMemoryDemo.App.csproj'
dotnet run --project $projectPath
