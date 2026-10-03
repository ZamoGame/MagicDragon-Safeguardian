[CmdletBinding()]
param(
    [ValidateSet('Dashboard','WorldSelection','Monitor','CheckOnce','CheckWorlds','CheckWorldIntegrity','CheckCharacters','CheckCharacterIntegrity','CheckLive','CheckLiveWorlds','CheckLiveCharacters','BackupAtSaveWorld','BackupAtSaveCharacter','CheckAfterExit','InspectBackup','RestoreBackup','ExportDiagnostics','Install','Uninstall')]
    [string]$Mode = 'Dashboard',
    [string]$BackupPath = '',
    [string]$TargetWorldPath = '',
    [string]$TargetCharacterPath = '',
    [switch]$Silent
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$AppVersion = '1.0.1'
$ProductName = 'MagicDragon Safeguardian'
$BackupAtSaveFailureMessage = 'STOP! The dragon found something strange, please check your game files and do not exit the session, save again to avoid corruption.'
$ScriptPath = $PSCommandPath
$ProductRoot = Join-Path $env:LOCALAPPDATA 'MagicDragon_Safeguardian'
$InstallDir = Join-Path $ProductRoot 'App'
$RecoveryRoot = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'MagicDragon_Safeguardian\Recovery'
$LogRoot = Join-Path $ProductRoot 'Logs'
$ConfigPath = Join-Path $ProductRoot 'Config.json'
$ConfigBackupPath = Join-Path $ProductRoot 'Config.lastgood.json'
$InstalledScript = Join-Path $InstallDir 'MagicDragon_Safeguardian.ps1'
$StatusPath = Join-Path $ProductRoot 'status.json'
$RestorePreviewPath = Join-Path $ProductRoot 'restore-preview.json'
$RestoreResultPath = Join-Path $ProductRoot 'restore-result.json'
$DiagnosticResultPath = Join-Path $ProductRoot 'diagnostic-result.json'
$ActiveSessionStatePath = Join-Path $ProductRoot 'active-world-session.json'

function Ensure-Directory([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        [void](New-Item -ItemType Directory -Path $Path -Force)
    }
}

function Write-AppLog([string]$Message, [string]$Level = 'INFO') {
    Ensure-Directory $LogRoot
    $line = '{0:u} [{1}] {2}' -f (Get-Date), $Level, $Message
    Add-Content -LiteralPath (Join-Path $LogRoot 'Safeguardian.log') -Value $line -Encoding UTF8
}

function Get-DefaultConfig {
    $documents = [Environment]::GetFolderPath('MyDocuments')
    [pscustomobject]@{
        SaveRoot = (Join-Path $env:USERPROFILE 'AppData\LocalLow\IronGate\Valheim\worlds_local')
        BackupRoot = (Join-Path $documents 'MagicDragon_Safeguardian\Backups')
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
}

function Save-Config($Config) {
    Ensure-Directory $ProductRoot
    $temporary = $ConfigPath + '.new'
    $json = $Config | ConvertTo-Json -Depth 4
    $utf8Bom = New-Object System.Text.UTF8Encoding -ArgumentList $true
    [IO.File]::WriteAllText($temporary, $json, $utf8Bom)
    if (Test-Path -LiteralPath $ConfigPath -PathType Leaf) {
        try { [IO.File]::Replace($temporary, $ConfigPath, $null, $true) }
        catch {
            [IO.File]::Copy($temporary, $ConfigPath, $true)
            Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
        }
    } else { [IO.File]::Move($temporary, $ConfigPath) }
    [IO.File]::Copy($ConfigPath, $ConfigBackupPath, $true)
}

function Get-Config {
    $defaults = Get-DefaultConfig
    if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf) -and -not (Test-Path -LiteralPath $ConfigBackupPath -PathType Leaf)) {
        Save-Config $defaults
        return $defaults
    }
    foreach ($candidate in @($ConfigPath, $ConfigBackupPath)) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
        try {
            $saved = Get-Content -LiteralPath $candidate -Raw -Encoding UTF8 | ConvertFrom-Json
            foreach ($name in @('SaveRoot','BackupRoot','AutoDiscoverWorlds','IncludedWorldNames','ExcludedWorldNames','AutoDiscoverCharacters','IncludedCharacterNames','ExcludedCharacterNames','RetainGoodBackups','RetainQuarantineBackups','MinimumFreeSpaceMB','PollSeconds','SettleSeconds','BackupAtSaveRetention','ShowWindowWhenValheimStarts')) {
                $property = $saved.PSObject.Properties[$name]
                if ($null -ne $property -and $null -ne $property.Value -and "$($property.Value)" -ne '') { $defaults.$name = $property.Value }
            }
            $defaults.SaveRoot = [Environment]::ExpandEnvironmentVariables([string]$defaults.SaveRoot)
            $defaults.BackupRoot = [Environment]::ExpandEnvironmentVariables([string]$defaults.BackupRoot)
            if ($candidate -eq $ConfigBackupPath) { Write-AppLog 'Primary config could not be read; last-known-good preferences were recovered.' 'WARN' }
            return $defaults
        } catch {
            Write-AppLog "Config candidate could not be read: $candidate. $($_.Exception.Message)" 'WARN'
        }
    }
    Write-AppLog 'No readable config remained; defaults used.' 'WARN'
    return $defaults
}

function Get-SteamCloudSaveRoots([ValidateSet('worlds','characters')][string]$FolderName) {
    $steamRoots = New-Object System.Collections.Generic.List[string]
    try {
        $steamPath = (Get-ItemProperty -LiteralPath 'HKCU:\Software\Valve\Steam' -Name SteamPath -ErrorAction Stop).SteamPath
        if (-not [string]::IsNullOrWhiteSpace([string]$steamPath)) { $steamRoots.Add([string]$steamPath) }
    } catch { }
    try {
        Get-Process -Name steam -ErrorAction SilentlyContinue | ForEach-Object {
            try { $steamRoots.Add((Split-Path -Parent $_.MainModule.FileName)) } catch { }
        }
    } catch { }
    foreach ($programRoot in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$programRoot)) { $steamRoots.Add((Join-Path ([string]$programRoot) 'Steam')) }
    }
    $seen = @{}
    foreach ($steamRoot in @($steamRoots)) {
        try {
            $userdata = Join-Path ([IO.Path]::GetFullPath(($steamRoot -replace '/', '\'))) 'userdata'
            if (-not (Test-Path -LiteralPath $userdata -PathType Container)) { continue }
            foreach ($account in @(Get-ChildItem -LiteralPath $userdata -Directory -ErrorAction SilentlyContinue)) {
                $remote = Join-Path $account.FullName '892970\remote'
                foreach ($relative in @($FolderName, ($FolderName + '_local'), ("IronGate\Valheim\$FolderName"), ("IronGate\Valheim\${FolderName}_local"))) {
                    $path = [IO.Path]::GetFullPath((Join-Path $remote $relative))
                    $key = $path.ToLowerInvariant()
                    if ($seen.ContainsKey($key)) { continue }
                    $seen[$key] = $true
                    [pscustomobject]@{ Path=$path; Label='Steam Cloud' }
                }
            }
        } catch { }
    }
}

function Test-SaveSelectionMatch($Values, [string]$Path, [string]$Name) {
    if (@($Values) -contains $Path -or @($Values) -contains $Name -or @($Values) -contains ("Name:" + $Name)) { return $true }
    foreach ($value in @($Values)) {
        try {
            $leaf = [IO.Path]::GetFileName(([string]$value).TrimEnd([char[]]@('\','/')))
            if ($leaf -ieq $Name -or [IO.Path]::GetFileNameWithoutExtension($leaf) -ieq $Name) { return $true }
        } catch { }
    }
    return $false
}

function Get-WorldRoots($Config) {
    $profile = [Environment]::GetFolderPath('UserProfile')
    $local = [Environment]::GetFolderPath('LocalApplicationData')
    $candidates = @(
        [pscustomobject]@{ Path=(Join-Path $profile 'AppData\LocalLow\IronGate\Valheim\worlds_local'); Label='LocalLow' },
        [pscustomobject]@{ Path=(Join-Path $profile 'AppData\LocalLow\IronGate\Valheim\worlds'); Label='LocalLow' },
        [pscustomobject]@{ Path=(Join-Path $local 'IronGate\Valheim\worlds_local'); Label='Local' },
        [pscustomobject]@{ Path=(Join-Path $local 'IronGate\Valheim\worlds'); Label='Local' }
    )
    $candidates += @(Get-SteamCloudSaveRoots 'worlds')
    $seen = @{}
    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrWhiteSpace([string]$candidate.Path)) { continue }
        try {
            $root = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables([string]$candidate.Path))
            $key = $root.ToLowerInvariant()
            if ($seen.ContainsKey($key)) { continue }
            $seen[$key] = $true
            [pscustomobject]@{ Path=$root; Label=[string]$candidate.Label }
        } catch { }
    }
}

function Get-DiscoverableWorlds {
    $config = Get-Config
    $found = @{}
    foreach ($rootInfo in @(Get-WorldRoots $config)) {
        if (-not (Test-Path -LiteralPath $rootInfo.Path -PathType Container)) { continue }
        Get-ChildItem -LiteralPath $rootInfo.Path -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            # These are Valheim-managed rolling/restore copies, not active worlds.
            if ($_.Name -match '_backup_.+-\d{8}-\d{6}$') { return }
            $hasSaveData = @(Get-ChildItem -LiteralPath $_.FullName -File -ErrorAction SilentlyContinue | Where-Object {
                $_.Name -like '_main.*' -or $_.Extension -eq '.chunk'
            }).Count -gt 0
            if ($hasSaveData) {
                $path = [IO.Path]::GetFullPath($_.FullName)
                $found[$path] = [pscustomobject]@{
                    Name=$_.Name
                    DisplayName=("{0} [{1}]" -f $_.Name, $rootInfo.Label)
                    Key=$path
                    Path=$path
                }
            }
        }
    }
    @($found.Values | Sort-Object DisplayName)
}

function Get-ValheimWorlds {
    $config = Get-Config
    $found = @{}
    foreach ($world in @(Get-DiscoverableWorlds)) {
        $included = Test-SaveSelectionMatch $config.IncludedWorldNames $world.Key $world.Name
        $excluded = Test-SaveSelectionMatch $config.ExcludedWorldNames $world.Key $world.Name
        $selected = if ([bool]$config.AutoDiscoverWorlds) { -not $excluded } else { $included -and -not $excluded }
        if ($selected) {
            $found[$world.Key] = [pscustomobject]@{ Name=$world.DisplayName; Path=$world.Path; Key=$world.Key }
        }
    }
    @($found.Values | Sort-Object Name)
}

function Get-CharacterRoots {
    $profile = [Environment]::GetFolderPath('UserProfile')
    $local = [Environment]::GetFolderPath('LocalApplicationData')
    $candidates = @(
        [pscustomobject]@{ Path=(Join-Path $profile 'AppData\LocalLow\IronGate\Valheim\characters_local'); Label='LocalLow' },
        [pscustomobject]@{ Path=(Join-Path $profile 'AppData\LocalLow\IronGate\Valheim\characters'); Label='LocalLow' },
        [pscustomobject]@{ Path=(Join-Path $local 'IronGate\Valheim\characters_local'); Label='Local' },
        [pscustomobject]@{ Path=(Join-Path $local 'IronGate\Valheim\characters'); Label='Local' }
    )
    $candidates += @(Get-SteamCloudSaveRoots 'characters')
    $seen = @{}
    foreach ($candidate in $candidates) {
        try {
            $path = [IO.Path]::GetFullPath([string]$candidate.Path)
            $key = $path.ToLowerInvariant()
            if ($seen.ContainsKey($key)) { continue }
            $seen[$key] = $true
            [pscustomobject]@{ Path=$path; Label=[string]$candidate.Label }
        } catch { }
    }
}

function Get-DiscoverableCharacters {
    $found = @{}
    foreach ($rootInfo in @(Get-CharacterRoots)) {
        if (-not (Test-Path -LiteralPath $rootInfo.Path -PathType Container)) { continue }
        Get-ChildItem -LiteralPath $rootInfo.Path -File -Filter '*.fch' -ErrorAction SilentlyContinue | ForEach-Object {
            if ($_.BaseName -match '_backup_.+-\d{8}-\d{6}$') { return }
            $path = [IO.Path]::GetFullPath($_.FullName)
            $found[$path] = [pscustomobject]@{
                Name=$_.BaseName
                DisplayName=("{0} [{1}]" -f $_.BaseName, $rootInfo.Label)
                Path=$path
                Source=[string]$rootInfo.Label
            }
        }
    }
    @($found.Values | Sort-Object DisplayName)
}

function Get-ValheimCharacters {
    $config = Get-Config
    $found = @{}
    foreach ($character in @(Get-DiscoverableCharacters)) {
        $included = Test-SaveSelectionMatch $config.IncludedCharacterNames $character.Path $character.Name
        $excluded = Test-SaveSelectionMatch $config.ExcludedCharacterNames $character.Path $character.Name
        $selected = if ([bool]$config.AutoDiscoverCharacters) { -not $excluded } else { $included -and -not $excluded }
        if ($selected) { $found[$character.Path] = $character }
    }
    @($found.Values | Sort-Object DisplayName)
}

function Test-ValheimCharacter([string]$CharacterPath) {
    $errors = New-Object System.Collections.Generic.List[string]
    $warnings = New-Object System.Collections.Generic.List[string]
    $version = $null
    $hash = ''
    $size = 0
    try {
        if (-not (Test-Path -LiteralPath $CharacterPath -PathType Leaf)) { throw "Character file does not exist: $CharacterPath" }
        $file = Get-Item -LiteralPath $CharacterPath
        $size = [int64]$file.Length
        if ($size -lt 16) { throw 'Character file is too short to contain a valid Valheim character.' }
        # Valheim 1.0 character files may use a signed/enveloped layout.  The
        # first four bytes are therefore retained only as a diagnostic marker;
        # they are not a bounded save-format version.  Older builds rejected
        # healthy files when that marker was greater than 10000 (for example
        # 42236).  Read and hash the whole file through one shared stream so a
        # live check can run while Valheim remains open.
        $stream = [IO.File]::Open($CharacterPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        try {
            $header = New-Object byte[] 4
            if ($stream.Read($header, 0, 4) -ne 4) { throw 'Character binary header could not be read.' }
            $version = [BitConverter]::ToUInt32($header, 0)
            $stream.Position = 0
            $sha = [Security.Cryptography.SHA256]::Create()
            try {
                $hashBytes = $sha.ComputeHash($stream)
                $hash = ([BitConverter]::ToString($hashBytes)).Replace('-', '')
            } finally { $sha.Dispose() }
        } finally { $stream.Dispose() }
    } catch {
        $errors.Add($_.Exception.Message)
    }
    [pscustomobject]@{
        Valid=($errors.Count -eq 0); Version=$version; HeaderMarker=$version; Size=$size; SHA256=$hash
        Errors=@($errors); Warnings=@($warnings); CheckedAt=(Get-Date).ToString('o')
    }
}

function Read-U16([byte[]]$Bytes, [int]$Offset) {
    if ($Offset + 2 -gt $Bytes.Length) { throw 'Unexpected end of file.' }
    [BitConverter]::ToUInt16($Bytes, $Offset)
}

function Read-U32([byte[]]$Bytes, [int]$Offset) {
    if ($Offset + 4 -gt $Bytes.Length) { throw 'Unexpected end of file.' }
    [BitConverter]::ToUInt32($Bytes, $Offset)
}

function Test-ValheimWorld([string]$WorldPath) {
    $errors = New-Object System.Collections.Generic.List[string]
    $warnings = New-Object System.Collections.Generic.List[string]
    $selectedGeneration = $null
    $formatVersion = $null
    $indexedChunks = 0

    if (-not (Test-Path -LiteralPath $WorldPath -PathType Container)) {
        $errors.Add("World folder does not exist: $WorldPath")
        return [pscustomobject]@{ Valid=$false; Generation=$null; FormatVersion=$null; IndexedChunks=0; Errors=@($errors); Warnings=@($warnings); CheckedAt=(Get-Date).ToString('o') }
    }

    $generations = @()
    Get-ChildItem -LiteralPath $WorldPath -File -ErrorAction SilentlyContinue | ForEach-Object {
        if ($_.Name -match '^_main\.(\d+)\.(ok|fwl2|db2|chunks)$') { $generations += [int64]$Matches[1] }
    }
    $generations = @($generations | Sort-Object -Unique -Descending)
    if ($generations.Count -eq 0) {
        $errors.Add('No _main.N.* generation files were found.')
    }

    foreach ($generation in $generations) {
        $base = Join-Path $WorldPath ("_main.$generation")
        $paths = @{
            ok = "$base.ok"
            fwl2 = "$base.fwl2"
            db2 = "$base.db2"
            chunks = "$base.chunks"
        }
        $missing = @($paths.Keys | Where-Object { -not (Test-Path -LiteralPath $paths[$_] -PathType Leaf) })
        if ($missing.Count -gt 0) {
            $warnings.Add("Generation $generation is incomplete (missing: $($missing -join ', ')).")
            continue
        }

        try {
            $okBytes = [IO.File]::ReadAllBytes($paths.ok)
            $fwlBytes = [IO.File]::ReadAllBytes($paths.fwl2)
            $dbBytes = [IO.File]::ReadAllBytes($paths.db2)
            $indexBytes = [IO.File]::ReadAllBytes($paths.chunks)
            if ($okBytes.Length -ne 4) { throw "_main.$generation.ok is not exactly 4 bytes." }
            if ($fwlBytes.Length -lt 9) { throw "_main.$generation.fwl2 is too short." }
            if ($dbBytes.Length -lt 6) { throw "_main.$generation.db2 is too short." }
            if ($indexBytes.Length -lt 10) { throw "_main.$generation.chunks is too short." }

            $okVersion = Read-U32 $okBytes 0
            $fwlVersion = Read-U32 $fwlBytes 4
            $dbVersion = Read-U16 $dbBytes 0
            $indexVersion = Read-U16 $indexBytes 0
            if (($okVersion -ne $fwlVersion) -or ($okVersion -ne $dbVersion) -or ($okVersion -ne $indexVersion)) {
                throw "Format versions disagree (ok=$okVersion, fwl2=$fwlVersion, db2=$dbVersion, chunks=$indexVersion)."
            }

            $count = [int](Read-U32 $indexBytes 6)
            $expectedLength = 10 + (11 * $count)
            if ($indexBytes.Length -ne $expectedLength) {
                throw "Chunk index length is $($indexBytes.Length), expected $expectedLength for $count entries."
            }

            for ($i = 0; $i -lt $count; $i++) {
                $offset = 10 + (11 * $i)
                $x = [int]$indexBytes[$offset]
                $y = [int]$indexBytes[$offset + 1]
                $layer = [int]$indexBytes[$offset + 2]
                $revision = [int](Read-U32 $indexBytes ($offset + 3))
                $objectCount = Read-U32 $indexBytes ($offset + 7)
                # Valheim stores the two coordinate bytes in index order, while
                # chunk filenames render them in the reverse order (Y_X).
                $chunkName = ('{0:x2}_{1:x2}__{2}_{3}.chunk' -f $y, $x, $layer, $revision)
                $chunkPath = Join-Path $WorldPath $chunkName
                if (-not (Test-Path -LiteralPath $chunkPath -PathType Leaf)) { throw "Indexed chunk is missing: $chunkName" }
                $chunkBytes = [IO.File]::ReadAllBytes($chunkPath)
                if ($chunkBytes.Length -lt 6) { throw "Chunk is too short: $chunkName" }
                $chunkVersion = Read-U16 $chunkBytes 0
                $chunkObjects = Read-U32 $chunkBytes 2
                if ($chunkVersion -ne $okVersion) { throw "Chunk format mismatch in $chunkName." }
                if ($chunkObjects -ne $objectCount) { throw "Object-count mismatch in $chunkName (index=$objectCount, file=$chunkObjects)." }
            }

            $selectedGeneration = $generation
            $formatVersion = $okVersion
            $indexedChunks = $count
            break
        } catch {
            $warnings.Add("Generation $generation failed validation: $($_.Exception.Message)")
        }
    }

    if ($null -eq $selectedGeneration) {
        $errors.Add('No complete, internally consistent committed generation was found.')
    }

    [pscustomobject]@{
        Valid = ($null -ne $selectedGeneration)
        Generation = $selectedGeneration
        FormatVersion = $formatVersion
        IndexedChunks = $indexedChunks
        Errors = @($errors)
        Warnings = @($warnings)
        CheckedAt = (Get-Date).ToString('o')
    }
}

function Test-ValheimWorldCaches([string]$WorldPath) {
    $errors = New-Object System.Collections.Generic.List[string]
    $warnings = New-Object System.Collections.Generic.List[string]
    $expected = @('cacheMinimapBiome','cacheMinimapHeight','cacheMinimapMask','cacheMinimapMeta')
    $existing = @(Get-ChildItem -LiteralPath $WorldPath -File -Filter 'cache*' -ErrorAction SilentlyContinue)
    if ($existing.Count -eq 0) {
        $warnings.Add('No minimap cache files are present. Valheim can regenerate caches, but none could be verified during this live check.')
    } else {
        foreach ($name in $expected) {
            $path = Join-Path $WorldPath $name
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                $errors.Add("Expected cache file is missing: $name")
                continue
            }
            try {
                $file = Get-Item -LiteralPath $path
                $minimum = if ($name -eq 'cacheMinimapMeta') { 8 } else { 1 }
                if ($file.Length -lt $minimum) { throw "Cache file is too short ($($file.Length) bytes)." }
                $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
                try {
                    $buffer = New-Object byte[] 65536
                    while ($stream.Read($buffer, 0, $buffer.Length) -gt 0) { }
                } finally { $stream.Dispose() }
            } catch {
                $errors.Add("Cache validation failed for $name`: $($_.Exception.Message)")
            }
        }
        foreach ($file in $existing | Where-Object { $expected -notcontains $_.Name }) {
            try {
                if ($file.Length -lt 1) { throw 'Cache file is empty.' }
                $stream = [IO.File]::Open($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
                try {
                    $buffer = New-Object byte[] 65536
                    while ($stream.Read($buffer, 0, $buffer.Length) -gt 0) { }
                } finally { $stream.Dispose() }
            } catch {
                $errors.Add("Cache validation failed for $($file.Name)`: $($_.Exception.Message)")
            }
        }
    }
    [pscustomobject]@{ Valid=($errors.Count -eq 0); Files=$existing.Count; Errors=@($errors); Warnings=@($warnings) }
}

function Test-AllWorldChunks([string]$WorldPath, [int]$ExpectedVersion) {
    $errors = New-Object System.Collections.Generic.List[string]
    $files = @(Get-ChildItem -LiteralPath $WorldPath -File -Filter '*.chunk' -ErrorAction SilentlyContinue)
    foreach ($file in $files) {
        try {
            $stream = [IO.File]::Open($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
            try {
                $header = New-Object byte[] 6
                if ($stream.Read($header, 0, 6) -ne 6) { throw 'Chunk header is truncated.' }
            } finally { $stream.Dispose() }
            $version = [BitConverter]::ToUInt16($header, 0)
            if ($version -ne $ExpectedVersion) { throw "Chunk format $version does not match committed format $ExpectedVersion." }
            [void][BitConverter]::ToUInt32($header, 2)
        } catch {
            $errors.Add("Chunk validation failed for $($file.Name)`: $($_.Exception.Message)")
        }
    }
    [pscustomobject]@{ Valid=($errors.Count -eq 0); Files=$files.Count; Errors=@($errors) }
}

function Get-LiveWorldFingerprint([string]$WorldPath) {
    if (-not (Test-Path -LiteralPath $WorldPath -PathType Container)) { return 'MISSING' }
    $lines = Get-ChildItem -LiteralPath $WorldPath -File -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -like '_main.*' -or $_.Extension -eq '.chunk' -or $_.Name -like 'cache*'
    } | Sort-Object Name | ForEach-Object { "$($_.Name)|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)" }
    [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($lines -join "`n")))
}

function Test-LiveWorldState([string]$WorldPath) {
    $world = Test-ValheimWorld $WorldPath
    $errors = New-Object System.Collections.Generic.List[string]
    $warnings = New-Object System.Collections.Generic.List[string]
    foreach ($item in @($world.Errors)) { $errors.Add([string]$item) }
    foreach ($item in @($world.Warnings)) { $warnings.Add([string]$item) }
    if ($world.Valid -and @($world.Warnings).Count -gt 0) {
        $errors.Add('A newer or incomplete _main generation is still present; the in-game save may not have finished.')
    }
    $chunks = if ($world.Valid) { Test-AllWorldChunks $WorldPath ([int]$world.FormatVersion) } else { [pscustomobject]@{ Valid=$false; Files=0; Errors=@() } }
    foreach ($item in @($chunks.Errors)) { $errors.Add([string]$item) }
    $caches = Test-ValheimWorldCaches $WorldPath
    foreach ($item in @($caches.Errors)) { $errors.Add([string]$item) }
    foreach ($item in @($caches.Warnings)) { $warnings.Add([string]$item) }
    [pscustomobject]@{
        Valid=($world.Valid -and $chunks.Valid -and $caches.Valid -and $errors.Count -eq 0)
        Generation=$world.Generation; FormatVersion=$world.FormatVersion; IndexedChunks=$world.IndexedChunks
        ChunkFiles=$chunks.Files; CacheFiles=$caches.Files; Errors=@($errors); Warnings=@($warnings)
    }
}

function Get-WorldFingerprint([string]$WorldPath) {
    if (-not (Test-Path -LiteralPath $WorldPath -PathType Container)) { return 'MISSING' }
    $lines = Get-ChildItem -LiteralPath $WorldPath -File -Recurse | Sort-Object FullName | ForEach-Object {
        $relative = $_.FullName.Substring($WorldPath.Length).TrimStart('\')
        "$relative|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)"
    }
    [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($lines -join "`n")))
}

function Wait-WorldStable([string]$WorldPath, [int]$Seconds) {
    $unchanged = 0
    $last = ''
    $deadline = (Get-Date).AddSeconds([Math]::Max(60, $Seconds * 4))
    while ((Get-Date) -lt $deadline) {
        $current = Get-WorldFingerprint $WorldPath
        if ($current -eq $last) { $unchanged += 2 } else { $unchanged = 0; $last = $current }
        if ($unchanged -ge $Seconds) { return $true }
        Start-Sleep -Seconds 2
    }
    return $false
}

function Get-FileFingerprint([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return 'MISSING' }
    $file = Get-Item -LiteralPath $Path
    "$($file.Length)|$($file.LastWriteTimeUtc.Ticks)"
}

function Wait-FileStable([string]$Path, [int]$Seconds) {
    $unchanged = 0
    $last = ''
    $deadline = (Get-Date).AddSeconds([Math]::Max(60, $Seconds * 4))
    while ((Get-Date) -lt $deadline) {
        $current = Get-FileFingerprint $Path
        if ($current -eq $last) { $unchanged += 2 } else { $unchanged = 0; $last = $current }
        if ($unchanged -ge $Seconds) { return $true }
        Start-Sleep -Seconds 2
    }
    return $false
}

function Get-BackupStorageHealth([int64]$EstimatedBytes = 0) {
    $config = Get-Config
    try {
        Ensure-Directory ([string]$config.BackupRoot)
        $root = [IO.Path]::GetPathRoot([IO.Path]::GetFullPath([string]$config.BackupRoot))
        $drive = New-Object -TypeName System.IO.DriveInfo -ArgumentList $root
        $free = [int64]$drive.AvailableFreeSpace
        $total = [int64]$drive.TotalSize
        $minimum = [int64][Math]::Max(([int64]$config.MinimumFreeSpaceMB * 1MB), [Math]::Ceiling($total * 0.05))
        $required = [int64][Math]::Max(($EstimatedBytes + 64MB), 64MB)
        $warning = if ($free -lt $required) {
            "Backup disk does not have enough free space (free $([Math]::Round($free / 1GB, 2)) GB; required about $([Math]::Round($required / 1GB, 2)) GB)."
        } elseif ($free -lt $minimum) {
            "Backup disk is nearly full: $([Math]::Round($free / 1GB, 2)) GB remains on $root."
        } else { '' }
        [pscustomobject]@{ Known=$true; FreeBytes=$free; TotalBytes=$total; RequiredBytes=$required; CanCreate=($free -ge $required); Warning=$warning; Root=$root }
    } catch {
        [pscustomobject]@{ Known=$false; FreeBytes=$null; TotalBytes=$null; RequiredBytes=$EstimatedBytes; CanCreate=$true; Warning="Backup storage capacity could not be checked: $($_.Exception.Message)"; Root='' }
    }
}

function Assert-BackupStorage([int64]$EstimatedBytes) {
    $health = Get-BackupStorageHealth $EstimatedBytes
    if (-not $health.CanCreate) { throw [string]$health.Warning }
    if (-not [string]::IsNullOrWhiteSpace([string]$health.Warning)) { Write-AppLog ([string]$health.Warning) 'WARN' }
    return $health
}

function Get-BackupFolder([ValidateSet('World','Character')][string]$AssetType) {
    $config = Get-Config
    $folderName = if ($AssetType -eq 'Character') { 'Characters' } else { 'Worlds' }
    $path = Join-Path ([string]$config.BackupRoot) $folderName
    Ensure-Directory $path
    return $path
}

function ConvertTo-SafeFolderName([string]$Name) {
    $safe = [string]$Name
    foreach ($invalid in [IO.Path]::GetInvalidFileNameChars()) { $safe = $safe.Replace([string]$invalid, '_') }
    $safe = $safe.Trim().TrimEnd('.')
    if ([string]::IsNullOrWhiteSpace($safe)) { return 'Unnamed' }
    if ($safe -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$') { $safe = '_' + $safe }
    return $safe
}

function Get-BackupItemFolder([ValidateSet('World','Character')][string]$AssetType, [string]$SourcePath) {
    $root = Get-BackupFolder $AssetType
    $name = if ($AssetType -eq 'Character') {
        [IO.Path]::GetFileNameWithoutExtension($SourcePath)
    } else {
        Split-Path -Leaf $SourcePath
    }
    $path = Join-Path $root (ConvertTo-SafeFolderName $name)
    Ensure-Directory $path
    return $path
}

function Test-ZipArchive([string]$ZipPath) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $buffer = New-Object byte[] 65536
        foreach ($entry in $archive.Entries) {
            if ([string]::IsNullOrEmpty($entry.Name)) { continue }
            $stream = $entry.Open()
            try { while ($stream.Read($buffer, 0, $buffer.Length) -gt 0) { } }
            finally { $stream.Dispose() }
        }
    } finally { $archive.Dispose() }
    return $true
}

function Test-BackupChecksum([string]$ZipPath) {
    if (-not (Test-Path -LiteralPath $ZipPath -PathType Leaf)) { throw "Backup ZIP does not exist: $ZipPath" }
    $sidecar = $ZipPath + '.sha256.txt'
    if (-not (Test-Path -LiteralPath $sidecar -PathType Leaf)) { throw 'The stored SHA-256 sidecar is missing.' }
    $line = [string](Get-Content -LiteralPath $sidecar -Encoding ASCII | Select-Object -First 1)
    if ($line -notmatch '^\s*([A-Fa-f0-9]{64})\b') { throw 'The stored SHA-256 sidecar is malformed.' }
    $expected = $Matches[1].ToUpperInvariant()
    $actual = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($actual -ne $expected) { throw "SHA-256 mismatch. Expected $expected but calculated $actual." }
    return $actual
}

function Expand-BackupSafely([string]$ZipPath, [string]$Destination) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    Ensure-Directory $Destination
    $root = [IO.Path]::GetFullPath($Destination).TrimEnd('\') + '\'
    $archive = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        foreach ($entry in $archive.Entries) {
            $relative = $entry.FullName.Replace('/', '\')
            $target = [IO.Path]::GetFullPath((Join-Path $Destination $relative))
            if (-not $target.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe ZIP entry was blocked: $($entry.FullName)" }
            if ([string]::IsNullOrEmpty($entry.Name)) {
                Ensure-Directory $target
                continue
            }
            Ensure-Directory (Split-Path -Parent $target)
            $input = $entry.Open()
            try {
                $output = [IO.File]::Open($target, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
                try { $input.CopyTo($output) } finally { $output.Dispose() }
            } finally { $input.Dispose() }
        }
    } finally { $archive.Dispose() }
}

function Test-ManifestFiles($Manifest, [string]$StagePath) {
    if ($null -eq $Manifest.PSObject.Properties['Files']) { throw 'Backup manifest does not contain a file inventory.' }
    $root = [IO.Path]::GetFullPath($StagePath).TrimEnd('\') + '\'
    foreach ($item in @($Manifest.Files)) {
        $target = [IO.Path]::GetFullPath((Join-Path $StagePath ([string]$item.Path)))
        if (-not $target.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe manifest path was blocked: $($item.Path)" }
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "Manifest file is missing: $($item.Path)" }
        $file = Get-Item -LiteralPath $target
        if ([int64]$file.Length -ne [int64]$item.Size) { throw "Manifest size mismatch: $($item.Path)" }
        $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
        if ($hash -ne [string]$item.SHA256) { throw "Manifest SHA-256 mismatch: $($item.Path)" }
    }
}

function Resolve-RestoreTarget($Manifest, [string]$WorldFolderName) {
    $config = Get-Config
    $roots = @(Get-WorldRoots $config)
    if ($null -ne $Manifest.PSObject.Properties['SourcePath'] -and -not [string]::IsNullOrWhiteSpace([string]$Manifest.SourcePath)) {
        try {
            $source = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables([string]$Manifest.SourcePath))
            $sourceParent = [IO.Path]::GetFullPath((Split-Path -Parent $source)).TrimEnd('\')
            foreach ($rootInfo in $roots) {
                $root = [IO.Path]::GetFullPath([string]$rootInfo.Path).TrimEnd('\')
                if ($sourceParent.Equals($root, [StringComparison]::OrdinalIgnoreCase)) {
                    return [pscustomobject]@{ Path=(Join-Path $root $WorldFolderName); Source=[string]$rootInfo.Label }
                }
            }
        } catch { }
    }

    $existing = @($roots | ForEach-Object {
        $candidate = Join-Path $_.Path $WorldFolderName
        if (Test-Path -LiteralPath $candidate -PathType Container) { [pscustomobject]@{ Path=$candidate; Source=$_.Label } }
    })
    if ($existing.Count -eq 1) { return $existing[0] }
    if ($existing.Count -gt 1) { throw "More than one existing world is named '$WorldFolderName'. Restore target is ambiguous." }

    $primaryInfo = $roots | Where-Object {
        $_.Label -eq 'LocalLow' -and $_.Path -match '[\\/]worlds_local$'
    } | Select-Object -First 1
    if ($null -eq $primaryInfo) { throw 'The default official LocalLow Valheim world folder could not be resolved.' }
    [pscustomobject]@{ Path=(Join-Path ([string]$primaryInfo.Path) $WorldFolderName); Source='LocalLow' }
}

function Get-BackupInspection([string]$ZipPath, [bool]$KeepStage = $false) {
    Ensure-Directory $RecoveryRoot
    $stage = Join-Path $RecoveryRoot ('.restore_staging_' + [guid]::NewGuid().ToString('N'))
    $valid = $false
    $checksumState = 'Failed'
    $zipState = 'Failed'
    $structureState = 'Failed'
    try {
        if (@(Get-Process -Name 'valheim' -ErrorAction SilentlyContinue).Count -gt 0) {
            throw 'Valheim is still running. Close it completely before restoring.'
        }
        $zipHash = Test-BackupChecksum $ZipPath
        $checksumState = 'Verified'
        Expand-BackupSafely $ZipPath $stage
        $zipState = 'Verified'
        $manifestPath = Join-Path $stage 'Safeguardian_manifest.json'
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'Safeguardian_manifest.json is missing from the backup.' }
        $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if (([string]$manifest.Kind) -ne 'GOOD') { throw "Only GOOD backups can be restored. This archive is '$($manifest.Kind)'." }
        if ($null -ne $manifest.PSObject.Properties['AssetType'] -and [string]$manifest.AssetType -ne 'World') {
            throw 'This is a character backup. The Verified Restore Wizard currently restores world backups only.'
        }
        Test-ManifestFiles $manifest $stage

        $worldDirs = @(Get-ChildItem -LiteralPath $stage -Directory -ErrorAction Stop)
        if ($worldDirs.Count -ne 1) { throw "The backup must contain exactly one world folder; found $($worldDirs.Count)." }
        $stagedWorld = $worldDirs[0].FullName
        $folderName = if ($null -ne $manifest.PSObject.Properties['WorldFolderName'] -and -not [string]::IsNullOrWhiteSpace([string]$manifest.WorldFolderName)) {
            [string]$manifest.WorldFolderName
        } else {
            ([string]$worldDirs[0].Name -replace '\s+\[(LocalLow|Local|Steam Cloud|Official)\]$', '')
        }
        if ([string]::IsNullOrWhiteSpace($folderName) -or $folderName.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) { throw 'The world folder name is invalid.' }
        $validation = Test-ValheimWorld $stagedWorld
        if (-not $validation.Valid) { throw "Extracted world validation failed: $($validation.Errors -join ' ')" }
        $structureState = 'Verified'
        $target = Resolve-RestoreTarget $manifest $folderName
        $created = if ($null -ne $manifest.PSObject.Properties['CreatedAt']) { [string]$manifest.CreatedAt } else { (Get-Item -LiteralPath $ZipPath).LastWriteTime.ToString('o') }
        $valid = $true
        return [pscustomobject]@{
            Valid=$true; BackupPath=[IO.Path]::GetFullPath($ZipPath); BackupStatus=[string]$manifest.Kind; World=$folderName
            Generation=$validation.Generation; Created=$created; StorageSource=$target.Source; TargetPath=$target.Path
            ZipIntegrity='Verified'; Checksum='Verified'; WorldStructure='Verified'; ZipSHA256=$zipHash
            StagePath=$stage; StagedWorld=$stagedWorld; Errors=@()
        }
    } catch {
        return [pscustomobject]@{
            Valid=$false; BackupPath=$ZipPath; BackupStatus='NOT RESTORABLE'; World='Unknown'; Generation=$null
            Created='Unknown'; StorageSource='Unknown'; TargetPath=''; ZipIntegrity=$zipState; Checksum=$checksumState
            WorldStructure=$structureState; ZipSHA256=''; StagePath=$stage; StagedWorld=''; Errors=@($_.Exception.Message)
        }
    } finally {
        if ((-not $KeepStage -or -not $valid) -and (Test-Path -LiteralPath $stage)) {
            Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Restore-VerifiedBackup([string]$ZipPath) {
    if (@(Get-Process -Name 'valheim' -ErrorAction SilentlyContinue).Count -gt 0) { throw 'Valheim is still running. Close it completely before restoring.' }
    if (Test-Path -LiteralPath $RestoreResultPath) { Remove-Item -LiteralPath $RestoreResultPath -Force -ErrorAction SilentlyContinue }
    $inspection = Get-BackupInspection $ZipPath $true
    if (-not $inspection.Valid) { throw ($inspection.Errors -join ' ') }
    $target = [string]$inspection.TargetPath
    $stage = [string]$inspection.StagePath
    $stagedWorld = [string]$inspection.StagedWorld
    $preserved = ''
    $placed = $false
    try {
        Ensure-Directory (Split-Path -Parent $target)
        Ensure-Directory $RecoveryRoot
        if (Test-Path -LiteralPath $target) {
            $preserved = Join-Path $RecoveryRoot ("$($inspection.World)_before_restore_" + (Get-Date -Format 'yyyyMMdd_HHmmss'))
            if (Test-Path -LiteralPath $preserved) { $preserved += '_' + [guid]::NewGuid().ToString('N').Substring(0,6) }
            Move-Item -LiteralPath $target -Destination $preserved
            Write-AppLog "Existing world preserved before restore: $preserved"
        }
        Move-Item -LiteralPath $stagedWorld -Destination $target
        $placed = $true
        $installedCheck = Test-ValheimWorld $target
        if (-not $installedCheck.Valid) { throw "Installed world failed final validation: $($installedCheck.Errors -join ' ')" }
        $result = [pscustomobject]@{
            Success=$true; World=$inspection.World; TargetPath=$target; RecoveryPath=$preserved
            Generation=$installedCheck.Generation; BackupPath=$ZipPath
            Message='Verified restore completed successfully.'
        }
        $result | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $RestoreResultPath -Encoding UTF8
        Write-AppLog "Verified restore completed for '$($inspection.World)' from $ZipPath to $target"
        return $result
    } catch {
        $restoreError = $_.Exception.Message
        try {
            if ($placed -and (Test-Path -LiteralPath $target)) {
                $failedPath = Join-Path $RecoveryRoot ("$($inspection.World)_failed_restore_" + (Get-Date -Format 'yyyyMMdd_HHmmss'))
                Move-Item -LiteralPath $target -Destination $failedPath
            }
            if (-not [string]::IsNullOrWhiteSpace($preserved) -and (Test-Path -LiteralPath $preserved)) {
                Move-Item -LiteralPath $preserved -Destination $target
                Write-AppLog "Restore failed; previous world rolled back to $target" 'WARN'
            }
        } catch {
            $restoreError += " Rollback also failed: $($_.Exception.Message)"
        }
        $result = [pscustomobject]@{
            Success=$false; World=$inspection.World; TargetPath=$target; RecoveryPath=$preserved
            Generation=$inspection.Generation; BackupPath=$ZipPath; Message=$restoreError
        }
        $result | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $RestoreResultPath -Encoding UTF8
        throw $restoreError
    } finally {
        if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

function New-WorldSnapshot([string]$WorldName, [string]$WorldPath, $Validation, [bool]$IsQuarantine, [string]$DestinationFolder = '', [string]$BackupClass = 'Standard') {
    $backupRoot = if ([string]::IsNullOrWhiteSpace($DestinationFolder)) { Get-BackupItemFolder 'World' $WorldPath } else { $DestinationFolder }
    Ensure-Directory $backupRoot
    $estimatedBytes = [int64](Get-ChildItem -LiteralPath $WorldPath -File -Recurse -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
    [void](Assert-BackupStorage $estimatedBytes)
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $kind = if ($IsQuarantine) { 'QUARANTINE' } else { 'GOOD' }
    $generationPart = if ($null -ne $Validation.Generation) { "_gen$($Validation.Generation)" } else { '' }
    # Use the real save-folder name rather than the UI label (for example
    # "MyWorld [Steam Cloud]").  This keeps one continuous archive history
    # when Valheim moves a save between local and cloud storage.
    $worldFolderName = Split-Path -Leaf $WorldPath
    $safeWorldName = $worldFolderName -replace '[^A-Za-z0-9._-]', '_'
    $baseName = if ($BackupClass -eq 'BackupAtSave') {
        '{0}_BackupAtSave_{1}_{2}{3}' -f $safeWorldName, $kind, $stamp, $generationPart
    } else {
        '{0}_{1}_{2}{3}' -f $safeWorldName, $kind, $stamp, $generationPart
    }
    $finalZip = Join-Path $backupRoot ($baseName + '.zip')
    if (Test-Path -LiteralPath $finalZip) { $finalZip = Join-Path $backupRoot ($baseName + '_' + [guid]::NewGuid().ToString('N').Substring(0,6) + '.zip') }
    $tempZip = $finalZip + '.building'
    $stage = Join-Path $backupRoot ('.staging_' + [guid]::NewGuid().ToString('N'))
    Ensure-Directory $stage
    try {
        $stageWorld = Join-Path $stage $worldFolderName
        Copy-Item -LiteralPath $WorldPath -Destination $stageWorld -Recurse -Force
        $copyValidation = Test-ValheimWorld $stageWorld
        if (-not $IsQuarantine -and -not $copyValidation.Valid) { throw 'The staged copy failed validation; no good backup was published.' }

        $files = Get-ChildItem -LiteralPath $stageWorld -File -Recurse | Sort-Object FullName | ForEach-Object {
            [pscustomobject]@{
                Path = $_.FullName.Substring($stage.Length).TrimStart('\')
                Size = $_.Length
                SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            }
        }
        $manifest = [pscustomobject]@{
            Application = $ProductName
            ApplicationVersion = $AppVersion
            AssetType = 'World'
            CreatedAt = (Get-Date).ToString('o')
            Computer = $env:COMPUTERNAME
            World = $WorldName
            WorldFolderName = (Split-Path -Leaf $WorldPath)
            SourcePath = [IO.Path]::GetFullPath($WorldPath)
            Kind = $kind
            BackupClass = $BackupClass
            Validation = $copyValidation
            Files = @($files)
        }
        $manifest | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $stage 'Safeguardian_manifest.json') -Encoding UTF8

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::CreateFromDirectory($stage, $tempZip, [IO.Compression.CompressionLevel]::Optimal, $false)
        [void](Test-ZipArchive $tempZip)
        Move-Item -LiteralPath $tempZip -Destination $finalZip
        $zipHash = (Get-FileHash -LiteralPath $finalZip -Algorithm SHA256).Hash
        "$zipHash  $([IO.Path]::GetFileName($finalZip))" | Set-Content -LiteralPath ($finalZip + '.sha256.txt') -Encoding ASCII
        return $finalZip
    } finally {
        if (Test-Path -LiteralPath $tempZip) { Remove-Item -LiteralPath $tempZip -Force -ErrorAction SilentlyContinue }
        if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

function New-CharacterSnapshot([string]$CharacterName, [string]$CharacterPath, $Validation, [bool]$IsQuarantine, [string]$DestinationFolder = '', [string]$BackupClass = 'Standard') {
    $backupRoot = if ([string]::IsNullOrWhiteSpace($DestinationFolder)) { Get-BackupItemFolder 'Character' $CharacterPath } else { $DestinationFolder }
    Ensure-Directory $backupRoot
    $companions = @($CharacterPath, ($CharacterPath + '.old'), ($CharacterPath + '.new')) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
    $estimatedBytes = [int64]($companions | ForEach-Object { (Get-Item -LiteralPath $_).Length } | Measure-Object -Sum).Sum
    [void](Assert-BackupStorage $estimatedBytes)
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $kind = if ($IsQuarantine) { 'QUARANTINE' } else { 'GOOD' }
    $characterFileName = [IO.Path]::GetFileNameWithoutExtension($CharacterPath)
    $safeName = $characterFileName -replace '[^A-Za-z0-9._-]', '_'
    $baseName = if ($BackupClass -eq 'BackupAtSave') {
        'Character_{0}_BackupAtSave_{1}_{2}_h{3}' -f $safeName, $kind, $stamp, $Validation.HeaderMarker
    } else {
        'Character_{0}_{1}_{2}_h{3}' -f $safeName, $kind, $stamp, $Validation.HeaderMarker
    }
    $finalZip = Join-Path $backupRoot ($baseName + '.zip')
    if (Test-Path -LiteralPath $finalZip) { $finalZip = Join-Path $backupRoot ($baseName + '_' + [guid]::NewGuid().ToString('N').Substring(0,6) + '.zip') }
    $tempZip = $finalZip + '.building'
    $stage = Join-Path $backupRoot ('.character_staging_' + [guid]::NewGuid().ToString('N'))
    Ensure-Directory $stage
    try {
        $stageCharacter = Join-Path $stage $safeName
        Ensure-Directory $stageCharacter
        foreach ($source in $companions) { Copy-Item -LiteralPath $source -Destination (Join-Path $stageCharacter ([IO.Path]::GetFileName($source))) -Force }
        $stagedMain = Join-Path $stageCharacter ([IO.Path]::GetFileName($CharacterPath))
        $copyValidation = Test-ValheimCharacter $stagedMain
        if (-not $IsQuarantine -and -not $copyValidation.Valid) { throw 'The staged character copy failed validation; no GOOD backup was published.' }
        if (-not $IsQuarantine -and $copyValidation.SHA256 -ne $Validation.SHA256) { throw 'The staged character hash does not match the source.' }

        $files = Get-ChildItem -LiteralPath $stageCharacter -File | Sort-Object Name | ForEach-Object {
            [pscustomobject]@{
                Path=$_.FullName.Substring($stage.Length).TrimStart('\')
                Size=$_.Length
                SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            }
        }
        $manifest = [pscustomobject]@{
            Application=$ProductName; ApplicationVersion=$AppVersion; AssetType='Character'
            CreatedAt=(Get-Date).ToString('o'); Computer=$env:COMPUTERNAME
            Character=$CharacterName; CharacterFileName=[IO.Path]::GetFileName($CharacterPath)
            SourcePath=[IO.Path]::GetFullPath($CharacterPath); StorageSource=(Split-Path -Parent $CharacterPath)
            Kind=$kind; BackupClass=$BackupClass; Validation=$copyValidation; Files=@($files)
        }
        $manifest | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $stage 'Safeguardian_manifest.json') -Encoding UTF8
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::CreateFromDirectory($stage, $tempZip, [IO.Compression.CompressionLevel]::Optimal, $false)
        [void](Test-ZipArchive $tempZip)
        Move-Item -LiteralPath $tempZip -Destination $finalZip
        $zipHash = (Get-FileHash -LiteralPath $finalZip -Algorithm SHA256).Hash
        "$zipHash  $([IO.Path]::GetFileName($finalZip))" | Set-Content -LiteralPath ($finalZip + '.sha256.txt') -Encoding ASCII
        return $finalZip
    } finally {
        if (Test-Path -LiteralPath $tempZip) { Remove-Item -LiteralPath $tempZip -Force -ErrorAction SilentlyContinue }
        if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

function Remove-ExpiredBackups([string]$WorldName, [bool]$Quarantine, [ValidateSet('World','Character')][string]$AssetType = 'World', [string]$SourcePath = '') {
    $config = Get-Config
    $kind = if ($Quarantine) { 'QUARANTINE' } else { 'GOOD' }
    $retain = if ($Quarantine) { [int]$config.RetainQuarantineBackups } else { [int]$config.RetainGoodBackups }
    $backupFolder = if ([string]::IsNullOrWhiteSpace($SourcePath)) { Get-BackupFolder $AssetType } else { Get-BackupItemFolder $AssetType $SourcePath }
    if ($retain -lt 1 -or -not (Test-Path -LiteralPath $backupFolder -PathType Container)) { return }
    $historyName = $WorldName
    if (-not [string]::IsNullOrWhiteSpace($SourcePath)) {
        $historyName = if ($AssetType -eq 'Character') {
            'Character_' + [IO.Path]::GetFileNameWithoutExtension($SourcePath)
        } else {
            Split-Path -Leaf $SourcePath
        }
    }
    $safeWorldName = $historyName -replace '[^A-Za-z0-9._-]', '_'
    $pattern = "${safeWorldName}_${kind}_*.zip"
    $old = @(Get-ChildItem -LiteralPath $backupFolder -File -Filter $pattern | Sort-Object LastWriteTimeUtc -Descending | Select-Object -Skip $retain)
    foreach ($item in $old) {
        try {
            Remove-Item -LiteralPath $item.FullName -Force
            $sidecar = $item.FullName + '.sha256.txt'
            if (Test-Path -LiteralPath $sidecar) { Remove-Item -LiteralPath $sidecar -Force }
        } catch {
            Write-AppLog "Old backup could not be removed: $($item.FullName). $($_.Exception.Message)" 'WARN'
        }
    }
}

function Remove-ExpiredBackupAtSave([string]$BackupAtSaveFolder) {
    $config = Get-Config
    $retain = [Math]::Max(1, [int]$config.BackupAtSaveRetention)
    if (-not (Test-Path -LiteralPath $BackupAtSaveFolder -PathType Container)) { return }
    $expired = @(Get-ChildItem -LiteralPath $BackupAtSaveFolder -File -Filter '*.zip' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -Skip $retain)
    foreach ($item in $expired) {
        try {
            Remove-Item -LiteralPath $item.FullName -Force
            $sidecar = $item.FullName + '.sha256.txt'
            if (Test-Path -LiteralPath $sidecar -PathType Leaf) { Remove-Item -LiteralPath $sidecar -Force }
            Write-AppLog "Expired BackupAtSave archive removed: $($item.FullName)"
        } catch {
            Write-AppLog "Expired BackupAtSave archive could not be removed: $($item.FullName). $($_.Exception.Message)" 'WARN'
        }
    }
}

function Save-Status([array]$Results, [string]$Message) {
    Ensure-Directory $ProductRoot
    [pscustomobject]@{
        CheckedAt = (Get-Date).ToString('o')
        Valid = ($Results.Count -gt 0 -and @($Results | Where-Object { -not $_.Valid }).Count -eq 0)
        Message = $Message
        Worlds = @($Results)
    } | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $StatusPath -Encoding UTF8
}

function Export-DiagnosticBundle {
    Ensure-Directory $ProductRoot
    if (Test-Path -LiteralPath $DiagnosticResultPath) { Remove-Item -LiteralPath $DiagnosticResultPath -Force -ErrorAction SilentlyContinue }
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $diagnosticsRoot = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'MagicDragon_Safeguardian\Diagnostics'
    Ensure-Directory $diagnosticsRoot
    $finalZip = Join-Path $diagnosticsRoot ("MagicDragon_Diagnostics_$stamp.zip")
    $stage = Join-Path $ProductRoot ('.diagnostics_' + [guid]::NewGuid().ToString('N'))
    Ensure-Directory $stage
    try {
        $config = Get-Config
        $worlds = @(Get-DiscoverableWorlds)
        $characters = @(Get-ValheimCharacters)
        $storage = Get-BackupStorageHealth 0
        $report = @(
            "MagicDragon Safeguardian diagnostic export"
            "Application version: $AppVersion"
            "Created: $((Get-Date).ToString('o'))"
            "Computer: $env:COMPUTERNAME"
            "Windows: $([Environment]::OSVersion.VersionString)"
            "PowerShell: $($PSVersionTable.PSVersion)"
            "Valheim running: $(@(Get-Process -Name 'valheim' -ErrorAction SilentlyContinue).Count -gt 0)"
            "Discovered worlds: $($worlds.Count)"
            "Discovered characters: $($characters.Count)"
            "Backup storage known: $($storage.Known)"
            "Backup free bytes: $($storage.FreeBytes)"
            "Backup storage warning: $($storage.Warning)"
            ''
            'This ZIP contains metadata and file listings only. It does not contain world or character save payloads.'
        )
        $report | Set-Content -LiteralPath (Join-Path $stage 'diagnostic-report.txt') -Encoding UTF8

        foreach ($source in @($ConfigPath, $ConfigBackupPath, $StatusPath, $RestorePreviewPath, $RestoreResultPath, $ActiveSessionStatePath, (Join-Path $LogRoot 'Safeguardian.log'))) {
            if (Test-Path -LiteralPath $source -PathType Leaf) { Copy-Item -LiteralPath $source -Destination (Join-Path $stage ([IO.Path]::GetFileName($source))) -Force }
        }

        $saveListing = New-Object System.Collections.Generic.List[object]
        foreach ($world in $worlds) {
            if (-not (Test-Path -LiteralPath $world.Path -PathType Container)) { continue }
            Get-ChildItem -LiteralPath $world.Path -File -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
                $saveListing.Add([pscustomobject]@{
                    AssetType='World'; Save=$world.DisplayName; SourcePath=$world.Path
                    RelativePath=$_.FullName.Substring($world.Path.Length).TrimStart('\')
                    Size=$_.Length; ModifiedUtc=$_.LastWriteTimeUtc.ToString('o')
                })
            }
        }
        foreach ($character in $characters) {
            foreach ($path in @($character.Path, ($character.Path + '.old'), ($character.Path + '.new'))) {
                if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
                $file = Get-Item -LiteralPath $path
                $saveListing.Add([pscustomobject]@{
                    AssetType='Character'; Save=$character.DisplayName; SourcePath=(Split-Path -Parent $character.Path)
                    RelativePath=$file.Name; Size=$file.Length; ModifiedUtc=$file.LastWriteTimeUtc.ToString('o')
                })
            }
        }
        $saveListingPath = Join-Path $stage 'save-file-listing.csv'
        if ($saveListing.Count -gt 0) {
            $saveListing | Export-Csv -LiteralPath $saveListingPath -NoTypeInformation -Encoding UTF8
        } else {
            '"AssetType","Save","SourcePath","RelativePath","Size","ModifiedUtc"' | Set-Content -LiteralPath $saveListingPath -Encoding UTF8
        }

        $backupListing = if (Test-Path -LiteralPath $config.BackupRoot -PathType Container) {
            $listingRoot = [IO.Path]::GetFullPath([string]$config.BackupRoot).TrimEnd('\')
            Get-ChildItem -LiteralPath $config.BackupRoot -File -Recurse -ErrorAction SilentlyContinue | Sort-Object FullName | ForEach-Object {
                [pscustomobject]@{ RelativePath=$_.FullName.Substring($listingRoot.Length).TrimStart('\'); Size=$_.Length; ModifiedUtc=$_.LastWriteTimeUtc.ToString('o') }
            }
        } else { @() }
        $backupListingPath = Join-Path $stage 'backup-file-listing.csv'
        if (@($backupListing).Count -gt 0) {
            $backupListing | Export-Csv -LiteralPath $backupListingPath -NoTypeInformation -Encoding UTF8
        } else {
            '"RelativePath","Size","ModifiedUtc"' | Set-Content -LiteralPath $backupListingPath -Encoding UTF8
        }

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::CreateFromDirectory($stage, $finalZip, [IO.Compression.CompressionLevel]::Optimal, $false)
        [void](Test-ZipArchive $finalZip)
        $result = [pscustomobject]@{
            Success=$true; Path=$finalZip; CreatedAt=(Get-Date).ToString('o')
            SHA256=(Get-FileHash -LiteralPath $finalZip -Algorithm SHA256).Hash
            Message='Diagnostic ZIP created without world or character payloads.'
        }
        $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $DiagnosticResultPath -Encoding UTF8
        Write-AppLog "Diagnostic export created: $finalZip"
        return $result
    } catch {
        $result = [pscustomobject]@{ Success=$false; Path=''; CreatedAt=(Get-Date).ToString('o'); SHA256=''; Message=$_.Exception.Message }
        $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $DiagnosticResultPath -Encoding UTF8
        Write-AppLog "Diagnostic export failed: $($_.Exception.Message)" 'ERROR'
        throw
    } finally {
        if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

function Show-Alert([string]$Title, [string]$Text, [bool]$IsError, $TrayIcon = $null) {
    try {
        Add-Type -AssemblyName System.Windows.Forms
        if ($null -ne $TrayIcon) {
            $TrayIcon.BalloonTipTitle = $Title
            $TrayIcon.BalloonTipText = $Text
            $TrayIcon.BalloonTipIcon = if ($IsError) { [Windows.Forms.ToolTipIcon]::Error } else { [Windows.Forms.ToolTipIcon]::Info }
            $TrayIcon.ShowBalloonTip(10000)
        } elseif (-not $Silent) {
            $icon = if ($IsError) { [Windows.Forms.MessageBoxIcon]::Error } else { [Windows.Forms.MessageBoxIcon]::Information }
            [void][Windows.Forms.MessageBox]::Show($Text, $Title, [Windows.Forms.MessageBoxButtons]::OK, $icon)
        }
        if ($IsError) { [Media.SystemSounds]::Exclamation.Play() }
    } catch { }
}

function Invoke-WorldSafeguard([string]$WorldName, [string]$WorldPath, [bool]$WaitForStable = $true, [bool]$RequireUnchanged = $false) {
    $config = Get-Config
    Write-AppLog "Check started for world '$WorldName' at $WorldPath"
    if ($WaitForStable -and -not (Wait-WorldStable $WorldPath ([int]$config.SettleSeconds))) {
        Write-AppLog "World '$WorldName' did not become stable; backup blocked." 'ERROR'
        return [pscustomobject]@{
            AssetType='World'; WorldName=$WorldName; WorldPath=$WorldPath; Valid=$false; Generation=$null; FormatVersion=$null; IndexedChunks=0
            BackupPath=''; Message='World files kept changing; backup blocked.'
            Errors=@('World files did not become stable before timeout.'); Warnings=@(); CheckedAt=(Get-Date).ToString('o')
        }
    }

    $stableFingerprint = if ($RequireUnchanged) { Get-WorldFingerprint $WorldPath } else { '' }

    $validation = Test-ValheimWorld $WorldPath
    $backupPath = ''
    if ($validation.Valid) {
        try {
            $backupPath = New-WorldSnapshot $WorldName $WorldPath $validation $false
            if (-not $RequireUnchanged) { Remove-ExpiredBackups $WorldName $false 'World' $WorldPath }
            $message = "VALID: generation $($validation.Generation), format $($validation.FormatVersion), $($validation.IndexedChunks) indexed chunks. Backup verified."
            Write-AppLog "World '$WorldName': $message Archive: $backupPath"
        } catch {
            $validation.Errors += "Backup creation failed: $($_.Exception.Message)"
            $validation.Valid = $false
            $message = 'Validation passed, but backup creation failed.'
            Write-AppLog "World '$WorldName' backup creation failed: $($_.Exception.Message)" 'ERROR'
        }
    } else {
        $message = 'CORRUPTION/INCOMPLETE SAVE DETECTED. No good backup was created.'
        try {
            if (Test-Path -LiteralPath $WorldPath -PathType Container) {
                $backupPath = New-WorldSnapshot $WorldName $WorldPath $validation $true
                if (-not $RequireUnchanged) { Remove-ExpiredBackups $WorldName $true 'World' $WorldPath }
            }
        } catch { Write-AppLog "Emergency quarantine copy failed: $($_.Exception.Message)" 'ERROR' }
        Write-AppLog "World '$WorldName': $message $($validation.Errors -join ' ')" 'ERROR'
    }
    if ($RequireUnchanged) {
        $endingFingerprint = Get-WorldFingerprint $WorldPath
        if ($endingFingerprint -ne $stableFingerprint) {
            if (-not [string]::IsNullOrWhiteSpace($backupPath) -and (Test-Path -LiteralPath $backupPath -PathType Leaf)) {
                Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue
                $sidecar = $backupPath + '.sha256.txt'
                if (Test-Path -LiteralPath $sidecar -PathType Leaf) { Remove-Item -LiteralPath $sidecar -Force -ErrorAction SilentlyContinue }
            }
            $backupPath = ''
            $validation.Valid = $false
            $validation.Errors += 'The world changed during the live check; no GOOD backup was accepted.'
            $message = 'LIVE CHECK INCONCLUSIVE: Valheim changed the files during inspection. Save again, wait for completion, and retry.'
            Write-AppLog "World '$WorldName': $message" 'WARN'
        } elseif (-not [string]::IsNullOrWhiteSpace($backupPath)) {
            Remove-ExpiredBackups $WorldName (-not [bool]$validation.Valid) 'World' $WorldPath
        }
    }

    [pscustomobject]@{
        AssetType='World'; WorldName=$WorldName; WorldPath=$WorldPath; Valid=[bool]$validation.Valid
        Generation=$validation.Generation; FormatVersion=$validation.FormatVersion; IndexedChunks=$validation.IndexedChunks
        BackupPath=$backupPath; Message=$message; Errors=@($validation.Errors); Warnings=@($validation.Warnings)
        CheckedAt=(Get-Date).ToString('o')
    }
}

function Invoke-CharacterSafeguard([string]$CharacterName, [string]$CharacterPath, [bool]$WaitForStable = $true, [bool]$RequireUnchanged = $false) {
    $config = Get-Config
    Write-AppLog "Check started for character '$CharacterName' at $CharacterPath"
    if ($WaitForStable -and -not (Wait-FileStable $CharacterPath ([int]$config.SettleSeconds))) {
        Write-AppLog "Character '$CharacterName' did not become stable; backup blocked." 'ERROR'
        return [pscustomobject]@{
            AssetType='Character'; WorldName=$CharacterName; WorldPath=$CharacterPath; Valid=$false; Generation=$null
            FormatVersion=$null; IndexedChunks=0; BackupPath=''; Message='Character file kept changing; backup blocked.'
            Errors=@('Character file did not become stable before timeout.'); Warnings=@(); CheckedAt=(Get-Date).ToString('o')
        }
    }
    $stableFingerprint = if ($RequireUnchanged) { Get-FileFingerprint $CharacterPath } else { '' }
    $validation = Test-ValheimCharacter $CharacterPath
    $backupPath = ''
    if ($validation.Valid) {
        try {
            $backupPath = New-CharacterSnapshot $CharacterName $CharacterPath $validation $false
            if (-not $RequireUnchanged) { Remove-ExpiredBackups ("Character_$CharacterName") $false 'Character' $CharacterPath }
            $message = "VALID CHARACTER: stable readable file, header marker $($validation.HeaderMarker), $($validation.Size) bytes. Backup verified."
            Write-AppLog "Character '$CharacterName': $message Archive: $backupPath"
        } catch {
            $validation.Errors += "Backup creation failed: $($_.Exception.Message)"
            $validation.Valid = $false
            $message = 'Character validation passed, but backup creation failed.'
            Write-AppLog "Character '$CharacterName' backup creation failed: $($_.Exception.Message)" 'ERROR'
        }
    } else {
        $message = 'CORRUPT/INCOMPLETE CHARACTER DETECTED. No GOOD backup was created.'
        try {
            if (Test-Path -LiteralPath $CharacterPath -PathType Leaf) {
                $backupPath = New-CharacterSnapshot $CharacterName $CharacterPath $validation $true
                if (-not $RequireUnchanged) { Remove-ExpiredBackups ("Character_$CharacterName") $true 'Character' $CharacterPath }
            }
        } catch { Write-AppLog "Emergency character quarantine failed: $($_.Exception.Message)" 'ERROR' }
        Write-AppLog "Character '$CharacterName': $message $($validation.Errors -join ' ')" 'ERROR'
    }
    if ($RequireUnchanged) {
        $endingFingerprint = Get-FileFingerprint $CharacterPath
        if ($endingFingerprint -ne $stableFingerprint) {
            if (-not [string]::IsNullOrWhiteSpace($backupPath) -and (Test-Path -LiteralPath $backupPath -PathType Leaf)) {
                Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue
                $sidecar = $backupPath + '.sha256.txt'
                if (Test-Path -LiteralPath $sidecar -PathType Leaf) { Remove-Item -LiteralPath $sidecar -Force -ErrorAction SilentlyContinue }
            }
            $backupPath = ''
            $validation.Valid = $false
            $validation.Errors += 'The character changed during the live check; no GOOD backup was accepted.'
            $message = 'LIVE CHARACTER CHECK INCONCLUSIVE: Valheim changed the character during inspection.'
            Write-AppLog "Character '$CharacterName': $message" 'WARN'
        } elseif (-not [string]::IsNullOrWhiteSpace($backupPath)) {
            Remove-ExpiredBackups ("Character_$CharacterName") (-not [bool]$validation.Valid) 'Character' $CharacterPath
        }
    }
    [pscustomobject]@{
        AssetType='Character'; WorldName=$CharacterName; WorldPath=$CharacterPath; Valid=[bool]$validation.Valid
        Generation=$null; FormatVersion=$validation.Version; IndexedChunks=0; BackupPath=$backupPath
        Message=$message; Errors=@($validation.Errors); Warnings=@($validation.Warnings); CheckedAt=(Get-Date).ToString('o')
    }
}

function Invoke-LiveWorldSafeguard([string]$WorldName, [string]$WorldPath) {
    Write-AppLog "Fast live world check started for '$WorldName' at $WorldPath"
    $state = $null
    $stable = $false
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $before = Get-LiveWorldFingerprint $WorldPath
        $state = Test-LiveWorldState $WorldPath
        Start-Sleep -Milliseconds 600
        $after = Get-LiveWorldFingerprint $WorldPath
        if ($before -eq $after) { $stable = $true; break }
    }
    if (-not $stable) {
        $state = [pscustomobject]@{
            Valid=$false; Generation=$null; FormatVersion=$null; IndexedChunks=0; ChunkFiles=0; CacheFiles=0
            Errors=@('World save files are still changing. Wait until Valheim finishes saving, then retry.'); Warnings=@()
        }
    }
    $valid = [bool]$state.Valid -and $stable
    $message = if ($valid) {
        "LIVE WORLD GOOD: generation $($state.Generation), $($state.IndexedChunks) indexed chunks, $($state.ChunkFiles) total chunk files and $($state.CacheFiles) cache files checked."
    } else {
        'LIVE WORLD CHECK FAILED: the current on-disk save is incomplete, inconsistent, unreadable, or still changing.'
    }
    $level = if ($valid) { 'INFO' } else { 'ERROR' }
    Write-AppLog "World '$WorldName': $message $($state.Errors -join ' ')" $level
    [pscustomobject]@{
        AssetType='World'; WorldName=$WorldName; WorldPath=$WorldPath; Valid=$valid
        Generation=$state.Generation; FormatVersion=$state.FormatVersion; IndexedChunks=$state.IndexedChunks
        BackupPath=''; Message=$message; Errors=@($state.Errors); Warnings=@($state.Warnings); CheckedAt=(Get-Date).ToString('o')
    }
}

function Get-CurrentSessionCharacter {
    $characters = @(Get-ValheimCharacters | ForEach-Object {
        $file = Get-Item -LiteralPath $_.Path -ErrorAction SilentlyContinue
        if ($null -ne $file) {
            [pscustomobject]@{ Name=$_.Name; DisplayName=$_.DisplayName; Path=$_.Path; Source=$_.Source; ModifiedUtc=$file.LastWriteTimeUtc }
        }
    } | Sort-Object ModifiedUtc -Descending)
    if ($characters.Count -gt 0) { return $characters[0] }
    return $null
}

function Invoke-LiveCharacterSafeguard($Character) {
    Write-AppLog "Fast live character check started for '$($Character.DisplayName)' at $($Character.Path)"
    $validation = $null
    $stable = $false
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $before = Get-FileFingerprint $Character.Path
        $validation = Test-ValheimCharacter $Character.Path
        Start-Sleep -Milliseconds 350
        $after = Get-FileFingerprint $Character.Path
        if ($before -eq $after) { $stable = $true; break }
    }
    if (-not $stable) {
        $validation = [pscustomobject]@{
            Valid=$false; Version=$null; Size=0; SHA256=''
            Errors=@('Character file is still changing. Wait until Valheim finishes saving, then retry.'); Warnings=@()
        }
    }
    $valid = [bool]$validation.Valid -and $stable
    $ageSeconds = [Math]::Max(0, [int]((Get-Date).ToUniversalTime() - $Character.ModifiedUtc).TotalSeconds)
    $message = if ($valid) {
        "LIVE CHARACTER GOOD: $($Character.DisplayName), stable readable file, header marker $($validation.HeaderMarker), $($validation.Size) bytes, last written $ageSeconds second(s) ago."
    } else {
        'LIVE CHARACTER CHECK FAILED: the current-session character file is invalid, unreadable, or still changing.'
    }
    $level = if ($valid) { 'INFO' } else { 'ERROR' }
    Write-AppLog "Character '$($Character.DisplayName)': $message $($validation.Errors -join ' ')" $level
    [pscustomobject]@{
        AssetType='Character'; WorldName=$Character.DisplayName; WorldPath=$Character.Path; Valid=$valid
        Generation=$null; FormatVersion=$validation.Version; IndexedChunks=0; BackupPath=''
        Message=$message; Errors=@($validation.Errors); Warnings=@($validation.Warnings); CheckedAt=(Get-Date).ToString('o')
    }
}

function Invoke-LiveSafeguards([ValidateSet('World','Character','All')][string]$Scope = 'All', $TrayIcon = $null,
    [string]$RequestedWorldPath = '', [string]$RequestedCharacterPath = '') {
    # Use a native PowerShell array here. Passing Generic.List[object] through
    # an [array] parameter can trigger "Argument types do not match" in Windows
    # PowerShell 5.1 after a successful validation.
    $results = @()
    if ($Scope -eq 'World' -or $Scope -eq 'All') {
        $liveWorlds = @()
        if (-not [string]::IsNullOrWhiteSpace($RequestedWorldPath)) {
            try {
                $requestedFullPath = [IO.Path]::GetFullPath($RequestedWorldPath)
                $liveWorlds = @(Get-DiscoverableWorlds | Where-Object {
                    [IO.Path]::GetFullPath([string]$_.Path).Equals($requestedFullPath, [StringComparison]::OrdinalIgnoreCase)
                })
            } catch { $liveWorlds = @() }
            if ($liveWorlds.Count -ne 1) {
                $message = "The active-session world could not be resolved safely: $RequestedWorldPath"
                Save-Status @() $message
                Write-AppLog $message 'ERROR'
                return @()
            }
            Write-AppLog "Live world target resolved from current-session save activity: $($liveWorlds[0].DisplayName) at $requestedFullPath"
        } else {
            $liveWorlds = @(Get-ValheimWorlds)
        }
        foreach ($world in $liveWorlds) {
            $displayName = if ($null -ne $world.PSObject.Properties['DisplayName']) { [string]$world.DisplayName } else { [string]$world.Name }
            $results += (Invoke-LiveWorldSafeguard $displayName $world.Path)
        }
    }
    if ($Scope -eq 'Character' -or $Scope -eq 'All') {
        $character = $null
        if (-not [string]::IsNullOrWhiteSpace($RequestedCharacterPath)) {
            try {
                $requestedFullPath = [IO.Path]::GetFullPath($RequestedCharacterPath)
                $matches = @(Get-ValheimCharacters | Where-Object {
                    [IO.Path]::GetFullPath([string]$_.Path).Equals($requestedFullPath, [StringComparison]::OrdinalIgnoreCase)
                })
                if ($matches.Count -eq 1) {
                    $file = Get-Item -LiteralPath $matches[0].Path -ErrorAction SilentlyContinue
                    if ($null -ne $file) {
                        $character = [pscustomobject]@{
                            Name=$matches[0].Name; DisplayName=$matches[0].DisplayName; Path=$matches[0].Path
                            Source=$matches[0].Source; ModifiedUtc=$file.LastWriteTimeUtc
                        }
                        Write-AppLog "Live character target resolved from current-session save activity: $($character.DisplayName) at $requestedFullPath"
                    }
                }
            } catch { $character = $null }
            if ($null -eq $character) {
                $message = "The current-session character could not be resolved safely: $RequestedCharacterPath"
                Save-Status @() $message
                Write-AppLog $message 'ERROR'
                return @()
            }
        } else {
            $character = Get-CurrentSessionCharacter
        }
        if ($null -ne $character) { $results += (Invoke-LiveCharacterSafeguard $character) }
    }
    if ($results.Count -eq 0) {
        $message = if ($Scope -eq 'Character') { 'No selected local or Steam Cloud Valheim character file was found.' } else { 'No selected Valheim world was found.' }
        Save-Status @() $message
        Write-AppLog $message 'WARN'
        Show-Alert $ProductName $message $true $TrayIcon
        return @()
    }
    $failed = @($results | Where-Object { -not $_.Valid })
    $message = if ($failed.Count -eq 0) {
        if ($Scope -eq 'Character') { 'Live character check passed. The detected current-session character file is coherent.' }
        elseif ($Scope -eq 'World') { 'Live world check passed. The committed generation, chunks, and cache files are coherent.' }
        else { 'Live save check passed.' }
    } else {
        "Live $($Scope.ToLowerInvariant()) check needs attention. $($failed.Count) save(s) failed verification."
    }
    Save-Status -Results ([array]$results) -Message $message
    Show-Alert $ProductName $message ($failed.Count -gt 0) $TrayIcon
    return $results
}

function Invoke-WorldIntegrityChecks {
    $worlds = @(Get-ValheimWorlds)
    if ($worlds.Count -eq 0) {
        $message = 'No selected Valheim world was found. Use Choose Worlds before running this check.'
        Save-Status @() $message
        Write-AppLog $message 'WARN'
        return @()
    }
    $results = @()
    foreach ($world in $worlds) {
        $displayName = [string]$world.Name
        Write-AppLog "Integrity-only world check started for '$displayName' at $($world.Path)"
        $validation = Test-ValheimWorld $world.Path
        $message = if ([bool]$validation.Valid) {
            "WORLD GOOD: generation $($validation.Generation), $($validation.IndexedChunks) indexed chunks. No backup was created."
        } else {
            'WORLD CHECK FAILED: no complete, internally consistent committed generation was found.'
        }
        $level = if ([bool]$validation.Valid) { 'INFO' } else { 'ERROR' }
        Write-AppLog "World '$displayName': $message $($validation.Errors -join ' ')" $level
        $results += [pscustomobject]@{
            AssetType='World'; WorldName=$displayName; WorldPath=$world.Path; Valid=[bool]$validation.Valid
            Generation=$validation.Generation; FormatVersion=$validation.FormatVersion; IndexedChunks=$validation.IndexedChunks
            BackupPath=''; Message=$message; Errors=@($validation.Errors); Warnings=@($validation.Warnings); CheckedAt=(Get-Date).ToString('o')
        }
    }
    $failed = @($results | Where-Object { -not $_.Valid })
    $message = if ($failed.Count -eq 0) {
        "$($results.Count) selected world(s) passed integrity verification. No backup was created."
    } else {
        "$($failed.Count) selected world(s) failed integrity verification. No backup was created."
    }
    Save-Status -Results ([array]$results) -Message $message
    return $results
}

function Invoke-CharacterIntegrityChecks {
    $characters = @(Get-ValheimCharacters)
    if ($characters.Count -eq 0) {
        $message = 'No selected Valheim character was found. Use Choose Characters before running this check.'
        Save-Status @() $message
        Write-AppLog $message 'WARN'
        return @()
    }
    $results = @()
    foreach ($character in $characters) {
        Write-AppLog "Integrity-only character check started for '$($character.DisplayName)' at $($character.Path)"
        $validation = Test-ValheimCharacter $character.Path
        $message = if ([bool]$validation.Valid) {
            "CHARACTER GOOD: stable readable file, $($validation.Size) bytes. No backup was created."
        } else {
            'CHARACTER CHECK FAILED: the character file is incomplete, unreadable, or inconsistent.'
        }
        $level = if ([bool]$validation.Valid) { 'INFO' } else { 'ERROR' }
        Write-AppLog "Character '$($character.DisplayName)': $message $($validation.Errors -join ' ')" $level
        $results += [pscustomobject]@{
            AssetType='Character'; WorldName=$character.DisplayName; WorldPath=$character.Path; Valid=[bool]$validation.Valid
            Generation=$null; FormatVersion=$validation.Version; IndexedChunks=0; BackupPath=''; Message=$message
            Errors=@($validation.Errors); Warnings=@($validation.Warnings); CheckedAt=(Get-Date).ToString('o')
        }
    }
    $failed = @($results | Where-Object { -not $_.Valid })
    $message = if ($failed.Count -eq 0) {
        "$($results.Count) selected character(s) passed integrity verification. No backup was created."
    } else {
        "$($failed.Count) selected character(s) failed integrity verification. No backup was created."
    }
    Save-Status -Results ([array]$results) -Message $message
    return $results
}

function Invoke-BackupAtSaveWorld([string]$WorldPath) {
    if ([string]::IsNullOrWhiteSpace($WorldPath) -or -not (Test-Path -LiteralPath $WorldPath -PathType Container)) {
        throw 'The active world folder for BackupAtSave is unavailable.'
    }
    $config = Get-Config
    $worldName = Split-Path -Leaf $WorldPath
    Write-AppLog "BackupAtSave world verification started for '$worldName' at $WorldPath"
    if (-not (Wait-WorldStable $WorldPath ([int]$config.SettleSeconds))) {
        Write-AppLog "BackupAtSave blocked because world '$worldName' did not become stable." 'ERROR'
        return [pscustomobject]@{ AssetType='World'; WorldName=$worldName; WorldPath=$WorldPath; Valid=$false; Generation=$null; FormatVersion=$null; IndexedChunks=0; BackupPath=''; Message=$BackupAtSaveFailureMessage; Errors=@('World files did not become stable.'); Warnings=@(); CheckedAt=(Get-Date).ToString('o') }
    }
    $sourceFingerprint = Get-WorldFingerprint $WorldPath
    $validation = Test-ValheimWorld $WorldPath
    $backupPath = ''
    $message = ''
    if ($validation.Valid) {
        $destination = Join-Path (Get-BackupItemFolder 'World' $WorldPath) 'BackupAtSave'
        $backupPath = New-WorldSnapshot $worldName $WorldPath $validation $false $destination 'BackupAtSave'
        if ((Get-WorldFingerprint $WorldPath) -ne $sourceFingerprint) {
            Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath ($backupPath + '.sha256.txt') -Force -ErrorAction SilentlyContinue
            $backupPath = ''
            $validation.Valid = $false
            $validation.Errors += 'The world changed while BackupAtSave was being created.'
            $message = $BackupAtSaveFailureMessage
        } else {
            Remove-ExpiredBackupAtSave $destination
            $message = "BACKUP AT SAVE GOOD: generation $($validation.Generation), $($validation.IndexedChunks) indexed chunks."
        }
    } else {
        $message = $BackupAtSaveFailureMessage
    }
    $level = if ($validation.Valid) { 'INFO' } else { 'ERROR' }
    Write-AppLog "World '$worldName': $message $($validation.Errors -join ' ')" $level
    $result = [pscustomobject]@{
        AssetType='World'; WorldName=$worldName; WorldPath=$WorldPath; Valid=[bool]$validation.Valid
        Generation=$validation.Generation; FormatVersion=$validation.FormatVersion; IndexedChunks=$validation.IndexedChunks
        BackupPath=$backupPath; Message=$message; Errors=@($validation.Errors); Warnings=@($validation.Warnings); CheckedAt=(Get-Date).ToString('o')
    }
    Save-Status -Results @($result) -Message $message
    return $result
}

function Invoke-BackupAtSaveCharacter([string]$CharacterPath) {
    if ([string]::IsNullOrWhiteSpace($CharacterPath) -or -not (Test-Path -LiteralPath $CharacterPath -PathType Leaf)) {
        throw 'The active selected character file for BackupAtSave is unavailable.'
    }
    $config = Get-Config
    $characterName = [IO.Path]::GetFileNameWithoutExtension($CharacterPath)
    Write-AppLog "BackupAtSave character verification started for '$characterName' at $CharacterPath"
    if (-not (Wait-FileStable $CharacterPath ([int]$config.SettleSeconds))) {
        Write-AppLog "BackupAtSave blocked because character '$characterName' did not become stable." 'ERROR'
        return [pscustomobject]@{ AssetType='Character'; WorldName=$characterName; WorldPath=$CharacterPath; Valid=$false; Generation=$null; FormatVersion=$null; IndexedChunks=0; BackupPath=''; Message=$BackupAtSaveFailureMessage; Errors=@('Character files did not become stable.'); Warnings=@(); CheckedAt=(Get-Date).ToString('o') }
    }
    $sourceFingerprint = Get-FileFingerprint $CharacterPath
    $validation = Test-ValheimCharacter $CharacterPath
    $backupPath = ''
    $message = ''
    if ($validation.Valid) {
        $destination = Join-Path (Get-BackupItemFolder 'Character' $CharacterPath) 'BackupAtSave'
        $backupPath = New-CharacterSnapshot $characterName $CharacterPath $validation $false $destination 'BackupAtSave'
        if ((Get-FileFingerprint $CharacterPath) -ne $sourceFingerprint) {
            Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath ($backupPath + '.sha256.txt') -Force -ErrorAction SilentlyContinue
            $backupPath = ''
            $validation.Valid = $false
            $validation.Errors += 'The character changed while BackupAtSave was being created.'
            $message = $BackupAtSaveFailureMessage
        } else {
            Remove-ExpiredBackupAtSave $destination
            $message = "BACKUP AT SAVE CHARACTER GOOD: $($validation.Size) bytes verified."
        }
    } else {
        $message = $BackupAtSaveFailureMessage
    }
    $level = if ($validation.Valid) { 'INFO' } else { 'ERROR' }
    Write-AppLog "Character '$characterName': $message $($validation.Errors -join ' ')" $level
    $result = [pscustomobject]@{
        AssetType='Character'; WorldName=$characterName; WorldPath=$CharacterPath; Valid=[bool]$validation.Valid
        Generation=$null; FormatVersion=$validation.Version; IndexedChunks=0; BackupPath=$backupPath
        Message=$message; Errors=@($validation.Errors); Warnings=@($validation.Warnings); CheckedAt=(Get-Date).ToString('o')
    }
    Save-Status -Results @($result) -Message $message
    return $result
}

function Invoke-AllSafeguards($TrayIcon = $null, [bool]$WaitForStable = $true, [bool]$RequireUnchanged = $false, [ValidateSet('World','Character','All')][string]$Scope = 'All') {
    # Keep these as native arrays even when discovery yields exactly one item.
    # Windows PowerShell 5.1 otherwise unwraps the output of the if expression
    # and StrictMode makes .Count fail on the resulting PSCustomObject.
    $worlds = @()
    $characters = @()
    if ($Scope -ne 'Character') { $worlds = @(Get-ValheimWorlds) }
    if ($Scope -ne 'World') { $characters = @(Get-ValheimCharacters) }
    if ($worlds.Count -eq 0 -and $characters.Count -eq 0) {
        $discoverable = @(Get-DiscoverableWorlds)
        $message = if ($Scope -eq 'Character') {
            'No selected local or Steam Cloud Valheim character files were found. Use the Characters page to choose characters.'
        } elseif ($discoverable.Count -eq 0) {
            'No selected worlds or characters were found.'
        } else {
            'No worlds are selected. Use Settings > Choose worlds to scrutinize.'
        }
        Save-Status @() $message
        Write-AppLog $message 'WARN'
        Show-Alert $ProductName $message $true $TrayIcon
        return @()
    }
    $results = @(
        $worlds | ForEach-Object { Invoke-WorldSafeguard $_.Name $_.Path $WaitForStable $RequireUnchanged }
        $characters | ForEach-Object { Invoke-CharacterSafeguard $_.DisplayName $_.Path $WaitForStable $RequireUnchanged }
    )
    $failed = @($results | Where-Object { -not $_.Valid })
    $good = @($results | Where-Object { $_.Valid })
    $storage = Get-BackupStorageHealth 0
    $storageWarning = [string]$storage.Warning
    if ($failed.Count -eq 0) {
        $worldCount = @($good | Where-Object { $_.AssetType -eq 'World' }).Count
        $characterCount = @($good | Where-Object { $_.AssetType -eq 'Character' }).Count
        $message = "$worldCount world(s) and $characterCount character(s) validated and backed up successfully."
        if (-not [string]::IsNullOrWhiteSpace($storageWarning)) { $message += " STORAGE WARNING: $storageWarning" }
        Save-Status $results $message
        Show-Alert $ProductName $message (-not [string]::IsNullOrWhiteSpace($storageWarning)) $TrayIcon
    } else {
        $names = ($failed | ForEach-Object { $_.WorldName }) -join ', '
        $message = "$($good.Count) save(s) protected; $($failed.Count) FAILED: $names. No failed save entered the GOOD history."
        if (-not [string]::IsNullOrWhiteSpace($storageWarning)) { $message += " STORAGE WARNING: $storageWarning" }
        Save-Status $results $message
        $guidance = if ($RequireUnchanged) { 'Wait for another completed in-game save and retry the live check.' } else { 'Review the failed save details in the log.' }
        Show-Alert $ProductName "$message $guidance" $true $TrayIcon
    }
    return $results
}

function New-Shortcut([string]$Path, [string]$Arguments) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = (Join-Path $PSHOME 'powershell.exe')
    $shortcut.Arguments = $Arguments
    $shortcut.WorkingDirectory = $InstallDir
    $shortcut.IconLocation = "$env:SystemRoot\System32\shell32.dll,77"
    $shortcut.Save()
}

function Stop-InstalledMonitor {
    try {
        $processes = @(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
            ($_.Name -eq 'powershell.exe' -or $_.Name -eq 'pwsh.exe') -and
            $_.ProcessId -ne $PID -and
            $_.CommandLine -like '*MagicDragon_Safeguardian.ps1*' -and
            $_.CommandLine -like '*-Mode*Monitor*'
        })
        foreach ($process in $processes) {
            Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
        }
        if ($processes.Count -gt 0) { Start-Sleep -Seconds 1 }
    } catch {
        Write-AppLog "Existing watcher could not be stopped automatically: $($_.Exception.Message)" 'WARN'
    }
}

function Install-Application {
    Stop-InstalledMonitor
    Ensure-Directory $InstallDir
    Ensure-Directory $RecoveryRoot
    Ensure-Directory $LogRoot
    Copy-Item -LiteralPath $ScriptPath -Destination $InstalledScript -Force
    $readme = Join-Path $PSScriptRoot 'README.txt'
    if (Test-Path -LiteralPath $readme) { Copy-Item -LiteralPath $readme -Destination (Join-Path $InstallDir 'README.txt') -Force }
    $license = Join-Path $PSScriptRoot 'LICENSE'
    if (Test-Path -LiteralPath $license) { Copy-Item -LiteralPath $license -Destination (Join-Path $InstallDir 'LICENSE') -Force }
    $assets = Join-Path $PSScriptRoot 'Assets'
    if (Test-Path -LiteralPath $assets -PathType Container) {
        $installedAssets = Join-Path $ProductRoot 'Assets'
        Ensure-Directory $installedAssets
        Copy-Item -Path (Join-Path $assets '*') -Destination $installedAssets -Recurse -Force
    }
    # Rewrite through the merged schema so v1 configurations gain the v2
    # auto-discovery and include/exclude settings without losing custom paths.
    Save-Config (Get-Config)

    $startup = Join-Path ([Environment]::GetFolderPath('Startup')) 'MagicDragon_Safeguardian.lnk'
    $programs = [Environment]::GetFolderPath('Programs')
    New-Shortcut $startup ('-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "{0}" -Mode Monitor -Silent' -f $InstalledScript)
    New-Shortcut (Join-Path $programs 'MagicDragon Safeguardian.lnk') ('-NoProfile -ExecutionPolicy Bypass -File "{0}" -Mode Dashboard' -f $InstalledScript)
    New-Shortcut (Join-Path $programs 'Uninstall MagicDragon Safeguardian.lnk') ('-NoProfile -ExecutionPolicy Bypass -File "{0}" -Mode Uninstall' -f $InstalledScript)
    Write-AppLog "Application $AppVersion installed/upgraded."
}

function Start-Monitor {
    if (-not (Test-Path -LiteralPath $InstalledScript)) { throw 'Install MagicDragon Safeguardian first.' }
    Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList @('-NoProfile','-WindowStyle','Hidden','-ExecutionPolicy','Bypass','-File',('"' + $InstalledScript + '"'),'-Mode','Monitor','-Silent')
}

function Uninstall-Application {
    $startup = Join-Path ([Environment]::GetFolderPath('Startup')) 'MagicDragon_Safeguardian.lnk'
    $programs = [Environment]::GetFolderPath('Programs')
    foreach ($path in @($startup, (Join-Path $programs 'MagicDragon Safeguardian.lnk'), (Join-Path $programs 'Uninstall MagicDragon Safeguardian.lnk'))) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
    if (Test-Path -LiteralPath $InstallDir) { Remove-Item -LiteralPath $InstallDir -Recurse -Force -ErrorAction SilentlyContinue }
    if (-not $Silent) {
        Add-Type -AssemblyName System.Windows.Forms
        [void][Windows.Forms.MessageBox]::Show('The watcher was removed from startup. Backups, recovery copies, configuration, logs, and diagnostic exports were kept.', $ProductName)
    }
}

function Get-BrandImagePath {
    foreach ($path in @(
        (Join-Path $PSScriptRoot 'Assets\MagicDragon.png'),
        (Join-Path $ProductRoot 'Assets\MagicDragon.png'),
        (Join-Path $PSScriptRoot 'MagicDragon.png'),
        (Join-Path $ProductRoot 'MagicDragon.png')
    )) {
        if (Test-Path -LiteralPath $path -PathType Leaf) { return $path }
    }
    return $null
}

function Show-WorldSelection {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    [Windows.Forms.Application]::EnableVisualStyles()
    $config = Get-Config
    $worlds = @(Get-DiscoverableWorlds)

    $form = New-Object Windows.Forms.Form
    $form.Text = 'Choose worlds - MagicDragon Safeguardian'
    $form.Size = New-Object Drawing.Size(590, 600)
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.Icon = [Drawing.SystemIcons]::Shield

    $title = New-Object Windows.Forms.Label
    $title.Location = New-Object Drawing.Point(24, 20)
    $title.Size = New-Object Drawing.Size(430, 32)
    $title.Font = New-Object Drawing.Font('Segoe UI', 15, [Drawing.FontStyle]::Bold)
    $title.Text = 'Select worlds to scrutinize'
    $form.Controls.Add($title)

    $imagePath = Get-BrandImagePath
    if ($null -ne $imagePath) {
        try {
            $picture = New-Object Windows.Forms.PictureBox
            $picture.Location = New-Object Drawing.Point(490, 10)
            $picture.Size = New-Object Drawing.Size(64, 64)
            $picture.SizeMode = 'Zoom'
            $picture.Image = [Drawing.Image]::FromFile($imagePath)
            $form.Controls.Add($picture)
        } catch { Write-AppLog "Brand image could not be displayed: $($_.Exception.Message)" 'WARN' }
    }

    $explain = New-Object Windows.Forms.Label
    $explain.Location = New-Object Drawing.Point(26, 62)
    $explain.Size = New-Object Drawing.Size(525, 42)
    $explain.Text = 'Checked worlds are validated and independently backed up whenever Valheim closes. Valheim backup folders are hidden from this list.'
    $form.Controls.Add($explain)

    $list = New-Object Windows.Forms.CheckedListBox
    $list.Location = New-Object Drawing.Point(26, 112)
    $list.Size = New-Object Drawing.Size(528, 300)
    $list.Font = New-Object Drawing.Font('Segoe UI', 10)
    $list.CheckOnClick = $true
    $list.Sorted = $true
    foreach ($world in $worlds) {
        $checked = if ([bool]$config.AutoDiscoverWorlds) {
            @($config.ExcludedWorldNames) -notcontains $world.Name
        } else {
            @($config.IncludedWorldNames) -contains $world.Name
        }
        [void]$list.Items.Add($world.Name, [bool]$checked)
    }
    $form.Controls.Add($list)

    $empty = New-Object Windows.Forms.Label
    $empty.Location = New-Object Drawing.Point(32, 245)
    $empty.Size = New-Object Drawing.Size(510, 40)
    $empty.TextAlign = 'MiddleCenter'
    $empty.ForeColor = [Drawing.Color]::DarkOrange
    $empty.Text = 'No folder-based Valheim 1.0 worlds were found.'
    $empty.Visible = ($worlds.Count -eq 0)
    $form.Controls.Add($empty)

    $selectAll = New-Object Windows.Forms.Button
    $selectAll.Location = New-Object Drawing.Point(26, 425)
    $selectAll.Size = New-Object Drawing.Size(100, 32)
    $selectAll.Text = 'Select all'
    $selectAll.Add_Click({ for ($i = 0; $i -lt $list.Items.Count; $i++) { $list.SetItemChecked($i, $true) } })
    $form.Controls.Add($selectAll)

    $clearAll = New-Object Windows.Forms.Button
    $clearAll.Location = New-Object Drawing.Point(138, 425)
    $clearAll.Size = New-Object Drawing.Size(100, 32)
    $clearAll.Text = 'Clear all'
    $clearAll.Add_Click({ for ($i = 0; $i -lt $list.Items.Count; $i++) { $list.SetItemChecked($i, $false) } })
    $form.Controls.Add($clearAll)

    $autoNew = New-Object Windows.Forms.CheckBox
    $autoNew.Location = New-Object Drawing.Point(28, 470)
    $autoNew.Size = New-Object Drawing.Size(520, 26)
    $autoNew.Text = 'Automatically scrutinize worlds created in the future'
    $autoNew.Checked = [bool]$config.AutoDiscoverWorlds
    $form.Controls.Add($autoNew)

    $modeHelp = New-Object Windows.Forms.Label
    $modeHelp.Location = New-Object Drawing.Point(48, 497)
    $modeHelp.Size = New-Object Drawing.Size(500, 38)
    $modeHelp.ForeColor = [Drawing.Color]::DimGray
    $modeHelp.Text = 'If enabled, currently unchecked worlds remain excluded; genuinely new world folders will be selected automatically.'
    $form.Controls.Add($modeHelp)

    $saveButton = New-Object Windows.Forms.Button
    $saveButton.Location = New-Object Drawing.Point(360, 535)
    $saveButton.Size = New-Object Drawing.Size(92, 32)
    $saveButton.Text = 'Save'
    $saveButton.Add_Click({
        $selected = New-Object System.Collections.Generic.List[string]
        $unselected = New-Object System.Collections.Generic.List[string]
        for ($i = 0; $i -lt $list.Items.Count; $i++) {
            $name = [string]$list.Items[$i]
            if ($list.GetItemChecked($i)) { $selected.Add($name) } else { $unselected.Add($name) }
        }
        if ($selected.Count -eq 0) {
            $answer = [Windows.Forms.MessageBox]::Show('No worlds are selected. Automatic validation and backup will be disabled for all current worlds. Continue?', $ProductName, [Windows.Forms.MessageBoxButtons]::YesNo, [Windows.Forms.MessageBoxIcon]::Warning)
            if ($answer -ne [Windows.Forms.DialogResult]::Yes) { return }
        }
        $updated = Get-Config
        $updated.AutoDiscoverWorlds = [bool]$autoNew.Checked
        if ($autoNew.Checked) {
            $updated.IncludedWorldNames = @()
            $updated.ExcludedWorldNames = @($unselected)
        } else {
            $updated.IncludedWorldNames = @($selected)
            $updated.ExcludedWorldNames = @()
        }
        Save-Config $updated
        Write-AppLog "World selection updated. Selected now: $($selected -join ', '). Auto-include new: $($autoNew.Checked)."
        $form.DialogResult = [Windows.Forms.DialogResult]::OK
        $form.Close()
    })
    $form.Controls.Add($saveButton)

    $cancelButton = New-Object Windows.Forms.Button
    $cancelButton.Location = New-Object Drawing.Point(462, 535)
    $cancelButton.Size = New-Object Drawing.Size(92, 32)
    $cancelButton.Text = 'Cancel'
    $cancelButton.Add_Click({ $form.DialogResult = [Windows.Forms.DialogResult]::Cancel; $form.Close() })
    $form.Controls.Add($cancelButton)
    $form.AcceptButton = $saveButton
    $form.CancelButton = $cancelButton
    return $form.ShowDialog()
}

function Show-Dashboard {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    [Windows.Forms.Application]::EnableVisualStyles()
    $config = Get-Config
    $form = New-Object Windows.Forms.Form
    $form.Text = $ProductName
    $form.Size = New-Object Drawing.Size(700, 460)
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.Icon = [Drawing.SystemIcons]::Shield

    $menu = New-Object Windows.Forms.MenuStrip
    $settingsMenu = New-Object Windows.Forms.ToolStripMenuItem('Settings')
    $chooseWorldsMenu = New-Object Windows.Forms.ToolStripMenuItem('Choose worlds to scrutinize...')
    [void]$settingsMenu.DropDownItems.Add($chooseWorldsMenu)
    [void]$menu.Items.Add($settingsMenu)
    $form.MainMenuStrip = $menu
    $form.Controls.Add($menu)

    $title = New-Object Windows.Forms.Label
    $title.Location = New-Object Drawing.Point(24, 32)
    $title.Size = New-Object Drawing.Size(560, 34)
    $title.Font = New-Object Drawing.Font('Segoe UI', 16, [Drawing.FontStyle]::Bold)
    $title.Text = 'MagicDragon Safeguardian'
    $form.Controls.Add($title)

    $status = New-Object Windows.Forms.TextBox
    $status.Location = New-Object Drawing.Point(26, 78)
    $status.Size = New-Object Drawing.Size(630, 235)
    $status.Font = New-Object Drawing.Font('Segoe UI', 10)
    $status.Multiline = $true
    $status.ReadOnly = $true
    $status.ScrollBars = 'Vertical'
    $status.BackColor = [Drawing.Color]::White
    $form.Controls.Add($status)

    $refresh = {
        $worlds = @(Get-ValheimWorlds)
        if ($worlds.Count -eq 0) {
            $status.ForeColor = [Drawing.Color]::DarkOrange
            $discoverable = @(Get-DiscoverableWorlds)
            $status.Text = if ($discoverable.Count -eq 0) { "No active Valheim 1.0 world folders found.`r`nScanned: $($config.SaveRoot)" } else { 'No worlds are selected. Open Settings > Choose worlds to scrutinize.' }
        } else {
            $lines = New-Object System.Collections.Generic.List[string]
            $failed = 0
            foreach ($world in $worlds) {
                $check = Test-ValheimWorld $world.Path
                if ($check.Valid) {
                    $lines.Add("[VALID] $($world.Name)  |  generation $($check.Generation)  |  format $($check.FormatVersion)  |  $($check.IndexedChunks) chunks")
                } else {
                    $failed++
                    $lines.Add("[FAILED] $($world.Name)  |  $($check.Errors -join ' ')")
                }
            }
            $status.ForeColor = if ($failed -eq 0) { [Drawing.Color]::DarkGreen } else { [Drawing.Color]::DarkRed }
            $status.Text = (($lines -join "`r`n") + "`r`n`r`nBackups: $($config.BackupRoot)")
        }
    }
    $chooseWorldsMenu.Add_Click({ [void](Show-WorldSelection); & $refresh })
    & $refresh

    $checkButton = New-Object Windows.Forms.Button
    $checkButton.Location = New-Object Drawing.Point(26, 325)
    $checkButton.Size = New-Object Drawing.Size(190, 38)
    $checkButton.Text = 'Check selected + back up'
    $checkButton.Add_Click({
        $checkButton.Enabled = $false
        try { [void](Invoke-AllSafeguards $null $false); & $refresh } finally { $checkButton.Enabled = $true }
    })
    $form.Controls.Add($checkButton)

    $backupButton = New-Object Windows.Forms.Button
    $backupButton.Location = New-Object Drawing.Point(230, 325)
    $backupButton.Size = New-Object Drawing.Size(145, 38)
    $backupButton.Text = 'Open backups'
    $backupButton.Add_Click({ Ensure-Directory $config.BackupRoot; Start-Process explorer.exe -ArgumentList ('"' + $config.BackupRoot + '"') })
    $form.Controls.Add($backupButton)

    $logButton = New-Object Windows.Forms.Button
    $logButton.Location = New-Object Drawing.Point(389, 325)
    $logButton.Size = New-Object Drawing.Size(120, 38)
    $logButton.Text = 'Open log'
    $logButton.Add_Click({ Ensure-Directory $LogRoot; Start-Process explorer.exe -ArgumentList ('"' + $LogRoot + '"') })
    $form.Controls.Add($logButton)

    $note = New-Object Windows.Forms.Label
    $note.Location = New-Object Drawing.Point(26, 380)
    $note.Size = New-Object Drawing.Size(630, 38)
    $note.ForeColor = [Drawing.Color]::DimGray
    $note.Text = 'Only worlds selected under Settings are scrutinized. Valheim backup_auto and backup_restore folders are hidden.'
    $form.Controls.Add($note)
    [void]$form.ShowDialog()
}

function Run-Monitor {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    $createdNew = $false
    $mutex = New-Object Threading.Mutex($true, 'Local\MagicDragon_Safeguardian_Monitor', [ref]$createdNew)
    if (-not $createdNew) { return }
    try {
        $config = Get-Config
        $tray = New-Object Windows.Forms.NotifyIcon
        $tray.Icon = [Drawing.SystemIcons]::Shield
        $tray.Text = $ProductName
        $tray.Visible = $true
        $menu = New-Object Windows.Forms.ContextMenuStrip
        $checkItem = $menu.Items.Add('Check protected saves and back up')
        $openItem = $menu.Items.Add('Open dashboard')
        $chooseItem = $menu.Items.Add('Choose worlds to scrutinize')
        $backupItem = $menu.Items.Add('Open backups')
        [void]$menu.Items.Add('-')
        $exitItem = $menu.Items.Add('Exit watcher')
        $tray.ContextMenuStrip = $menu
        $checkItem.Add_Click({ [void](Invoke-AllSafeguards $tray $false) })
        $openItem.Add_Click({ Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $InstalledScript + '"'),'-Mode','Dashboard') })
        $chooseItem.Add_Click({ Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $InstalledScript + '"'),'-Mode','WorldSelection') })
        $backupItem.Add_Click({ Ensure-Directory $config.BackupRoot; Start-Process explorer.exe -ArgumentList ('"' + $config.BackupRoot + '"') })
        $exitItem.Add_Click({ $tray.Visible = $false; [Windows.Forms.Application]::Exit() })

        $script:ValheimWasRunning = (@(Get-Process -Name 'valheim' -ErrorAction SilentlyContinue).Count -gt 0)
        $timer = New-Object Windows.Forms.Timer
        $timer.Interval = [Math]::Max(2000, [int]$config.PollSeconds * 1000)
        $timer.Add_Tick({
            $running = (@(Get-Process -Name 'valheim' -ErrorAction SilentlyContinue).Count -gt 0)
            if ($script:ValheimWasRunning -and -not $running) {
                Write-AppLog 'Valheim process closed; automatic safeguard started.'
                [void](Invoke-AllSafeguards $tray $true)
            }
            $script:ValheimWasRunning = $running
        })
        $timer.Start()
        Write-AppLog 'Background watcher started.'
        [Windows.Forms.Application]::Run()
        $timer.Stop()
        $tray.Dispose()
    } finally { $mutex.ReleaseMutex(); $mutex.Dispose() }
}

try {
    switch ($Mode) {
        'Install' {
            Install-Application
            Start-Monitor
            Show-Alert $ProductName 'Installed. The watcher will start with Windows and protect selected Valheim worlds and characters whenever Valheim closes.' $false
        }
        'CheckOnce' {
            $results = @(Invoke-AllSafeguards $null $false)
            if ($results.Count -eq 0 -or @($results | Where-Object { -not $_.Valid }).Count -gt 0) { exit 2 }
        }
        'CheckWorlds' {
            $results = @(Invoke-AllSafeguards $null $false $false 'World')
            if ($results.Count -eq 0 -or @($results | Where-Object { -not $_.Valid }).Count -gt 0) { exit 2 }
        }
        'CheckWorldIntegrity' {
            $results = @(Invoke-WorldIntegrityChecks)
            if ($results.Count -eq 0 -or @($results | Where-Object { -not $_.Valid }).Count -gt 0) { exit 2 }
        }
        'CheckCharacters' {
            $results = @(Invoke-AllSafeguards $null $false $false 'Character')
            if ($results.Count -eq 0 -or @($results | Where-Object { -not $_.Valid }).Count -gt 0) { exit 2 }
        }
        'CheckCharacterIntegrity' {
            $results = @(Invoke-CharacterIntegrityChecks)
            if ($results.Count -eq 0 -or @($results | Where-Object { -not $_.Valid }).Count -gt 0) { exit 2 }
        }
        'CheckLive' {
            Write-AppLog 'User-requested fast read-only live save check started while Valheim is running.'
            $results = @(Invoke-LiveSafeguards 'All')
            if ($results.Count -eq 0 -or @($results | Where-Object { -not $_.Valid }).Count -gt 0) { exit 2 }
        }
        'CheckLiveWorlds' {
            if ([string]::IsNullOrWhiteSpace($TargetWorldPath)) {
                Write-AppLog 'Live world check refused because no current-session world target was supplied.' 'ERROR'
                exit 2
            }
            Write-AppLog "User-requested fast read-only live world check started for the detected active world: $TargetWorldPath"
            $results = @(Invoke-LiveSafeguards 'World' $null $TargetWorldPath)
            if ($results.Count -eq 0 -or @($results | Where-Object { -not $_.Valid }).Count -gt 0) { exit 2 }
        }
        'CheckLiveCharacters' {
            if ([string]::IsNullOrWhiteSpace($TargetCharacterPath)) {
                Write-AppLog 'Live character check refused because no current-session character target was supplied.' 'ERROR'
                exit 2
            }
            Write-AppLog "User-requested fast read-only live character check started for the detected active character: $TargetCharacterPath"
            $results = @(Invoke-LiveSafeguards 'Character' $null '' $TargetCharacterPath)
            if ($results.Count -eq 0 -or @($results | Where-Object { -not $_.Valid }).Count -gt 0) { exit 2 }
        }
        'BackupAtSaveWorld' {
            $results = @(Invoke-BackupAtSaveWorld $TargetWorldPath)
            if ($results.Count -eq 0 -or @($results | Where-Object { -not $_.Valid }).Count -gt 0) { exit 2 }
        }
        'BackupAtSaveCharacter' {
            $results = @(Invoke-BackupAtSaveCharacter $TargetCharacterPath)
            if ($results.Count -eq 0 -or @($results | Where-Object { -not $_.Valid }).Count -gt 0) { exit 2 }
        }
        'CheckAfterExit' {
            $results = @(Invoke-AllSafeguards $null $true)
            if ($results.Count -eq 0 -or @($results | Where-Object { -not $_.Valid }).Count -gt 0) { exit 2 }
        }
        'InspectBackup' {
            if ([string]::IsNullOrWhiteSpace($BackupPath)) { throw 'No backup ZIP was selected.' }
            if (Test-Path -LiteralPath $RestorePreviewPath) { Remove-Item -LiteralPath $RestorePreviewPath -Force -ErrorAction SilentlyContinue }
            $inspection = Get-BackupInspection $BackupPath $false
            $inspection | Select-Object Valid,BackupPath,BackupStatus,World,Generation,Created,StorageSource,TargetPath,ZipIntegrity,Checksum,WorldStructure,ZipSHA256,Errors |
                ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $RestorePreviewPath -Encoding UTF8
            if (-not $inspection.Valid) { exit 2 }
        }
        'RestoreBackup' {
            if ([string]::IsNullOrWhiteSpace($BackupPath)) { throw 'No backup ZIP was selected.' }
            [void](Restore-VerifiedBackup $BackupPath)
        }
        'ExportDiagnostics' {
            [void](Export-DiagnosticBundle)
        }
        'WorldSelection' { [void](Show-WorldSelection) }
        'Monitor' { Run-Monitor }
        'Uninstall' { Uninstall-Application }
        default { Show-Dashboard }
    }
} catch {
    $location = if ($null -ne $_.InvocationInfo -and -not [string]::IsNullOrWhiteSpace([string]$_.InvocationInfo.PositionMessage)) {
        ' ' + ([string]$_.InvocationInfo.PositionMessage -replace "`r?`n", ' ')
    } else { '' }
    Write-AppLog ($_.Exception.Message + $location) 'FATAL'
    Show-Alert $ProductName $_.Exception.Message $true
    exit 1
}
