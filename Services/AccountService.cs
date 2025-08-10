using MDTadusMod.Data;
using System.Diagnostics;
using System.Xml.Serialization;
using System.Linq;

namespace MDTadusMod.Services
{
    public class AccountService
    {
        private readonly IAppPaths _paths;
        private readonly string _basePath;
        private readonly string _accountsFilePath;
        private readonly string _accountDataPath;

        public AccountService(IAppPaths paths)
        {
            _paths = paths;
            _basePath = _paths.DataDir;
            _accountsFilePath = _paths.Combine("accounts.xml");
            _accountDataPath = _paths.Combine("AccountData");
            Directory.CreateDirectory(_basePath);
            Directory.CreateDirectory(_accountDataPath);
        }

        public async Task<List<Account>> GetAccountsAsync()
        {
            if (!File.Exists(_accountsFilePath))
                return new List<Account>();

            try
            {
                var ser = new XmlSerializer(typeof(List<Account>));
                using var fs = File.OpenRead(_accountsFilePath);
                var accounts = (List<Account>?)ser.Deserialize(fs) ?? new();

                bool touched = false;
                foreach (var a in accounts)
                {
                    if (a.Id == Guid.Empty)
                    {
                        a.Id = Guid.NewGuid();
                        touched = true;
                    }
                }
                if (touched)
                    await SaveAccountsAsync(accounts);

                return accounts;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AccountService] Read accounts failed: {ex}");
                return new List<Account>();
            }
        }

        public async Task SaveAccountsAsync(List<Account> accounts)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_accountsFilePath)!);
                var ser = new XmlSerializer(typeof(List<Account>));
                using var fs = File.Create(_accountsFilePath);
                ser.Serialize(fs, accounts);
                await fs.FlushAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AccountService] Save accounts failed: {ex}");
            }
        }

        public async Task<AccountData?> GetAccountDataAsync(Guid accountId)
        {
            var filePath = Path.Combine(_accountDataPath, $"{accountId}.xml");
            if (!File.Exists(filePath))
                return null;

            try
            {
                var ser = new XmlSerializer(typeof(AccountData));
                using var fs = File.OpenRead(filePath);
                var data = (AccountData?)ser.Deserialize(fs);

                if (data?.Characters != null)
                    foreach (var c in data.Characters)
                        c.RehydrateEquipment();

                return data;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AccountService] Failed to deserialize {accountId}: {ex.Message}. Deleting cache.");
                try { File.Delete(filePath); } catch { }
                return null;
            }
        }

        public async Task SaveAccountDataAsync(AccountData data)
        {
            var filePath = Path.Combine(_accountDataPath, $"{data.AccountId}.xml");
            try
            {
                Directory.CreateDirectory(_accountDataPath);
                var ser = new XmlSerializer(typeof(AccountData));
                using var fs = File.Create(filePath);
                ser.Serialize(fs, data);
                await fs.FlushAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AccountService] SaveAccountData failed for {data.AccountId}: {ex}");
            }
        }

        public async Task DeleteAccountAsync(Guid accountId)
        {
            var accounts = await GetAccountsAsync();
            var toRemove = accounts.FirstOrDefault(a => a.Id == accountId);
            if (toRemove != null)
            {
                accounts.Remove(toRemove);
                await SaveAccountsAsync(accounts);
            }

            var filePath = Path.Combine(_accountDataPath, $"{accountId}.xml");
            if (File.Exists(filePath))
            {
                try { File.Delete(filePath); } catch (Exception ex) { Debug.WriteLine(ex); }
            }
        }
    }
}
