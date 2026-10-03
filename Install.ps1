[CmdletBinding()]
param(
    [switch]$Uninstall,
    [switch]$DeleteUserData,
    [switch]$KeepUserData,
    [int]$UninstallerProcessId = 0,
    [switch]$DeleteCleanupScript
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$productName = 'MagicDragon Safeguardian'
$productRoot = Join-Path $env:LOCALAPPDATA 'MagicDragon_Safeguardian'
$configFile = Join-Path $productRoot 'Config.json'
$configBackupFile = Join-Path $productRoot 'Config.lastgood.json'
$appDir = Join-Path $productRoot 'App'
$assetsDir = Join-Path $productRoot 'Assets'
$logsDir = Join-Path $productRoot 'Logs'
$documentsDataRoot = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'MagicDragon_Safeguardian'
$recoveryDir = Join-Path $documentsDataRoot 'Recovery'
$legacyRecoveryDir = Join-Path $productRoot 'Recovery'
$exePath = Join-Path $appDir 'MagicDragon_Safeguardian.exe'
$uninstallExePath = Join-Path $productRoot 'Uninstall_MagicDragon_Safeguardian.exe'
$startupLink = Join-Path ([Environment]::GetFolderPath('Startup')) 'MagicDragon_Safeguardian.lnk'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValueName = 'MagicDragon Safeguardian'
$desktopLink = Join-Path ([Environment]::GetFolderPath('Desktop')) 'MagicDragon Safeguardian.lnk'
$programs = [Environment]::GetFolderPath('Programs')
$menuLink = Join-Path $programs 'MagicDragon Safeguardian.lnk'
$uninstallLink = Join-Path $programs 'Uninstall MagicDragon Safeguardian.lnk'
$uninstallRegistryKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MagicDragon_Safeguardian'

function Ensure-Directory([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        [void](New-Item -ItemType Directory -Path $Path -Force)
    }
}

function Stop-SafeguardianProcesses {
    Get-Process -Name 'MagicDragon_Safeguardian' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    try {
        Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
            ($_.Name -eq 'powershell.exe' -or $_.Name -eq 'pwsh.exe') -and
            $_.ProcessId -ne $PID -and
            $_.CommandLine -like '*MagicDragon_Safeguardian.ps1*' -and
            $_.CommandLine -like '*-Mode*Monitor*'
        } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    } catch { }
    Start-Sleep -Milliseconds 700
}

function New-Shortcut([string]$Path, [string]$Target, [string]$Arguments, [string]$Icon) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $Target
    $shortcut.Arguments = $Arguments
    $shortcut.WorkingDirectory = $appDir
    $shortcut.IconLocation = $Icon
    $shortcut.Save()
}

function Show-Result([string]$Text, [bool]$Error = $false) {
    Add-Type -AssemblyName System.Windows.Forms
    $icon = if ($Error) { [Windows.Forms.MessageBoxIcon]::Error } else { [Windows.Forms.MessageBoxIcon]::Information }
    [void][Windows.Forms.MessageBox]::Show($Text, $productName, [Windows.Forms.MessageBoxButtons]::OK, $icon)
}

function Schedule-CleanupScriptDeletion {
    if (-not $DeleteCleanupScript) { return }
    try {
        $command = 'ping 127.0.0.1 -n 3 > nul & del /f /q "{0}"' -f $PSCommandPath.Replace('"', '""')
        Start-Process -FilePath $env:ComSpec -ArgumentList @('/d','/c',$command) -WindowStyle Hidden
    } catch { }
}

try {
    if ($Uninstall) {
        if (-not $DeleteUserData -and -not $KeepUserData) {
            Add-Type -AssemblyName System.Windows.Forms
            $choice = [Windows.Forms.MessageBox]::Show(
                "Delete all MagicDragon backups and related data too?`r`n`r`nYES deletes the app and all data.`r`nNO removes only the app and keeps the data.`r`nCANCEL makes no changes.",
                $productName, [Windows.Forms.MessageBoxButtons]::YesNoCancel, [Windows.Forms.MessageBoxIcon]::Warning,
                [Windows.Forms.MessageBoxDefaultButton]::Button2)
            if ($choice -eq [Windows.Forms.DialogResult]::Cancel) { exit 0 }
            if ($choice -eq [Windows.Forms.DialogResult]::Yes) { $DeleteUserData = $true } else { $KeepUserData = $true }
        }
        if ($UninstallerProcessId -gt 0) {
            try { Wait-Process -Id $UninstallerProcessId -ErrorAction SilentlyContinue } catch { }
        }
        Stop-SafeguardianProcesses
        foreach ($link in @($startupLink, $desktopLink, $menuLink, $uninstallLink)) {
            if (Test-Path -LiteralPath $link) { Remove-Item -LiteralPath $link -Force }
        }
        Remove-ItemProperty -LiteralPath $runKey -Name $runValueName -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $uninstallRegistryKey) {
            Remove-Item -LiteralPath $uninstallRegistryKey -Recurse -Force -ErrorAction SilentlyContinue
        }
        if (Test-Path -LiteralPath $appDir) { Remove-Item -LiteralPath $appDir -Recurse -Force -ErrorAction SilentlyContinue }
        if (Test-Path -LiteralPath $assetsDir) { Remove-Item -LiteralPath $assetsDir -Recurse -Force -ErrorAction SilentlyContinue }
        if (Test-Path -LiteralPath $uninstallExePath) { Remove-Item -LiteralPath $uninstallExePath -Force -ErrorAction SilentlyContinue }
        if ($DeleteUserData) {
            $documentsData = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'MagicDragon_Safeguardian'
            if ((Split-Path -Leaf $documentsData) -ne 'MagicDragon_Safeguardian') { throw 'Refused to delete an unexpected Documents path.' }
            if (Test-Path -LiteralPath $documentsData -PathType Container) {
                Remove-Item -LiteralPath $documentsData -Recurse -Force -ErrorAction Stop
            }
            if ((Split-Path -Leaf $productRoot) -ne 'MagicDragon_Safeguardian') { throw 'Refused to delete an unexpected application-data path.' }
            if (Test-Path -LiteralPath $productRoot -PathType Container) {
                Remove-Item -LiteralPath $productRoot -Recurse -Force -ErrorAction Stop
            }
            Show-Result "The application, automatic watcher, backups, recovery copies, diagnostics, settings, and logs were removed."
        } else {
            Show-Result "The application and automatic watcher were removed.`r`n`r`nYour backups, recovery copies, settings, logs, and diagnostic exports were preserved."
        }
        Schedule-CleanupScriptDeletion
        exit 0
    }

    Stop-SafeguardianProcesses
    foreach ($folder in @($productRoot, $appDir, $assetsDir, $logsDir, $documentsDataRoot, $recoveryDir)) { Ensure-Directory $folder }
    $obsoleteTitleLogo = Join-Path $assetsDir 'MagicDragonTitle.png'
    if (Test-Path -LiteralPath $obsoleteTitleLogo -PathType Leaf) {
        Remove-Item -LiteralPath $obsoleteTitleLogo -Force
    }
    if (Test-Path -LiteralPath $legacyRecoveryDir -PathType Container) {
        foreach ($item in @(Get-ChildItem -LiteralPath $legacyRecoveryDir -Force -ErrorAction SilentlyContinue)) {
            $destination = Join-Path $recoveryDir $item.Name
            if (Test-Path -LiteralPath $destination) {
                $destination = Join-Path $recoveryDir ($item.Name + '_legacy_' + (Get-Date -Format 'yyyyMMdd_HHmmss'))
            }
            Move-Item -LiteralPath $item.FullName -Destination $destination -Force
        }
        Remove-Item -LiteralPath $legacyRecoveryDir -Force -ErrorAction SilentlyContinue
    }

    $sourceFile = Join-Path $PSScriptRoot 'Source\MagicDragonSafeguardian.cs'
    $uninstallSourceFile = Join-Path $PSScriptRoot 'Source\MagicDragonUninstall.cs'
    $engineFile = Join-Path $PSScriptRoot 'Engine\MagicDragon_Safeguardian.ps1'
    $mascotFile = Join-Path $PSScriptRoot 'Assets\MagicDragon.png'
    $iconFile = Join-Path $PSScriptRoot 'Assets\MagicDragon.ico'
    foreach ($required in @($sourceFile, $uninstallSourceFile, $engineFile, $mascotFile, $iconFile)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required package file is missing: $required" }
    }

    $installedSource = Join-Path $appDir 'MagicDragonSafeguardian.cs'
    $installedUninstallSource = Join-Path $appDir 'MagicDragonUninstall.cs'
    $installedEngine = Join-Path $appDir 'MagicDragon_Safeguardian.ps1'
    $installedInstaller = Join-Path $appDir 'Install.ps1'
    $installedMascot = Join-Path $assetsDir 'MagicDragon.png'
    $installedIcon = Join-Path $assetsDir 'MagicDragon.ico'
    Copy-Item -LiteralPath $sourceFile -Destination $installedSource -Force
    Copy-Item -LiteralPath $uninstallSourceFile -Destination $installedUninstallSource -Force
    Copy-Item -LiteralPath $engineFile -Destination $installedEngine -Force
    Copy-Item -LiteralPath $PSCommandPath -Destination $installedInstaller -Force
    Copy-Item -LiteralPath $mascotFile -Destination $installedMascot -Force
    Copy-Item -LiteralPath $iconFile -Destination $installedIcon -Force
    $readme = Join-Path $PSScriptRoot 'README.txt'
    if (Test-Path -LiteralPath $readme) { Copy-Item -LiteralPath $readme -Destination (Join-Path $appDir 'README.txt') -Force }
    $license = Join-Path $PSScriptRoot 'LICENSE'
    if (Test-Path -LiteralPath $license) { Copy-Item -LiteralPath $license -Destination (Join-Path $appDir 'LICENSE') -Force }

    if (-not (Test-Path -LiteralPath $configFile -PathType Leaf) -and -not (Test-Path -LiteralPath $configBackupFile -PathType Leaf)) {
        $config = [pscustomobject]@{
            SaveRoot = (Join-Path $env:USERPROFILE 'AppData\LocalLow\IronGate\Valheim\worlds_local')
            BackupRoot = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'MagicDragon_Safeguardian\Backups')
            AutoDiscoverWorlds = $true
            IncludedWorldNames = @()
            ExcludedWorldNames = @()
            AutoDiscoverCharacters = $true
            IncludedCharacterNames = @()
            ExcludedCharacterNames = @()
            RetainGoodBackups = 10
            RetainQuarantineBackups = 5
            MinimumFreeSpaceMB = 2048
            PollSeconds = 5
            SettleSeconds = 5
            BackupAtSaveRetention = 5
            ShowWindowWhenValheimStarts = $true
        }
        $config | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $configFile -Encoding UTF8
        Copy-Item -LiteralPath $configFile -Destination $configBackupFile -Force
    }
    try {
        $installedConfig = $null
        $loadedConfigPath = ''
        foreach ($candidate in @($configFile, $configBackupFile)) {
            if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
            try {
                $installedConfig = Get-Content -LiteralPath $candidate -Raw -Encoding UTF8 | ConvertFrom-Json
                $loadedConfigPath = $candidate
                break
            } catch { }
        }
        if ($null -eq $installedConfig) { throw 'Neither Config.json nor Config.lastgood.json could be read.' }
        $knownSettings = @('SaveRoot','BackupRoot','AutoDiscoverWorlds','IncludedWorldNames','ExcludedWorldNames','AutoDiscoverCharacters','IncludedCharacterNames','ExcludedCharacterNames','RetainGoodBackups','RetainQuarantineBackups','MinimumFreeSpaceMB','PollSeconds','SettleSeconds','BackupAtSaveRetention','ShowWindowWhenValheimStarts')
        $cleanConfig = [ordered]@{}
        foreach ($settingName in $knownSettings) {
            $settingProperty = $installedConfig.PSObject.Properties[$settingName]
            if ($null -ne $settingProperty) { $cleanConfig[$settingName] = $settingProperty.Value }
        }
        $installedConfig = [pscustomobject]$cleanConfig
        $installedConfig | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $configFile -Encoding UTF8
        Copy-Item -LiteralPath $configFile -Destination $configBackupFile -Force
        $launchWithValheim = $true
        $launchProperty = $installedConfig.PSObject.Properties['ShowWindowWhenValheimStarts']
        if ($null -ne $launchProperty) { $launchWithValheim = [bool]$launchProperty.Value }
        $installedBackupRoot = [Environment]::ExpandEnvironmentVariables([string]$installedConfig.BackupRoot)
        if (-not [string]::IsNullOrWhiteSpace($installedBackupRoot)) {
            Ensure-Directory $installedBackupRoot
            Ensure-Directory (Join-Path $installedBackupRoot 'Worlds')
            Ensure-Directory (Join-Path $installedBackupRoot 'Characters')
        }
    } catch {
        throw "The Worlds and Characters backup folders could not be created: $($_.Exception.Message)"
    }

    $tempExe = Join-Path $appDir 'MagicDragon_Safeguardian.building.exe'
    if (Test-Path -LiteralPath $tempExe) { Remove-Item -LiteralPath $tempExe -Force }
    $compilerCandidates = @(
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
    )
    $compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if ($null -eq $compiler) { throw 'The built-in .NET Framework C# compiler (csc.exe) was not found.' }
    $compilerArgs = @(
        '/nologo',
        '/target:winexe',
        '/platform:anycpu',
        '/optimize+',
        '/codepage:65001',
        ("/win32icon:`"$installedIcon`""),
        ("/out:`"$tempExe`""),
        '/reference:System.dll',
        '/reference:System.Core.dll',
        '/reference:System.Drawing.dll',
        '/reference:System.Windows.Forms.dll',
        '/reference:System.Web.Extensions.dll',
        '/reference:System.IO.Compression.dll',
        '/reference:System.IO.Compression.FileSystem.dll',
        ("`"$installedSource`"")
    )
    $compilerOutput = & $compiler @compilerArgs 2>&1
    if ($LASTEXITCODE -ne 0) { throw ("Windows application compilation failed:`r`n" + ($compilerOutput -join "`r`n")) }
    if (-not (Test-Path -LiteralPath $tempExe -PathType Leaf)) { throw 'The Windows executable was not produced.' }

    $tempUninstallExe = Join-Path $productRoot 'Uninstall_MagicDragon_Safeguardian.building.exe'
    if (Test-Path -LiteralPath $tempUninstallExe) { Remove-Item -LiteralPath $tempUninstallExe -Force }
    $uninstallCompilerArgs = @(
        '/nologo',
        '/target:winexe',
        '/platform:anycpu',
        '/optimize+',
        '/codepage:65001',
        ("/win32icon:`"$installedIcon`""),
        ("/out:`"$tempUninstallExe`""),
        '/reference:System.dll',
        '/reference:System.Drawing.dll',
        '/reference:System.Windows.Forms.dll',
        ("`"$installedUninstallSource`"")
    )
    $uninstallCompilerOutput = & $compiler @uninstallCompilerArgs 2>&1
    if ($LASTEXITCODE -ne 0) { throw ("Uninstaller compilation failed:`r`n" + ($uninstallCompilerOutput -join "`r`n")) }
    if (-not (Test-Path -LiteralPath $tempUninstallExe -PathType Leaf)) { throw 'The Windows uninstaller executable was not produced.' }

    $previousExe = Join-Path $appDir 'MagicDragon_Safeguardian.previous.exe'
    if (Test-Path -LiteralPath $previousExe) { Remove-Item -LiteralPath $previousExe -Force }
    if (Test-Path -LiteralPath $exePath) { Move-Item -LiteralPath $exePath -Destination $previousExe -Force }
    Move-Item -LiteralPath $tempExe -Destination $exePath -Force
    if (Test-Path -LiteralPath $uninstallExePath) { Remove-Item -LiteralPath $uninstallExePath -Force }
    Move-Item -LiteralPath $tempUninstallExe -Destination $uninstallExePath -Force

    if (Test-Path -LiteralPath $startupLink) { Remove-Item -LiteralPath $startupLink -Force }
    if (-not (Test-Path -LiteralPath $runKey)) { [void](New-Item -Path $runKey -Force) }
    if ($launchWithValheim) {
        Set-ItemProperty -LiteralPath $runKey -Name $runValueName -Value ('"{0}" --monitor' -f $exePath)
    } else {
        Remove-ItemProperty -LiteralPath $runKey -Name $runValueName -ErrorAction SilentlyContinue
    }
    New-Shortcut $desktopLink $exePath '' $installedIcon
    New-Shortcut $menuLink $exePath '' $installedIcon
    New-Shortcut $uninstallLink $uninstallExePath '' $installedIcon

    $estimatedSize = [int][Math]::Ceiling(((Get-ChildItem -LiteralPath $productRoot -File -Recurse -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum) / 1KB)
    $uninstallKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey(
        'Software\Microsoft\Windows\CurrentVersion\Uninstall\MagicDragon_Safeguardian')
    if ($null -eq $uninstallKey) { throw 'Windows could not create the Installed Apps registration.' }
    try {
        $uninstallKey.SetValue('DisplayName', $productName, [Microsoft.Win32.RegistryValueKind]::String)
        $uninstallKey.SetValue('DisplayVersion', '1.0.1', [Microsoft.Win32.RegistryValueKind]::String)
        $uninstallKey.SetValue('Publisher', 'HardcoreApe', [Microsoft.Win32.RegistryValueKind]::String)
        $uninstallKey.SetValue('InstallLocation', $productRoot, [Microsoft.Win32.RegistryValueKind]::String)
        $uninstallKey.SetValue('DisplayIcon', ($exePath + ',0'), [Microsoft.Win32.RegistryValueKind]::String)
        $uninstallKey.SetValue('UninstallString', ('"{0}"' -f $uninstallExePath), [Microsoft.Win32.RegistryValueKind]::String)
        $uninstallKey.SetValue('NoModify', 1, [Microsoft.Win32.RegistryValueKind]::DWord)
        $uninstallKey.SetValue('NoRepair', 1, [Microsoft.Win32.RegistryValueKind]::DWord)
        $uninstallKey.SetValue('EstimatedSize', $estimatedSize, [Microsoft.Win32.RegistryValueKind]::DWord)
        $uninstallKey.SetValue('InstallDate', (Get-Date -Format 'yyyyMMdd'), [Microsoft.Win32.RegistryValueKind]::String)
    } finally {
        $uninstallKey.Dispose()
    }

    Start-Process -FilePath $exePath
    Show-Result "MagicDragon Safeguardian 1.0.1 was installed successfully.`r`n`r`nIt is now listed in Windows Installed Apps / Programs and Features. Desktop and Start Menu shortcuts open the existing dashboard even when the guardian is hidden in the system tray.`r`n`r`nThe application is distributed under the MIT License and includes its unofficial-community-tool disclaimer. The executable is not digitally signed, so Windows SmartScreen or antivirus software may display a warning.`r`n`r`nClick the large dragon on the Dashboard to hear a random comment. BackupAtSave verifies and preserves the active selected world and character after detected manual or automatic in-game saves. Existing settings, backups, recovery copies, and logs were preserved."
} catch {
    $operation = if ($Uninstall) { 'Uninstall failed' } else { 'Installation failed' }
    Show-Result ("${operation}:`r`n`r`n" + $_.Exception.Message) $true
    Schedule-CleanupScriptDeletion
    exit 1
}
