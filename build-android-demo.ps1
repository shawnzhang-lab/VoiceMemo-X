$ErrorActionPreference = 'Stop'

$projectRoot = $PSScriptRoot
$androidProject = Join-Path $projectRoot 'android\VoiceMemoryDemo.Android'
$toolRoot = Join-Path $projectRoot 'tools\android-build'
$sdkRoot = Join-Path $toolRoot 'sdk'
$gradleRoot = Join-Path $toolRoot 'gradle-8.9'
$downloadRoot = Join-Path $toolRoot 'downloads'
$publishRoot = Join-Path $projectRoot 'publish\android'

New-Item -ItemType Directory -Force -Path $toolRoot, $downloadRoot, $publishRoot | Out-Null

function Get-TrustedDownload {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    & curl.exe --fail --location --retry 6 --retry-all-errors --continue-at - --output $Destination $Url
    if ($LASTEXITCODE -ne 0) {
        throw "下载失败：$Url"
    }
}

if ([string]::IsNullOrWhiteSpace($env:JAVA_HOME) -or
    -not (Test-Path -LiteralPath (Join-Path $env:JAVA_HOME 'bin\java.exe') -PathType Leaf)) {
    throw '请先安装 JDK 17，并把 JAVA_HOME 指向 JDK 根目录。为保证供应链可复现，本脚本不会从 latest 地址自动下载 JDK。'
}

$javaExe = Join-Path $env:JAVA_HOME 'bin\java.exe'
$javaVersionOutput = & cmd.exe /d /c "`"$javaExe`" -version 2>&1" | Out-String
if ($LASTEXITCODE -ne 0) { throw "无法读取 JAVA_HOME 中的 Java 版本：$javaVersionOutput" }
if ($javaVersionOutput -notmatch 'version "17(?:\.|\")') {
    throw "Android Demo 需要 JDK 17；当前 JAVA_HOME 不是 JDK 17：$javaVersionOutput"
}

if (-not (Test-Path (Join-Path $sdkRoot 'cmdline-tools\latest\bin\sdkmanager.bat'))) {
    $toolsZip = Join-Path $downloadRoot 'android-command-line-tools.zip'
    if (-not (Test-Path $toolsZip)) {
        Write-Host '正在下载 Android 命令行工具…'
        Get-TrustedDownload 'https://dl.google.com/android/repository/commandlinetools-win-15859902_latest.zip' $toolsZip
    }
    $toolsHash = (Get-FileHash -LiteralPath $toolsZip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($toolsHash -ne '90ae805d20434428bffcb699c290860f19bb5f66a67e6b330067e3de801fb04a') {
        throw "Android 命令行工具校验失败：$toolsHash"
    }
    $toolsExtract = Join-Path $toolRoot 'android-tools-extract'
    if (Test-Path $toolsExtract) { Remove-Item -LiteralPath $toolsExtract -Recurse -Force }
    Expand-Archive $toolsZip -DestinationPath $toolsExtract -Force
    $latestRoot = Join-Path $sdkRoot 'cmdline-tools\latest'
    New-Item -ItemType Directory -Force -Path (Split-Path $latestRoot) | Out-Null
    Move-Item -LiteralPath (Join-Path $toolsExtract 'cmdline-tools') -Destination $latestRoot
    Remove-Item -LiteralPath $toolsExtract -Recurse -Force
}

if (-not (Test-Path (Join-Path $gradleRoot 'bin\gradle.bat'))) {
    $gradleZip = Join-Path $downloadRoot 'gradle-8.9-bin-verified.zip'
    if (-not (Test-Path $gradleZip)) {
        Write-Host '正在下载 Gradle 8.9…'
        # Tencent's public mirror avoids the official redirect through GitHub,
        # which is frequently slow on mainland networks. The package is still
        # verified against Gradle's official SHA-256 before extraction.
        Get-TrustedDownload 'https://mirrors.cloud.tencent.com/gradle/gradle-8.9-bin.zip' $gradleZip
    }
    $gradleHash = (Get-FileHash -LiteralPath $gradleZip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($gradleHash -ne 'd725d707bfabd4dfdc958c624003b3c80accc03f7037b5122c4b1d0ef15cecab') {
        throw "Gradle 压缩包校验失败：$gradleHash"
    }
    Expand-Archive $gradleZip -DestinationPath $toolRoot -Force
}

$env:ANDROID_HOME = $sdkRoot
$env:ANDROID_SDK_ROOT = $sdkRoot
$sdkManager = Join-Path $sdkRoot 'cmdline-tools\latest\bin\sdkmanager.bat'

# sdkmanager reads license answers directly from a console stream on Windows.
# A cmd loop is more reliable here than piping a PowerShell string.
$licenseCommand = "(for /l %i in (1,1,20) do @echo y) | `"$sdkManager`" --sdk_root=`"$sdkRoot`" --licenses >nul"
& cmd.exe /d /c $licenseCommand
if ($LASTEXITCODE -ne 0) { throw 'Android SDK 许可确认失败。' }
& $sdkManager --sdk_root=$sdkRoot 'platform-tools' 'platforms;android-35' 'build-tools;35.0.0'
if ($LASTEXITCODE -ne 0) { throw 'Android SDK 组件安装失败。' }

$gradle = Join-Path $gradleRoot 'bin\gradle.bat'
& $gradle --project-dir $androidProject clean assembleDebug
if ($LASTEXITCODE -ne 0) { throw 'Android APK 编译失败。' }

$sourceApk = Join-Path $androidProject 'app\build\outputs\apk\debug\app-debug.apk'
$targetApk = Join-Path $publishRoot '速说速记X-android-demo.apk'
Copy-Item -LiteralPath $sourceApk -Destination $targetApk -Force
Write-Host "Android Demo 发布完成：$targetApk"
