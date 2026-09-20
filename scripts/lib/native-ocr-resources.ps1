function Add-ExportDocNativeOcrResources {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$Configuration,
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Copies,
        [string]$OnnxRuntimePath,
        [string]$RustTarget,
        [switch]$SkipBuild
    )
    $ocrManifest = Join-Path $RepositoryRoot 'apps/exportdoc-ocr-rs/Cargo.toml'
    if (-not $SkipBuild) {
        $ocrArguments = @('build', '--locked', '--manifest-path', $ocrManifest)
        if ($Configuration -eq 'Release') { $ocrArguments += '--release' }
        if ($RustTarget) { $ocrArguments += @('--target', $RustTarget) }
        Invoke-ExportDocExternal -FilePath 'cargo' -Arguments $ocrArguments -WorkingDirectory $RepositoryRoot -DisplayName 'Build native Rust OCR worker'
    }
    $suffix = if ($env:OS -eq 'Windows_NT') { '.exe' } else { '' }
    $profile = $Configuration.ToLowerInvariant()
    $ocrTarget = if ($RustTarget) { Join-Path $env:CARGO_TARGET_DIR $RustTarget } else { $env:CARGO_TARGET_DIR }
    $Copies[(Join-Path $ocrTarget "$profile/exportdoc-ocr$suffix")] = "sidecar/ocr/exportdoc-ocr$suffix"
    $Copies[(Join-Path $RepositoryRoot 'apps/exportdoc-ocr-rs/README.md')] = 'sidecar/ocr/README.md'
    $platform = if ($env:OS -eq 'Windows_NT') { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
    $architecture = if ($RustTarget -like 'aarch64-*') { 'arm64' } elseif ($RustTarget -like 'x86_64-*') { 'x64' } else { [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant() }
    $runtimeName = if ($platform -eq 'win') { 'onnxruntime.dll' } elseif ($platform -eq 'osx') { 'libonnxruntime.dylib' } else { 'libonnxruntime.so' }
    if ([string]::IsNullOrWhiteSpace($OnnxRuntimePath)) {
        $OnnxRuntimePath = Get-ExportDocNativeRuntimeLibrary -RepositoryRoot $RepositoryRoot -PackageId 'microsoft.ml.onnxruntime' -RuntimeIdentifier "$platform-$architecture" -LibraryName $runtimeName
    }
    Assert-NativePackagePath -Path $OnnxRuntimePath
    if (-not (Test-Path -LiteralPath $OnnxRuntimePath -PathType Leaf)) { throw 'Pass -OnnxRuntimePath with the governed native library from the locked ONNX Runtime package.' }
    $Copies[$OnnxRuntimePath] = "sidecar/ocr/$runtimeName"
    foreach ($name in @('LICENSE', 'ThirdPartyNotices.txt')) {
        $notice = Join-Path (Split-Path -Parent $OnnxRuntimePath) $name
        if (-not (Test-Path -LiteralPath $notice -PathType Leaf)) { throw "OCR native library is missing $name; use the governed package resource extractor." }
        $Copies[$notice] = "sidecar/ocr/$name"
    }
    $providerName = if ($platform -eq 'win') { 'onnxruntime_providers_shared.dll' } elseif ($platform -eq 'osx') { 'libonnxruntime_providers_shared.dylib' } else { 'libonnxruntime_providers_shared.so' }
    $provider = Join-Path (Split-Path -Parent $OnnxRuntimePath) $providerName
    if (Test-Path -LiteralPath $provider -PathType Leaf) { $Copies[$provider] = "sidecar/ocr/$providerName" }
    foreach ($name in @('det/inference.onnx', 'det/inference.yml', 'rec/inference.onnx', 'rec/inference.yml', 'MODEL_INFO.txt', 'THIRD_PARTY_NOTICES.md')) {
        $Copies[(Join-Path $RepositoryRoot "OcrModels/PaddleOCR/V6/$name")] = "OcrModels/PaddleOCR/V6/$name"
    }
    if ($platform -eq 'win' -and $architecture -eq 'x64') {
        $crtRoot = Join-Path $RepositoryRoot '.codex-runtime/native-ocr-crt'
        Invoke-ExportDocExternal -FilePath 'pwsh' -Arguments @('-NoProfile', '-File', (Join-Path $RepositoryRoot 'scripts/provision-visual-cpp-runtime.ps1'), '-DestinationDirectory', $crtRoot) -WorkingDirectory $RepositoryRoot -DisplayName 'Prepare governed app-local OCR runtime'
        $crt = Get-Content -LiteralPath (Join-Path $crtRoot 'msvc-runtime.json') -Raw | ConvertFrom-Json
        foreach ($name in (@($crt.files | ForEach-Object { $_.name }) + @('msvc-runtime.json', 'MSVC_RUNTIME_NOTICES.md'))) {
            $Copies[(Join-Path $crtRoot $name)] = "sidecar/ocr/$name"
        }
    }
}
