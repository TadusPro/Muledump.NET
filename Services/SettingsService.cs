using MDTadusMod.Data;
using System.Diagnostics;
using System.Xml.Serialization;

namespace MDTadusMod.Services
{
    public class SettingsService
    {
        private readonly IAppPaths _paths;

        public SettingsService(IAppPaths paths)
        {
            _paths = paths;
            LoadSettings();
            LoadGlobalSettings();
        }

        private const string SettingsFileName = "AccountView_settings.xml";
        private const string GlobalSettingsFileName = "Global_settings.xml";

        private string SettingsFilePath => _paths.Combine(SettingsFileName);
        private string GlobalSettingsFilePath => _paths.Combine(GlobalSettingsFileName);

        public AccountViewOptions GlobalOptions { get; private set; } = new();
        public GlobalSettings GlobalSettings { get; private set; } = new();

        private List<AccountViewOptions> _allAccountOptions = new();
        public event Action? OnChange;

        public void UpdateGlobalOption(string propertyName, object value)
        {
            var prop = typeof(AccountViewOptions).GetProperty(propertyName);
            if (prop == null) return;

            prop.SetValue(GlobalOptions, value);
            foreach (var acct in _allAccountOptions)
                prop.SetValue(acct, value);

            SaveSettings();
            NotifyStateChanged();
        }

        public void UpdateGlobalSetting(string propertyName, object value)
        {
            var prop = typeof(GlobalSettings).GetProperty(propertyName);
            if (prop == null) return;

            prop.SetValue(GlobalSettings, value);
            SaveGlobalSettings();
            NotifyStateChanged();
        }

        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
                var serializer = new XmlSerializer(typeof(AccountViewOptions));
                using var writer = new StreamWriter(SettingsFilePath);
                serializer.Serialize(writer, GlobalOptions);
            }
            catch (Exception ex) { Debug.WriteLine($"Error saving settings: {ex}"); }
        }

        private void LoadSettings()
        {
            if (!File.Exists(SettingsFilePath))
            {
                GlobalOptions = new AccountViewOptions();
                return;
            }

            try
            {
                using var fs = File.OpenRead(SettingsFilePath);
                GlobalOptions = (AccountViewOptions?)new XmlSerializer(typeof(AccountViewOptions)).Deserialize(fs)
                                ?? new AccountViewOptions();
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine(ex);
                TryBackup(SettingsFilePath);
                GlobalOptions = new AccountViewOptions();
            }
        }

        public void LoadAccounts(List<AccountViewOptions> accountOptions)
        {
            _allAccountOptions = accountOptions ?? new();
            NotifyStateChanged();
        }

        private void SaveGlobalSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(GlobalSettingsFilePath)!);
                var serializer = new XmlSerializer(typeof(GlobalSettings));
                using var writer = new StreamWriter(GlobalSettingsFilePath);
                serializer.Serialize(writer, GlobalSettings);
            }
            catch (Exception ex) { Debug.WriteLine($"Error saving global settings: {ex}"); }
        }

        private void LoadGlobalSettings()
        {
            if (!File.Exists(GlobalSettingsFilePath))
            {
                GlobalSettings = new GlobalSettings();
                return;
            }

            try
            {
                using var fs = File.OpenRead(GlobalSettingsFilePath);
                GlobalSettings = (GlobalSettings?)new XmlSerializer(typeof(GlobalSettings)).Deserialize(fs)
                                  ?? new GlobalSettings();
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine(ex);
                TryBackup(GlobalSettingsFilePath);
                GlobalSettings = new GlobalSettings();
            }
        }

        private static void TryBackup(string path)
        {
            try { File.Move(path, path + ".bak", overwrite: true); }
            catch (Exception ex) { Debug.WriteLine($"Backup failed: {ex}"); }
        }

        private void NotifyStateChanged() => OnChange?.Invoke();
    }
}
