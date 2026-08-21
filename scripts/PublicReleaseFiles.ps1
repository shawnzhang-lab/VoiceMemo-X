Set-StrictMode -Version Latest

function Get-PublicReleaseInventory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$ProjectRoot
    )

    $resolvedRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path.TrimEnd('\')
    $rootFiles = @(
        '.editorconfig', '.gitattributes', '.gitignore', 'ASSET_LICENSE.md', 'CHANGELOG.md', 'CONTRIBUTING.md',
        'LICENSE', 'NOTICE', 'PRIVACY.md', 'README.md', 'README.en.md',
        'RELEASE_CHECKLIST.md', 'SECURITY.md', 'THIRD_PARTY_NOTICES.md', 'VoiceMemoryDemo.sln',
        'build-android-demo.ps1', 'publish-win-x64.ps1', 'run-demo.ps1'
    )
    $publicRoots = @('.github', 'android', 'docs', 'Models', 'scripts', 'src', 'tests')
    $excludedPathPattern = '(?i)(^|[\\/])(bin|obj|build|publish|artifacts|test-data|test-results|tmp|tools|CharacterCandidates|__pycache__|\.gradle|\.idea|\.vs|\.vscode)([\\/]|$)'
    $excludedTestPattern = '(?i)^tests[\\/](VoiceMemoryDemo\.EnglishAsrBenchmark(?:V22(?:\.Evaluator)?)?|VoiceMemoryDemo\.EnglishAsrDictionaryDevReplay|VoiceMemoryDemo\.EnglishE2E)([\\/]|$)|^tests[\\/][^\\/]+\.py$'

    $missing = [System.Collections.Generic.List[string]]::new()
    $candidateFiles = [System.Collections.Generic.List[System.IO.FileInfo]]::new()

    foreach ($relative in $rootFiles) {
        $path = Join-Path $resolvedRoot $relative
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $candidateFiles.Add((Get-Item -LiteralPath $path))
        }
        else {
            $missing.Add($relative)
        }
    }

    foreach ($relativeRoot in $publicRoots) {
        $path = Join-Path $resolvedRoot $relativeRoot
        if (-not (Test-Path -LiteralPath $path -PathType Container)) { continue }

        foreach ($file in Get-ChildItem -LiteralPath $path -Recurse -File) {
            $relative = $file.FullName.Substring($resolvedRoot.Length).TrimStart('\')
            if ($relative -match $excludedPathPattern -or $relative -match $excludedTestPattern) { continue }
            if ($file.Name -eq 'local.properties') { continue }
            if ($relativeRoot -eq 'Models' -and $file.Extension -ne '.md' -and $file.Name -ne 'LICENSE') { continue }
            $candidateFiles.Add($file)
        }
    }

    [pscustomobject]@{
        ProjectRoot = $resolvedRoot
        Files = @($candidateFiles | Sort-Object FullName -Unique)
        MissingRequiredFiles = @($missing)
        ExcludedPathPattern = $excludedPathPattern
        ExcludedTestPattern = $excludedTestPattern
    }
}

