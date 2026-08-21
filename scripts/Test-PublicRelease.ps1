[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$ListFiles
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Split-Path -Parent $PSScriptRoot)).Path.TrimEnd('\')
$failures = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()

. (Join-Path $PSScriptRoot 'PublicReleaseFiles.ps1')
$inventory = Get-PublicReleaseInventory -ProjectRoot $projectRoot
$candidateFiles = $inventory.Files
$excludedPathPattern = $inventory.ExcludedPathPattern
$excludedTestPattern = $inventory.ExcludedTestPattern
foreach ($relative in $inventory.MissingRequiredFiles) {
    $failures.Add("缺少公开仓库必需文件：$relative")
}
$forbiddenExtensions = @(
    '.aac', '.avi', '.db', '.flac', '.jks', '.key', '.keystore', '.m4a', '.m4v',
    '.mov', '.mp3', '.mp4', '.onnx', '.p12', '.pem', '.pfx', '.rar', '.safetensors',
    '.tar', '.wav', '.wma', '.zip', '.7z'
)
$allowedExtensions = @(
    '.cs', '.csproj', '.gradle', '.ico', '.java', '.json', '.md', '.png', '.pro',
    '.properties', '.ps1', '.sln', '.txt', '.xaml', '.xml', '.yml', '.yaml'
)
$allowedExtensionlessNames = @('.editorconfig', '.gitattributes', '.gitignore', 'LICENSE', 'NOTICE')
$textExtensions = @(
    '.cs', '.csproj', '.gradle', '.json', '.md', '.ps1', '.pro', '.properties',
    '.sln', '.xml', '.xaml', '.yml', '.yaml', '.java', '.txt'
)
$secretPatterns = [ordered]@{
    'Tencent SecretID' = 'AKID[A-Za-z0-9]{12,}'
    'DeepSeek/OpenAI style key' = '(?<![A-Za-z0-9])sk-[A-Za-z0-9_-]{20,}'
    'GitHub token' = '(?<![A-Za-z0-9])(ghp|github_pat)_[A-Za-z0-9_]{20,}'
    'Private key block' = '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----'
    'Hard-coded application secret' = '(?i)(TencentSecretKey|TencentSecretId|DeepSeekApiKey)\s*=\s*["''][^"''<>]{8,}["'']'
    'Absolute Windows user path' = '(?i)C:\\Users\\[^\\/\s]+'
    'Chinese mobile number' = '(?<!\d)1[3-9]\d{9}(?!\d)'
}

foreach ($file in $candidateFiles) {
    $relative = $file.FullName.Substring($projectRoot.Length).TrimStart('\')
    if ($file.Length -gt 50MB) {
        $failures.Add("公开候选文件超过 50MB：$relative ($([math]::Round($file.Length / 1MB, 1)) MB)")
    }
    if ($allowedExtensions -notcontains $file.Extension.ToLowerInvariant() -and
        $allowedExtensionlessNames -notcontains $file.Name) {
        $failures.Add("公开候选包含未审核的文件类型：$relative")
    }
    if ($forbiddenExtensions -contains $file.Extension.ToLowerInvariant()) {
        $failures.Add("公开候选包含禁止的二进制/用户数据：$relative")
    }
    if ($textExtensions -notcontains $file.Extension.ToLowerInvariant() -and $file.Name -notin @('LICENSE', 'NOTICE')) {
        continue
    }
    $content = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction Stop
    foreach ($entry in $secretPatterns.GetEnumerator()) {
        if ($content -match $entry.Value) {
            $failures.Add("疑似 $($entry.Key)：$relative")
        }
    }
}

& (Join-Path $PSScriptRoot 'Test-MarkdownLinks.ps1')

$gitDir = Join-Path $projectRoot '.git'
if (Test-Path -LiteralPath $gitDir) {
    $candidateSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $candidateFiles) {
        [void]$candidateSet.Add($file.FullName.Substring($projectRoot.Length).TrimStart('\').Replace('\', '/'))
    }
    $tracked = & git -c core.quotepath=false -C $projectRoot ls-files
    if ($LASTEXITCODE -ne 0) {
        $failures.Add('无法读取 Git 跟踪清单。')
        $tracked = @()
    }
    foreach ($relative in $tracked) {
        if (-not $candidateSet.Contains($relative.Replace('\', '/'))) {
            $failures.Add("Git 已跟踪但不在公开 allowlist：$relative")
        }
        if ($relative -match $excludedPathPattern -or $relative -match $excludedTestPattern) {
            $failures.Add("Git 已跟踪应排除内容：$relative")
        }
        $extension = [IO.Path]::GetExtension($relative).ToLowerInvariant()
        if ($forbiddenExtensions -contains $extension) {
            $failures.Add("Git 已跟踪禁止文件：$relative")
        }
    }
}
else {
    $warnings.Add('当前目录尚未初始化 Git；已按公开 allowlist 扫描，初始化后需再次运行。')
}

if (-not $SkipBuild) {
    & dotnet build (Join-Path $projectRoot 'VoiceMemoryDemo.sln') -c Release
    if ($LASTEXITCODE -ne 0) {
        $failures.Add("Release 构建失败，退出码 $LASTEXITCODE")
    }
}

$candidateBytes = ($candidateFiles | Measure-Object Length -Sum).Sum
Write-Host "公开候选：$($candidateFiles.Count) 个文件，$([math]::Round($candidateBytes / 1MB, 2)) MB"
if ($ListFiles) {
    $candidateFiles | ForEach-Object {
        Write-Host ('  ' + $_.FullName.Substring($projectRoot.Length).TrimStart('\'))
    }
}
foreach ($warning in $warnings) { Write-Warning $warning }

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Error $failure -ErrorAction Continue }
    throw "公开发布检查失败：$($failures.Count) 项。"
}

Write-Host 'PUBLIC_RELEASE_CHECK_PASS' -ForegroundColor Green
