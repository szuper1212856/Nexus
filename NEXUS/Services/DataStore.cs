using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using NEXUS.Models;

namespace NEXUS.Services
{
    /// <summary>
    /// Owns the on-disk state. Writes are debounced so rapid UI edits do not hit the
    /// filesystem on every keystroke, and are written atomically via a temp file.
    /// </summary>
    public class DataStore
    {
        private readonly DispatcherTimer _debounce;
        private readonly JsonSerializerOptions _json = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        public string RootFolder { get; }
        public string DataFile { get; }
        public string SettingsFile { get; }
        public AppData Data { get; private set; } = new AppData();

        public DataStore()
        {
            RootFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NEXUS");
            Directory.CreateDirectory(RootFolder);
            DataFile = Path.Combine(RootFolder, "data.json");
            SettingsFile = Path.Combine(RootFolder, "settings.json");

            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _debounce.Tick += (s, e) => { _debounce.Stop(); SaveNow(); };
        }

        public bool IsFirstRun { get; private set; }

        public void Load()
        {
            AppData data = null;
            AppSettings settings = null;

            try
            {
                if (File.Exists(DataFile))
                    data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(DataFile), _json);
            }
            catch (Exception ex)
            {
                BackupCorruptFile(DataFile, ex);
            }

            try
            {
                if (File.Exists(SettingsFile))
                    settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile), _json);
            }
            catch (Exception ex)
            {
                BackupCorruptFile(SettingsFile, ex);
            }

            IsFirstRun = data == null;
            Data = data ?? new AppData();
            Data.Settings = settings ?? Data.Settings ?? new AppSettings();

            // Re-attach subtask handlers lost during deserialisation.
            foreach (var t in Data.Tasks) t.RefreshSubTaskState();

            // A deadline in the past rolls forward to the next Wednesday unless disabled.
            if (Data.Settings.MissionDeadline == default)
                Data.Settings.MissionDeadline = AppSettings.NextWednesday(DateTime.Now);
            else if (Data.Settings.AutoRollDeadline && Data.Settings.MissionDeadline < DateTime.Now.AddDays(-1))
                Data.Settings.MissionDeadline = AppSettings.NextWednesday(DateTime.Now);
        }

        /// <summary>Queues a save. Safe to call from any change handler.</summary>
        public void RequestSave()
        {
            _debounce.Stop();
            _debounce.Start();
        }

        public void SaveNow()
        {
            _debounce.Stop();
            try
            {
                Data.SavedUtc = DateTime.UtcNow;
                var settings = Data.Settings;

                // Settings live in their own file; the data file keeps a copy for exports.
                WriteAtomic(SettingsFile, JsonSerializer.Serialize(settings, _json));

                Data.Settings = null;
                var payload = JsonSerializer.Serialize(Data, _json);
                Data.Settings = settings;
                WriteAtomic(DataFile, payload);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("NEXUS save failed: " + ex.Message);
            }
        }

        /// <summary>Writes tasks, activity, sessions and settings into one portable bundle.</summary>
        public void Export(string path)
        {
            var bundle = new AppData
            {
                SchemaVersion = Data.SchemaVersion,
                SavedUtc = DateTime.UtcNow,
                Tasks = Data.Tasks,
                Activity = Data.Activity,
                FocusSessions = Data.FocusSessions,
                Settings = Data.Settings
            };
            File.WriteAllText(path, JsonSerializer.Serialize(bundle, _json));
        }

        /// <summary>Replaces the in-memory data with a bundle from disk. Throws on invalid input.</summary>
        public AppData ReadBundle(string path)
        {
            var bundle = JsonSerializer.Deserialize<AppData>(File.ReadAllText(path), _json);
            if (bundle == null) throw new InvalidDataException("The file did not contain NEXUS data.");
            foreach (var t in bundle.Tasks) t.RefreshSubTaskState();
            return bundle;
        }

        private static void WriteAtomic(string path, string contents)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, contents);
            File.Copy(tmp, path, true);
            File.Delete(tmp);
        }

        private static void BackupCorruptFile(string path, Exception ex)
        {
            try
            {
                if (File.Exists(path))
                    File.Copy(path, path + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true);
            }
            catch { /* best effort */ }
            System.Diagnostics.Debug.WriteLine("NEXUS load failed for " + path + ": " + ex.Message);
        }
    }
}
