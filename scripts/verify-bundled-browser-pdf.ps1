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

$root = [System.IO.Path]::GetFullPath($BrowserRoot)
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
if (-not $isWindowsPlatform) { & chmod +x -- $browser.FullName }
New-Item -ItemType Directory -Path $work -Force | Out-Null
try {
    $html = Join-Path $work "print-test.html"; $pdf = Join-Path $work "print-test.pdf"
    '<!doctype html><meta charset="utf-8"><style>@page{size:A4;margin:12mm}body{font-family:sans-serif}</style><h1>ExportDocManager PDF</h1><p>Bundled browser verification</p>' | Set-Content -LiteralPath $html -Encoding UTF8
    $uri = [Uri]::new($html).AbsoluteUri
    & $browser.FullName --headless --no-sandbox --disable-gpu --disable-dev-shm-usage "--print-to-pdf=$pdf" $uri
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $pdf -PathType Leaf) -or (Get-Item -LiteralPath $pdf).Length -lt 1000) { throw "Bundled browser failed to generate a PDF." }
    $signature = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($pdf), 0, 5)
    if ($signature -ne '%PDF-') { throw "Bundled browser output is not a valid PDF." }
    Write-Host "Bundled browser PDF verification passed: $($browser.FullName)"
} finally { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
