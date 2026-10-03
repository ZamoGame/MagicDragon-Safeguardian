using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("MagicDragon Safeguardian")]
[assembly: AssemblyProduct("MagicDragon Safeguardian")]
[assembly: AssemblyCompany("HardcoreApe")]
[assembly: AssemblyCopyright("Copyright © HardcoreApe 2026")]
[assembly: AssemblyVersion("1.0.1.0")]
[assembly: AssemblyFileVersion("1.0.1.0")]
[assembly: AssemblyInformationalVersion("1.0.1")]

namespace MagicDragonSafeguardian
{
    internal static class AppPaths
    {
        internal static readonly string ProductRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MagicDragon_Safeguardian");
        internal static readonly string AppRoot = Path.Combine(ProductRoot, "App");
        internal static readonly string EngineScript = Path.Combine(AppRoot, "MagicDragon_Safeguardian.ps1");
        internal static readonly string ConfigFile = Path.Combine(ProductRoot, "Config.json");
        internal static readonly string ConfigBackupFile = Path.Combine(ProductRoot, "Config.lastgood.json");
        internal static readonly string StatusFile = Path.Combine(ProductRoot, "status.json");
        internal static readonly string LogFile = Path.Combine(ProductRoot, "Logs", "Safeguardian.log");
        internal static readonly string MascotFile = Path.Combine(ProductRoot, "Assets", "MagicDragon.png");
        internal static readonly string RecoveryRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "MagicDragon_Safeguardian", "Recovery");
        internal static readonly string RestorePreviewFile = Path.Combine(ProductRoot, "restore-preview.json");
        internal static readonly string RestoreResultFile = Path.Combine(ProductRoot, "restore-result.json");
        internal static readonly string DiagnosticResultFile = Path.Combine(ProductRoot, "diagnostic-result.json");
        internal static readonly string ActiveSessionFile = Path.Combine(ProductRoot, "active-world-session.json");
    }

    public sealed class ActiveWorldSessionState
    {
        public bool ValheimRunning { get; set; }
        public string ProcessStartedUtc { get; set; }
        public string SessionStartedUtc { get; set; }
        public string GameScene { get; set; }
        public string SceneDetectedUtc { get; set; }
        public bool WorldSaveDetected { get; set; }
        public string ActiveWorldName { get; set; }
        public string ActiveWorldPath { get; set; }
        public string LastSaveActivityUtc { get; set; }
        public string SettledAfterUtc { get; set; }
        public bool CharacterSaveDetected { get; set; }
        public string ActiveCharacterName { get; set; }
        public string ActiveCharacterPath { get; set; }
        public string LastCharacterSaveActivityUtc { get; set; }
        public string CharacterSettledAfterUtc { get; set; }
        public string UpdatedUtc { get; set; }

        internal static ActiveWorldSessionState Load()
        {
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (File.Exists(AppPaths.ActiveSessionFile))
                    {
                        try
                        {
                            return serializer.Deserialize<ActiveWorldSessionState>(
                                File.ReadAllText(AppPaths.ActiveSessionFile, Encoding.UTF8));
                        }
                        catch { if (attempt == 2) throw; }
                    }
                    Thread.Sleep(40);
                }
                return null;
            }
            catch { return null; }
        }

        internal void Save()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.ProductRoot);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                string temporary = AppPaths.ActiveSessionFile + ".new";
                File.WriteAllText(temporary, serializer.Serialize(this), new UTF8Encoding(true));
                if (File.Exists(AppPaths.ActiveSessionFile))
                {
                    try { File.Replace(temporary, AppPaths.ActiveSessionFile, null, true); }
                    catch
                    {
                        File.Copy(temporary, AppPaths.ActiveSessionFile, true);
                        File.Delete(temporary);
                    }
                }
                else File.Move(temporary, AppPaths.ActiveSessionFile);
            }
            catch { }
        }
    }

    public sealed class AppConfig
    {
        public string SaveRoot { get; set; }
        public string BackupRoot { get; set; }
        public bool AutoDiscoverWorlds { get; set; }
        public string[] IncludedWorldNames { get; set; }
        public string[] ExcludedWorldNames { get; set; }
        public bool AutoDiscoverCharacters { get; set; }
        public string[] IncludedCharacterNames { get; set; }
        public string[] ExcludedCharacterNames { get; set; }
        public int RetainGoodBackups { get; set; }
        public int RetainQuarantineBackups { get; set; }
        public int MinimumFreeSpaceMB { get; set; }
        public int PollSeconds { get; set; }
        public int SettleSeconds { get; set; }
        public int BackupAtSaveRetention { get; set; }
        public bool ShowWindowWhenValheimStarts { get; set; }

        public AppConfig()
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            SaveRoot = Path.Combine(profile, "AppData", "LocalLow", "IronGate", "Valheim", "worlds_local");
            BackupRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "MagicDragon_Safeguardian", "Backups");
            AutoDiscoverWorlds = true;
            IncludedWorldNames = new string[0];
            ExcludedWorldNames = new string[0];
            AutoDiscoverCharacters = true;
            IncludedCharacterNames = new string[0];
            ExcludedCharacterNames = new string[0];
            RetainGoodBackups = 10;
            RetainQuarantineBackups = 5;
            MinimumFreeSpaceMB = 2048;
            PollSeconds = 5;
            SettleSeconds = 5;
            BackupAtSaveRetention = 5;
            ShowWindowWhenValheimStarts = true;
        }

        public static AppConfig Load()
        {
            AppConfig result = new AppConfig();
            bool hasCharacterSelectionSettings = true;
            bool hasShowOnLaunchSetting = true;
            bool loaded = TryLoad(AppPaths.ConfigFile, out result, out hasCharacterSelectionSettings,
                out hasShowOnLaunchSetting);
            if (!loaded)
            {
                result = new AppConfig();
                TryLoad(AppPaths.ConfigBackupFile, out result, out hasCharacterSelectionSettings,
                    out hasShowOnLaunchSetting);
            }
            AppConfig defaults = new AppConfig();
            if (String.IsNullOrWhiteSpace(result.SaveRoot)) result.SaveRoot = defaults.SaveRoot;
            if (String.IsNullOrWhiteSpace(result.BackupRoot)) result.BackupRoot = defaults.BackupRoot;
            if (result.IncludedWorldNames == null) result.IncludedWorldNames = new string[0];
            if (result.ExcludedWorldNames == null) result.ExcludedWorldNames = new string[0];
            if (!hasCharacterSelectionSettings) result.AutoDiscoverCharacters = true;
            if (result.IncludedCharacterNames == null) result.IncludedCharacterNames = new string[0];
            if (result.ExcludedCharacterNames == null) result.ExcludedCharacterNames = new string[0];
            if (result.RetainGoodBackups < 1) result.RetainGoodBackups = 10;
            if (result.RetainQuarantineBackups < 1) result.RetainQuarantineBackups = 5;
            if (result.MinimumFreeSpaceMB < 128) result.MinimumFreeSpaceMB = 2048;
            if (result.PollSeconds < 2) result.PollSeconds = 5;
            if (result.SettleSeconds < 2) result.SettleSeconds = 5;
            if (result.BackupAtSaveRetention < 1) result.BackupAtSaveRetention = 5;
            if (!hasShowOnLaunchSetting) result.ShowWindowWhenValheimStarts = true;
            return result;
        }

        private static bool TryLoad(string path, out AppConfig result, out bool hasCharacterSelectionSettings,
            out bool hasShowOnLaunchSetting)
        {
            result = new AppConfig();
            hasCharacterSelectionSettings = true;
            hasShowOnLaunchSetting = true;
            try
            {
                if (!File.Exists(path)) return false;
                string json = File.ReadAllText(path, Encoding.UTF8);
                hasCharacterSelectionSettings = json.IndexOf("\"AutoDiscoverCharacters\"", StringComparison.OrdinalIgnoreCase) >= 0;
                hasShowOnLaunchSetting = json.IndexOf("\"ShowWindowWhenValheimStarts\"", StringComparison.OrdinalIgnoreCase) >= 0;
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                AppConfig loaded = serializer.Deserialize<AppConfig>(json);
                if (loaded == null) return false;
                result = loaded;
                return true;
            }
            catch { return false; }
        }

        public void Save()
        {
            Directory.CreateDirectory(AppPaths.ProductRoot);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            string json = serializer.Serialize(this);
            string temporary = AppPaths.ConfigFile + ".new";
            File.WriteAllText(temporary, json, new UTF8Encoding(true));
            if (File.Exists(AppPaths.ConfigFile))
            {
                try { File.Replace(temporary, AppPaths.ConfigFile, null, true); }
                catch
                {
                    File.Copy(temporary, AppPaths.ConfigFile, true);
                    File.Delete(temporary);
                }
            }
            else File.Move(temporary, AppPaths.ConfigFile);
            File.Copy(AppPaths.ConfigFile, AppPaths.ConfigBackupFile, true);
        }
    }

    internal static class SteamCloudDiscovery
    {
        internal static bool MatchesSelection(HashSet<string> values, string key, string name)
        {
            if (values.Contains(key) || values.Contains(name) || values.Contains("Name:" + name)) return true;
            foreach (string value in values)
            {
                try
                {
                    string leaf = Path.GetFileName(value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (leaf.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                        Path.GetFileNameWithoutExtension(leaf).Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch { }
            }
            return false;
        }

        internal static List<string> GetSaveRoots(string folderName)
        {
            List<string> results = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string steamRoot in GetSteamRoots())
            {
                string userdata = Path.Combine(steamRoot, "userdata");
                if (!Directory.Exists(userdata)) continue;
                try
                {
                    foreach (string account in Directory.GetDirectories(userdata))
                    {
                        string remote = Path.Combine(account, "892970", "remote");
                        AddCandidate(results, seen, Path.Combine(remote, folderName));
                        AddCandidate(results, seen, Path.Combine(remote, folderName + "_local"));
                        AddCandidate(results, seen, Path.Combine(remote, "IronGate", "Valheim", folderName));
                        AddCandidate(results, seen, Path.Combine(remote, "IronGate", "Valheim", folderName + "_local"));
                    }
                }
                catch { }
            }
            return results;
        }

        private static List<string> GetSteamRoots()
        {
            List<string> roots = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    object value = key == null ? null : key.GetValue("SteamPath");
                    if (value != null) AddRoot(roots, seen, value.ToString());
                }
            }
            catch { }
            try
            {
                foreach (Process process in Process.GetProcessesByName("steam"))
                {
                    try { AddRoot(roots, seen, Path.GetDirectoryName(process.MainModule.FileName)); }
                    catch { }
                    finally { process.Dispose(); }
                }
            }
            catch { }
            AddRoot(roots, seen, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
            AddRoot(roots, seen, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));
            return roots;
        }

        private static void AddRoot(List<string> roots, HashSet<string> seen, string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return;
            try
            {
                string full = Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar));
                if (seen.Add(full)) roots.Add(full);
            }
            catch { }
        }

        private static void AddCandidate(List<string> roots, HashSet<string> seen, string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                if (seen.Add(full)) roots.Add(full);
            }
            catch { }
        }
    }

    internal sealed class WorldInfo
    {
        public string Name;
        public string DisplayName;
        public string Key;
        public string Folder;
        public bool Selected;
        public override string ToString() { return DisplayName; }
    }

    internal static class WorldDiscovery
    {
        private static readonly Regex BackupName = new Regex("_backup_.+-[0-9]{8}-[0-9]{6}$", RegexOptions.IgnoreCase);

        internal static List<WorldInfo> Discover(AppConfig config)
        {
            List<WorldInfo> worlds = new List<WorldInfo>();
            Dictionary<string, WorldInfo> found = new Dictionary<string, WorldInfo>(StringComparer.OrdinalIgnoreCase);
            try
            {
                HashSet<string> included = new HashSet<string>(config.IncludedWorldNames, StringComparer.OrdinalIgnoreCase);
                HashSet<string> excluded = new HashSet<string>(config.ExcludedWorldNames, StringComparer.OrdinalIgnoreCase);
                foreach (string root in GetWorldRoots(config))
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (string folder in Directory.GetDirectories(root))
                    {
                        string name = Path.GetFileName(folder);
                        if (BackupName.IsMatch(name)) continue;
                        bool hasData = false;
                        try
                        {
                            foreach (string file in Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly))
                            {
                                string fileName = Path.GetFileName(file);
                                if (fileName.StartsWith("_main.", StringComparison.OrdinalIgnoreCase) ||
                                    fileName.EndsWith(".chunk", StringComparison.OrdinalIgnoreCase))
                                {
                                    hasData = true;
                                    break;
                                }
                            }
                        }
                        catch { }
                        if (!hasData) continue;
                        string key = Path.GetFullPath(folder);
                        bool selected = config.AutoDiscoverWorlds
                            ? !SteamCloudDiscovery.MatchesSelection(excluded, key, name)
                            : SteamCloudDiscovery.MatchesSelection(included, key, name);
                        string source = GetSourceLabel(root);
                        found[key] = new WorldInfo
                        {
                            Name = name,
                            DisplayName = name + " [" + source + "]",
                            Key = key,
                            Folder = key,
                            Selected = selected
                        };
                    }
                }
            }
            catch { }
            worlds.AddRange(found.Values);
            worlds.Sort(delegate(WorldInfo a, WorldInfo b) { return StringComparer.OrdinalIgnoreCase.Compare(a.DisplayName, b.DisplayName); });
            return worlds;
        }

        private static List<string> GetWorldRoots(AppConfig config)
        {
            List<string> roots = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            AddRoot(roots, seen, Path.Combine(profile, "AppData", "LocalLow", "IronGate", "Valheim", "worlds_local"));
            AddRoot(roots, seen, Path.Combine(profile, "AppData", "LocalLow", "IronGate", "Valheim", "worlds"));
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            AddRoot(roots, seen, Path.Combine(local, "IronGate", "Valheim", "worlds_local"));
            AddRoot(roots, seen, Path.Combine(local, "IronGate", "Valheim", "worlds"));
            foreach (string cloudRoot in SteamCloudDiscovery.GetSaveRoots("worlds")) AddRoot(roots, seen, cloudRoot);
            return roots;
        }

        private static void AddRoot(List<string> roots, HashSet<string> seen, string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return;
            try
            {
                string full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
                if (seen.Add(full)) roots.Add(full);
            }
            catch { }
        }

        private static string GetSourceLabel(string root)
        {
            if (root.IndexOf(Path.DirectorySeparatorChar + "userdata" + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) >= 0 && root.IndexOf(Path.DirectorySeparatorChar + "892970" + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) >= 0) return "Steam Cloud";
            if (root.IndexOf(Path.DirectorySeparatorChar + "LocalLow" + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) >= 0) return "LocalLow";
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (root.StartsWith(local, StringComparison.OrdinalIgnoreCase)) return "Local";
            return "Official";
        }
    }

    internal sealed class CharacterInfo
    {
        public string Name;
        public string DisplayName;
        public string Key;
        public string File;
        public bool Selected;
        public override string ToString() { return DisplayName; }
    }

    internal static class CharacterDiscovery
    {
        private static readonly Regex BackupName = new Regex("_backup_.+-[0-9]{8}-[0-9]{6}$", RegexOptions.IgnoreCase);

        internal static List<CharacterInfo> Discover(AppConfig config)
        {
            List<CharacterInfo> characters = new List<CharacterInfo>();
            Dictionary<string, CharacterInfo> found = new Dictionary<string, CharacterInfo>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> included = new HashSet<string>(config.IncludedCharacterNames, StringComparer.OrdinalIgnoreCase);
            HashSet<string> excluded = new HashSet<string>(config.ExcludedCharacterNames, StringComparer.OrdinalIgnoreCase);
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            List<string> roots = new List<string>(new string[]
            {
                Path.Combine(profile, "AppData", "LocalLow", "IronGate", "Valheim", "characters_local"),
                Path.Combine(profile, "AppData", "LocalLow", "IronGate", "Valheim", "characters"),
                Path.Combine(local, "IronGate", "Valheim", "characters_local"),
                Path.Combine(local, "IronGate", "Valheim", "characters")
            });
            roots.AddRange(SteamCloudDiscovery.GetSaveRoots("characters"));
            foreach (string root in roots)
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (string file in Directory.GetFiles(root, "*.fch", SearchOption.TopDirectoryOnly))
                    {
                        if (BackupName.IsMatch(Path.GetFileNameWithoutExtension(file))) continue;
                        string key = Path.GetFullPath(file);
                        string name = Path.GetFileNameWithoutExtension(file);
                        bool selected = config.AutoDiscoverCharacters
                            ? !SteamCloudDiscovery.MatchesSelection(excluded, key, name)
                            : SteamCloudDiscovery.MatchesSelection(included, key, name);
                        string source = root.IndexOf(Path.DirectorySeparatorChar + "userdata" + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase) >= 0 ? "Steam Cloud" :
                            (root.IndexOf(Path.DirectorySeparatorChar + "LocalLow" + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase) >= 0 ? "LocalLow" : "Local");
                        found[key] = new CharacterInfo
                        {
                            Name = name,
                            DisplayName = name + " [" + source + "]",
                            Key = key,
                            File = key,
                            Selected = selected
                        };
                    }
                }
                catch { }
            }
            characters.AddRange(found.Values);
            characters.Sort(delegate(CharacterInfo a, CharacterInfo b) { return StringComparer.OrdinalIgnoreCase.Compare(a.DisplayName, b.DisplayName); });
            return characters;
        }
    }

    internal sealed class StatusSummary
    {
        public bool HasStatus;
        public bool Valid;
        public string CheckedAt = "Never";
        public string Message = "No scrutiny has been run yet.";
        public string Detail = "Waiting for the first verified check.";

        public static StatusSummary Load()
        {
            StatusSummary summary = new StatusSummary();
            try
            {
                if (!File.Exists(AppPaths.StatusFile)) return summary;
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                object raw = serializer.DeserializeObject(File.ReadAllText(AppPaths.StatusFile, Encoding.UTF8));
                Dictionary<string, object> root = raw as Dictionary<string, object>;
                if (root == null) return summary;
                summary.HasStatus = true;
                object value;
                if (root.TryGetValue("Valid", out value)) summary.Valid = Convert.ToBoolean(value);
                if (root.TryGetValue("CheckedAt", out value) && value != null)
                {
                    DateTime parsed;
                    summary.CheckedAt = DateTime.TryParse(value.ToString(), out parsed) ? parsed.ToString("dd MMM yyyy HH:mm") : value.ToString();
                }
                if (root.TryGetValue("Message", out value) && value != null) summary.Message = value.ToString();
                StringBuilder detail = new StringBuilder();
                object worldsValue;
                if (root.TryGetValue("Worlds", out worldsValue))
                {
                    object[] rows = worldsValue as object[];
                    if (rows != null)
                    {
                        foreach (object rowObject in rows)
                        {
                            Dictionary<string, object> row = rowObject as Dictionary<string, object>;
                            if (row == null) continue;
                            string name = row.ContainsKey("WorldName") && row["WorldName"] != null ? row["WorldName"].ToString() : "World";
                            string asset = row.ContainsKey("AssetType") && row["AssetType"] != null ? row["AssetType"].ToString() : "World";
                            bool ok = row.ContainsKey("Valid") && Convert.ToBoolean(row["Valid"]);
                            detail.Append(ok ? "[GOOD] " : "[FAILED] ");
                            detail.Append(asset);
                            detail.Append(": ");
                            detail.Append(name);
                            if (asset.Equals("World", StringComparison.OrdinalIgnoreCase) && row.ContainsKey("Generation") && row["Generation"] != null)
                            {
                                detail.Append("  generation ");
                                detail.Append(row["Generation"]);
                            }
                            detail.AppendLine();
                        }
                    }
                }
                summary.Detail = detail.Length > 0 ? detail.ToString().TrimEnd() : summary.Message;
            }
            catch (Exception ex)
            {
                summary.Message = "Status file could not be read.";
                summary.Detail = ex.Message;
            }
            return summary;
        }
    }

    internal static class BackupOrganizer
    {
        internal static void Organize(string backupRoot)
        {
            if (String.IsNullOrWhiteSpace(backupRoot) || !Directory.Exists(backupRoot)) return;
            string worldsRoot = Path.Combine(backupRoot, "Worlds");
            string charactersRoot = Path.Combine(backupRoot, "Characters");
            Directory.CreateDirectory(worldsRoot);
            Directory.CreateDirectory(charactersRoot);
            foreach (string scanRoot in new string[] { backupRoot, worldsRoot, charactersRoot })
            {
                try
                {
                    foreach (string zipPath in Directory.GetFiles(scanRoot, "*.zip", SearchOption.TopDirectoryOnly))
                        OrganizeOne(zipPath, worldsRoot, charactersRoot);
                }
                catch (Exception ex) { WriteLog("Existing backup organization skipped in " + scanRoot + ": " + ex.Message); }
            }
        }

        private static void OrganizeOne(string zipPath, string worldsRoot, string charactersRoot)
        {
            try
            {
                Dictionary<string, object> manifest;
                using (ZipArchive archive = ZipFile.OpenRead(zipPath))
                {
                    ZipArchiveEntry entry = archive.GetEntry("Safeguardian_manifest.json");
                    if (entry == null) return;
                    using (StreamReader reader = new StreamReader(entry.Open(), Encoding.UTF8))
                    {
                        JavaScriptSerializer serializer = new JavaScriptSerializer();
                        manifest = serializer.DeserializeObject(reader.ReadToEnd()) as Dictionary<string, object>;
                    }
                }
                if (manifest == null) return;
                object application;
                if (!manifest.TryGetValue("Application", out application) || application == null ||
                    application.ToString().IndexOf("MagicDragon", StringComparison.OrdinalIgnoreCase) < 0) return;
                object typeValue;
                string assetType = manifest.TryGetValue("AssetType", out typeValue) && typeValue != null ? typeValue.ToString() : "World";
                string name = assetType.Equals("Character", StringComparison.OrdinalIgnoreCase)
                    ? ReadName(manifest, "CharacterFileName", "Character")
                    : ReadName(manifest, "WorldFolderName", "World");
                if (assetType.Equals("Character", StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(name);
                string destinationRoot = assetType.Equals("Character", StringComparison.OrdinalIgnoreCase) ? charactersRoot : worldsRoot;
                string destinationFolder = Path.Combine(destinationRoot, SafeFolderName(name));
                string destinationZip = Path.Combine(destinationFolder, Path.GetFileName(zipPath));
                if (Path.GetDirectoryName(zipPath).Equals(destinationFolder, StringComparison.OrdinalIgnoreCase)) return;
                Directory.CreateDirectory(destinationFolder);
                if (File.Exists(destinationZip)) { WriteLog("Existing backup was not moved because its destination already exists: " + zipPath); return; }
                string sidecar = zipPath + ".sha256.txt";
                string destinationSidecar = destinationZip + ".sha256.txt";
                if (File.Exists(sidecar) && File.Exists(destinationSidecar)) { WriteLog("Existing backup was not moved because its checksum destination already exists: " + zipPath); return; }
                File.Move(zipPath, destinationZip);
                try
                {
                    if (File.Exists(sidecar)) File.Move(sidecar, destinationSidecar);
                }
                catch
                {
                    if (!File.Exists(zipPath) && File.Exists(destinationZip)) File.Move(destinationZip, zipPath);
                    throw;
                }
                WriteLog("Existing backup organized into: " + destinationFolder);
            }
            catch (Exception ex) { WriteLog("Existing backup could not be organized: " + zipPath + ". " + ex.Message); }
        }

        private static string ReadName(Dictionary<string, object> manifest, string preferred, string fallback)
        {
            object value;
            if (manifest.TryGetValue(preferred, out value) && value != null && !String.IsNullOrWhiteSpace(value.ToString())) return value.ToString();
            if (manifest.TryGetValue(fallback, out value) && value != null && !String.IsNullOrWhiteSpace(value.ToString())) return value.ToString();
            return "Unnamed";
        }

        private static string SafeFolderName(string name)
        {
            StringBuilder safe = new StringBuilder(String.IsNullOrWhiteSpace(name) ? "Unnamed" : name.Trim());
            HashSet<char> invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            for (int index = 0; index < safe.Length; index++) if (invalid.Contains(safe[index])) safe[index] = '_';
            string result = safe.ToString().TrimEnd('.', ' ');
            if (String.IsNullOrWhiteSpace(result)) result = "Unnamed";
            if (Regex.IsMatch(result, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase)) result = "_" + result;
            return result;
        }

        private static void WriteLog(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.LogFile));
                File.AppendAllText(AppPaths.LogFile, DateTime.UtcNow.ToString("u") + " [INFO] " + message + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }

    internal static class EngineRunner
    {
        internal static int Run(string mode)
        {
            return Run(mode, null);
        }

        internal static int Run(string mode, string backupPath)
        {
            return Run(mode, backupPath, null);
        }

        internal static int Run(string mode, string backupPath, string targetWorldPath)
        {
            return Run(mode, backupPath, targetWorldPath, null);
        }

        internal static int Run(string mode, string backupPath, string targetWorldPath, string targetCharacterPath)
        {
            try
            {
                if (!File.Exists(AppPaths.EngineScript))
                {
                    WriteFailure(mode, "The PowerShell engine script was not found.");
                    return 90;
                }
                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = "powershell.exe";
                start.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + AppPaths.EngineScript + "\" -Mode " + mode + " -Silent";
                if (!String.IsNullOrWhiteSpace(backupPath))
                    start.Arguments += " -BackupPath \"" + backupPath.Replace("\"", "\\\"") + "\"";
                if (!String.IsNullOrWhiteSpace(targetWorldPath))
                    start.Arguments += " -TargetWorldPath \"" + targetWorldPath.Replace("\"", "\\\"") + "\"";
                if (!String.IsNullOrWhiteSpace(targetCharacterPath))
                    start.Arguments += " -TargetCharacterPath \"" + targetCharacterPath.Replace("\"", "\\\"") + "\"";
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                start.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process process = Process.Start(start))
                {
                    if (process == null)
                    {
                        WriteFailure(mode, "Windows could not start the PowerShell engine.");
                        return 91;
                    }
                    process.WaitForExit();
                    return process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                WriteFailure(mode, ex.Message);
                return 91;
            }
        }

        private static void WriteFailure(string mode, string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.LogFile));
                File.AppendAllText(AppPaths.LogFile,
                    DateTime.UtcNow.ToString("u") + " [FATAL] Engine mode '" + mode + "' could not complete: " + message + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch { }
        }
    }

    public sealed class RestorePreview
    {
        public bool Valid { get; set; }
        public string BackupPath { get; set; }
        public string BackupStatus { get; set; }
        public string World { get; set; }
        public object Generation { get; set; }
        public string Created { get; set; }
        public string StorageSource { get; set; }
        public string TargetPath { get; set; }
        public string ZipIntegrity { get; set; }
        public string Checksum { get; set; }
        public string WorldStructure { get; set; }
        public string ZipSHA256 { get; set; }
        public object[] Errors { get; set; }

        internal static RestorePreview Load()
        {
            if (!File.Exists(AppPaths.RestorePreviewFile)) return null;
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            return serializer.Deserialize<RestorePreview>(File.ReadAllText(AppPaths.RestorePreviewFile, Encoding.UTF8));
        }
    }

    public sealed class RestoreResult
    {
        public bool Success { get; set; }
        public string World { get; set; }
        public string TargetPath { get; set; }
        public string RecoveryPath { get; set; }
        public object Generation { get; set; }
        public string BackupPath { get; set; }
        public string Message { get; set; }

        internal static RestoreResult Load()
        {
            if (!File.Exists(AppPaths.RestoreResultFile)) return null;
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            return serializer.Deserialize<RestoreResult>(File.ReadAllText(AppPaths.RestoreResultFile, Encoding.UTF8));
        }
    }

    public sealed class DiagnosticResult
    {
        public bool Success { get; set; }
        public string Path { get; set; }
        public string CreatedAt { get; set; }
        public string SHA256 { get; set; }
        public string Message { get; set; }

        internal static DiagnosticResult Load()
        {
            if (!File.Exists(AppPaths.DiagnosticResultFile)) return null;
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            return serializer.Deserialize<DiagnosticResult>(File.ReadAllText(AppPaths.DiagnosticResultFile, Encoding.UTF8));
        }
    }

    internal sealed class RestoreWizardForm : Form
    {
        private readonly string backupPath;
        private readonly Label worldValue;
        private readonly Label statusValue;
        private readonly Label generationValue;
        private readonly Label createdValue;
        private readonly Label sourceValue;
        private readonly Label checksumValue;
        private readonly Label zipValue;
        private readonly Label structureValue;
        private readonly TextBox targetValue;
        private readonly Label progress;
        private readonly Button restoreButton;
        private readonly Button cancelButton;
        private RestorePreview preview;

        internal RestoreWizardForm(string selectedBackup, Icon applicationIcon)
        {
            backupPath = selectedBackup;
            Text = "Verified Restore Wizard";
            ClientSize = new Size(620, 500);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = SystemColors.Control;
            Font = new Font("MS Sans Serif", 9F);
            Icon = applicationIcon;

            Label heading = new Label();
            heading.Text = "Verified Restore Wizard";
            heading.Font = new Font("MS Sans Serif", 13F, FontStyle.Bold);
            heading.Location = new Point(20, 16);
            heading.Size = new Size(570, 28);
            Controls.Add(heading);

            GroupBox details = new GroupBox();
            details.Text = "Backup verification";
            details.Location = new Point(20, 48);
            details.Size = new Size(580, 345);
            Controls.Add(details);

            worldValue = AddRow(details, "World:", 28);
            statusValue = AddRow(details, "Backup status:", 60);
            generationValue = AddRow(details, "Save generation:", 92);
            createdValue = AddRow(details, "Created:", 124);
            sourceValue = AddRow(details, "Storage source:", 156);
            checksumValue = AddRow(details, "SHA-256 checksum:", 188);
            zipValue = AddRow(details, "ZIP integrity:", 220);
            structureValue = AddRow(details, "World structure:", 252);
            Label targetCaption = new Label();
            targetCaption.Text = "Restore target:";
            targetCaption.Location = new Point(18, 290);
            targetCaption.Size = new Size(140, 24);
            details.Controls.Add(targetCaption);
            targetValue = new TextBox();
            targetValue.Location = new Point(165, 286);
            targetValue.Size = new Size(390, 25);
            targetValue.ReadOnly = true;
            details.Controls.Add(targetValue);

            progress = new Label();
            progress.Text = "Verifying the selected backup...";
            progress.Location = new Point(22, 405);
            progress.Size = new Size(570, 34);
            Controls.Add(progress);

            restoreButton = new Button();
            restoreButton.Text = "Restore Backup";
            restoreButton.Location = new Point(290, 448);
            restoreButton.Size = new Size(145, 36);
            restoreButton.Enabled = false;
            restoreButton.Click += delegate { StartRestore(); };
            Controls.Add(restoreButton);
            cancelButton = new Button();
            cancelButton.Text = "Cancel";
            cancelButton.Location = new Point(450, 448);
            cancelButton.Size = new Size(145, 36);
            cancelButton.Click += delegate { Close(); };
            Controls.Add(cancelButton);
            CancelButton = cancelButton;

            Shown += delegate { StartVerification(); };
        }

        private Label AddRow(Control parent, string caption, int y)
        {
            Label key = new Label();
            key.Text = caption;
            key.Location = new Point(18, y);
            key.Size = new Size(140, 24);
            parent.Controls.Add(key);
            Label value = new Label();
            value.Text = "Verifying...";
            value.Location = new Point(165, y);
            value.Size = new Size(390, 24);
            value.BorderStyle = BorderStyle.Fixed3D;
            value.TextAlign = ContentAlignment.MiddleLeft;
            parent.Controls.Add(value);
            return value;
        }

        private void StartVerification()
        {
            restoreButton.Enabled = false;
            cancelButton.Enabled = false;
            ThreadPool.QueueUserWorkItem(delegate
            {
                int code = EngineRunner.Run("InspectBackup", backupPath);
                RestorePreview loaded = null;
                try { loaded = RestorePreview.Load(); } catch { }
                BeginInvoke((MethodInvoker)delegate
                {
                    preview = loaded;
                    cancelButton.Enabled = true;
                    if (preview == null)
                    {
                        progress.Text = "Verification could not produce a result. Review the Logs page.";
                        progress.ForeColor = Color.Maroon;
                        return;
                    }
                    worldValue.Text = preview.World ?? "Unknown";
                    statusValue.Text = preview.BackupStatus ?? "Unknown";
                    generationValue.Text = preview.Generation == null ? "Unknown" : preview.Generation.ToString();
                    DateTime created;
                    createdValue.Text = DateTime.TryParse(preview.Created, out created) ? created.ToString("dd MMM yyyy HH:mm") : (preview.Created ?? "Unknown");
                    sourceValue.Text = preview.StorageSource ?? "Unknown";
                    checksumValue.Text = preview.Checksum ?? "Failed";
                    zipValue.Text = preview.ZipIntegrity ?? "Failed";
                    structureValue.Text = preview.WorldStructure ?? "Failed";
                    targetValue.Text = preview.TargetPath ?? "";
                    bool good = code == 0 && preview.Valid;
                    statusValue.ForeColor = good ? Color.DarkGreen : Color.Maroon;
                    checksumValue.ForeColor = good ? Color.DarkGreen : Color.Maroon;
                    zipValue.ForeColor = good ? Color.DarkGreen : Color.Maroon;
                    structureValue.ForeColor = good ? Color.DarkGreen : Color.Maroon;
                    restoreButton.Enabled = good;
                    progress.Text = good ? "All checks passed. The backup is ready for a protected restore."
                        : "Restore blocked: " + FormatErrors(preview.Errors);
                    progress.ForeColor = good ? Color.DarkGreen : Color.Maroon;
                });
            });
        }

        private string FormatErrors(object[] errors)
        {
            if (errors == null || errors.Length == 0) return "verification failed.";
            return String.Join(" ", errors.Select(delegate(object e) { return e == null ? "" : e.ToString(); }).ToArray());
        }

        private void StartRestore()
        {
            if (preview == null || !preview.Valid) return;
            if (Process.GetProcessesByName("valheim").Length > 0)
            {
                MessageBox.Show(this, "Valheim is still running. Close it completely before restoring.",
                    "Restore blocked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string confirmation = "Valheim is closed. The existing world will be preserved in Recovery before restoration.\r\n\r\n" +
                "World: " + preview.World + "\r\nGeneration: " + preview.Generation + "\r\nTarget: " + preview.TargetPath +
                "\r\n\r\nRestore this verified " + preview.BackupStatus + " backup?";
            if (MessageBox.Show(this, confirmation, "Confirm verified restore", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes) return;

            restoreButton.Enabled = false;
            cancelButton.Enabled = false;
            progress.ForeColor = Color.Black;
            progress.Text = "Restoring from the verified staging copy. Do not start Valheim...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                int code = EngineRunner.Run("RestoreBackup", backupPath);
                RestoreResult result = null;
                try { result = RestoreResult.Load(); } catch { }
                BeginInvoke((MethodInvoker)delegate
                {
                    cancelButton.Enabled = true;
                    if (code == 0 && result != null && result.Success)
                    {
                        string preserved = String.IsNullOrWhiteSpace(result.RecoveryPath)
                            ? "No previous world existed at the destination."
                            : "Previous world preserved at:\r\n" + result.RecoveryPath;
                        MessageBox.Show(this, "Restore completed successfully.\r\n\r\nWorld: " + result.World +
                            "\r\nGeneration: " + result.Generation + "\r\n\r\n" + preserved,
                            "Verified restore complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        DialogResult = DialogResult.OK;
                        Close();
                        return;
                    }
                    restoreButton.Enabled = preview.Valid;
                    progress.ForeColor = Color.Maroon;
                    progress.Text = "Restore failed and rollback was attempted. Review the Logs page.";
                    MessageBox.Show(this, result == null ? progress.Text : result.Message,
                        "Restore failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                });
            });
        }
    }

    internal sealed class CheckModeForm : Form
    {
        internal bool CreateBackup { get; private set; }

        internal CheckModeForm(string assetPlural, Icon icon)
        {
            Text = "Check " + assetPlural;
            ClientSize = new Size(470, 185);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = SystemColors.Control;
            Font = new Font("MS Sans Serif", 8F, FontStyle.Regular);
            if (icon != null) Icon = icon;

            Label prompt = new Label();
            prompt.Text = "Choose what MagicDragon should do with the selected " + assetPlural.ToLowerInvariant() + ":";
            prompt.Location = new Point(20, 18);
            prompt.Size = new Size(430, 36);
            prompt.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(prompt);

            Button verifyOnly = MakeChoiceButton("Check Integrity Only", 20, 67, 200);
            verifyOnly.Click += delegate
            {
                CreateBackup = false;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(verifyOnly);

            Button verifyAndBackup = MakeChoiceButton("Check and Create Backup", 235, 67, 215);
            verifyAndBackup.Click += delegate
            {
                CreateBackup = true;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(verifyAndBackup);

            Button cancel = MakeChoiceButton("Cancel", 175, 126, 120);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);
            CancelButton = cancel;
        }

        private Button MakeChoiceButton(string text, int x, int y, int width)
        {
            Button button = new Button();
            button.Text = text;
            button.Location = new Point(x, y);
            button.Size = new Size(width, 42);
            button.Font = new Font("MS Sans Serif", 8F, FontStyle.Regular);
            button.FlatStyle = FlatStyle.Standard;
            button.UseVisualStyleBackColor = false;
            button.BackColor = SystemColors.Control;
            button.TextAlign = ContentAlignment.MiddleCenter;
            return button;
        }
    }

    internal sealed class PixelPictureBox : Control
    {
        public Image Image { get; set; }
        public PixelPictureBox() { DoubleBuffered = true; BackColor = Color.Black; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Image == null) return;
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.CompositingQuality = CompositingQuality.HighSpeed;
            float scale = Math.Min((float)ClientSize.Width / Image.Width, (float)ClientSize.Height / Image.Height);
            int width = Math.Max(1, (int)(Image.Width * scale));
            int height = Math.Max(1, (int)(Image.Height * scale));
            int x = (ClientSize.Width - width) / 2;
            int y = (ClientSize.Height - height) / 2;
            e.Graphics.DrawImage(Image, new Rectangle(x, y, width, height));
        }
    }

    internal sealed class DragonSpeechBubble : Control
    {
        private const int TailHeight = 18;
        private string message = String.Empty;

        internal string Message
        {
            get { return message; }
            set
            {
                message = value ?? String.Empty;
                Size = MeasureBubble(message);
                UpdateBubbleRegion();
                Invalidate();
            }
        }

        internal DragonSpeechBubble()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Font = new Font("MS Sans Serif", 8F, FontStyle.Regular, GraphicsUnit.Point);
        }

        private Size MeasureBubble(string text)
        {
            const int minimumWidth = 180;
            const int maximumWidth = 380;
            Size singleLine = TextRenderer.MeasureText(text, Font, new Size(1000, 1000),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            int width = Math.Max(minimumWidth, Math.Min(maximumWidth, singleLine.Width + 30));
            Size wrapped = TextRenderer.MeasureText(text, Font, new Size(width - 24, 300),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            int bodyHeight = Math.Max(46, wrapped.Height + 18);
            return new Size(width, bodyHeight + TailHeight + 2);
        }

        private GraphicsPath CreateBubblePath()
        {
            Rectangle body = new Rectangle(1, 1, Math.Max(1, ClientSize.Width - 3),
                Math.Max(1, ClientSize.Height - TailHeight - 2));
            int radius = Math.Min(10, Math.Max(2, Math.Min(body.Width, body.Height) / 3));
            int diameter = radius * 2;
            int tailRight = Math.Min(body.Right - radius, body.Left + 58);
            int tailLeft = Math.Min(tailRight - 8, body.Left + 34);
            int tailTip = Math.Max(body.Left + 7, tailLeft - 10);
            GraphicsPath path = new GraphicsPath();
            path.StartFigure();
            path.AddArc(body.Left, body.Top, diameter, diameter, 180, 90);
            path.AddLine(body.Left + radius, body.Top, body.Right - radius, body.Top);
            path.AddArc(body.Right - diameter, body.Top, diameter, diameter, 270, 90);
            path.AddLine(body.Right, body.Top + radius, body.Right, body.Bottom - radius);
            path.AddArc(body.Right - diameter, body.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddLine(body.Right - radius, body.Bottom, tailRight, body.Bottom);
            path.AddLine(tailRight, body.Bottom, tailTip, ClientSize.Height - 2);
            path.AddLine(tailTip, ClientSize.Height - 2, tailLeft, body.Bottom);
            path.AddLine(tailLeft, body.Bottom, body.Left + radius, body.Bottom);
            path.AddArc(body.Left, body.Bottom - diameter, diameter, diameter, 90, 90);
            path.AddLine(body.Left, body.Bottom - radius, body.Left, body.Top + radius);
            path.CloseFigure();
            return path;
        }

        private void UpdateBubbleRegion()
        {
            if (ClientSize.Width < 4 || ClientSize.Height < TailHeight + 4) return;
            using (GraphicsPath path = CreateBubblePath())
            {
                Region previous = Region;
                Region = new Region(path);
                if (previous != null) previous.Dispose();
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateBubbleRegion();
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle body = new Rectangle(1, 1, Math.Max(1, ClientSize.Width - 3),
                Math.Max(1, ClientSize.Height - TailHeight - 2));
            using (GraphicsPath bubblePath = CreateBubblePath())
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(255, 255, 204)))
            using (Pen border = new Pen(Color.Black, 1F))
            {
                e.Graphics.FillPath(fill, bubblePath);
                e.Graphics.DrawPath(border, bubblePath);
            }

            Rectangle textArea = new Rectangle(body.Left + 12, body.Top + 9,
                Math.Max(1, body.Width - 24), Math.Max(1, body.Height - 16));
            TextRenderer.DrawText(e.Graphics, message, Font, textArea, Color.Black,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak |
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
    }

    internal enum ClassicCaptionGlyph
    {
        Minimize,
        Maximize,
        Restore,
        Help,
        Close
    }

    internal sealed class ClassicTitleLabel : Control
    {
        internal ClassicTitleLabel()
        {
            BackColor = Color.Navy;
            ForeColor = Color.White;
            Font = new Font("MS Sans Serif", 12F, FontStyle.Bold, GraphicsUnit.Pixel);
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, BackColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class ClassicCaptionButton : Button
    {
        private ClassicCaptionGlyph glyph;
        private bool pressed;

        internal ClassicCaptionGlyph Glyph
        {
            get { return glyph; }
            set { glyph = value; Invalidate(); }
        }

        internal ClassicCaptionButton(ClassicCaptionGlyph buttonGlyph)
        {
            glyph = buttonGlyph;
            Size = new Size(32, 32);
            Text = String.Empty;
            TabStop = false;
            FlatStyle = FlatStyle.Standard;
            UseVisualStyleBackColor = false;
            BackColor = SystemColors.Control;
            Font = new Font("MS Sans Serif", 10F, FontStyle.Bold, GraphicsUnit.Point);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            pressed = e.Button == MouseButtons.Left;
            base.OnMouseDown(e);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            base.OnMouseUp(e);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            pressed = false;
            base.OnMouseLeave(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(SystemColors.Control);
            e.Graphics.SmoothingMode = SmoothingMode.None;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.None;
            int offset = pressed ? 1 : 0;
            int cx = ClientSize.Width / 2 + offset;
            int cy = ClientSize.Height / 2 + offset;

            using (Pen white = new Pen(SystemColors.ControlLightLight, 1F))
            using (Pen light = new Pen(SystemColors.ControlLight, 1F))
            using (Pen dark = new Pen(SystemColors.ControlDark, 1F))
            using (Pen black = new Pen(Color.Black, 1F))
            using (Pen thick = new Pen(Color.Black, 3F))
            using (Brush brush = new SolidBrush(Color.Black))
            {
                int right = Math.Max(1, ClientSize.Width - 1);
                int bottom = Math.Max(1, ClientSize.Height - 1);
                if (!pressed)
                {
                    e.Graphics.DrawLine(white, 0, 0, right - 1, 0);
                    e.Graphics.DrawLine(white, 0, 0, 0, bottom - 1);
                    e.Graphics.DrawLine(light, 1, 1, right - 2, 1);
                    e.Graphics.DrawLine(light, 1, 1, 1, bottom - 2);
                    e.Graphics.DrawLine(dark, 1, bottom - 1, right - 1, bottom - 1);
                    e.Graphics.DrawLine(dark, right - 1, 1, right - 1, bottom - 1);
                    e.Graphics.DrawLine(black, 0, bottom, right, bottom);
                    e.Graphics.DrawLine(black, right, 0, right, bottom);
                }
                else
                {
                    e.Graphics.DrawLine(black, 0, 0, right, 0);
                    e.Graphics.DrawLine(black, 0, 0, 0, bottom);
                    e.Graphics.DrawLine(dark, 1, 1, right - 1, 1);
                    e.Graphics.DrawLine(dark, 1, 1, 1, bottom - 1);
                    e.Graphics.DrawLine(white, 1, bottom, right, bottom);
                    e.Graphics.DrawLine(white, right, 1, right, bottom);
                }

                if (glyph == ClassicCaptionGlyph.Minimize)
                {
                    e.Graphics.FillRectangle(brush, cx - 7, cy + 4, 14, 3);
                }
                else if (glyph == ClassicCaptionGlyph.Maximize)
                {
                    e.Graphics.FillRectangle(brush, cx - 7, cy - 7, 14, 13);
                    e.Graphics.FillRectangle(SystemBrushes.Control, cx - 5, cy - 3, 10, 7);
                }
                else if (glyph == ClassicCaptionGlyph.Restore)
                {
                    e.Graphics.FillRectangle(brush, cx - 3, cy - 7, 10, 9);
                    e.Graphics.FillRectangle(SystemBrushes.Control, cx - 1, cy - 4, 6, 4);
                    e.Graphics.FillRectangle(brush, cx - 7, cy - 3, 10, 10);
                    e.Graphics.FillRectangle(SystemBrushes.Control, cx - 5, cy, 6, 5);
                }
                else if (glyph == ClassicCaptionGlyph.Help)
                {
                    using (Font helpFont = new Font("MS Sans Serif", 18F, FontStyle.Bold, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(e.Graphics, "?", helpFont,
                        new Rectangle(offset, offset, ClientSize.Width, ClientSize.Height),
                        Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                }
                else
                {
                    e.Graphics.DrawLine(thick, cx - 6, cy - 6, cx + 6, cy + 6);
                    e.Graphics.DrawLine(thick, cx + 6, cy - 6, cx - 6, cy + 6);
                }
            }
        }
    }

    internal sealed class MainForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RegisterWindowMessage(string messageName);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        private const int WM_NCHITTEST = 0x0084;
        internal static readonly int WM_ACTIVATE_MAGICDRAGON = unchecked((int)RegisterWindowMessage(
            "HardcoreApe.MagicDragonSafeguardian.ActivateDashboard.v1"));
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;
        private const int ResizeBorder = 8;
        private const string LiveWorldSessionRequiredMessage =
            "The dragon says that to live check a save file, you must enter a world and save at least once.\r\n" +
            "Wait for the save indicator to disappear!";
        private const string BackupAtSaveBusyMessage =
            "The dragon is preparing a ritual for a BackupAtSave! Don't disturb him; try again as soon as he's finished.";
        private const string BackupAtSaveFailureMessage =
            "STOP! The dragon found something strange, please check your game files and do not exit the session, save again to avoid corruption.";
        private static readonly string[] DragonComments = new string[]
        {
            "Hey, stop it, haha you're tickling me!",
            "Your personal magic dragon reporting for duty, sir!",
            "ABRACADABRA! Oops.. I spilled the potion.",
            "MagicDragon at your service, sir!",
            "My cousin is a purple dragon... at least he has a dragonfly as a sidekick... me? I have no one!",
            "Your click helped me digest—*BURP*—oh dear, I set the wand on fire!",
            "Hello! Ready for a gaming session?",
            "I'll do the work even if you don't pay me... for now.",
            "Do you need help? I'm here for that!",
            "Ready to check some files with my magic",
            "A dragon? A wizard? A guardian? Turns out, I'm all of them!",
            "Among spells, the most powerful one is storing some of your backups on a different drive.",
            "Mirror spell! Oh, I just made another backup.",
            "AH! You scared me!"
        };

        private readonly Font uiFont = new Font("MS Sans Serif", 8F, FontStyle.Regular, GraphicsUnit.Point);
        private readonly Stack<int> tabHistory = new Stack<int>();
        private readonly bool monitorOwner;
        private readonly bool startHidden;
        private bool exitRequested;
        private bool valheimWasRunning;
        private bool checkRunning;
        private bool afterExitPending;
        private readonly Dictionary<string, string> sessionWorldFingerprints =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> sessionCharacterFingerprints =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string pendingAtSaveWorldPath = String.Empty;
        private string pendingAtSaveWorldFingerprint = String.Empty;
        private DateTime pendingAtSaveWorldDueUtc = DateTime.MinValue;
        private string pendingAtSaveCharacterPath = String.Empty;
        private string pendingAtSaveCharacterFingerprint = String.Empty;
        private DateTime pendingAtSaveCharacterDueUtc = DateTime.MinValue;
        private bool atSaveBackupRunning;
        private DateTime valheimProcessStartedUtc = DateTime.MinValue;
        private DateTime worldSessionStartedUtc = DateTime.MinValue;
        private DateTime activeWorldSaveUtc = DateTime.MinValue;
        private DateTime activeWorldSettledAfterUtc = DateTime.MinValue;
        private string activeWorldPath = String.Empty;
        private string activeWorldName = String.Empty;
        private DateTime activeCharacterSaveUtc = DateTime.MinValue;
        private DateTime activeCharacterSettledAfterUtc = DateTime.MinValue;
        private string activeCharacterPath = String.Empty;
        private string activeCharacterName = String.Empty;
        private string currentGameScene = "Unknown";
        private DateTime sceneDetectedUtc = DateTime.MinValue;
        private long lastObservedLogLength;
        private string lastSceneEventName = String.Empty;
        private string lastSceneEventLine = String.Empty;
        private bool sceneTransitionChanged;
        private bool loadingControls;
        private bool suppressTabHistory;
        private int lastTabIndex;
        private AppConfig config;
        private List<WorldInfo> worlds;
        private List<CharacterInfo> characters;
        private Image mascot;
        private readonly Random dragonCommentRandom = new Random();
        private int previousDragonComment = -1;

        private TabControl tabs;
        private Label watcherValue;
        private Label watcherStatusValue;
        private Label lastCheckValue;
        private Label protectedValue;
        private Label latestValue;
        private Label backupValue;
        private TextBox dashboardStatus;
        private CheckBox dashboardAuto;
        private Button checkNowButton;
        private Button liveCheckButton;
        private Button characterCheckButton;
        private Button liveCharacterCheckButton;
        private CheckedListBox worldList;
        private Label worldPathLabel;
        private CheckBox worldsAuto;
        private CheckedListBox characterList;
        private Label characterPathLabel;
        private CheckBox charactersAuto;
        private ListView backupList;
        private TextBox backupRootBox;
        private NumericUpDown goodRetention;
        private NumericUpDown quarantineRetention;
        private NumericUpDown minimumFreeSpace;
        private NumericUpDown settleSeconds;
        private NumericUpDown backupAtSaveRetention;
        private Label backupAtSaveRecommended;
        private CheckBox showOnValheimLaunch;
        private TextBox logBox;
        private NotifyIcon tray;
        private System.Windows.Forms.Timer processTimer;
        private ClassicTitleLabel titleCaption;
        private ClassicCaptionButton maximizeCaptionButton;
        private DragonSpeechBubble dragonSpeechBubble;
        private System.Windows.Forms.Timer dragonSpeechTimer;

        internal MainForm(bool ownsMonitor, bool hidden)
        {
            monitorOwner = ownsMonitor;
            startHidden = hidden;
            Opacity = 0D;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint, true);
            config = AppConfig.Load();
            BackupOrganizer.Organize(config.BackupRoot);
            worlds = WorldDiscovery.Discover(config);
            characters = CharacterDiscovery.Discover(config);
            LoadMascot();
            BuildInterface();
            RefreshEverything();
            PrepareFirstPaint();
            if (monitorOwner) SetupMonitor();
            Shown += delegate
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (startHidden && !(valheimWasRunning && config.ShowWindowWhenValheimStarts))
                    {
                        ShowInTaskbar = false;
                        Hide();
                    }
                    Opacity = 1D;
                    if (Visible)
                    {
                        Invalidate(true);
                        Update();
                    }
                });
            };
            FormClosed += delegate
            {
                if (dragonSpeechTimer == null) return;
                dragonSpeechTimer.Stop();
                dragonSpeechTimer.Dispose();
                dragonSpeechTimer = null;
            };
        }

        private void LoadMascot()
        {
            try
            {
                if (File.Exists(AppPaths.MascotFile))
                {
                    using (FileStream stream = new FileStream(AppPaths.MascotFile, FileMode.Open, FileAccess.Read))
                    using (Image loaded = Image.FromStream(stream)) mascot = new Bitmap(loaded);
                }
            }
            catch { mascot = null; }
        }

        private void BuildInterface()
        {
            Text = "MagicDragon Safeguardian";
            ClientSize = new Size(820, 740);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = SystemColors.Control;
            Font = uiFont;
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            MinimumSize = new Size(720, 620);
            MaximizeBox = true;
            MinimizeBox = true;

            Panel frame = new Panel();
            frame.Dock = DockStyle.Fill;
            frame.BorderStyle = BorderStyle.Fixed3D;
            Controls.Add(frame);

            Panel titleBar = new Panel();
            titleBar.Dock = DockStyle.Top;
            titleBar.Height = 44;
            titleBar.BackColor = Color.Navy;
            titleBar.MouseDown += DragWindow;
            frame.Controls.Add(titleBar);

            PixelPictureBox tinyDragon = new PixelPictureBox();
            tinyDragon.Location = new Point(8, 6);
            tinyDragon.Size = new Size(32, 32);
            tinyDragon.Image = mascot;
            tinyDragon.BackColor = Color.Navy;
            tinyDragon.MouseDown += DragWindow;
            titleBar.Controls.Add(tinyDragon);

            titleCaption = new ClassicTitleLabel();
            titleCaption.Text = "MagicDragon Safeguardian";
            titleCaption.Location = new Point(47, 6);
            titleCaption.Size = new Size(560, 32);
            titleCaption.MouseDown += DragWindow;
            titleCaption.MouseDoubleClick += delegate { ToggleMaximize(); };
            titleBar.Controls.Add(titleCaption);
            titleBar.MouseDoubleClick += delegate { ToggleMaximize(); };

            ClassicCaptionButton minimize = new ClassicCaptionButton(ClassicCaptionGlyph.Minimize);
            minimize.Location = new Point(672, 6);
            minimize.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            minimize.Click += delegate { WindowState = FormWindowState.Minimized; };
            titleBar.Controls.Add(minimize);

            maximizeCaptionButton = new ClassicCaptionButton(ClassicCaptionGlyph.Maximize);
            maximizeCaptionButton.Location = new Point(708, 6);
            maximizeCaptionButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            maximizeCaptionButton.Click += delegate { ToggleMaximize(); };
            titleBar.Controls.Add(maximizeCaptionButton);

            ClassicCaptionButton help = new ClassicCaptionButton(ClassicCaptionGlyph.Help);
            help.Location = new Point(744, 6);
            help.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            help.Click += delegate { ShowHelp(); };
            titleBar.Controls.Add(help);

            ClassicCaptionButton close = new ClassicCaptionButton(ClassicCaptionGlyph.Close);
            close.Location = new Point(780, 6);
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.Click += delegate { Close(); };
            titleBar.Controls.Add(close);

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 68;
            frame.Controls.Add(bottom);

            Label versionLabel = new Label();
            versionLabel.Text = "Version 1.0.1";
            versionLabel.Location = new Point(12, 36);
            versionLabel.Size = new Size(150, 20);
            versionLabel.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            versionLabel.ForeColor = SystemColors.GrayText;
            versionLabel.Font = new Font("MS Sans Serif", 7F, FontStyle.Regular, GraphicsUnit.Point);
            bottom.Controls.Add(versionLabel);

            Label signature = new Label();
            signature.Text = "Made by HardcoreApe 2026 ©";
            signature.Location = new Point(470, 1);
            signature.Size = new Size(330, 16);
            signature.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            signature.TextAlign = ContentAlignment.TopRight;
            signature.ForeColor = SystemColors.GrayText;
            signature.Font = new Font("MS Sans Serif", 7F, FontStyle.Regular, GraphicsUnit.Point);
            bottom.Controls.Add(signature);

            Button settingsBottom = ClassicButton("Settings", 155, 38);
            settingsBottom.Location = new Point(333, 26);
            settingsBottom.Anchor = AnchorStyles.Top;
            settingsBottom.Click += delegate { NavigateToTab(4); };
            bottom.Controls.Add(settingsBottom);
            bottom.Resize += delegate
            {
                settingsBottom.Left = Math.Max(0, (bottom.ClientSize.Width - settingsBottom.Width) / 2);
            };

            Button closeBottom = ClassicButton("Close", 155, 38);
            closeBottom.Location = new Point(635, 26);
            closeBottom.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            closeBottom.Click += delegate { HideOrClose(); };
            bottom.Controls.Add(closeBottom);

            Label resizeGrip = new Label();
            resizeGrip.Text = "◢";
            resizeGrip.Location = new Point(800, 40);
            resizeGrip.Size = new Size(16, 16);
            resizeGrip.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            resizeGrip.Font = new Font("MS Sans Serif", 8F, FontStyle.Bold);
            resizeGrip.Cursor = Cursors.SizeNWSE;
            resizeGrip.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left || WindowState != FormWindowState.Normal) return;
                ReleaseCapture();
                SendMessage(Handle, 0xA1, HTBOTTOMRIGHT, 0);
            };
            bottom.Controls.Add(resizeGrip);
            resizeGrip.BringToFront();

            tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Font = new Font("MS Sans Serif", 8F, FontStyle.Regular);
            tabs.Padding = new Point(18, 7);
            frame.Controls.Add(tabs);
            tabs.BringToFront();
            titleBar.BringToFront();
            bottom.BringToFront();

            BuildDashboardTab();
            BuildWorldsTab();
            BuildCharactersTab();
            BuildBackupsTab();
            BuildSettingsTab();
            BuildLogsTab();
            lastTabIndex = tabs.SelectedIndex;
            tabs.SelectedIndexChanged += TabsSelectedIndexChanged;
            ApplyCompactScale();
        }

        private void PrepareFirstPaint()
        {
            // A multiline Win32 TextBox creates its non-client scrollbar lazily.
            // Create and paint it while the form is still fully transparent so the
            // scrollbar cannot pop into view one frame after the rest of the UI.
            CreateControl();
            PerformLayout();
            if (dashboardStatus == null) return;
            dashboardStatus.CreateControl();
            IntPtr preparedStatusHandle = dashboardStatus.Handle;
            dashboardStatus.PerformLayout();
            dashboardStatus.Invalidate(true);
            dashboardStatus.Update();
        }

        private void ApplyCompactScale()
        {
            const float factor = 0.756F;
            SuspendLayout();
            MinimumSize = Size.Empty;
            Scale(new SizeF(factor, factor));
            titleCaption.Font = new Font("MS Sans Serif", 18F, FontStyle.Bold, GraphicsUnit.Pixel);
            ClientSize = new Size(620, 608);
            MinimumSize = new Size(544, 469);
            foreach (TabPage page in tabs.TabPages) page.AutoScrollMinSize = new Size(597, 438);
            if (tabs.TabPages.Count > 4) tabs.TabPages[4].AutoScrollMinSize = new Size(597, 495);
            ResumeLayout(true);
        }

        private Button ClassicButton(string text, int width, int height)
        {
            Button button = new Button();
            button.Text = text;
            button.Size = new Size(width, height);
            button.Font = new Font("MS Sans Serif", 8F, FontStyle.Regular);
            button.FlatStyle = FlatStyle.Standard;
            button.UseVisualStyleBackColor = false;
            button.BackColor = SystemColors.Control;
            button.TextAlign = ContentAlignment.MiddleCenter;
            return button;
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, 0x2, 0);
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            if (maximizeCaptionButton != null)
                maximizeCaptionButton.Glyph = WindowState == FormWindowState.Maximized
                    ? ClassicCaptionGlyph.Restore : ClassicCaptionGlyph.Maximize;
        }

        private void TabsSelectedIndexChanged(object sender, EventArgs e)
        {
            if (!suppressTabHistory && lastTabIndex >= 0 && lastTabIndex != tabs.SelectedIndex)
                tabHistory.Push(lastTabIndex);
            lastTabIndex = tabs.SelectedIndex;
        }

        private void NavigateToTab(int index)
        {
            if (index < 0 || index >= tabs.TabPages.Count || tabs.SelectedIndex == index) return;
            tabs.SelectedIndex = index;
        }

        private void GoBack()
        {
            int target = tabHistory.Count > 0 ? tabHistory.Pop() : 0;
            if (target < 0 || target >= tabs.TabPages.Count) target = 0;
            suppressTabHistory = true;
            try { tabs.SelectedIndex = target; }
            finally
            {
                suppressTabHistory = false;
                lastTabIndex = tabs.SelectedIndex;
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WM_ACTIVATE_MAGICDRAGON)
            {
                BeginInvoke((MethodInvoker)delegate { ShowMain(); });
                message.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref message);
            if (message.Msg != WM_NCHITTEST || WindowState != FormWindowState.Normal) return;
            long packed = message.LParam.ToInt64();
            int screenX = unchecked((short)(packed & 0xFFFF));
            int screenY = unchecked((short)((packed >> 16) & 0xFFFF));
            Point point = PointToClient(new Point(screenX, screenY));
            bool left = point.X <= ResizeBorder;
            bool right = point.X >= ClientSize.Width - ResizeBorder;
            bool top = point.Y <= ResizeBorder;
            bool bottom = point.Y >= ClientSize.Height - ResizeBorder;
            if (left && top) message.Result = (IntPtr)HTTOPLEFT;
            else if (right && top) message.Result = (IntPtr)HTTOPRIGHT;
            else if (left && bottom) message.Result = (IntPtr)HTBOTTOMLEFT;
            else if (right && bottom) message.Result = (IntPtr)HTBOTTOMRIGHT;
            else if (left) message.Result = (IntPtr)HTLEFT;
            else if (right) message.Result = (IntPtr)HTRIGHT;
            else if (top) message.Result = (IntPtr)HTTOP;
            else if (bottom) message.Result = (IntPtr)HTBOTTOM;
        }

        private TabPage NewTab(string name)
        {
            TabPage page = new TabPage(name);
            page.BackColor = SystemColors.Control;
            page.Font = uiFont;
            page.AutoScroll = true;
            page.AutoScrollMinSize = new Size(790, 580);
            tabs.TabPages.Add(page);
            return page;
        }

        private Label PlainLabel(Control parent, string text, int x, int y, int width, int height, Font font)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y);
            label.Size = new Size(width, height);
            label.Font = font == null ? uiFont : font;
            parent.Controls.Add(label);
            return label;
        }

        private void AddPair(Control parent, string caption, out Label value, int y)
        {
            PlainLabel(parent, caption, 330, y, 150, 28, new Font("MS Sans Serif", 8F));
            value = PlainLabel(parent, "-", 480, y, 300, 40, new Font("MS Sans Serif", 8F));
        }

        private void BuildDashboardTab()
        {
            TabPage page = NewTab("Dashboard");
            Panel monitor = new Panel();
            monitor.Location = new Point(20, 22);
            monitor.Size = new Size(280, 250);
            monitor.BorderStyle = BorderStyle.Fixed3D;
            monitor.BackColor = SystemColors.ControlLight;
            page.Controls.Add(monitor);

            Panel screen = new Panel();
            screen.Location = new Point(16, 14);
            screen.Size = new Size(244, 190);
            screen.BorderStyle = BorderStyle.Fixed3D;
            screen.BackColor = SystemColors.ControlLightLight;
            monitor.Controls.Add(screen);
            PixelPictureBox dragon = new PixelPictureBox();
            dragon.Dock = DockStyle.Fill;
            dragon.Image = mascot;
            dragon.BackColor = SystemColors.ControlLightLight;
            dragon.Cursor = Cursors.Hand;
            dragon.AccessibleName = "Interactive MagicDragon mascot";
            dragon.AccessibleDescription = "Click the dragon to hear a random comment.";
            dragon.Click += delegate { ShowDragonSpeech(); };
            screen.Controls.Add(dragon);
            Panel stand = new Panel();
            stand.Location = new Point(92, 207);
            stand.Size = new Size(94, 12);
            stand.BorderStyle = BorderStyle.Fixed3D;
            monitor.Controls.Add(stand);
            Panel foot = new Panel();
            foot.Location = new Point(60, 225);
            foot.Size = new Size(160, 12);
            foot.BorderStyle = BorderStyle.Fixed3D;
            monitor.Controls.Add(foot);

            Label slogan = PlainLabel(page, "Safer Worlds.\r\nHappier Adventures.", 25, 280, 270, 52,
                new Font("MS Sans Serif", 8F, FontStyle.Bold));
            slogan.TextAlign = ContentAlignment.MiddleCenter;

            dragonSpeechBubble = new DragonSpeechBubble();
            dragonSpeechBubble.Location = new Point(180, 12);
            dragonSpeechBubble.Size = new Size(220, 70);
            dragonSpeechBubble.Visible = false;
            dragonSpeechBubble.Cursor = Cursors.Hand;
            dragonSpeechBubble.Click += delegate { HideDragonSpeech(); };
            page.Controls.Add(dragonSpeechBubble);

            AddPair(page, "Watcher:", out watcherValue, 30);
            AddPair(page, "Status:", out watcherStatusValue, 73);
            AddPair(page, "Last scrutiny:", out lastCheckValue, 116);
            AddPair(page, "Protected saves:", out protectedValue, 159);
            AddPair(page, "Latest result:", out latestValue, 202);
            AddPair(page, "Backup folder:", out backupValue, 245);

            GroupBox actions = new GroupBox();
            actions.Text = "World Quick Actions";
            actions.Location = new Point(20, 340);
            actions.Size = new Size(760, 105);
            actions.Font = new Font("MS Sans Serif", 8F, FontStyle.Regular);
            page.Controls.Add(actions);

            checkNowButton = ClassicButton("Check Worlds", 125, 38);
            checkNowButton.Location = new Point(14, 25);
            checkNowButton.Click += delegate { ShowWorldCheckChoices(); };
            actions.Controls.Add(checkNowButton);
            Button choose = ClassicButton("Choose Worlds...", 145, 38);
            choose.Location = new Point(149, 25);
            choose.Click += delegate { NavigateToTab(1); };
            actions.Controls.Add(choose);
            Button openBackups = ClassicButton("Open Backups", 140, 38);
            openBackups.Location = new Point(304, 25);
            openBackups.Click += delegate { RefreshBackupList(); NavigateToTab(3); };
            actions.Controls.Add(openBackups);
            Button viewLog = ClassicButton("View Log", 120, 38);
            viewLog.Location = new Point(454, 25);
            viewLog.Click += delegate { RefreshLog(); NavigateToTab(5); };
            actions.Controls.Add(viewLog);
            liveCheckButton = ClassicButton("● Live World Check", 155, 38);
            liveCheckButton.ForeColor = Color.Red;
            liveCheckButton.Location = new Point(584, 25);
            liveCheckButton.Click += delegate { StartScrutiny(false, true, "World"); };
            actions.Controls.Add(liveCheckButton);
            dashboardAuto = new CheckBox();
            dashboardAuto.Location = new Point(16, 70);
            dashboardAuto.Size = new Size(420, 28);
            dashboardAuto.Text = "Automatically scrutinize new worlds";
            dashboardAuto.CheckedChanged += DashboardAutoChanged;
            actions.Controls.Add(dashboardAuto);

            GroupBox characterActions = new GroupBox();
            characterActions.Text = "Character Quick Actions";
            characterActions.Location = new Point(20, 455);
            characterActions.Size = new Size(760, 82);
            characterActions.Font = new Font("MS Sans Serif", 8F, FontStyle.Regular);
            page.Controls.Add(characterActions);
            characterCheckButton = ClassicButton("Check Characters", 150, 38);
            characterCheckButton.Location = new Point(14, 25);
            characterCheckButton.Click += delegate { ShowCharacterCheckChoices(); };
            characterActions.Controls.Add(characterCheckButton);
            Button chooseCharacters = ClassicButton("Choose Characters...", 220, 38);
            chooseCharacters.Location = new Point(174, 25);
            chooseCharacters.Click += delegate { NavigateToTab(2); };
            characterActions.Controls.Add(chooseCharacters);
            liveCharacterCheckButton = ClassicButton("● Live Character Check", 185, 38);
            liveCharacterCheckButton.ForeColor = Color.Red;
            liveCharacterCheckButton.Location = new Point(404, 25);
            liveCharacterCheckButton.Click += delegate { StartScrutiny(false, true, "Character"); };
            characterActions.Controls.Add(liveCharacterCheckButton);
            PlainLabel(characterActions, "Checks the latest selected character.", 600, 25, 140, 42,
                new Font("MS Sans Serif", 7F));

            GroupBox statusGroup = new GroupBox();
            statusGroup.Text = "Status";
            statusGroup.Location = new Point(20, 552);
            statusGroup.Size = new Size(760, 105);
            page.Controls.Add(statusGroup);
            dashboardStatus = new TextBox();
            dashboardStatus.Location = new Point(14, 22);
            dashboardStatus.Size = new Size(728, 66);
            dashboardStatus.Multiline = true;
            dashboardStatus.ReadOnly = true;
            dashboardStatus.ScrollBars = ScrollBars.Vertical;
            dashboardStatus.BackColor = SystemColors.Window;
            statusGroup.Controls.Add(dashboardStatus);
        }

        private void ShowDragonSpeech()
        {
            if (dragonSpeechBubble == null || DragonComments.Length == 0) return;
            int selected;
            do { selected = dragonCommentRandom.Next(DragonComments.Length); }
            while (DragonComments.Length > 1 && selected == previousDragonComment);
            previousDragonComment = selected;
            dragonSpeechBubble.Message = DragonComments[selected];
            dragonSpeechBubble.Visible = true;
            dragonSpeechBubble.BringToFront();

            if (dragonSpeechTimer == null)
            {
                dragonSpeechTimer = new System.Windows.Forms.Timer();
                dragonSpeechTimer.Interval = 4000;
                dragonSpeechTimer.Tick += delegate { HideDragonSpeech(); };
            }
            dragonSpeechTimer.Stop();
            dragonSpeechTimer.Start();
        }

        private void HideDragonSpeech()
        {
            if (dragonSpeechTimer != null) dragonSpeechTimer.Stop();
            if (dragonSpeechBubble != null) dragonSpeechBubble.Visible = false;
        }

        private void BuildWorldsTab()
        {
            TabPage page = NewTab("Worlds");
            PlainLabel(page, "Select the worlds MagicDragon Safeguardian should scrutinize:", 26, 28, 620, 30,
                new Font("MS Sans Serif", 8F, FontStyle.Bold));
            Button back = ClassicButton("< Back", 100, 32);
            back.Location = new Point(660, 14);
            back.Click += delegate { GoBack(); };
            page.Controls.Add(back);
            worldList = new CheckedListBox();
            worldList.Location = new Point(20, 70);
            worldList.Size = new Size(450, 367);
            worldList.CheckOnClick = true;
            worldList.Font = new Font("MS Sans Serif", 8F);
            worldList.SelectedIndexChanged += delegate
            {
                int index = worldList.SelectedIndex;
                worldPathLabel.Text = index >= 0 && index < worlds.Count ? worlds[index].Folder : "Select a world to view its folder.";
            };
            page.Controls.Add(worldList);

            GroupBox tools = new GroupBox();
            tools.Text = "Selection";
            tools.Location = new Point(490, 70);
            tools.Size = new Size(270, 300);
            page.Controls.Add(tools);
            Button all = ClassicButton("Select All", 210, 42);
            all.Location = new Point(22, 34);
            all.Click += delegate { SetEveryWorld(true); };
            tools.Controls.Add(all);
            Button none = ClassicButton("Clear All", 210, 42);
            none.Location = new Point(22, 88);
            none.Click += delegate { SetEveryWorld(false); };
            tools.Controls.Add(none);
            Button refresh = ClassicButton("Refresh List", 210, 42);
            refresh.Location = new Point(22, 142);
            refresh.Click += delegate { RefreshWorldDiscovery(); };
            tools.Controls.Add(refresh);
            worldsAuto = new CheckBox();
            worldsAuto.Location = new Point(22, 190);
            worldsAuto.Size = new Size(215, 44);
            worldsAuto.Text = "Automatically include worlds created later";
            tools.Controls.Add(worldsAuto);
            Button save = ClassicButton("Save Selection", 210, 42);
            save.Location = new Point(22, 244);
            save.Click += delegate { SaveWorldSelection(); };
            tools.Controls.Add(save);

            GroupBox pathGroup = new GroupBox();
            pathGroup.Text = "World folder";
            pathGroup.Location = new Point(20, 452);
            pathGroup.Size = new Size(740, 80);
            page.Controls.Add(pathGroup);
            worldPathLabel = PlainLabel(pathGroup, "Select a world to view its folder.", 16, 28, 705, 42, uiFont);
        }

        private void BuildCharactersTab()
        {
            TabPage page = NewTab("Characters");
            PlainLabel(page, "Select the characters MagicDragon Safeguardian should scrutinize:", 26, 28, 620, 30,
                new Font("MS Sans Serif", 8F, FontStyle.Bold));
            Button back = ClassicButton("< Back", 100, 32);
            back.Location = new Point(660, 14);
            back.Click += delegate { GoBack(); };
            page.Controls.Add(back);
            characterList = new CheckedListBox();
            characterList.Location = new Point(20, 70);
            characterList.Size = new Size(450, 367);
            characterList.CheckOnClick = true;
            characterList.Font = new Font("MS Sans Serif", 8F);
            characterList.SelectedIndexChanged += delegate
            {
                int index = characterList.SelectedIndex;
                characterPathLabel.Text = index >= 0 && index < characters.Count ? characters[index].File : "Select a character to view its file.";
            };
            page.Controls.Add(characterList);

            GroupBox tools = new GroupBox();
            tools.Text = "Selection";
            tools.Location = new Point(490, 70);
            tools.Size = new Size(270, 300);
            page.Controls.Add(tools);
            Button all = ClassicButton("Select All", 210, 42);
            all.Location = new Point(22, 34);
            all.Click += delegate { SetEveryCharacter(true); };
            tools.Controls.Add(all);
            Button none = ClassicButton("Clear All", 210, 42);
            none.Location = new Point(22, 88);
            none.Click += delegate { SetEveryCharacter(false); };
            tools.Controls.Add(none);
            Button refresh = ClassicButton("Refresh List", 210, 42);
            refresh.Location = new Point(22, 142);
            refresh.Click += delegate { RefreshCharacterDiscovery(); };
            tools.Controls.Add(refresh);
            charactersAuto = new CheckBox();
            charactersAuto.Location = new Point(22, 190);
            charactersAuto.Size = new Size(215, 44);
            charactersAuto.Text = "Automatically include characters created later";
            tools.Controls.Add(charactersAuto);
            Button save = ClassicButton("Save Selection", 210, 42);
            save.Location = new Point(22, 244);
            save.Click += delegate { SaveCharacterSelection(); };
            tools.Controls.Add(save);

            GroupBox pathGroup = new GroupBox();
            pathGroup.Text = "Character file";
            pathGroup.Location = new Point(20, 452);
            pathGroup.Size = new Size(740, 80);
            page.Controls.Add(pathGroup);
            characterPathLabel = PlainLabel(pathGroup, "Select a character to view its file.", 16, 28, 705, 42, uiFont);
        }

        private void BuildBackupsTab()
        {
            TabPage page = NewTab("Backups");
            PlainLabel(page, "Verified and quarantined independent snapshots", 26, 26, 650, 30,
                new Font("MS Sans Serif", 8F, FontStyle.Bold));
            Button back = ClassicButton("< Back", 100, 32);
            back.Location = new Point(680, 14);
            back.Click += delegate { GoBack(); };
            page.Controls.Add(back);
            backupList = new ListView();
            backupList.Location = new Point(20, 55);
            backupList.Size = new Size(760, 420);
            backupList.View = View.Details;
            backupList.FullRowSelect = true;
            backupList.GridLines = true;
            backupList.Columns.Add("Archive", 300);
            backupList.Columns.Add("Content", 90);
            backupList.Columns.Add("Kind", 100);
            backupList.Columns.Add("Modified", 160);
            backupList.Columns.Add("Size", 90);
            page.Controls.Add(backupList);
            Button open = ClassicButton("Open Backup Folder", 190, 42);
            open.Location = new Point(20, 490);
            open.Click += delegate { OpenFolder(config.BackupRoot); };
            page.Controls.Add(open);
            Button verify = ClassicButton("Verify Selected ZIP", 190, 42);
            verify.Location = new Point(224, 490);
            verify.Click += delegate { VerifySelectedBackup(); };
            page.Controls.Add(verify);
            Button refresh = ClassicButton("Refresh", 130, 42);
            refresh.Location = new Point(428, 490);
            refresh.Click += delegate { RefreshBackupList(); };
            page.Controls.Add(refresh);
            Button restore = ClassicButton("Restore Selected Backup", 200, 42);
            restore.Location = new Point(572, 490);
            restore.Click += delegate { ShowRestoreWizard(); };
            page.Controls.Add(restore);
        }

        private void BuildSettingsTab()
        {
            TabPage page = NewTab("Settings");
            PlainLabel(page, "Application settings", 26, 28, 500, 30,
                new Font("MS Sans Serif", 8F, FontStyle.Bold));
            Button back = ClassicButton("< Back", 100, 32);
            back.Location = new Point(680, 14);
            back.Click += delegate { GoBack(); };
            page.Controls.Add(back);

            GroupBox paths = new GroupBox();
            paths.Text = String.Empty;
            paths.Location = new Point(20, 70);
            paths.Size = new Size(760, 100);
            page.Controls.Add(paths);
            Label pathsCaption = PlainLabel(page, "Folders", 34, 62, 78, 20,
                new Font("MS Sans Serif", 8F, FontStyle.Regular));
            pathsCaption.BackColor = SystemColors.Control;
            pathsCaption.BringToFront();
            PlainLabel(paths, "Backups folder:", 18, 35, 150, 26, uiFont);
            backupRootBox = new TextBox();
            backupRootBox.Location = new Point(175, 32);
            backupRootBox.Size = new Size(480, 27);
            backupRootBox.Validated += SettingsValueChanged;
            paths.Controls.Add(backupRootBox);
            Button browseBackup = ClassicButton("Browse...", 82, 30);
            browseBackup.Location = new Point(665, 30);
            browseBackup.Click += delegate { BrowseInto(backupRootBox); };
            paths.Controls.Add(browseBackup);
            PlainLabel(paths, "World and character backups are stored independently here.", 175, 67, 540, 26,
                new Font("MS Sans Serif", 7F));

            GroupBox retention = new GroupBox();
            retention.Text = "Retention and timing";
            retention.Location = new Point(20, 178);
            retention.Size = new Size(760, 232);
            page.Controls.Add(retention);
            PlainLabel(retention, "GOOD backups kept per world:", 24, 30, 280, 28, uiFont);
            goodRetention = MakeNumber(retention, 330, 26, 1, 999);
            goodRetention.ValueChanged += SettingsValueChanged;
            PlainLabel(retention, "QUARANTINE copies kept per world:", 24, 66, 300, 28, uiFont);
            quarantineRetention = MakeNumber(retention, 330, 62, 1, 999);
            quarantineRetention.ValueChanged += SettingsValueChanged;
            PlainLabel(retention, "BackupAtSave kept per world/character:", 24, 102, 310, 28, uiFont);
            backupAtSaveRetention = MakeNumber(retention, 330, 98, 1, 50);
            backupAtSaveRetention.ValueChanged += BackupAtSaveRetentionChanged;
            backupAtSaveRecommended = PlainLabel(retention, "(recommended)", 410, 102, 180, 28,
                new Font("MS Sans Serif", 7F, FontStyle.Regular));
            PlainLabel(retention, "Warn below free disk space:", 24, 138, 280, 28, uiFont);
            minimumFreeSpace = MakeNumber(retention, 330, 134, 128, 1048576);
            minimumFreeSpace.ValueChanged += SettingsValueChanged;
            PlainLabel(retention, "MB", 410, 138, 100, 28, uiFont);
            PlainLabel(retention, "File-settle time after Valheim closes:", 24, 174, 310, 28, uiFont);
            settleSeconds = MakeNumber(retention, 330, 170, 2, 120);
            settleSeconds.ValueChanged += SettingsValueChanged;
            PlainLabel(retention, "seconds", 410, 174, 100, 28, uiFont);
            PlainLabel(retention, "Files must remain unchanged for this long before validation and backup begin.", 24, 206, 700, 20,
                new Font("MS Sans Serif", 7F));

            GroupBox automation = new GroupBox();
            automation.Text = "Valheim launch";
            automation.Location = new Point(20, 418);
            automation.Size = new Size(760, 105);
            page.Controls.Add(automation);
            showOnValheimLaunch = new CheckBox();
            showOnValheimLaunch.Location = new Point(24, 27);
            showOnValheimLaunch.Size = new Size(430, 26);
            showOnValheimLaunch.Text = "Launch MagicDragon Safeguardian when Windows starts";
            showOnValheimLaunch.CheckedChanged += LaunchPreferenceChanged;
            automation.Controls.Add(showOnValheimLaunch);
            PlainLabel(automation, "Changes are saved automatically. The guardian remains active after Valheim closes.", 48, 58, 680, 24,
                new Font("MS Sans Serif", 7F));

            Button save = ClassicButton("Save Settings", 170, 42);
            save.Location = new Point(20, 535);
            save.Click += delegate { SaveSettings(); };
            page.Controls.Add(save);
            Button recovery = ClassicButton("Open Recovery Folder", 190, 42);
            recovery.Location = new Point(204, 535);
            recovery.Click += delegate { OpenFolder(AppPaths.RecoveryRoot); };
            page.Controls.Add(recovery);
            page.AutoScrollMinSize = new Size(790, 580);
        }

        private NumericUpDown MakeNumber(Control parent, int x, int y, int min, int max)
        {
            NumericUpDown number = new NumericUpDown();
            number.Location = new Point(x, y);
            number.Size = new Size(70, 28);
            number.Minimum = min;
            number.Maximum = max;
            parent.Controls.Add(number);
            return number;
        }

        private void BuildLogsTab()
        {
            TabPage page = NewTab("Logs");
            PlainLabel(page, "Safeguardian activity log", 26, 26, 600, 30,
                new Font("MS Sans Serif", 8F, FontStyle.Bold));
            Button back = ClassicButton("< Back", 100, 32);
            back.Location = new Point(680, 14);
            back.Click += delegate { GoBack(); };
            page.Controls.Add(back);
            logBox = new TextBox();
            logBox.Location = new Point(20, 52);
            logBox.Size = new Size(760, 430);
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.WordWrap = false;
            logBox.ScrollBars = ScrollBars.Both;
            logBox.BackColor = SystemColors.Window;
            logBox.Font = new Font("MS Sans Serif", 9F);
            page.Controls.Add(logBox);
            Button refresh = ClassicButton("Refresh", 130, 42);
            refresh.Location = new Point(20, 495);
            refresh.Click += delegate { RefreshLog(); };
            page.Controls.Add(refresh);
            Button openFolder = ClassicButton("Open Log Folder", 170, 42);
            openFolder.Location = new Point(164, 495);
            openFolder.Click += delegate { OpenFolder(Path.GetDirectoryName(AppPaths.LogFile)); };
            page.Controls.Add(openFolder);
            Button diagnostics = ClassicButton("Export Diagnostics ZIP", 205, 42);
            diagnostics.Location = new Point(348, 495);
            diagnostics.Click += delegate { ExportDiagnostics(); };
            page.Controls.Add(diagnostics);
        }

        private void SetupMonitor()
        {
            tray = new NotifyIcon();
            tray.Icon = Icon;
            tray.Text = "MagicDragon Safeguardian";
            tray.Visible = true;
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Font = uiFont;
            menu.Items.Add("Open Dashboard", null, delegate { ShowMain(); });
            menu.Items.Add("Check Worlds...", null, delegate { ShowMain(); ShowWorldCheckChoices(); });
            menu.Items.Add("Check Characters...", null, delegate { ShowMain(); ShowCharacterCheckChoices(); });
            menu.Items.Add("Live World Check", null, delegate { ShowMain(); StartScrutiny(false, true, "World"); });
            menu.Items.Add("Live Character Check", null, delegate { ShowMain(); StartScrutiny(false, true, "Character"); });
            menu.Items.Add("Choose Worlds", null, delegate { ShowMain(); NavigateToTab(1); });
            menu.Items.Add("Choose Characters...", null, delegate { ShowMain(); NavigateToTab(2); });
            menu.Items.Add("Open Backups", null, delegate { ShowMain(); RefreshBackupList(); NavigateToTab(3); });
            menu.Items.Add("Settings", null, delegate { ShowMain(); NavigateToTab(4); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { exitRequested = true; Close(); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowMain(); };

            valheimWasRunning = IsValheimRunning();
            ResetActiveWorldSession(valheimWasRunning);
            processTimer = new System.Windows.Forms.Timer();
            processTimer.Interval = Math.Max(2000, config.PollSeconds * 1000);
            processTimer.Tick += ProcessTimerTick;
            processTimer.Start();
        }

        private void ProcessTimerTick(object sender, EventArgs e)
        {
            try
            {
                config = AppConfig.Load();
                if (processTimer != null) processTimer.Interval = Math.Max(2000, config.PollSeconds * 1000);
                bool running = IsValheimRunning();
                if (!valheimWasRunning && running)
                {
                    ResetActiveWorldSession(true);
                    if (config.ShowWindowWhenValheimStarts) ShowMain();
                }
                if (running) UpdateActiveWorldSession();
                TryStartBackupAtSave();
                if (valheimWasRunning && !running)
                {
                    CaptureFinalSaveChanges();
                    TryStartBackupAtSave();
                    ResetActiveWorldSession(false);
                    afterExitPending = true;
                    if (!checkRunning && !atSaveBackupRunning && !HasPendingAtSaveBackup())
                    {
                        afterExitPending = false;
                        StartScrutiny(true);
                    }
                }
                valheimWasRunning = running;
                UpdateCheckButtonState();
                RefreshDashboardOnly();
            }
            catch (Exception ex)
            {
                WriteMonitorLog("Monitor tick recovered from an unexpected error: " + ex.Message, "ERROR");
                valheimWasRunning = IsValheimRunning();
                UpdateCheckButtonState();
            }
        }

        private static void WriteMonitorLog(string message, string level)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.LogFile));
                File.AppendAllText(AppPaths.LogFile, DateTime.UtcNow.ToString("u") + " [" + level + "] " +
                    message + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        private void UpdateCheckButtonState()
        {
            if (checkNowButton != null) checkNowButton.Enabled = true;
            if (liveCheckButton != null) liveCheckButton.Enabled = true;
            if (characterCheckButton != null) characterCheckButton.Enabled = true;
            if (liveCharacterCheckButton != null) liveCharacterCheckButton.Enabled = true;
        }

        private DateTime GetValheimProcessStartUtc()
        {
            DateTime earliest = DateTime.MaxValue;
            try
            {
                foreach (Process process in Process.GetProcessesByName("valheim"))
                {
                    try
                    {
                        DateTime started = process.StartTime.ToUniversalTime();
                        if (started < earliest) earliest = started;
                    }
                    catch { }
                    finally { process.Dispose(); }
                }
            }
            catch { }
            return earliest == DateTime.MaxValue ? DateTime.MinValue : earliest;
        }

        private void ResetActiveWorldSession(bool running)
        {
            sessionWorldFingerprints.Clear();
            sessionCharacterFingerprints.Clear();
            activeWorldPath = String.Empty;
            activeWorldName = String.Empty;
            activeWorldSaveUtc = DateTime.MinValue;
            activeWorldSettledAfterUtc = DateTime.MinValue;
            activeCharacterPath = String.Empty;
            activeCharacterName = String.Empty;
            activeCharacterSaveUtc = DateTime.MinValue;
            activeCharacterSettledAfterUtc = DateTime.MinValue;
            valheimProcessStartedUtc = running ? GetValheimProcessStartUtc() : DateTime.MinValue;
            worldSessionStartedUtc = DateTime.UtcNow;
            lastObservedLogLength = 0;
            lastSceneEventName = String.Empty;
            lastSceneEventLine = String.Empty;
            sceneTransitionChanged = false;
            sceneDetectedUtc = DateTime.MinValue;
            currentGameScene = running ? ReadValheimScene(true) : "Closed";
            if (running && currentGameScene.Equals("World", StringComparison.OrdinalIgnoreCase) &&
                sceneDetectedUtc != DateTime.MinValue)
                worldSessionStartedUtc = sceneDetectedUtc;
            if (running)
            {
                foreach (WorldInfo world in WorldDiscovery.Discover(AppConfig.Load()))
                    sessionWorldFingerprints[world.Folder] = GetWorldSaveFingerprint(world.Folder);
                foreach (CharacterInfo character in CharacterDiscovery.Discover(AppConfig.Load()).Where(
                    delegate(CharacterInfo item) { return item.Selected; }))
                    sessionCharacterFingerprints[character.File] = GetCharacterSaveFingerprint(character.File);
            }
            SaveActiveWorldSession(running);
        }

        private void UpdateActiveWorldSession()
        {
            if (!IsValheimRunning()) return;
            DateTime processStart = GetValheimProcessStartUtc();
            if (processStart != DateTime.MinValue && (valheimProcessStartedUtc == DateTime.MinValue ||
                Math.Abs((processStart - valheimProcessStartedUtc).TotalSeconds) > 2))
            {
                ResetActiveWorldSession(true);
            }

            string detectedScene = ReadValheimScene(false);
            bool explicitMenuTransition = sceneTransitionChanged &&
                detectedScene.Equals("Menu", StringComparison.OrdinalIgnoreCase);

            if (sceneTransitionChanged)
            {
                currentGameScene = detectedScene;
                if (detectedScene.Equals("Menu", StringComparison.OrdinalIgnoreCase))
                {
                    ClearActiveWorld();
                }
                else if (detectedScene.Equals("World", StringComparison.OrdinalIgnoreCase))
                {
                    // A new world-scene transition may be X -> menu -> Y. Forget X now and
                    // require an actual save from the newly entered world before live checking.
                    ClearActiveWorld();
                    worldSessionStartedUtc = sceneDetectedUtc == DateTime.MinValue
                        ? DateTime.UtcNow : sceneDetectedUtc;
                }
            }

            WorldInfo newestChangedWorld = null;
            DateTime newestWriteUtc = DateTime.MinValue;
            WorldInfo newestBackupWorld = null;
            DateTime newestBackupWriteUtc = DateTime.MinValue;
            foreach (WorldInfo world in WorldDiscovery.Discover(AppConfig.Load()))
            {
                string fingerprint = GetWorldSaveFingerprint(world.Folder);
                string previous;
                if (!sessionWorldFingerprints.TryGetValue(world.Folder, out previous))
                {
                    sessionWorldFingerprints[world.Folder] = fingerprint;
                    DateTime createdWrite = GetLatestWorldSaveWriteUtc(world.Folder);
                    if (world.Selected &&
                        !explicitMenuTransition &&
                        createdWrite > newestBackupWriteUtc)
                    {
                        newestBackupWorld = world;
                        newestBackupWriteUtc = createdWrite;
                    }
                    if (!explicitMenuTransition &&
                        createdWrite > newestWriteUtc)
                    {
                        newestChangedWorld = world;
                        newestWriteUtc = createdWrite;
                    }
                    continue;
                }
                if (String.Equals(previous, fingerprint, StringComparison.Ordinal)) continue;
                sessionWorldFingerprints[world.Folder] = fingerprint;
                DateTime writeUtc = GetLatestWorldSaveWriteUtc(world.Folder);
                if (world.Selected &&
                    !explicitMenuTransition &&
                    writeUtc > newestBackupWriteUtc)
                {
                    newestBackupWorld = world;
                    newestBackupWriteUtc = writeUtc;
                }
                if (!explicitMenuTransition &&
                    writeUtc > newestWriteUtc)
                {
                    newestChangedWorld = world;
                    newestWriteUtc = writeUtc;
                }
            }

            if (newestChangedWorld != null)
            {
                // A real save fingerprint change is authoritative even when Player.log failed to
                // append its World-scene line and still reports an older Menu event. Only a Menu
                // transition observed during this poll blocks the save from becoming a live target.
                currentGameScene = "World";
                activeWorldPath = newestChangedWorld.Folder;
                activeWorldName = newestChangedWorld.DisplayName;
                activeWorldSaveUtc = newestWriteUtc;
                activeWorldSettledAfterUtc = newestWriteUtc.AddSeconds(Math.Max(2, config.SettleSeconds));
                WriteMonitorLog("Current-session world save detected: " + activeWorldName + " at " + activeWorldPath, "INFO");
            }
            else
            {
                if (String.Equals(currentGameScene, "Unknown", StringComparison.OrdinalIgnoreCase) &&
                    !String.Equals(detectedScene, "Unknown", StringComparison.OrdinalIgnoreCase))
                    currentGameScene = detectedScene;
                if (currentGameScene.Equals("Menu", StringComparison.OrdinalIgnoreCase))
                {
                    ClearActiveWorld();
                }
            }
            if (newestBackupWorld != null)
                ScheduleWorldBackupAtSave(newestBackupWorld.Folder,
                    GetWorldSaveFingerprint(newestBackupWorld.Folder), newestBackupWriteUtc);
            UpdateActiveCharacterSession();
            SaveActiveWorldSession(true);
        }

        private void ClearActiveWorld()
        {
            activeWorldPath = String.Empty;
            activeWorldName = String.Empty;
            activeWorldSaveUtc = DateTime.MinValue;
            activeWorldSettledAfterUtc = DateTime.MinValue;
            activeCharacterPath = String.Empty;
            activeCharacterName = String.Empty;
            activeCharacterSaveUtc = DateTime.MinValue;
            activeCharacterSettledAfterUtc = DateTime.MinValue;
        }

        private void UpdateActiveCharacterSession()
        {
            CharacterInfo newestCharacter = null;
            string newestFingerprint = String.Empty;
            DateTime newestWriteUtc = DateTime.MinValue;
            foreach (CharacterInfo character in CharacterDiscovery.Discover(AppConfig.Load()).Where(
                delegate(CharacterInfo item) { return item.Selected; }))
            {
                string fingerprint = GetCharacterSaveFingerprint(character.File);
                string previous;
                if (!sessionCharacterFingerprints.TryGetValue(character.File, out previous))
                {
                    sessionCharacterFingerprints[character.File] = fingerprint;
                    continue;
                }
                if (String.Equals(previous, fingerprint, StringComparison.Ordinal)) continue;
                sessionCharacterFingerprints[character.File] = fingerprint;
                DateTime written = GetLatestCharacterSaveWriteUtc(character.File);
                if (currentGameScene.Equals("World", StringComparison.OrdinalIgnoreCase) &&
                    written > newestWriteUtc)
                {
                    newestCharacter = character;
                    newestFingerprint = fingerprint;
                    newestWriteUtc = written;
                }
            }
            if (newestCharacter != null)
            {
                activeCharacterPath = newestCharacter.File;
                activeCharacterName = newestCharacter.DisplayName;
                activeCharacterSaveUtc = newestWriteUtc;
                activeCharacterSettledAfterUtc = newestWriteUtc.AddSeconds(Math.Max(2, config.SettleSeconds));
                WriteMonitorLog("Current-session character save detected: " + activeCharacterName + " at " + activeCharacterPath, "INFO");
                ScheduleCharacterBackupAtSave(newestCharacter.File, newestFingerprint, newestWriteUtc);
            }
        }

        private void CaptureFinalSaveChanges()
        {
            WorldInfo newestWorld = null;
            DateTime newestWorldWrite = DateTime.MinValue;
            foreach (WorldInfo world in WorldDiscovery.Discover(AppConfig.Load()).Where(
                delegate(WorldInfo item) { return item.Selected; }))
            {
                string fingerprint = GetWorldSaveFingerprint(world.Folder);
                string previous;
                if (sessionWorldFingerprints.TryGetValue(world.Folder, out previous) &&
                    !String.Equals(previous, fingerprint, StringComparison.Ordinal))
                {
                    DateTime written = GetLatestWorldSaveWriteUtc(world.Folder);
                    if (written > newestWorldWrite)
                    {
                        newestWorld = world;
                        newestWorldWrite = written;
                    }
                }
                sessionWorldFingerprints[world.Folder] = fingerprint;
            }
            if (newestWorld != null)
                ScheduleWorldBackupAtSave(newestWorld.Folder,
                    GetWorldSaveFingerprint(newestWorld.Folder), newestWorldWrite);

            CharacterInfo newestCharacter = null;
            string newestCharacterFingerprint = String.Empty;
            DateTime newestCharacterWrite = DateTime.MinValue;
            foreach (CharacterInfo character in CharacterDiscovery.Discover(AppConfig.Load()).Where(
                delegate(CharacterInfo item) { return item.Selected; }))
            {
                string fingerprint = GetCharacterSaveFingerprint(character.File);
                string previous;
                if (sessionCharacterFingerprints.TryGetValue(character.File, out previous) &&
                    !String.Equals(previous, fingerprint, StringComparison.Ordinal))
                {
                    DateTime written = GetLatestCharacterSaveWriteUtc(character.File);
                    if (written > newestCharacterWrite)
                    {
                        newestCharacter = character;
                        newestCharacterFingerprint = fingerprint;
                        newestCharacterWrite = written;
                    }
                }
                sessionCharacterFingerprints[character.File] = fingerprint;
            }
            if (newestCharacter != null)
                ScheduleCharacterBackupAtSave(newestCharacter.File,
                    newestCharacterFingerprint, newestCharacterWrite);
        }

        private void ScheduleWorldBackupAtSave(string path, string fingerprint, DateTime writtenUtc)
        {
            pendingAtSaveWorldPath = path;
            pendingAtSaveWorldFingerprint = fingerprint;
            DateTime baseTime = writtenUtc > DateTime.UtcNow ? DateTime.UtcNow : writtenUtc;
            pendingAtSaveWorldDueUtc = baseTime.AddSeconds(Math.Max(2, config.SettleSeconds));
        }

        private void ScheduleCharacterBackupAtSave(string path, string fingerprint, DateTime writtenUtc)
        {
            pendingAtSaveCharacterPath = path;
            pendingAtSaveCharacterFingerprint = fingerprint;
            DateTime baseTime = writtenUtc > DateTime.UtcNow ? DateTime.UtcNow : writtenUtc;
            pendingAtSaveCharacterDueUtc = baseTime.AddSeconds(Math.Max(2, config.SettleSeconds));
        }

        private bool HasPendingAtSaveBackup()
        {
            return !String.IsNullOrWhiteSpace(pendingAtSaveWorldPath) ||
                !String.IsNullOrWhiteSpace(pendingAtSaveCharacterPath);
        }

        private void TryStartBackupAtSave()
        {
            if (atSaveBackupRunning || checkRunning) return;
            DateTime now = DateTime.UtcNow;
            bool worldJob = !String.IsNullOrWhiteSpace(pendingAtSaveWorldPath) &&
                now >= pendingAtSaveWorldDueUtc;
            bool characterJob = !worldJob && !String.IsNullOrWhiteSpace(pendingAtSaveCharacterPath) &&
                now >= pendingAtSaveCharacterDueUtc;
            if (!worldJob && !characterJob) return;

            string path = worldJob ? pendingAtSaveWorldPath : pendingAtSaveCharacterPath;
            bool stillSelected = worldJob
                ? WorldDiscovery.Discover(config).Any(delegate(WorldInfo item)
                    { return item.Selected && item.Folder.Equals(path, StringComparison.OrdinalIgnoreCase); })
                : CharacterDiscovery.Discover(config).Any(delegate(CharacterInfo item)
                    { return item.Selected && item.File.Equals(path, StringComparison.OrdinalIgnoreCase); });
            if (!stillSelected)
            {
                if (worldJob)
                {
                    pendingAtSaveWorldPath = String.Empty;
                    pendingAtSaveWorldFingerprint = String.Empty;
                    pendingAtSaveWorldDueUtc = DateTime.MinValue;
                }
                else
                {
                    pendingAtSaveCharacterPath = String.Empty;
                    pendingAtSaveCharacterFingerprint = String.Empty;
                    pendingAtSaveCharacterDueUtc = DateTime.MinValue;
                }
                return;
            }
            string expectedFingerprint = worldJob ? pendingAtSaveWorldFingerprint : pendingAtSaveCharacterFingerprint;
            string currentFingerprint = worldJob ? GetWorldSaveFingerprint(path) : GetCharacterSaveFingerprint(path);
            if (!String.Equals(expectedFingerprint, currentFingerprint, StringComparison.Ordinal))
            {
                if (worldJob)
                {
                    pendingAtSaveWorldFingerprint = currentFingerprint;
                    pendingAtSaveWorldDueUtc = now.AddSeconds(Math.Max(2, config.SettleSeconds));
                }
                else
                {
                    pendingAtSaveCharacterFingerprint = currentFingerprint;
                    pendingAtSaveCharacterDueUtc = now.AddSeconds(Math.Max(2, config.SettleSeconds));
                }
                return;
            }

            if (worldJob)
            {
                pendingAtSaveWorldPath = String.Empty;
                pendingAtSaveWorldFingerprint = String.Empty;
                pendingAtSaveWorldDueUtc = DateTime.MinValue;
            }
            else
            {
                pendingAtSaveCharacterPath = String.Empty;
                pendingAtSaveCharacterFingerprint = String.Empty;
                pendingAtSaveCharacterDueUtc = DateTime.MinValue;
            }

            atSaveBackupRunning = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                int exitCode = worldJob
                    ? EngineRunner.Run("BackupAtSaveWorld", null, path, null)
                    : EngineRunner.Run("BackupAtSaveCharacter", null, null, path);
                CompleteOnUiThread(delegate
                {
                    atSaveBackupRunning = false;
                    if (exitCode == 0) PreserveLiveTargetAfterBackup(worldJob, path);
                    RefreshBackupList();
                    RefreshLog();
                    if (tray != null)
                    {
                        tray.BalloonTipTitle = "MagicDragon BackupAtSave";
                        tray.BalloonTipText = exitCode == 0
                            ? (worldJob ? "The active world save was verified and backed up." : "The active character save was verified and backed up.")
                            : BackupAtSaveFailureMessage;
                        tray.BalloonTipIcon = exitCode == 0 ? ToolTipIcon.Info : ToolTipIcon.Warning;
                        tray.ShowBalloonTip(8000);
                    }
                    if (!IsValheimRunning() && afterExitPending && !HasPendingAtSaveBackup() && !checkRunning)
                    {
                        afterExitPending = false;
                        StartScrutiny(true);
                    }
                });
            });
        }

        private void PreserveLiveTargetAfterBackup(bool worldBackup, string sourcePath)
        {
            if (!monitorOwner || !IsValheimRunning() ||
                !currentGameScene.Equals("World", StringComparison.OrdinalIgnoreCase)) return;
            if (worldBackup)
            {
                WorldInfo world = WorldDiscovery.Discover(AppConfig.Load()).FirstOrDefault(
                    delegate(WorldInfo item) { return item.Folder.Equals(sourcePath, StringComparison.OrdinalIgnoreCase); });
                if (world == null) return;
                activeWorldPath = world.Folder;
                activeWorldName = world.DisplayName;
                activeWorldSaveUtc = GetLatestWorldSaveWriteUtc(world.Folder);
                activeWorldSettledAfterUtc = DateTime.UtcNow;
            }
            else
            {
                CharacterInfo character = CharacterDiscovery.Discover(AppConfig.Load()).FirstOrDefault(
                    delegate(CharacterInfo item) { return item.Selected && item.File.Equals(sourcePath, StringComparison.OrdinalIgnoreCase); });
                if (character == null) return;
                activeCharacterPath = character.File;
                activeCharacterName = character.DisplayName;
                activeCharacterSaveUtc = GetLatestCharacterSaveWriteUtc(character.File);
                activeCharacterSettledAfterUtc = DateTime.UtcNow;
            }
            SaveActiveWorldSession(true);
        }

        private void SaveActiveWorldSession(bool running)
        {
            ActiveWorldSessionState state = new ActiveWorldSessionState();
            state.ValheimRunning = running;
            state.ProcessStartedUtc = ToRoundTrip(valheimProcessStartedUtc);
            state.SessionStartedUtc = ToRoundTrip(worldSessionStartedUtc);
            state.GameScene = currentGameScene;
            state.SceneDetectedUtc = ToRoundTrip(sceneDetectedUtc);
            state.WorldSaveDetected = running && !String.IsNullOrWhiteSpace(activeWorldPath);
            state.ActiveWorldName = activeWorldName;
            state.ActiveWorldPath = activeWorldPath;
            state.LastSaveActivityUtc = ToRoundTrip(activeWorldSaveUtc);
            state.SettledAfterUtc = ToRoundTrip(activeWorldSettledAfterUtc);
            state.CharacterSaveDetected = running && !String.IsNullOrWhiteSpace(activeCharacterPath);
            state.ActiveCharacterName = activeCharacterName;
            state.ActiveCharacterPath = activeCharacterPath;
            state.LastCharacterSaveActivityUtc = ToRoundTrip(activeCharacterSaveUtc);
            state.CharacterSettledAfterUtc = ToRoundTrip(activeCharacterSettledAfterUtc);
            state.UpdatedUtc = DateTime.UtcNow.ToString("o");
            state.Save();
        }

        private static string ToRoundTrip(DateTime value)
        {
            return value == DateTime.MinValue ? String.Empty : value.ToUniversalTime().ToString("o");
        }

        private static bool TryParseUtc(string value, out DateTime result)
        {
            return DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out result);
        }

        private string ReadValheimScene(bool fromBeginning)
        {
            string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData", "LocalLow", "IronGate", "Valheim", "Player.log");
            try
            {
                if (!File.Exists(logPath)) return "Unknown";
                string logText;
                long tailStart;
                using (FileStream stream = new FileStream(logPath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    bool truncated = stream.Length < lastObservedLogLength;
                    if (truncated)
                    {
                        lastSceneEventName = String.Empty;
                        lastSceneEventLine = String.Empty;
                    }
                    lastObservedLogLength = stream.Length;
                    tailStart = Math.Max(0, stream.Length - (512 * 1024));
                    stream.Seek(tailStart, SeekOrigin.Begin);
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true, 4096, true))
                        logText = reader.ReadToEnd();
                }

                Regex scenePattern = new Regex(
                    @"(?m)^(?<line>.*?(?:Starting\s+to\s+load\s+scene|Loading\s+scene|LoadScene(?:Async)?)\s*:?\s*(?<scene>[^\s\(]+).*)$",
                    RegexOptions.IgnoreCase);
                MatchCollection matches = scenePattern.Matches(logText);
                if (matches.Count == 0)
                {
                    sceneTransitionChanged = false;
                    return currentGameScene;
                }

                Match last = matches[matches.Count - 1];
                string sceneName = Path.GetFileNameWithoutExtension(
                    last.Groups["scene"].Value.Trim().Trim('"', '\''));
                string eventLine = last.Groups["line"].Value.Trim();
                sceneTransitionChanged = fromBeginning ||
                    !String.Equals(eventLine, lastSceneEventLine, StringComparison.Ordinal) ||
                    !String.Equals(sceneName, lastSceneEventName, StringComparison.OrdinalIgnoreCase);
                if (sceneTransitionChanged)
                {
                    lastSceneEventName = sceneName;
                    lastSceneEventLine = eventLine;
                    DateTime parsedSceneUtc;
                    sceneDetectedUtc = TryParseValheimLogTimestamp(eventLine, out parsedSceneUtc)
                        ? parsedSceneUtc : DateTime.UtcNow;
                }
                if (sceneName.Equals("start", StringComparison.OrdinalIgnoreCase)) return "Menu";
                if (sceneName.Equals("main", StringComparison.OrdinalIgnoreCase)) return "World";
            }
            catch { sceneTransitionChanged = false; }
            return currentGameScene;
        }

        private static bool TryParseValheimLogTimestamp(string line, out DateTime utc)
        {
            utc = DateTime.MinValue;
            Match stamp = Regex.Match(line ?? String.Empty,
                @"^\s*(\d{1,2}/\d{1,2}/\d{4}\s+\d{1,2}:\d{2}:\d{2})");
            if (!stamp.Success) return false;
            DateTime local;
            if (!DateTime.TryParse(stamp.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AllowWhiteSpaces, out local) &&
                !DateTime.TryParse(stamp.Groups[1].Value, out local)) return false;
            utc = DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();
            return true;
        }

        private static string GetWorldSaveFingerprint(string folder)
        {
            StringBuilder value = new StringBuilder();
            try
            {
                foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                    .Where(IsWorldSaveFile).OrderBy(delegate(string path) { return path; }, StringComparer.OrdinalIgnoreCase))
                    AppendFileStamp(value, file);
            }
            catch { }
            return value.ToString();
        }

        private static DateTime GetLatestWorldSaveWriteUtc(string folder)
        {
            DateTime newest = DateTime.MinValue;
            try
            {
                foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Where(IsWorldSaveFile))
                {
                    DateTime written = File.GetLastWriteTimeUtc(file);
                    if (written > newest) newest = written;
                }
            }
            catch { }
            return newest;
        }

        private static string GetCharacterSaveFingerprint(string file)
        {
            StringBuilder value = new StringBuilder();
            AppendFileStamp(value, file);
            AppendFileStamp(value, file + ".old");
            AppendFileStamp(value, file + ".new");
            return value.ToString();
        }

        private static DateTime GetLatestCharacterSaveWriteUtc(string file)
        {
            DateTime newest = DateTime.MinValue;
            foreach (string candidate in new string[] { file, file + ".old", file + ".new" })
            {
                try
                {
                    if (!File.Exists(candidate)) continue;
                    DateTime written = File.GetLastWriteTimeUtc(candidate);
                    if (written > newest) newest = written;
                }
                catch { }
            }
            return newest;
        }

        private static bool IsWorldSaveFile(string path)
        {
            string name = Path.GetFileName(path);
            return name.StartsWith("_main.", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".chunk", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".cache", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("cache", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryGetActiveLiveWorld(out ActiveWorldSessionState state, out string reason)
        {
            state = null;
            reason = String.Empty;
            if (!IsValheimRunning())
            {
                reason = "The dragon says Valheim is not running. Use Check Worlds for an offline verification.";
                return false;
            }
            // Only the process that owns the monitor may mutate the shared live-session state.
            // A second visible window reads the monitor snapshot. Updating from that process
            // would start with empty baselines and could erase the active world after BackupAtSave.
            if (monitorOwner) UpdateActiveWorldSession();
            state = ActiveWorldSessionState.Load();
            DateTime currentProcessStart = GetValheimProcessStartUtc();
            DateTime recordedProcessStart;
            if (state == null || !state.ValheimRunning || !TryParseUtc(state.ProcessStartedUtc, out recordedProcessStart) ||
                currentProcessStart == DateTime.MinValue || Math.Abs((recordedProcessStart - currentProcessStart).TotalSeconds) > 2)
            {
                reason = LiveWorldSessionRequiredMessage;
                return false;
            }
            if (!state.WorldSaveDetected || String.IsNullOrWhiteSpace(state.ActiveWorldPath))
            {
                reason = LiveWorldSessionRequiredMessage;
                return false;
            }
            if (!String.Equals(state.GameScene, "World", StringComparison.OrdinalIgnoreCase))
            {
                reason = LiveWorldSessionRequiredMessage;
                return false;
            }
            if (!Directory.Exists(state.ActiveWorldPath))
            {
                reason = "The world detected for this session is no longer available at:\r\n" + state.ActiveWorldPath;
                return false;
            }
            DateTime settledAfter;
            if (TryParseUtc(state.SettledAfterUtc, out settledAfter) && DateTime.UtcNow < settledAfter.ToUniversalTime())
            {
                int seconds = Math.Max(1, (int)Math.Ceiling((settledAfter.ToUniversalTime() - DateTime.UtcNow).TotalSeconds));
                reason = "A save was detected for " + state.ActiveWorldName + ", but its files are still settling. Wait about " + seconds + " second(s), then retry.";
                return false;
            }
            return true;
        }

        private bool TryGetActiveLiveCharacter(out ActiveWorldSessionState state, out string reason)
        {
            state = null;
            reason = String.Empty;
            if (!IsValheimRunning())
            {
                reason = "The dragon says Valheim is not running. Use Check Characters for an offline verification.";
                return false;
            }
            if (monitorOwner) UpdateActiveWorldSession();
            state = ActiveWorldSessionState.Load();
            DateTime currentProcessStart = GetValheimProcessStartUtc();
            DateTime recordedProcessStart;
            if (state == null || !state.ValheimRunning ||
                !TryParseUtc(state.ProcessStartedUtc, out recordedProcessStart) ||
                currentProcessStart == DateTime.MinValue ||
                Math.Abs((recordedProcessStart - currentProcessStart).TotalSeconds) > 2 ||
                !String.Equals(state.GameScene, "World", StringComparison.OrdinalIgnoreCase) ||
                !state.CharacterSaveDetected || String.IsNullOrWhiteSpace(state.ActiveCharacterPath))
            {
                reason = LiveWorldSessionRequiredMessage;
                return false;
            }
            string detectedCharacterPath = state.ActiveCharacterPath;
            CharacterInfo selectedCharacter = CharacterDiscovery.Discover(AppConfig.Load()).FirstOrDefault(
                delegate(CharacterInfo item)
                {
                    return item.Selected && item.File.Equals(detectedCharacterPath, StringComparison.OrdinalIgnoreCase);
                });
            if (selectedCharacter == null)
            {
                reason = "The character saved in this game session is not selected in Choose Characters. Select it, save the selection, save again in Valheim, and retry.";
                return false;
            }
            if (!File.Exists(state.ActiveCharacterPath))
            {
                reason = "The character detected for this session is no longer available at:\r\n" + state.ActiveCharacterPath;
                return false;
            }
            DateTime settledAfter;
            if (TryParseUtc(state.CharacterSettledAfterUtc, out settledAfter) && DateTime.UtcNow < settledAfter.ToUniversalTime())
            {
                int seconds = Math.Max(1, (int)Math.Ceiling((settledAfter.ToUniversalTime() - DateTime.UtcNow).TotalSeconds));
                reason = "A character save was detected for " + state.ActiveCharacterName +
                    ", but its file is still settling. Wait about " + seconds + " second(s), then retry.";
                return false;
            }
            return true;
        }

        private static void AppendFileStamp(StringBuilder value, string path)
        {
            try
            {
                FileInfo file = new FileInfo(path);
                if (!file.Exists) return;
                value.Append(file.FullName.ToUpperInvariant()).Append('|')
                    .Append(file.Length).Append('|').Append(file.LastWriteTimeUtc.Ticks).Append(';');
            }
            catch { }
        }

        private void CompleteOnUiThread(MethodInvoker completion)
        {
            try
            {
                if (IsDisposed || Disposing) return;
                if (InvokeRequired) BeginInvoke(completion);
                else completion();
            }
            catch
            {
                // A closing window must never leave the guardian's internal busy flags locked.
                checkRunning = false;
            }
        }

        private bool IsValheimRunning()
        {
            try { return Process.GetProcessesByName("valheim").Length > 0; }
            catch { return false; }
        }

        private void ShowWorldCheckChoices()
        {
            using (CheckModeForm choice = new CheckModeForm("Worlds", Icon))
            {
                if (choice.ShowDialog(this) == DialogResult.OK)
                    StartScrutiny(false, false, "World", choice.CreateBackup);
            }
        }

        private void ShowCharacterCheckChoices()
        {
            using (CheckModeForm choice = new CheckModeForm("Characters", Icon))
            {
                if (choice.ShowDialog(this) == DialogResult.OK)
                    StartScrutiny(false, false, "Character", choice.CreateBackup);
            }
        }

        private void StartScrutiny(bool afterExit)
        {
            StartScrutiny(afterExit, false, "All", true);
        }

        private void StartScrutiny(bool afterExit, bool liveSession)
        {
            StartScrutiny(afterExit, liveSession, "All", true);
        }

        private void StartScrutiny(bool afterExit, bool liveSession, string scope)
        {
            StartScrutiny(afterExit, liveSession, scope, true);
        }

        private void StartScrutiny(bool afterExit, bool liveSession, string scope, bool createBackup)
        {
            if (checkRunning || atSaveBackupRunning)
            {
                if (afterExit) afterExitPending = true;
                else MessageBox.Show(this, atSaveBackupRunning
                    ? BackupAtSaveBusyMessage
                    : "Another verification is already running. Please wait for it to finish, then try again.",
                    "MagicDragon Safeguardian", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            bool valheimRunning = IsValheimRunning();
            ActiveWorldSessionState liveWorld = null;
            ActiveWorldSessionState liveCharacter = null;
            List<WorldInfo> selected = WorldDiscovery.Discover(AppConfig.Load()).Where(delegate(WorldInfo w) { return w.Selected; }).ToList();
            int characterCount = CharacterDiscovery.Discover(AppConfig.Load()).Count(delegate(CharacterInfo c) { return c.Selected; });
            bool missingWorlds = scope.Equals("World", StringComparison.OrdinalIgnoreCase) && !liveSession && selected.Count == 0;
            bool missingCharacters = scope.Equals("Character", StringComparison.OrdinalIgnoreCase) && characterCount == 0;
            bool missingAll = scope.Equals("All", StringComparison.OrdinalIgnoreCase) && selected.Count == 0 && characterCount == 0;
            if (missingWorlds || missingCharacters || missingAll)
            {
                string missing = missingWorlds ? "No worlds are selected. Use Choose Worlds before running this check."
                    : (missingCharacters ? (liveSession
                        ? "No characters are selected. Use Choose Characters before running Live Character Check."
                        : "No characters are selected. Use Choose Characters before running this check.")
                    : "No worlds or characters are selected.");
                MessageBox.Show(this, missing, "MagicDragon Safeguardian",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!afterExit && !liveSession && valheimRunning)
            {
                MessageBox.Show(this, "Valheim is running. Use the appropriate Live World or Live Character Check after saving, or close Valheim before creating verified backups.",
                    "MagicDragon Safeguardian", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (liveSession && !valheimRunning)
            {
                MessageBox.Show(this, "The dragon says Valheim is not running. Use check now for a normal offline verification",
                    "MagicDragon Safeguardian", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (liveSession && scope.Equals("World", StringComparison.OrdinalIgnoreCase))
            {
                string reason;
                if (!TryGetActiveLiveWorld(out liveWorld, out reason))
                {
                    MessageBox.Show(this, reason, "Live World Check", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }
            if (liveSession && scope.Equals("Character", StringComparison.OrdinalIgnoreCase))
            {
                string reason;
                if (!TryGetActiveLiveCharacter(out liveCharacter, out reason))
                {
                    MessageBox.Show(this, reason, "Live Character Check", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }
            if (liveSession && MessageBox.Show(this,
                "First save inside Valheim and wait until its saving indicator has completely disappeared.\r\n\r\n" +
                (liveWorld == null ? String.Empty : "Detected world: " + liveWorld.ActiveWorldName + "\r\n\r\n") +
                (liveCharacter == null ? String.Empty : "Detected character: " + liveCharacter.ActiveCharacterName + "\r\n\r\n") +
                "This is a fast read-only " + scope.ToLowerInvariant() + " check; it does not create a backup ZIP.\r\n" +
                "Do not save again during its brief verification. Start now?",
                "Live " + scope + " Check", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            checkRunning = true;
            UpdateCheckButtonState();
            dashboardStatus.Text = liveSession
                ? "Fast live " + scope.ToLowerInvariant() + " check in progress...\r\nDo not save again until this finishes."
                : "Scrutiny in progress...\r\nDo not start Valheim until this finishes.";
            ThreadPool.QueueUserWorkItem(delegate
            {
                string mode;
                if (liveSession) mode = scope.Equals("Character", StringComparison.OrdinalIgnoreCase) ? "CheckLiveCharacters" :
                    (scope.Equals("World", StringComparison.OrdinalIgnoreCase) ? "CheckLiveWorlds" : "CheckLive");
                else if (afterExit) mode = "CheckAfterExit";
                else mode = scope.Equals("Character", StringComparison.OrdinalIgnoreCase)
                    ? (createBackup ? "CheckCharacters" : "CheckCharacterIntegrity") :
                    (scope.Equals("World", StringComparison.OrdinalIgnoreCase)
                        ? (createBackup ? "CheckWorlds" : "CheckWorldIntegrity") : "CheckOnce");
                int exitCode;
                try
                {
                    exitCode = EngineRunner.Run(mode, null,
                        liveSession && liveWorld != null ? liveWorld.ActiveWorldPath : null,
                        liveSession && liveCharacter != null ? liveCharacter.ActiveCharacterPath : null);
                }
                catch { exitCode = 91; }
                CompleteOnUiThread(delegate
                {
                    checkRunning = false;
                    UpdateCheckButtonState();
                    RefreshEverything();
                    string message;
                    if (liveSession)
                        message = exitCode == 0
                            ? (liveWorld != null
                                ? "The dragon has finished the spell for " + liveWorld.ActiveWorldName + ". The world save file is healthy. No backup ZIP was created; you may continue playing or close the game."
                                : (liveCharacter != null
                                    ? "The dragon has finished the spell for " + liveCharacter.ActiveCharacterName + ". The character save file is healthy. No backup ZIP was created; you may continue playing or close the game."
                                    : "The live " + scope.ToLowerInvariant() + " files are coherent. No backup ZIP was created; you may continue playing."))
                            : "The live " + scope.ToLowerInvariant() + " check found an incomplete, inconsistent, unreadable, or changing save. Review Logs, wait for saving to finish, and retry.";
                    else if (exitCode == 0)
                        message = !createBackup &&
                            (scope.Equals("World", StringComparison.OrdinalIgnoreCase) ||
                                scope.Equals("Character", StringComparison.OrdinalIgnoreCase))
                            ? scope + " integrity verification completed. No backup ZIP was created."
                            : scope + " save verification and backup completed.";
                    else
                        message = "One or more " + scope.ToLowerInvariant() + " saves failed scrutiny. Check the Logs tab.";
                    if (tray != null)
                    {
                        tray.BalloonTipTitle = "MagicDragon Safeguardian";
                        tray.BalloonTipText = message;
                        tray.BalloonTipIcon = exitCode == 0 ? ToolTipIcon.Info : ToolTipIcon.Error;
                        tray.ShowBalloonTip(10000);
                    }
                    if (!afterExit && Visible)
                    {
                        MessageBox.Show(this, message, "MagicDragon Safeguardian", MessageBoxButtons.OK,
                            exitCode == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                    }
                    if (afterExitPending && !IsValheimRunning() && !atSaveBackupRunning && !HasPendingAtSaveBackup())
                    {
                        afterExitPending = false;
                        StartScrutiny(true);
                    }
                });
            });
        }

        private void RefreshEverything()
        {
            config = AppConfig.Load();
            worlds = WorldDiscovery.Discover(config);
            characters = CharacterDiscovery.Discover(config);
            loadingControls = true;
            try
            {
                RefreshWorldList();
                RefreshCharacterList();
                RefreshDashboardOnly();
                RefreshBackupList();
                RefreshSettings();
                RefreshLog();
                UpdateCheckButtonState();
            }
            finally { loadingControls = false; }
        }

        private void RefreshDashboardOnly()
        {
            StatusSummary status = StatusSummary.Load();
            int selected = worlds.Count(delegate(WorldInfo w) { return w.Selected; });
            int selectedCharacters = characters.Count(delegate(CharacterInfo c) { return c.Selected; });
            watcherValue.Text = "Valheim save guardian";
            if (IsValheimRunning())
            {
                ActiveWorldSessionState active = ActiveWorldSessionState.Load();
                if (active != null && active.WorldSaveDetected && !String.IsNullOrWhiteSpace(active.ActiveWorldName))
                    watcherStatusValue.Text = "Detected: " + active.ActiveWorldName;
                else if (active != null && String.Equals(active.GameScene, "Menu", StringComparison.OrdinalIgnoreCase))
                    watcherStatusValue.Text = "Valheim main menu - no active world";
                else if (active != null && String.Equals(active.GameScene, "World", StringComparison.OrdinalIgnoreCase))
                    watcherStatusValue.Text = "World loaded - waiting for save";
                else watcherStatusValue.Text = "Valheim running - detecting game state";
            }
            else watcherStatusValue.Text = "Monitoring valheim.exe";
            lastCheckValue.Text = status.CheckedAt;
            protectedValue.Text = selected + " worlds + " + selectedCharacters + " characters";
            latestValue.Text = status.HasStatus ? (status.Valid ? "GOOD backups verified" : "ATTENTION REQUIRED") : "Not checked yet";
            latestValue.ForeColor = status.HasStatus && !status.Valid ? Color.Maroon : Color.Black;
            backupValue.Text = config.BackupRoot;
            dashboardStatus.Text = status.Message + Environment.NewLine + status.Detail;
            dashboardAuto.Checked = config.AutoDiscoverWorlds;
        }

        private void RefreshWorldList()
        {
            worldList.Items.Clear();
            foreach (WorldInfo world in worlds) worldList.Items.Add(world.DisplayName, world.Selected);
            worldsAuto.Checked = config.AutoDiscoverWorlds;
            worldPathLabel.Text = worlds.Count == 0 ? "No folder-based Valheim 1.0 worlds were found." : "Select a world to view its folder.";
        }

        private void RefreshWorldDiscovery()
        {
            Dictionary<string, bool> currentChecks = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < worlds.Count && i < worldList.Items.Count; i++)
                currentChecks[worlds[i].Key] = worldList.GetItemChecked(i);
            bool autoInclude = worldsAuto.Checked;
            config = AppConfig.Load();
            worlds = WorldDiscovery.Discover(config);
            RefreshWorldList();
            for (int i = 0; i < worlds.Count; i++)
            {
                bool selected;
                if (currentChecks.TryGetValue(worlds[i].Key, out selected)) worldList.SetItemChecked(i, selected);
            }
            worldsAuto.Checked = autoInclude;
            MessageBox.Show(this, worlds.Count + " official Valheim world(s) found.",
                "World list refreshed", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void SetEveryWorld(bool value)
        {
            for (int i = 0; i < worldList.Items.Count; i++) worldList.SetItemChecked(i, value);
        }

        private void RefreshCharacterList()
        {
            characterList.Items.Clear();
            foreach (CharacterInfo character in characters) characterList.Items.Add(character.DisplayName, character.Selected);
            charactersAuto.Checked = config.AutoDiscoverCharacters;
            characterPathLabel.Text = characters.Count == 0 ? "No supported local or Steam Cloud Valheim characters were found." : "Select a character to view its file.";
        }

        private void RefreshCharacterDiscovery()
        {
            Dictionary<string, bool> currentChecks = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < characters.Count && i < characterList.Items.Count; i++)
                currentChecks[characters[i].Key] = characterList.GetItemChecked(i);
            bool autoInclude = charactersAuto.Checked;
            config = AppConfig.Load();
            characters = CharacterDiscovery.Discover(config);
            RefreshCharacterList();
            for (int i = 0; i < characters.Count; i++)
            {
                bool selected;
                if (currentChecks.TryGetValue(characters[i].Key, out selected)) characterList.SetItemChecked(i, selected);
            }
            charactersAuto.Checked = autoInclude;
            MessageBox.Show(this, characters.Count + " official Valheim character(s) found.",
                "Character list refreshed", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void SetEveryCharacter(bool value)
        {
            for (int i = 0; i < characterList.Items.Count; i++) characterList.SetItemChecked(i, value);
        }

        private void SaveWorldSelection()
        {
            if (!ApplyWorldSelectionControls()) return;
            config.Save();
            RefreshEverything();
            MessageBox.Show(this, "World selection saved.", "MagicDragon Safeguardian", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private bool ApplyWorldSelectionControls()
        {
            List<string> selected = new List<string>();
            List<string> unselected = new List<string>();
            for (int i = 0; i < worldList.Items.Count; i++)
            {
                string key = worlds[i].Key;
                bool uniqueName = worlds.Count(delegate(WorldInfo item) { return item.Name.Equals(worlds[i].Name, StringComparison.OrdinalIgnoreCase); }) == 1;
                List<string> target = worldList.GetItemChecked(i) ? selected : unselected;
                target.Add(key);
                if (uniqueName) target.Add("Name:" + worlds[i].Name);
            }
            if (selected.Count == 0 && MessageBox.Show(this,
                "No worlds are selected. This disables protection for every current world. Continue?",
                "MagicDragon Safeguardian", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return false;
            config.AutoDiscoverWorlds = worldsAuto.Checked;
            if (worldsAuto.Checked)
            {
                config.IncludedWorldNames = new string[0];
                config.ExcludedWorldNames = unselected.ToArray();
            }
            else
            {
                config.IncludedWorldNames = selected.ToArray();
                config.ExcludedWorldNames = new string[0];
            }
            return true;
        }

        private void SaveCharacterSelection()
        {
            if (!ApplyCharacterSelectionControls()) return;
            config.Save();
            RefreshEverything();
            MessageBox.Show(this, "Character selection saved.", "MagicDragon Safeguardian", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private bool ApplyCharacterSelectionControls()
        {
            List<string> selected = new List<string>();
            List<string> unselected = new List<string>();
            for (int i = 0; i < characterList.Items.Count; i++)
            {
                string key = characters[i].Key;
                bool uniqueName = characters.Count(delegate(CharacterInfo item) { return item.Name.Equals(characters[i].Name, StringComparison.OrdinalIgnoreCase); }) == 1;
                List<string> target = characterList.GetItemChecked(i) ? selected : unselected;
                target.Add(key);
                if (uniqueName) target.Add("Name:" + characters[i].Name);
            }
            if (selected.Count == 0 && MessageBox.Show(this,
                "No characters are selected. This disables protection for every current character. Continue?",
                "MagicDragon Safeguardian", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return false;
            config.AutoDiscoverCharacters = charactersAuto.Checked;
            if (charactersAuto.Checked)
            {
                config.IncludedCharacterNames = new string[0];
                config.ExcludedCharacterNames = unselected.ToArray();
            }
            else
            {
                config.IncludedCharacterNames = selected.ToArray();
                config.ExcludedCharacterNames = new string[0];
            }
            return true;
        }

        private void DashboardAutoChanged(object sender, EventArgs e)
        {
            if (loadingControls) return;
            List<WorldInfo> current = WorldDiscovery.Discover(config);
            config.AutoDiscoverWorlds = dashboardAuto.Checked;
            if (dashboardAuto.Checked)
            {
                List<string> excluded = new List<string>();
                foreach (WorldInfo world in current.Where(delegate(WorldInfo w) { return !w.Selected; }))
                {
                    excluded.Add(world.Key);
                    if (current.Count(delegate(WorldInfo item) { return item.Name.Equals(world.Name, StringComparison.OrdinalIgnoreCase); }) == 1)
                        excluded.Add("Name:" + world.Name);
                }
                config.ExcludedWorldNames = excluded.ToArray();
                config.IncludedWorldNames = new string[0];
            }
            else
            {
                List<string> included = new List<string>();
                foreach (WorldInfo world in current.Where(delegate(WorldInfo w) { return w.Selected; }))
                {
                    included.Add(world.Key);
                    if (current.Count(delegate(WorldInfo item) { return item.Name.Equals(world.Name, StringComparison.OrdinalIgnoreCase); }) == 1)
                        included.Add("Name:" + world.Name);
                }
                config.IncludedWorldNames = included.ToArray();
                config.ExcludedWorldNames = new string[0];
            }
            config.Save();
            RefreshEverything();
        }

        private void RefreshBackupList()
        {
            backupList.Items.Clear();
            try
            {
                if (!Directory.Exists(config.BackupRoot)) return;
                foreach (FileInfo file in new DirectoryInfo(config.BackupRoot).GetFiles("*.zip", SearchOption.AllDirectories).OrderByDescending(delegate(FileInfo f) { return f.LastWriteTimeUtc; }))
                {
                    string kind = ReadBackupKind(file.FullName);
                    string content = ReadBackupContentType(file.FullName);
                    ListViewItem item = new ListViewItem(file.Name);
                    item.SubItems.Add(content);
                    item.SubItems.Add(kind);
                    item.SubItems.Add(file.LastWriteTime.ToString("dd MMM yyyy HH:mm"));
                    item.SubItems.Add(FormatBytes(file.Length));
                    item.Tag = file.FullName;
                    if (kind == "QUARANTINE") item.ForeColor = Color.Maroon;
                    backupList.Items.Add(item);
                }
            }
            catch { }
        }

        private string ReadBackupKind(string zipPath)
        {
            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(zipPath))
                {
                    ZipArchiveEntry entry = archive.GetEntry("Safeguardian_manifest.json");
                    if (entry == null) return "UNKNOWN";
                    using (StreamReader reader = new StreamReader(entry.Open(), Encoding.UTF8))
                    {
                        JavaScriptSerializer serializer = new JavaScriptSerializer();
                        Dictionary<string, object> manifest = serializer.DeserializeObject(reader.ReadToEnd()) as Dictionary<string, object>;
                        object value;
                        string kind = manifest != null && manifest.TryGetValue("Kind", out value) && value != null
                            ? value.ToString() : String.Empty;
                        if (kind.Equals("GOOD", StringComparison.OrdinalIgnoreCase)) return "GOOD";
                        if (kind.Equals("QUARANTINE", StringComparison.OrdinalIgnoreCase)) return "QUARANTINE";
                        return "LEGACY";
                    }
                }
            }
            catch { return "UNKNOWN"; }
        }

        private string ReadBackupContentType(string zipPath)
        {
            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(zipPath))
                {
                    ZipArchiveEntry entry = archive.GetEntry("Safeguardian_manifest.json");
                    if (entry == null) return "World";
                    using (StreamReader reader = new StreamReader(entry.Open(), Encoding.UTF8))
                    {
                        JavaScriptSerializer serializer = new JavaScriptSerializer();
                        Dictionary<string, object> manifest = serializer.DeserializeObject(reader.ReadToEnd()) as Dictionary<string, object>;
                        object value;
                        if (manifest != null && manifest.TryGetValue("AssetType", out value) && value != null &&
                            value.ToString().Equals("Character", StringComparison.OrdinalIgnoreCase)) return "Character";
                    }
                }
            }
            catch { }
            return "World";
        }

        private string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L) return (bytes / (1024d * 1024d * 1024d)).ToString("0.0") + " GB";
            if (bytes >= 1024L * 1024L) return (bytes / (1024d * 1024d)).ToString("0.0") + " MB";
            if (bytes >= 1024L) return (bytes / 1024d).ToString("0.0") + " KB";
            return bytes + " B";
        }

        private void VerifySelectedBackup()
        {
            if (backupList.SelectedItems.Count == 0) return;
            string path = backupList.SelectedItems[0].Tag as string;
            try
            {
                byte[] buffer = new byte[65536];
                using (ZipArchive archive = ZipFile.OpenRead(path))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (String.IsNullOrEmpty(entry.Name)) continue;
                        using (Stream stream = entry.Open()) while (stream.Read(buffer, 0, buffer.Length) > 0) { }
                    }
                }
                MessageBox.Show(this, "Every ZIP entry decompressed successfully.", "Backup verified", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Backup verification failed:\r\n" + ex.Message, "Backup damaged", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowRestoreWizard()
        {
            if (backupList.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Select a GOOD world backup first.", "Verified Restore Wizard",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ListViewItem selected = backupList.SelectedItems[0];
            string content = selected.SubItems.Count > 1 ? selected.SubItems[1].Text : "World";
            string kind = selected.SubItems.Count > 2 ? selected.SubItems[2].Text : "";
            if (content.Equals("Character", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "Character backups are verified and preserved, but this release restores worlds through the wizard. Character archives remain available for manual recovery.",
                    "Character backup selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!kind.Equals("GOOD", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "Only a backup marked GOOD can enter the restore wizard. QUARANTINE copies remain available for manual recovery analysis.",
                    "Restore blocked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (IsValheimRunning())
            {
                MessageBox.Show(this, "Valheim is still running. Close it completely before opening the restore wizard.",
                    "Restore blocked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string path = selected.Tag as string;
            if (String.IsNullOrWhiteSpace(path)) return;
            using (RestoreWizardForm wizard = new RestoreWizardForm(path, Icon))
            {
                if (wizard.ShowDialog(this) == DialogResult.OK) RefreshEverything();
            }
        }

        private void RefreshSettings()
        {
            backupRootBox.Text = config.BackupRoot;
            goodRetention.Value = Math.Min(goodRetention.Maximum, Math.Max(goodRetention.Minimum, config.RetainGoodBackups));
            quarantineRetention.Value = Math.Min(quarantineRetention.Maximum, Math.Max(quarantineRetention.Minimum, config.RetainQuarantineBackups));
            minimumFreeSpace.Value = Math.Min(minimumFreeSpace.Maximum, Math.Max(minimumFreeSpace.Minimum, config.MinimumFreeSpaceMB));
            settleSeconds.Value = Math.Min(settleSeconds.Maximum, Math.Max(settleSeconds.Minimum, config.SettleSeconds));
            backupAtSaveRetention.Value = Math.Min(backupAtSaveRetention.Maximum,
                Math.Max(backupAtSaveRetention.Minimum, config.BackupAtSaveRetention));
            backupAtSaveRecommended.Visible = backupAtSaveRetention.Value == 5;
            showOnValheimLaunch.Checked = config.ShowWindowWhenValheimStarts;
        }

        private void SaveSettings()
        {
            try
            {
                ApplySettingsControls();
                Directory.CreateDirectory(config.BackupRoot);
                Directory.CreateDirectory(Path.Combine(config.BackupRoot, "Worlds"));
                Directory.CreateDirectory(Path.Combine(config.BackupRoot, "Characters"));
                config.Save();
                UpdateStartupRegistration();
                BackupOrganizer.Organize(config.BackupRoot);
                if (processTimer != null) processTimer.Interval = Math.Max(2000, config.PollSeconds * 1000);
                RefreshEverything();
                string launchMessage = config.ShowWindowWhenValheimStarts
                    ? "The hidden guardian will start at Windows sign-in so it can detect Valheim."
                    : "Automatic Windows startup is disabled. Launch MagicDragon manually when you want protection.";
                MessageBox.Show(this, "Settings saved. World and character backup folders are ready and organized by save name.\r\n\r\n" + launchMessage,
                    "MagicDragon Safeguardian", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Settings could not be saved:\r\n" + ex.Message, "MagicDragon Safeguardian", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SettingsValueChanged(object sender, EventArgs e)
        {
            PersistSettingsControls(false);
        }

        private void BackupAtSaveRetentionChanged(object sender, EventArgs e)
        {
            if (backupAtSaveRecommended != null)
                backupAtSaveRecommended.Visible = backupAtSaveRetention.Value == 5;
            PersistSettingsControls(false);
        }

        private void LaunchPreferenceChanged(object sender, EventArgs e)
        {
            PersistSettingsControls(true);
        }

        private void PersistSettingsControls(bool updateStartup)
        {
            if (loadingControls) return;
            try
            {
                ApplySettingsControls();
                config.Save();
                if (updateStartup) UpdateStartupRegistration();
                if (processTimer != null) processTimer.Interval = Math.Max(2000, config.PollSeconds * 1000);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The preference could not be saved:\r\n" + ex.Message,
                    "MagicDragon Safeguardian", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ApplySettingsControls()
        {
            config.BackupRoot = backupRootBox.Text.Trim();
            config.RetainGoodBackups = (int)goodRetention.Value;
            config.RetainQuarantineBackups = (int)quarantineRetention.Value;
            config.MinimumFreeSpaceMB = (int)minimumFreeSpace.Value;
            config.SettleSeconds = (int)settleSeconds.Value;
            config.BackupAtSaveRetention = (int)backupAtSaveRetention.Value;
            config.ShowWindowWhenValheimStarts = showOnValheimLaunch.Checked;
        }

        private void UpdateStartupRegistration()
        {
            const string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
            const string valueName = "MagicDragon Safeguardian";
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(runKeyPath))
            {
                if (key == null) throw new InvalidOperationException("The Windows startup registry key could not be opened.");
                if (config.ShowWindowWhenValheimStarts)
                {
                    key.SetValue(valueName, "\"" + Application.ExecutablePath + "\" --monitor", RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(valueName, false);
                }
            }

            string legacyShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                "MagicDragon_Safeguardian.lnk");
            if (File.Exists(legacyShortcut)) File.Delete(legacyShortcut);
        }

        private void BrowseInto(TextBox box)
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            dialog.SelectedPath = Directory.Exists(box.Text) ? box.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.SelectedPath;
            dialog.Dispose();
        }

        private void RefreshLog()
        {
            try
            {
                if (!File.Exists(AppPaths.LogFile)) { logBox.Text = "No log has been created yet."; return; }
                string[] lines = File.ReadAllLines(AppPaths.LogFile);
                int start = Math.Max(0, lines.Length - 500);
                logBox.Text = String.Join(Environment.NewLine, lines.Skip(start).ToArray());
                logBox.SelectionStart = logBox.TextLength;
                logBox.ScrollToCaret();
            }
            catch (Exception ex) { logBox.Text = ex.Message; }
        }

        private void ExportDiagnostics()
        {
            if (MessageBox.Show(this,
                "Create a diagnostic ZIP containing logs, configuration, validation results, and file listings?\r\n\r\n" +
                "World and character save contents will not be included.",
                "Export Diagnostics", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                int code = EngineRunner.Run("ExportDiagnostics");
                DiagnosticResult result = null;
                try { result = DiagnosticResult.Load(); } catch { }
                BeginInvoke((MethodInvoker)delegate
                {
                    RefreshLog();
                    if (code == 0 && result != null && result.Success)
                    {
                        MessageBox.Show(this, "Diagnostic ZIP created without world or character payloads.\r\n\r\n" +
                            result.Path + "\r\n\r\nSHA-256:\r\n" + result.SHA256,
                            "Diagnostics ready", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        OpenFolder(Path.GetDirectoryName(result.Path));
                    }
                    else
                    {
                        MessageBox.Show(this, result == null ? "Diagnostic export failed. Review the activity log." : result.Message,
                            "Diagnostic export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                });
            });
        }

        private void OpenFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Cannot open folder", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void ShowHelp()
        {
            MessageBox.Show(this,
                "MagicDragon Safeguardian 1.0.1\r\n\r\n" +
                "1. Choose protected worlds in the Worlds tab.\r\n" +
                "   LocalLow, Local, and locally synchronized Steam Cloud userdata are scanned.\r\n" +
                "2. Choose protected characters in the Characters tab.\r\n" +
                "3. BackupAtSave validates changed selected saves and keeps a separate rolling history.\r\n" +
                "4. After Valheim closes, selected worlds and characters are validated.\r\n" +
                "5. Manual Live World Check is enabled only after MagicDragon detects which world was saved in the current Valheim session.\r\n" +
                "6. The detected world name is shown before the read-only live validation starts.\r\n" +
                "7. The Live World/Character Check function only works while the game is running and after the game or the user has saved the session at least once.\r\n" +
                "   If you return to the menu, the Live Check function retains the last save in memory.\r\n" +
                "   If you enter a different world with the same or a different character, you must save at least once to overwrite the data currently being monitored by Live Check.\r\n" +
                "8. Use Logs > Export Diagnostics ZIP when troubleshooting.\r\n\r\n" +
                "No program can prevent disk failure. Periodically copy your backup folder to another physical drive.\r\n\r\n" +
                "Licensed under the MIT License. Copyright © 2026 HardcoreApe.\r\n\r\n" +
                "The distributed package contains no Valheim saves, usernames, configuration files, logs, or generated diagnostic reports.\r\n\r\n" +
                "This executable is not digitally signed. Windows SmartScreen or antivirus software may display a warning.\r\n\r\n" +
                "MagicDragon Safeguardian is an unofficial community tool. It is not affiliated with, sponsored by, or endorsed by Iron Gate Studio or Coffee Stain Publishing. Valheim is a trademark of its respective owners.",
                "About MagicDragon Safeguardian", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        internal void ShowMain()
        {
            SuspendLayout();
            try { RefreshEverything(); }
            finally { ResumeLayout(false); }
            Opacity = 1D;
            ShowInTaskbar = true;
            if (!Visible) Show();
            WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
            if (IsHandleCreated) SetForegroundWindow(Handle);
            Invalidate(true);
            Update();
        }

        private void HideOrClose()
        {
            if (monitorOwner) { ShowInTaskbar = false; Hide(); }
            else Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (monitorOwner && !exitRequested && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                ShowInTaskbar = false;
                Hide();
                return;
            }
            if (tray != null) tray.Visible = false;
            base.OnFormClosing(e);
        }
    }

    internal static class Program
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);
        private static readonly IntPtr HwndBroadcast = new IntPtr(0xFFFF);
        private const string ActivationEventName = "Local\\HardcoreApe.MagicDragonSafeguardian.ActivateDashboard.v1";

        [STAThread]
        private static void Main(string[] args)
        {
            bool startHidden = args.Any(delegate(string a) { return a.Equals("--monitor", StringComparison.OrdinalIgnoreCase); });
            bool created;
            Mutex mutex = new Mutex(true, "Local\\MagicDragon_Safeguardian_Win95_Monitor", out created);
            Application.SetCompatibleTextRenderingDefault(false);
            if (!created)
            {
                try
                {
                    using (EventWaitHandle activationEvent = EventWaitHandle.OpenExisting(ActivationEventName))
                        activationEvent.Set();
                }
                catch { }
                PostMessage(HwndBroadcast, MainForm.WM_ACTIVATE_MAGICDRAGON, IntPtr.Zero, IntPtr.Zero);
                mutex.Dispose();
                return;
            }
            EventWaitHandle dashboardActivationEvent = null;
            RegisteredWaitHandle dashboardActivationWait = null;
            try
            {
                bool activationEventCreated;
                dashboardActivationEvent = new EventWaitHandle(false, EventResetMode.AutoReset,
                    ActivationEventName, out activationEventCreated);
                MainForm form = new MainForm(true, startHidden);
                dashboardActivationWait = ThreadPool.RegisterWaitForSingleObject(dashboardActivationEvent,
                    delegate(object state, bool timedOut)
                    {
                        try
                        {
                            if (!form.IsDisposed && form.IsHandleCreated)
                                form.BeginInvoke((MethodInvoker)delegate { form.ShowMain(); });
                        }
                        catch { }
                    }, null, Timeout.Infinite, false);
                Application.Run(form);
            }
            finally
            {
                if (dashboardActivationWait != null) dashboardActivationWait.Unregister(null);
                if (dashboardActivationEvent != null) dashboardActivationEvent.Dispose();
                if (created) mutex.ReleaseMutex();
                mutex.Dispose();
            }
        }
    }
}
