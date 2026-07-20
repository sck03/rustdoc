param([Parameter(Mandatory = $true)][string]$BrowserRoot)
$ErrorActionPreference = "Stop"
$root = [System.IO.Path]::GetFullPath($BrowserRoot)
$browser = Get-ChildItem $root -File -Recurse -ErrorAction Stop |
    Where-Object { $_.Name -in @("chrome-headless-shell.exe", "chrome-headless-shell", "chrome", "chromium") } |
    Select-Object -First 1
if ($null -eq $browser) { throw "Bundled browser executable was not found under $root" }
if (-not $IsWindows) { & chmod +x -- $browser.FullName }
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("exportdoc-browser-pdf-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $work -Force | Out-Null
try {
    $html = Join-Path $work "print-test.html"; $pdf = Join-Path $work "print-test.pdf"
    '<!doctype html><meta charset="utf-8"><style>@page{size:A4;margin:12mm}body{font-family:sans-serif}</style><h1>ExportDocManager PDF</h1><p>Chromium ARM64 browser verification</p>' | Set-Content $html -Encoding UTF8
    $uri = [Uri]::new($html).AbsoluteUri
    & $browser.FullName --headless --no-sandbox --disable-gpu --disable-dev-shm-usage "--print-to-pdf=$pdf" $uri
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $pdf) -or (Get-Item $pdf).Length -lt 1000) { throw "Bundled browser failed to generate a PDF." }
    $signature = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($pdf), 0, 5)
    if ($signature -ne '%PDF-') { throw "Bundled browser output is not a valid PDF." }
    Write-Host "Bundled browser PDF verification passed: $($browser.FullName)"
} finally { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
