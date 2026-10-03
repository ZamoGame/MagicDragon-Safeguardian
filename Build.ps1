[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'Build')
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Ensure-Directory([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        [void](New-Item -ItemType Directory -Path $Path -Force)
    }
}

function Invoke-Compiler([string]$Compiler, [string[]]$Arguments, [string]$Description) {
    $output = & $Compiler @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw ("$Description failed:`r`n" + ($output -join "`r`n"))
    }
}

$sourceRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$mainSource = Join-Path $sourceRoot 'Source\MagicDragonSafeguardian.cs'
$uninstallSource = Join-Path $sourceRoot 'Source\MagicDragonUninstall.cs'
$engineSource = Join-Path $sourceRoot 'Engine\MagicDragon_Safeguardian.ps1'
$iconSource = Join-Path $sourceRoot 'Assets\MagicDragon.ico'
$mascotSource = Join-Path $sourceRoot 'Assets\MagicDragon.png'
$readmeSource = Join-Path $sourceRoot 'README.txt'
$licenseSource = Join-Path $sourceRoot 'LICENSE'

foreach ($required in @($mainSource, $uninstallSource, $engineSource, $iconSource, $mascotSource, $licenseSource)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required source file is missing: $required"
    }
}

$compilerCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if ($null -eq $compiler) {
    throw 'The .NET Framework 4 C# compiler was not found. Enable/install .NET Framework 4.x, then run BUILD.cmd again.'
}

Ensure-Directory $outputRoot
$mainExe = Join-Path $outputRoot 'MagicDragon_Safeguardian.exe'
$uninstallExe = Join-Path $outputRoot 'Uninstall_MagicDragon_Safeguardian.exe'
foreach ($target in @($mainExe, $uninstallExe)) {
    if (Test-Path -LiteralPath $target -PathType Leaf) { Remove-Item -LiteralPath $target -Force }
}

$mainArguments = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/codepage:65001',
    ("/win32icon:`"$iconSource`""), ("/out:`"$mainExe`""),
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll', '/reference:System.Web.Extensions.dll',
    '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll',
    ("`"$mainSource`"")
)
Invoke-Compiler $compiler $mainArguments 'Main application compilation'

$uninstallArguments = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/codepage:65001',
    ("/win32icon:`"$iconSource`""), ("/out:`"$uninstallExe`""),
    '/reference:System.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll',
    ("`"$uninstallSource`"")
)
Invoke-Compiler $compiler $uninstallArguments 'Uninstaller compilation'

Copy-Item -LiteralPath $engineSource -Destination (Join-Path $outputRoot 'MagicDragon_Safeguardian.ps1') -Force
Copy-Item -LiteralPath $iconSource -Destination (Join-Path $outputRoot 'MagicDragon.ico') -Force
Copy-Item -LiteralPath $mascotSource -Destination (Join-Path $outputRoot 'MagicDragon.png') -Force
if (Test-Path -LiteralPath $readmeSource -PathType Leaf) {
    Copy-Item -LiteralPath $readmeSource -Destination (Join-Path $outputRoot 'README.txt') -Force
}
Copy-Item -LiteralPath $licenseSource -Destination (Join-Path $outputRoot 'LICENSE') -Force

$buildInfo = @(
    'MagicDragon Safeguardian 1.0.1 source build'
    "Built: $((Get-Date).ToString('o'))"
    "Compiler: $compiler"
    "Main source: $mainSource"
    "Engine source: $engineSource"
    ''
    'For a complete per-user installation, run INSTALL.cmd from the source root.'
)
$buildInfo | Set-Content -LiteralPath (Join-Path $outputRoot 'BUILD-INFO.txt') -Encoding UTF8

Write-Host ''
Write-Host 'Build succeeded.' -ForegroundColor Green
Write-Host "Main executable: $mainExe"
Write-Host "Uninstaller:      $uninstallExe"
Write-Host 'Run INSTALL.cmd from the source root to install and register the complete application.'
