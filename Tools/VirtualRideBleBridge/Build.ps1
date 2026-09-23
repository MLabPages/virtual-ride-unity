$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$metadata = Join-Path $env:WINDIR 'System32\WinMetadata'
$source = Join-Path $PSScriptRoot 'Program.cs'
$outputDirectory = Join-Path $projectRoot 'Assets\StreamingAssets\Bluetooth'
$output = Join-Path $outputDirectory 'VirtualRideBleBridge.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "C# compiler not found: $compiler"
}
foreach ($name in @('Windows.Devices.winmd', 'Windows.Foundation.winmd', 'Windows.Storage.winmd')) {
    if (-not (Test-Path -LiteralPath (Join-Path $metadata $name))) {
        throw "Windows Runtime metadata not found: $(Join-Path $metadata $name)"
    }
}

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
& $compiler /nologo /target:exe /platform:x64 /optimize+ `
    "/out:$output" `
    "/reference:$(Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\System.dll')" `
    "/reference:$(Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\System.Core.dll')" `
    "/reference:$(Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\System.Runtime.WindowsRuntime.dll')" `
    "/reference:$(Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\System.Runtime.InteropServices.WindowsRuntime.dll')" `
    "/reference:$(Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\System.Runtime.dll')" `
    "/reference:$(Join-Path $metadata 'Windows.Devices.winmd')" `
    "/reference:$(Join-Path $metadata 'Windows.Foundation.winmd')" `
    "/reference:$(Join-Path $metadata 'Windows.Storage.winmd')" `
    $source
if ($LASTEXITCODE -ne 0) {
    throw "BLE bridge compile failed with exit code $LASTEXITCODE"
}
Write-Host "Built $output"
