MAGICDRAGON SAFEGUARDIAN 1.0.1
WINDOWS SAVE VALIDATION AND BACKUP UTILITY FOR VALHEIM
======================================================

Made by HardcoreApe 2026 ©
Released under the MIT License


CONTENTS
========

1. What MagicDragon Safeguardian does
2. Safety rules before you begin
3. Requirements and supported saves
4. Installing or upgrading
5. First-time setup
6. Dashboard and navigation
7. Choosing worlds
8. Choosing characters
9. Normal integrity checks and backups
10. Live World Check
11. Live Character Check
12. BackupAtSave
13. Automatic protection after Valheim closes
14. What the validators inspect
15. Backup folders, GOOD, and QUARANTINE
16. Verified Restore Wizard
17. Settings
18. Logs and diagnostic export
19. BUILD, INSTALL, and UNINSTALL scripts
20. Privacy statement
21. Known limitations
22. MIT License summary
23. Valheim trademark and non-affiliation disclaimer
24. Publishing and redistribution precautions
25. Reporting bugs


1. WHAT MAGICDRAGON SAFEGUARDIAN DOES
=====================================

MagicDragon Safeguardian is a free, open-source Windows utility that checks
Valheim world and character save files and creates independent, dated backups
outside Valheim's own rolling-backup system.

Its main functions are:

  - discover supported Valheim worlds and characters stored on this Windows PC;
  - let the user choose exactly which saves are protected;
  - perform a read-only integrity check without creating a backup;
  - perform an integrity check and create a verified ZIP backup;
  - check the active world or character after an in-game save while Valheim is
    still running;
  - detect manual and automatic Valheim save-file changes and create a limited
    BackupAtSave history when the changed save is healthy;
  - validate selected saves after Valheim closes;
  - retain separate GOOD and QUARANTINE histories;
  - restore a verified GOOD world backup through a staged, rollback-capable
    restore process;
  - warn when backup storage is running low;
  - export troubleshooting information without copying the large world or
    character save payloads.

MagicDragon is an additional safety layer. It does not replace occasional
backups on another physical drive.


2. SAFETY RULES BEFORE YOU BEGIN
================================

  1. Keep at least one copy of important backups on another physical drive.
  2. Do not restore a world while Valheim is running.
  3. Wait for Valheim's save indicator to disappear before using a Live Check.
  4. Do not save again while a Live Check or BackupAtSave operation is reading
     the same files.
  5. Treat QUARANTINE as diagnostic material, not as a proven working backup.
  6. Review a diagnostic ZIP before sharing it because it can contain local
     paths and the Windows computer name.
  7. Never disable Windows Defender or SmartScreen globally to run this tool.


3. REQUIREMENTS AND SUPPORTED SAVES
===================================

Requirements:

  - Windows 10 or Windows 11;
  - Windows PowerShell 5.1;
  - Microsoft .NET Framework 4.x, including the built-in csc.exe compiler;
  - Valheim saves synchronized or stored locally on the same Windows account.

World discovery includes folder-based saves found under accessible locations
such as:

  AppData\LocalLow\IronGate\Valheim\worlds_local
  AppData\LocalLow\IronGate\Valheim\worlds
  AppData\Local\IronGate\Valheim\worlds_local
  AppData\Local\IronGate\Valheim\worlds
  Steam\userdata\<SteamID>\892970\remote\worlds
  Steam\userdata\<SteamID>\892970\remote\worlds_local

Character discovery includes supported .fch files under corresponding
characters and characters_local folders in LocalLow, Local, and accessible
Steam userdata mirrors.

Steam Cloud files must exist in a locally synchronized Steam userdata folder.
MagicDragon does not sign in to Steam and cannot inspect a remote-only cloud
copy that is not present on the computer.


4. INSTALLING OR UPGRADING
==========================

  1. Close Valheim completely.
  2. Exit an older running MagicDragon instance from its system-tray menu.
  3. Extract the complete MagicDragon_Safeguardian.zip to a normal folder.
  4. Double-click INSTALL.cmd.
  5. Answer Y and wait for the success message.

Administrator access is not normally required. Installation is performed for
the current Windows user.

The installer:

  - compiles the included C# source locally;
  - installs the application under:

      %LOCALAPPDATA%\MagicDragon_Safeguardian\

  - creates a dragon-icon Desktop shortcut;
  - creates Start Menu application and uninstaller shortcuts;
  - registers MagicDragon in Windows Installed Apps and Control Panel >
    Programs and Features;
  - preserves existing MagicDragon settings, backups, recovery copies,
    diagnostics, and logs during an upgrade.

The installed executable is:

  %LOCALAPPDATA%\MagicDragon_Safeguardian\App\MagicDragon_Safeguardian.exe

If MagicDragon is already running in the system tray, opening the Desktop
shortcut, Start Menu shortcut, or installed EXE brings the existing dashboard
to the foreground instead of starting a second guardian.


5. FIRST-TIME SETUP
===================

  1. Open the Worlds page.
  2. Press Refresh List if a recently created world is not shown.
  3. Check every world you want MagicDragon to protect.
  4. Choose whether future worlds should be included automatically.
  5. Press Save Selection.
  6. Open the Characters page and repeat the same steps for characters.
  7. Open Settings and confirm the backup folder, retention values, free-space
     warning, settle time, and Windows-start preference.
  8. Press Save Settings.

The default backup root is:

  Documents\MagicDragon_Safeguardian\Backups\


6. DASHBOARD AND NAVIGATION
===========================

The Dashboard displays:

  - whether Valheim is running;
  - whether the main menu or an active saved world has been detected;
  - the latest scrutiny time and result;
  - the number of selected protected worlds;
  - the configured backup folder;
  - world and character quick actions;
  - the latest status message.

Dashboard buttons provide access to normal checks, live checks, selection
pages, backups, logs, and settings. Secondary pages contain a Back button that
returns to the previous page.

Close hides the guardian in the system tray when monitoring is active. Use
Exit from the tray menu to terminate it completely.


7. CHOOSING WORLDS
==================

The Worlds page lists discovered supported worlds and their storage source.

  - Select All checks every listed world.
  - Clear All clears the selection.
  - Refresh List scans the official supported locations again.
  - Automatically include worlds created later protects newly discovered
    worlds without requiring another manual selection.
  - Save Selection stores the current choice.

Selecting an item displays its exact folder path. This helps distinguish worlds
with similar names stored under LocalLow, Local, or a Steam userdata mirror.


8. CHOOSING CHARACTERS
======================

The Characters page works like the Worlds page.

At least one character must be selected before Live Character Check can be
used. Refresh List scans again for characters created after MagicDragon opened.

Valheim-generated files whose names identify them as rolling backups are not
treated as active characters.


9. NORMAL INTEGRITY CHECKS AND BACKUPS
======================================

Press Check Worlds or Check Characters. MagicDragon presents two choices:

  Check Integrity Only
      Validates the selected saves in their official Valheim locations. It does
      not create a ZIP.

  Check and Create Backup
      Validates the selected saves, copies them into staging, validates the
      staged copy, creates a ZIP, tests ZIP decompression, and records a SHA-256
      checksum. Only verified archives enter the GOOD history.

Normal checks operate only on the worlds or characters selected by the user.
They are appropriate when Valheim is closed or when the user wants to inspect
all selected saves instead of the current live session.


10. LIVE WORLD CHECK
====================

Live World Check is a fast, read-only verification of the world that Valheim
has saved during the current game session.

Correct procedure:

  1. Start MagicDragon before or while Valheim is running.
  2. Enter a world.
  3. Save manually or wait for a Valheim autosave.
  4. Wait until the in-game save indicator disappears.
  5. Alt-Tab to MagicDragon.
  6. Press Live World Check.
  7. Wait for the result before saving again.

The Live Check function works only after MagicDragon observes save activity in
the current Valheim process session. If the user returns to the main menu, the
last saved target can remain in memory. After entering a different world, the
user must save that new world at least once before Live World Check can monitor
and identify it as the new target.

Live World Check does not create a backup ZIP. BackupAtSave is the separate
function that creates a verified archive following detected save activity.


11. LIVE CHARACTER CHECK
========================

Live Character Check is the character equivalent of Live World Check.

Requirements:

  - Valheim must be running inside a world;
  - at least one character must have been selected in MagicDragon;
  - the active character file must have changed during the current session;
  - the save activity must have settled before the check begins.

The check reads and hashes the detected selected .fch file and confirms that it
remains unchanged during inspection. It does not create a backup ZIP.

When switching to another character, enter a world and save that character at
least once so MagicDragon can replace the previous live target.


12. BACKUPATSAVE
================

BackupAtSave watches for supported selected save files changed by Valheim while
MagicDragon is running. This includes detected manual saves and autosaves.

After the configured settle period, MagicDragon:

  1. identifies the changed selected world and character;
  2. validates the source files;
  3. creates a staged snapshot;
  4. validates the staged snapshot;
  5. creates and decompression-tests the ZIP;
  6. publishes it only when the result is healthy;
  7. removes the oldest BackupAtSave archive when the configured per-save limit
     has been exceeded.

The default BackupAtSave retention is five archives per world or character.

If validation fails, MagicDragon warns:

  STOP! The dragon found something strange, please check your game files and
  do not exit the session, save again to avoid corruption.

Do not depend on BackupAtSave as the only backup layer. A sudden power loss can
interrupt Valheim before either program receives a complete committed save.


13. AUTOMATIC PROTECTION AFTER VALHEIM CLOSES
=============================================

When MagicDragon is running and detects that valheim.exe has closed, it waits
for the configured file-settle interval and scrutinizes the selected supported
world and character saves. Healthy results can enter the independent verified
backup history.

The default settle time is five seconds. Increase it if save files reside on a
slow disk or require additional synchronization time.

MagicDragon can remain hidden in the system tray after Valheim closes.


14. WHAT THE VALIDATORS INSPECT
===============================

For folder-based Valheim worlds, MagicDragon checks items including:

  - a complete matching committed generation:

      _main.N.fwl2
      _main.N.db2
      _main.N.chunks
      _main.N.ok

  - compatible format/version markers;
  - the chunk-index structure;
  - every referenced chunk coordinate and revision;
  - referenced object counts;
  - chunk headers and readability;
  - available cache files;
  - file stability during live inspection;
  - staged-copy consistency;
  - completed ZIP decompression;
  - SHA-256 integrity information.

For characters, MagicDragon checks items including:

  - that the .fch file is readable and has a plausible minimum size;
  - that the complete file can be hashed;
  - that it does not change during inspection;
  - that the copied file matches the source;
  - that the finished ZIP can be decompressed.

These are structural checks. They cannot prove that every gameplay object or
character field is semantically correct inside Valheim.


15. BACKUP FOLDERS, GOOD, AND QUARANTINE
========================================

Default layout:

  Documents\MagicDragon_Safeguardian\Backups\
      Worlds\
          <WorldName>\
              <WorldName>_GOOD_....zip
              <WorldName>_QUARANTINE_....zip
              BackupAtSave\
      Characters\
          <CharacterName>\
              Character_<CharacterName>_GOOD_....zip
              Character_<CharacterName>_QUARANTINE_....zip
              BackupAtSave\

GOOD means that the source and staged snapshot passed MagicDragon's structural
checks, the archive passed decompression testing, and its integrity information
was created. GOOD world archives are eligible for the Verified Restore Wizard.

QUARANTINE means that MagicDragon detected an incomplete or inconsistent save
and preserved the available material for troubleshooting or expert manual
recovery. QUARANTINE is not proof that the archive can be loaded by Valheim.

Retention values are applied independently for each world and character.


16. VERIFIED RESTORE WIZARD
===========================

The Restore Wizard restores GOOD world backups only.

  1. Close Valheim completely.
  2. Open Backups.
  3. Select a GOOD world archive.
  4. Press Restore Selected Backup.
  5. Review the displayed world, generation, creation time, storage source,
     checksum, ZIP integrity, and world-structure result.
  6. Confirm the restore only if the target is correct.

Before replacing the active world, MagicDragon:

  - verifies the checksum sidecar when present;
  - tests safe ZIP decompression;
  - validates the archive manifest and world structure;
  - verifies that Valheim is closed;
  - extracts into a temporary staging directory;
  - validates the extracted copy again;
  - moves the existing world into:

      Documents\MagicDragon_Safeguardian\Recovery\
      <WorldName>_before_restore_DATE_TIME

  - installs the staged world;
  - validates the installed result;
  - attempts to restore the previous world automatically if installation or
    final validation fails.

The original backup ZIP is opened read-only and is never modified by restore.
Character archives are not restored automatically by this wizard.


17. SETTINGS
============

Backups folder
    Root folder used for independent world and character backup histories.

GOOD backups kept per world
    Maximum normal verified archives retained for each world.

QUARANTINE copies kept per world
    Maximum diagnostic copies retained for each world.

BackupAtSave kept per world/character
    Maximum automatic save-event archives retained per save. The default value
    is 5 and is marked recommended.

Warn below free disk space
    Minimum free-space threshold in MB. MagicDragon can also warn when the
    remaining percentage is critically low.

File-settle time after Valheim closes
    Number of seconds save files must remain unchanged before validation and
    backup begin. The default is 5 seconds.

Launch MagicDragon Safeguardian when Windows starts
    Registers or removes the current-user Windows startup entry. When enabled,
    MagicDragon begins hidden monitoring at sign-in and remains available in
    the system tray.

Open Recovery Folder
    Opens the safety copies created before a world restore. Recovery is separate
    from the normal backup history.

Press Save Settings after making changes.


18. LOGS AND DIAGNOSTIC EXPORT
==============================

The Logs page can refresh the activity log, open its folder, and export a
diagnostic ZIP.

A diagnostic export contains troubleshooting material such as:

  - the activity log;
  - configuration values;
  - recent validation and restore results;
  - system and storage information;
  - file listings and timestamps.

It intentionally excludes the potentially enormous world and character save
payloads. It can still reveal the computer name, Windows username through file
paths, custom folder names, world names, and character names. Inspect and redact
the report before posting it publicly.


19. BUILD, INSTALL, AND UNINSTALL SCRIPTS
=========================================

INSTALL.cmd
    Recommended for normal users. It runs Install.ps1, compiles the Windows
    Forms executables from the included C# source, installs the PowerShell
    engine and assets, creates shortcuts, and registers the uninstaller.

Install.ps1
    Implements per-user installation, upgrade, registration, and cleanup.
    Normal users should launch it through INSTALL.cmd.

BUILD.cmd
    Source-package build entry point for Windows. It calls Build.ps1 and writes
    compiled output into the Build folder. It does not install or register the
    application.

Build.ps1
    Compiles the main application and uninstaller with the .NET Framework C#
    compiler and copies the runtime engine, artwork, README, and license into
    the selected build output folder.

BUILD_LINUX.sh
    Optional reproducible cross-build script for developers with Mono mcs.
    It produces Windows-targeted executables; it does not create a native Linux
    or macOS application.

UNINSTALL.cmd
    Opens the installed graphical uninstaller. If the installed executable is
    unavailable, it falls back to Install.ps1 -Uninstall.

Uninstall choices:

  NO
      Remove only the application while retaining backups and MagicDragon data.

  YES
      Remove the application and all MagicDragon backups, recovery copies,
      diagnostics, settings, and logs. This choice requires confirmation and
      should be treated as destructive.

  CANCEL
      Make no changes.


20. PRIVACY STATEMENT
=====================

MagicDragon Safeguardian does not include telemetry, analytics, advertising,
account login, or automatic network upload code. It operates on local files
available to the current Windows user.

The distributed release packages contain no Valheim saves, usernames, user
configuration, activity logs, backups, recovery copies, or generated diagnostic
reports.

After installation, MagicDragon creates local data including:

  %LOCALAPPDATA%\MagicDragon_Safeguardian\
      application files, configuration, status, and logs

  Documents\MagicDragon_Safeguardian\
      backups, recovery copies, and optional diagnostic exports

Backups naturally contain copies of the user's selected Valheim saves. Users
are responsible for protecting these local files and for reviewing diagnostic
exports before sharing them.


21. KNOWN LIMITATIONS
=====================

  - The application runs on Windows only.
  - The Linux build script produces Windows executables, not a Linux app.
  - Folder-based Valheim 1.0 world saves are supported. Legacy flat .db/.fwl
    worlds are not fully validated by the new-world validator.
  - Opaque Xbox or Microsoft Store save containers are not supported.
  - Steam Cloud saves must have a locally synchronized accessible copy.
  - Live Check identification is based on observed current-session save activity.
    After switching worlds or characters, save the new session at least once.
  - Returning to the main menu can leave the most recent saved target in memory;
    a later world or character replaces it only after a new save is detected.
  - Live checks are read-only and do not create an archive.
  - Character validation proves readability, hashing, copying, and stability;
    it cannot prove that every internal field is semantically loadable.
  - Structural world validation cannot guarantee correct gameplay behavior.
  - A software check cannot prevent physical disk failure, theft, malware, file
    deletion by another program, or corruption that occurs before a complete
    save reaches disk.
  - A backup on the same physical disk can be lost with the original data.
  - An interrupted save or immediate power loss can occur before BackupAtSave
    has a complete healthy generation to archive.
  - The executable is not digitally signed and can trigger Windows warnings.
  - No restore operation is risk-free. Keep external copies before restoring.


22. MIT LICENSE SUMMARY
=======================

MagicDragon Safeguardian is distributed under the MIT License.

The MIT License permits use, copying, modification, merging, publication,
distribution, sublicensing, and sale of copies, provided that the copyright and
license notice are retained.

The software is provided "AS IS", without warranty of any kind. The authors or
copyright holders are not liable for claims, damages, or other liability arising
from the software or its use.

This summary is not a replacement for the complete LICENSE file included with
the application and source package.

Copyright (c) 2026 HardcoreApe


23. VALHEIM TRADEMARK AND NON-AFFILIATION DISCLAIMER
=====================================================

MagicDragon Safeguardian is an unofficial community tool. It is not affiliated
with, sponsored by, approved by, or endorsed by Iron Gate Studio or Coffee Stain
Publishing. Valheim and associated trademarks, game content, and intellectual
property belong to their respective owners.

The name Valheim is used only to identify compatibility and the purpose of the
tool. The MagicDragon mascot and application branding are independent artwork
and must not be presented as official Valheim branding.


24. PUBLISHING AND REDISTRIBUTION PRECAUTIONS
=============================================

The following precautions are recommended when publishing or redistributing
MagicDragon:

  1. Use one public GitHub repository as the canonical source.
  2. Publish a tagged release such as v1.0.1 with the application ZIP, source,
     release notes, and SHA256SUMS.txt.
  3. Include the complete source code, README, and MIT LICENSE.
  4. Keep the copyright notice and non-affiliation disclaimer visible.
  5. State clearly that the executable is unsigned and that SmartScreen or an
     antivirus product may display a warning.
  6. Never instruct users to disable security software globally.
  7. Do not describe the tool as official, approved, guaranteed, or able to
     prevent every form of corruption.
  8. Use accurate wording such as "an unofficial save-validation and backup
     utility for Valheim."
  9. Do not use the official Valheim logo as the application icon or branding.
 10. Do not publish Valheim saves, usernames, Config.json, logs, diagnostic
     reports, personal paths, API keys, or any other private user material.
 11. Download the finished release once and verify its published SHA-256 values.
 12. Explain that the executable can be built locally from the public source.
 13. Read the current rules of any community before posting. Ask subreddit
     moderators through modmail before making a self-promotional post.
 14. Understand that subreddit moderator approval covers only that subreddit;
     it is not authorization from Iron Gate Studio or Coffee Stain Publishing.
 15. Review the current Valheim terms and content-usage rules before every
     public release because those terms can change.
 16. Respond promptly if a platform moderator or rights holder raises a concern.

25. REPORTING BUGS
==================

Use the Issues section of the canonical GitHub repository when available.

Include:

  - MagicDragon Safeguardian version: 1.0.1;
  - Windows version;
  - Valheim version;
  - whether the save is LocalLow, Local, or a locally synchronized Steam copy;
  - whether Valheim was closed, at the main menu, or inside a world;
  - the exact action performed;
  - numbered reproduction steps;
  - expected behavior;
  - actual behavior and complete error message;
  - the relevant log lines and timestamps;
  - whether the problem repeats after restarting MagicDragon.

When useful, create a diagnostic ZIP from Logs > Export Diagnostics ZIP. Review
and redact private paths, names, or computer information before attaching it.

Do not post complete world or character saves publicly. Do not include passwords,
authentication data, personal documents, or unrelated system files. If a report
may describe a security vulnerability, contact the maintainer privately through
the method listed in the canonical repository instead of disclosing exploitable
details in a public issue.


END OF README
=============
