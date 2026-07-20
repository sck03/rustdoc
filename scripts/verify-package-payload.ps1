[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackageRoot,
    [Parameter(Mandatory = $true)][ValidateSet("Desktop", "Server", "Container")][string]$Profile,
    [Parameter(Mandatory = $true)][string]$RuntimeIdentifier
)

$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath($PackageRoot)
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "Package root does not exist: $root" }

$runtimeName = if ($RuntimeIdentifier.StartsWith("win-")) { "onnxruntime.dll" } elseif ($RuntimeIdentifier.StartsWith("osx-")) { "libonnxruntime.dylib" } else { "libonnxruntime.so" }
$runtimeFiles = @(Get-ChildItem -LiteralPath $root -File -Recurse | Where-Object Name -eq $runtimeName)
if ($runtimeFiles.Count -ne 1) { throw "Expected exactly one shared $runtimeName, found $($runtimeFiles.Count)." }

foreach ($relative in @("OcrModels/PaddleOCR/V6/det/inference.onnx", "OcrModels/PaddleOCR/V6/rec/inference.onnx")) {
    $matches = @(Get-ChildItem -LiteralPath $root -File -Recurse | Where-Object { $_.FullName.Replace('\','/').EndsWith($relative, [StringComparison]::OrdinalIgnoreCase) })
    if ($matches.Count -ne 1) { throw "Expected exactly one OCR model '$relative', found $($matches.Count)." }
}

$browserRoots = @(Get-ChildItem -LiteralPath $root -Directory -Recurse | Where-Object Name -eq "Browsers")
if ($Profile -eq "Container") {
    if ($browserRoots.Count -ne 0) { throw "Container payload must use system Chromium and must not bundle Browsers/." }
} elseif ($browserRoots.Count -ne 1) {
    throw "$Profile payload must contain exactly one Browsers root, found $($browserRoots.Count)."
}

if ($Profile -ne "Container") {
    $browserRoot = $browserRoots[0].FullName
    $expectedPlatform = switch ($RuntimeIdentifier) {
        "win-x64" { "win64" }
        "linux-x64" { "linux64" }
        "linux-arm64" { "ChromiumArm64" }
        "osx-x64" { "mac-x64" }
        "osx-arm64" { "mac-arm64" }
        default { throw "No browser payload mapping for $RuntimeIdentifier." }
    }
    $platformDirectories = if ($RuntimeIdentifier -eq "linux-arm64") {
        @(Get-ChildItem -LiteralPath $browserRoot -Directory | Where-Object Name -eq "ChromiumArm64")
    } else {
        $chromeRoot = Join-Path $browserRoot "ChromeForTesting"
        if (-not (Test-Path -LiteralPath $chromeRoot -PathType Container)) { throw "ChromeForTesting root is missing for $RuntimeIdentifier." }
        @(Get-ChildItem -LiteralPath $chromeRoot -Directory)
    }
    if ($platformDirectories.Count -ne 1 -or $platformDirectories[0].Name -ne $expectedPlatform) {
        throw "Browser payload must contain only '$expectedPlatform'; found '$($platformDirectories.Name -join ', ')'."
    }
}

$duplicateNativeNames = @(Get-ChildItem -LiteralPath $root -File -Recurse |
    Where-Object { $_.Name -match '^(onnxruntime\.dll|libonnxruntime(?:\.[0-9.]+)?\.(?:so|dylib))$' } |
    Group-Object Name | Where-Object Count -gt 1)
if ($duplicateNativeNames.Count) { throw "Duplicate ONNX Runtime native files: $($duplicateNativeNames.Name -join ', ')" }

$forbiddenPayload = @(Get-ChildItem -LiteralPath $root -File -Recurse | Where-Object {
    $_.Extension -eq ".pdb" -or $_.Name -match 'onnxruntime_providers_shared|^Microsoft\.ML\.OnnxRuntime\.dll$|^onnxruntime\.lib$'
})
if ($forbiddenPayload.Count) { throw "Release payload contains removable debug or duplicate ONNX files: $($forbiddenPayload.FullName -join '; ')" }

$files = @(Get-ChildItem -LiteralPath $root -File -Recurse)
$bytes = ($files | Measure-Object Length -Sum).Sum
Write-Host "Package payload verified: Profile=$Profile RID=$RuntimeIdentifier Files=$($files.Count) Bytes=$bytes SharedOnnxRuntime=$($runtimeFiles[0].FullName)"
