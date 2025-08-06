using MDTadusMod.Data;
using System.Diagnostics;
using System.Reflection;
using System.Xml.Serialization;

namespace MDTadusMod.Services
{
    public class SettingsService
    {
        private const string SettingsFileName = "AccountView_settings.xml";
        private const string GlobalSettingsFileName = "Global_settings.xml";
        private static string SettingsFilePath => Path.Combine(FileSystem.AppDataDirectory, SettingsFileName);
        private static string GlobalSettingsFilePath => Path.Combine(FileSystem.AppDataDirectory, GlobalSettingsFileName);

        public AccountViewOptions GlobalOptions { get; private set; } = new();
        public GlobalSettings GlobalSettings { get; private set; } = new();

        // This would be populated with your actual account data
        private List<AccountViewOptions> _allAccountOptions = new();

        public event Action OnChange;

        public SettingsService()
        {
            LoadSettings();
            LoadGlobalSettings();
        }

        public void UpdateGlobalOption(string propertyName, object value)
        {
            // Use GetProperty for properties, not GetField
            var property = typeof(AccountViewOptions).GetProperty(propertyName);
            if (property != null)
            {
                // 1. Update the global options
                property.SetValue(GlobalOptions, value);

                // 2. Update the same property for all account-specific options
                foreach (var accountOptions in _allAccountOptions)
                {
                    property.SetValue(accountOptions, value);
                }

                // 3. Save changes and notify the UI
                SaveSettings();
                NotifyStateChanged();
            }
        }

        public void UpdateGlobalSetting(string propertyName, object value)
        {
            var property = typeof(GlobalSettings).GetProperty(propertyName);
            if (property != null)
            {
                property.SetValue(GlobalSettings, value);
                SaveGlobalSettings();
                NotifyStateChanged();
            }
        }

        private void SaveSettings()
        {
            try
            {
                var serializer = new XmlSerializer(typeof(AccountViewOptions));
                using (var writer = new StreamWriter(SettingsFilePath))
                {
                    serializer.Serialize(writer, GlobalOptions);
                }
            }
            catch (Exception ex)
            {
                // Log the exception to the console for debugging
                Debug.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        private void LoadSettings()
        {
            if (!File.Exists(SettingsFilePath))
            {
                GlobalOptions = new AccountViewOptions();   // first run
                return;
            }

            try
            {
                using var fs = File.OpenRead(SettingsFilePath);
                GlobalOptions = (AccountViewOptions?)new XmlSerializer(
                                    typeof(AccountViewOptions))
                                    .Deserialize(fs)
                                ?? new AccountViewOptions();
            }
            catch (InvalidOperationException ex) // bad/out‑of‑date XML
            {
                Debug.WriteLine(ex);              // see why it failed
                File.Move(SettingsFilePath, SettingsFilePath + ".bak");
                GlobalOptions = new AccountViewOptions();   // start fresh
            }
        }


        public void LoadAccounts(List<AccountViewOptions> accountOptions)
        {
            _allAccountOptions = accountOptions;
            if (_allAccountOptions.Any())
            {
                // You might want to decide how to sync global and specific settings here
            }
            NotifyStateChanged();
        }

        private void SaveGlobalSettings()
        {
            try
            {
                var serializer = new XmlSerializer(typeof(GlobalSettings));
                using (var writer = new StreamWriter(GlobalSettingsFilePath))
                {
                    serializer.Serialize(writer, GlobalSettings);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving global settings: {ex.Message}");
            }
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
                GlobalSettings = (GlobalSettings?)new XmlSerializer(
                                    typeof(GlobalSettings))
                                    .Deserialize(fs)
                                ?? new GlobalSettings();
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine(ex);
                File.Move(GlobalSettingsFilePath, GlobalSettingsFilePath + ".bak");
                GlobalSettings = new GlobalSettings();
            }
        }

        private void NotifyStateChanged() => OnChange?.Invoke();
    }
}