function Add-ExportDocNativeOcrResources {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$Configuration,
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Copies,
        [string]$OnnxRuntimePath,
        [switch]$SkipBuild
    )
    $ocrManifest = Join-Path $RepositoryRoot 'apps/exportdoc-ocr-rs/Cargo.toml'
    if (-not $SkipBuild) {
        $ocrArguments = @('build', '--locked', '--manifest-path', $ocrManifest)
        if ($Configuration -eq 'Release') { $ocrArguments += '--release' }
        Invoke-ExportDocExternal -FilePath 'cargo' -Arguments $ocrArguments -WorkingDirectory $RepositoryRoot -DisplayName 'Build native Rust OCR worker'
    }
    $suffix = if ($env:OS -eq 'Windows_NT') { '.exe' } else { '' }
    $profile = $Configuration.ToLowerInvariant()
    $Copies[(Join-Path $env:CARGO_TARGET_DIR "$profile/exportdoc-ocr$suffix")] = "sidecar/ocr/exportdoc-ocr$suffix"
    $Copies[(Join-Path $RepositoryRoot 'apps/exportdoc-ocr-rs/README.md')] = 'sidecar/ocr/README.md'
    $platform = if ($env:OS -eq 'Windows_NT') { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
    $runtimeName = if ($platform -eq 'win') { 'onnxruntime.dll' } elseif ($platform -eq 'osx') { 'libonnxruntime.dylib' } else { 'libonnxruntime.so' }
    if ([string]::IsNullOrWhiteSpace($OnnxRuntimePath)) {
        $nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $RepositoryRoot '.codex-runtime/nuget-packages' }
        $lockedPackages = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'src/ExportDocManager.Infrastructure.PdfOcr/packages.lock.json') -Raw | ConvertFrom-Json
        $versions = @($lockedPackages.dependencies.PSObject.Properties | ForEach-Object {
            $_.Value.PSObject.Properties | Where-Object { $_.Name -ieq 'Microsoft.ML.OnnxRuntime' } | ForEach-Object { $_.Value.resolved }
        } | Select-Object -Unique)
        if ($versions.Count -ne 1) { throw 'ONNX Runtime must have one governed exact version.' }
        $OnnxRuntimePath = Join-Path $nugetRoot "microsoft.ml.onnxruntime/$($versions[0])/runtimes/$platform-$architecture/native/$runtimeName"
    }
    Assert-NativePackagePath -Path $OnnxRuntimePath
    if (-not (Test-Path -LiteralPath $OnnxRuntimePath -PathType Leaf)) { throw 'Pass -OnnxRuntimePath with the governed native library from the locked ONNX Runtime package.' }
    $Copies[$OnnxRuntimePath] = "sidecar/ocr/$runtimeName"
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
