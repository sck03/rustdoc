param([Parameter(Mandatory = $true)][string]$BrowserRoot)
$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$isWindowsPlatform = [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [Runtime.InteropServices.OSPlatform]::Windows)
$pathComparison = if ($isWindowsPlatform) {
    [System.StringComparison]::OrdinalIgnoreCase
} else {
    [System.StringComparison]::Ordinal
}

function Assert-RepositoryChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Purpose
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $repositoryPrefix = $repoRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($repositoryPrefix, $pathComparison)) {
        throw "$Purpose must stay inside the repository workspace. Resolved path: $fullPath"
    }

    return $fullPath
}

$root = Assert-RepositoryChildPath -Path $BrowserRoot -Purpose "Bundled browser root"
if (-not (Test-Path -LiteralPath $root -PathType Container)) {
    throw "Bundled browser root does not exist: $root"
}
$workRoot = Assert-RepositoryChildPath -Path (Join-Path $repoRoot ".codex-runtime/browser-pdf-check") -Purpose "Browser PDF verification workspace"
$work = Assert-RepositoryChildPath -Path (Join-Path $workRoot ([Guid]::NewGuid().ToString("N"))) -Purpose "Browser PDF verification run"
$browserCandidates = @(Get-ChildItem -LiteralPath $root -File -Recurse -ErrorAction Stop |
    Where-Object { $_.Name -in @("chrome-headless-shell.exe", "chrome-headless-shell", "chrome", "chromium") } |
    Sort-Object FullName)
if ($browserCandidates.Count -ne 1) {
    throw "Expected exactly one bundled browser executable under $root, found $($browserCandidates.Count)."
}
$browser = $browserCandidates[0]
$transientLogNames = @("chrome_debug.log", "debug.log")
$existingTransientLogs = @(Get-ChildItem -LiteralPath $root -File -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -in $transientLogNames } |
    Select-Object -ExpandProperty FullName)
if (-not $isWindowsPlatform) {
    & chmod +x -- $browser.FullName
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to make the bundled browser executable: $($browser.FullName)"
    }
}
New-Item -ItemType Directory -Path $work -Force | Out-Null
$process = $null
try {
    $html = Join-Path $work "print-test.html"
    $pdf = Join-Path $work "print-test.pdf"
    $profile = Join-Path $work "browser-profile"
    $log = Join-Path $work "browser.log"
    New-Item -ItemType Directory -Path $profile -Force | Out-Null
    '<!doctype html><meta charset="utf-8"><style>@page{size:A4;margin:12mm}body{font-family:sans-serif}</style><h1>ExportDocManager PDF</h1><p>Bundled browser verification</p>' | Set-Content -LiteralPath $html -Encoding UTF8
    $uri = [Uri]::new($html).AbsoluteUri
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $browser.FullName
    $startInfo.WorkingDirectory = $work
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.Environment["CHROME_LOG_FILE"] = $log
    @(
        "--headless",
        "--no-sandbox",
        "--disable-gpu",
        "--disable-dev-shm-usage",
        "--disable-logging",
        "--disable-breakpad",
        "--user-data-dir=$profile",
        "--print-to-pdf=$pdf",
        $uri
    ) | ForEach-Object { [void]$startInfo.ArgumentList.Add($_) }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    [void]$process.Start()
    $standardOutput = $process.StandardOutput.ReadToEndAsync()
    $standardError = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(60000)) {
        try { $process.Kill($true) } catch { }
        [void]$process.WaitForExit(5000)
        throw "Bundled browser PDF verification timed out after 60 seconds."
    }
    $output = $standardOutput.GetAwaiter().GetResult()
    $errorOutput = $standardError.GetAwaiter().GetResult()
    if (-not [string]::IsNullOrWhiteSpace($output)) { Write-Host $output.Trim() }
    if ($process.ExitCode -ne 0) {
        throw "Bundled browser failed to generate a PDF (exit $($process.ExitCode)): $($errorOutput.Trim())"
    }
    if (-not (Test-Path -LiteralPath $pdf -PathType Leaf) -or (Get-Item -LiteralPath $pdf).Length -lt 1000) {
        throw "Bundled browser failed to generate a valid-sized PDF."
    }
    $signature = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($pdf), 0, 5)
    if ($signature -ne '%PDF-') { throw "Bundled browser output is not a valid PDF." }
    Write-Host "Bundled browser PDF verification passed: $($browser.FullName)"
} finally {
    if ($null -ne $process) {
        $process.Dispose()
    }
    Get-ChildItem -LiteralPath $root -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -in $transientLogNames -and $_.FullName -notin $existingTransientLogs } |
        Remove-Item -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
