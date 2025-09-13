using MDTadusMod.Data;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Web;
using System.Linq;
using System.Text;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace MDTadusMod.Services
{
    public class RotmgApiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<RotmgApiService> _logger;
        private const string ClientToken = "0";

        public RotmgApiService(HttpClient httpClient, ILogger<RotmgApiService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        private static bool IsLockout(string s) =>
            !string.IsNullOrEmpty(s) &&
            s.IndexOf("LOGIN ATTEMPT LIMIT REACHED", StringComparison.OrdinalIgnoreCase) >= 0;

        public async Task<AccountData> GetAccountDataAsync(Account account, AccountData existingAccountData = null)
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["AccountId"] = account.Id,
                ["AccountEmail"] = account.Email
            });

            Log($"Starting data fetch for {account.Email}...", LogLevel.Debug);

            var newAccountData = new AccountData { AccountId = account.Id };

            var accessToken = await VerifyAccountAndGetToken(account, newAccountData);

            if (newAccountData.PasswordError)
            {
                Log($"Token verification failed: {newAccountData.LastErrorMessage}", LogLevel.Warning);
                return existingAccountData ?? newAccountData;
            }

            if (string.IsNullOrEmpty(accessToken))
            {
                if (IsLockout(newAccountData.LastErrorMessage))
                {
                    Log($"Lockout detected in token step: {newAccountData.LastErrorMessage}", LogLevel.Warning);
                    throw new LoginLockoutException(newAccountData.LastErrorMessage);
                }

                Log($"Token verification failed: {newAccountData.LastErrorMessage}", LogLevel.Warning);
                return existingAccountData ?? newAccountData;
            }

            Log("Access token obtained.", LogLevel.Debug);

            var charListResponse = await _httpClient.PostAsync("https://www.realmofthemadgod.com/char/list", new FormUrlEncodedContent(new []
            {
                new KeyValuePair<string, string>("accessToken", accessToken),
                new KeyValuePair<string, string>("muleDump", "true")
            }));

            if (charListResponse.IsSuccessStatusCode)
            {
                var content = await charListResponse.Content.ReadAsStringAsync();
                Log($"Fetched character list (len={content?.Length ?? 0}).", LogLevel.Debug);
                ParseCharListXml(content, newAccountData);
            }
            else
            {
                var errorMsg = $"Failed to fetch character list: {charListResponse.ReasonPhrase}";
                newAccountData.LastErrorMessage = errorMsg;
                Log(errorMsg, LogLevel.Warning);
                return existingAccountData ?? newAccountData;
            }

            Log("Data fetch finished.", LogLevel.Debug);
            return newAccountData;
        }

        private void ParseCharListXml(string xml, AccountData accountData)
        {
            var xDoc = XDocument.Parse(xml);
            var charsElement = xDoc.Root;
            var accountElement = charsElement.Element("Account");

            // --- Account Info & Account-Level Enchantment Data ---
            if (accountElement != null)
            {
                accountData.Name = accountElement.Element("Name")?.Value;
                accountData.Credits = SafeInt(accountElement.Element("Credits"));
                accountData.Fame = SafeInt(accountElement.Element("Fame"));
                accountData.MaxNumChars = SafeIntAttr(charsElement.Attribute("maxNumChars"));
                var guildElement = accountElement.Element("Guild");
                if (guildElement != null)
                {
                    accountData.GuildName = guildElement.Element("Name")?.Value;
                    accountData.GuildRank = SafeInt(guildElement.Element("Rank"));
                }

                // --- Class Stats & Stars ---
                var statsElement = accountElement.Element("Stats");
                if (statsElement != null)
                {
                    int totalStars = 0;
                    foreach (var classStats in statsElement.Elements("ClassStats"))
                    {
                        var bestFame = SafeInt(classStats.Element("BestBaseFame"));
                        totalStars += CalculateStars(bestFame);
                    }
                    accountData.Star = totalStars;
                }

                // Use the correct parser for account-level enchantments (which use the 'id' attribute)
                accountData.UniqueItemData = ParseAccountEnchantmentMap(accountElement.Element("UniqueItemInfo"));
                accountData.UniqueGiftItemData = ParseAccountEnchantmentMap(accountElement.Element("UniqueGiftItemInfo"));
                accountData.UniqueTemporaryGiftItemData = ParseAccountEnchantmentMap(accountElement.Element("UniqueTemporaryGiftItemInfo"));
                accountData.MaterialStorageItemData = ParseAccountEnchantmentMap(accountElement.Element("MaterialStorageData"));
            }

            var existingPetInstanceIds = new HashSet<int>();
            bool seasonalPetInvParsed = false;
            bool nonSeasonalPetInvParsed = false;

            // --- Characters ---
            accountData.Characters = charsElement.Elements("Char")
                .Select(c =>
                {
                    var character = new Character
                    {
                        Id = SafeIntAttr(c.Attribute("id")),
                        ObjectType = SafeInt(c.Element("ObjectType")),
                        Skin = SafeInt(c.Element("Texture")),
                        Level = SafeInt(c.Element("Level")),
                        Exp = SafeInt(c.Element("Exp")),
                        CurrentFame = SafeInt(c.Element("CurrentFame")),
                        EquipQS = c.Element("EquipQS")?.Value,
                        MaxHitPoints = SafeInt(c.Element("MaxHitPoints")),
                        MaxMagicPoints = SafeInt(c.Element("MaxMagicPoints")),
                        Attack = SafeInt(c.Element("Attack")),
                        Defense = SafeInt(c.Element("Defense")),
                        Speed = SafeInt(c.Element("Speed")),
                        Dexterity = SafeInt(c.Element("Dexterity")),
                        Vitality = SafeInt(c.Element("HpRegen")),
                        Wisdom = SafeInt(c.Element("MpRegen")),
                        PCStats = c.Element("PCStats")?.Value,
                        Seasonal = c.Element("Seasonal")?.Value == "True",
                        HasBackpack = c.Element("HasBackpack")?.Value == "1",
                        // Use the character-specific parser (which uses the 'type' attribute)
                        UniqueItemData = ParseCharacterEnchantmentMap(c.Element("UniqueItemInfo"))
                    };

                    var equipmentString = c.Element("Equipment")?.Value;
                    if (!string.IsNullOrEmpty(equipmentString))
                    {
                        var itemStrings = equipmentString.Split(',').Where(s => !string.IsNullOrWhiteSpace(s));
                        foreach (var itemStr in itemStrings)
                        {
                            var idPart = itemStr.Split('#')[0];
                            if (!int.TryParse(idPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId))
                                continue;

                            character.UniqueItemData.TryGetValue(itemId.ToString(CultureInfo.InvariantCulture), out var enchantList);
                            character.EquipmentList.Add(new Item(itemId, enchantList?.FirstOrDefault()));
                        }
                    }

                    character.ParsePCStats();

                    // --- Pet Data ---
                    var petElement = c.Element("Pet");
                    if (petElement != null)
                    {
                        var instanceId = SafeIntAttr(petElement.Attribute("instanceId"));
                        if (existingPetInstanceIds.Add(instanceId)) // True if the pet is new
                        {
                            var pet = new Pet
                            {
                                InstanceId = instanceId,
                                Name = (string)petElement.Attribute("name"),
                                ObjectType = SafeIntAttr(petElement.Attribute("type")),
                                Rarity = SafeIntAttr(petElement.Attribute("rarity")),
                                MaxAbilityPower = SafeIntAttr(petElement.Attribute("maxAbilityPower")),
                                Skin = SafeIntAttr(petElement.Attribute("skin")),
                                Shader = SafeIntAttr(petElement.Attribute("shader")),
                                CreatedOn = (string)petElement.Attribute("createdOn"),
                                Abilities = petElement.Element("Abilities")?.Elements("Ability")
                                    .Select(ab => new PetAbility
                                    {
                                        Type = SafeIntAttr(ab.Attribute("type")),
                                        Power = SafeIntAttr(ab.Attribute("power")),
                                        Points = SafeIntAttr(ab.Attribute("points"))
                                    }).ToList() ?? new List<PetAbility>()
                            };
                            accountData.Pets.Add(pet);
                        }

                        // Merge pet unique item data into the account-level dictionary
                        var petUniqueItemInfo = ParseAccountEnchantmentMap(petElement.Element("UniqueItemInfo"));
                        foreach (var entry in petUniqueItemInfo)
                        {
                            accountData.UniquePetItemData[entry.Key] = entry.Value;
                        }

                        // Parse shared pet inventory
                        if ((string)petElement.Attribute("incInv") == "1")
                        {
                            var targetInventory = character.Seasonal ? accountData.SeasonalPetInventory : accountData.NonSeasonalPetInventory;
                            var wasParsed = character.Seasonal ? seasonalPetInvParsed : nonSeasonalPetInvParsed;

                            if (!wasParsed)
                            {
                                var invString = (string)petElement.Attribute("inv");
                                if (!string.IsNullOrEmpty(invString))
                                {
                                    // Parse the inv format: "0,8;X;item1,item2#enchantId,..."
                                    var invParts = invString.Split(';');
                                    if (invParts.Length >= 3)
                                    {
                                        var itemsString = invParts[2];
                                        if (!string.IsNullOrEmpty(itemsString))
                                        {
                                            var itemStrings = itemsString.Split(',').Where(s => !string.IsNullOrWhiteSpace(s));
                                            foreach (var itemStr in itemStrings)
                                            {
                                                var parts = itemStr.Split('#');
                                                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId))
                                                    continue;

                                                string enchantData = null;
                                                if (parts.Length > 1)
                                                {
                                                    accountData.UniquePetItemData.TryGetValue(parts[1], out enchantData);
                                                }
                                                targetInventory.Items.Add(new Item(itemId, enchantData));
                                            }
                                        }

                                        if (character.Seasonal) seasonalPetInvParsed = true;
                                        else nonSeasonalPetInvParsed = true;
                                    }
                                }
                            }
                        }
                    }

                    return character;
                }).ToList();

            // --- Account-Level Containers (from within the <Account> element) ---
            if (accountElement != null)
            {
                // --- Vault ---
                var vaultElement = accountElement.Element("Vault");
                if (vaultElement != null)
                {
                    var allVaultItems = vaultElement.Elements("Chest")
                        .SelectMany(chest => chest.Value.Split(',').Where(s => !string.IsNullOrWhiteSpace(s)));
                    
                    foreach (var itemStr in allVaultItems)
                    {
                        var parts = itemStr.Split('#');
                        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId))
                            continue;

                        string enchantData = null;
                        if (parts.Length > 1)
                        {
                            accountData.UniqueItemData.TryGetValue(parts[1], out enchantData);
                        }
                        accountData.Vault.Items.Add(new Item(itemId, enchantData));
                    }
                }

                // --- Material Storage ---
                var materialStorageElement = accountElement.Element("MaterialStorage");
                if (materialStorageElement != null)
                {
                    var allMaterialItems = materialStorageElement.Elements("Chest")
                        .SelectMany(chest => chest.Value.Split(',').Where(s => !string.IsNullOrWhiteSpace(s)));

                    foreach (var itemStr in allMaterialItems)
                    {
                        var parts = itemStr.Split('#');
                        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId))
                            continue;

                        string enchantData = null;
                        if (parts.Length > 1)
                        {
                            accountData.MaterialStorageItemData.TryGetValue(parts[1], out enchantData);
                        }
                        accountData.MaterialStorage.Items.Add(new Item(itemId, enchantData));
                    }
                }

                // --- Gifts ---
                var giftsElement = accountElement.Element("Gifts");
                if (giftsElement != null)
                {
                    var itemStrings = giftsElement.Value.Split(',').Where(s => !string.IsNullOrWhiteSpace(s));
                    foreach (var itemStr in itemStrings)
                    {
                        var parts = itemStr.Split('#');
                        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId))
                            continue;

                        string enchantData = null;
                        if (parts.Length > 1)
                        {
                            accountData.UniqueGiftItemData.TryGetValue(parts[1], out enchantData);
                        }
                        accountData.Gifts.Add(new Item(itemId, enchantData));
                    }
                }

                // --- Temporary Gifts ---
                var tempGiftsElement = accountElement.Element("TemporaryGifts");
                if (tempGiftsElement != null)
                {
                    var itemStrings = tempGiftsElement.Value.Split(',').Where(s => !string.IsNullOrWhiteSpace(s));
                    foreach (var itemStr in itemStrings)
                    {
                        var parts = itemStr.Split('#');
                        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId))
                            continue;

                        string enchantData = null;
                        if (parts.Length > 1)
                        {
                            accountData.UniqueTemporaryGiftItemData.TryGetValue(parts[1], out enchantData);
                        }
                        accountData.TemporaryGifts.Add(new Item(itemId, enchantData));
                    }
                }

                // --- Potions (no enchantments) ---
                var potionsElement = accountElement.Element("Potions");
                if (potionsElement != null)
                {
                    var parts = potionsElement.Value.Split(',').Where(s => !string.IsNullOrWhiteSpace(s));
                    foreach (var p in parts)
                    {
                        if (int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId))
                        {
                            accountData.Potions.Add(new Item(itemId, null));
                        }
                    }
                }
            }
        }

        private int CalculateStars(int fame)
        {
            if (fame >= 15000) return 5;
            if (fame >= 5000) return 4;
            if (fame >= 1500) return 3;
            if (fame >= 500) return 2;
            if (fame >= 20) return 1;
            return 0;
        }

        private Dictionary<string, string> ParseAccountEnchantmentMap(XElement uniqueItemInfoElement)
        {
            if (uniqueItemInfoElement == null) return new Dictionary<string, string>();
            
            return uniqueItemInfoElement.Elements("ItemData")
                .Where(el => el.Attribute("id") != null)
                .ToDictionary(
                    item => (string)item.Attribute("id"),
                    item => item.Value
                );
        }

        private Dictionary<string, List<string>> ParseCharacterEnchantmentMap(XElement uniqueItemInfoElement)
        {
            var uniqueItemData = new Dictionary<string, List<string>>();
            if (uniqueItemInfoElement == null)
            {
                return uniqueItemData;
            }

            foreach (var itemElement in uniqueItemInfoElement.Elements("ItemData"))
            {
                var key = (string)itemElement.Attribute("type");
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                if (!uniqueItemData.ContainsKey(key))
                {
                    uniqueItemData[key] = new List<string>();
                }
                
                uniqueItemData[key].Add(itemElement.Value);
            }
            return uniqueItemData;
        }

        // VerifyAccountAndGetToken(): keep structured debug, consider trimming content size if too verbose
        private async Task<string> VerifyAccountAndGetToken(Account account, AccountData accountData)
        {
            var uriBuilder = new UriBuilder("https://www.realmofthemadgod.com/account/verify");
            var query = HttpUtility.ParseQueryString(uriBuilder.Query);
            query["clientToken"] = ClientToken;
            query["game_net"] = "Unity";
            query["play_platform"] = "Unity";
            uriBuilder.Query = query.ToString();

            HttpContent content;
            if (account.Email.Contains(':') && !account.Email.Contains('@')) // steamworks
            {
                content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("guid", account.Email),
                    new KeyValuePair<string, string>("secret", account.Password)
                });
            }
            else
            {
                content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("guid", account.Email),
                    new KeyValuePair<string, string>("password", account.Password)
                });
            }

            var response = await _httpClient.PostAsync(uriBuilder.ToString(), content);
            var responseString = await response.Content.ReadAsStringAsync();

            // Keep at Debug; flip to Trace or shorten if too noisy
            _logger.LogDebug("Verify response Status={Status} Length={Length}", response.StatusCode, responseString?.Length ?? 0);

            try
            {
                var xml = XDocument.Parse(responseString);
                if (xml.Root?.Name == "Error")
                {
                    var errorValue = xml.Root.Value;
                    if (errorValue == "WebChangePasswordDialog.passwordError")
                    {
                        accountData.PasswordError = true;
                        accountData.LastErrorMessage = "Password error";
                        return null;
                    }

                    accountData.LastErrorMessage = string.IsNullOrWhiteSpace(errorValue)
                        ? $"Empty error response (Status: {response.StatusCode})"
                        : errorValue;

                    _logger.LogWarning("API Error: {Error}", accountData.LastErrorMessage);
                    return null;
                }

                if (response.IsSuccessStatusCode && xml.Root?.Element("AccessToken") != null)
                {
                    accountData.LastErrorMessage = "";
                    return xml.Root.Element("AccessToken").Value;
                }
            }
            catch (System.Xml.XmlException ex)
            {
                LogException("XML parse error", ex);
                _logger.LogDebug("Response content (invalid XML) length={Length}", responseString?.Length ?? 0);
                accountData.LastErrorMessage = $"Invalid XML response: {ex.Message}";
            }

            if (!response.IsSuccessStatusCode)
            {
                accountData.LastErrorMessage = $"HTTP {response.StatusCode}: {response.ReasonPhrase}";
                throw new Exception($"Verification failed: {response.ReasonPhrase} - {responseString}");
            }

            return null;
        }

        // ---------- Safe numeric parsing helpers ----------

        private static int SafeInt(XElement el)
        {
            if (el == null) return 0;
            return SafeInt(el.Value);
        }

        private static int SafeIntAttr(XAttribute attr)
        {
            if (attr == null) return 0;
            return SafeInt(attr.Value);
        }

        private static int SafeInt(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;

            var t = s.Trim();

            if (long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
            {
                if (l > int.MaxValue) return int.MaxValue;
                if (l < int.MinValue) return int.MinValue;
                return (int)l;
            }

            if (double.TryParse(t, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var d))
            {
                if (d > int.MaxValue) return int.MaxValue;
                if (d < int.MinValue) return int.MinValue;
                return (int)System.Math.Truncate(d);
            }

            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(t.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out l))
            {
                if (l > int.MaxValue) return int.MaxValue;
                if (l < int.MinValue) return int.MinValue;
                return (int)l;
            }

            return 0;
        }

        // ---------- Logging helpers (mirroring ReloadQueue style) ----------

        private void Log(string message, LogLevel level = LogLevel.Information)
        {
            var stamped = $"[{DateTime.UtcNow:O}] [RotmgApi] {message}";
            switch (level)
            {
                case LogLevel.Warning:
                    _logger.LogWarning("{Message}", stamped);
                    break;
                case LogLevel.Error:
                case LogLevel.Critical:
                    _logger.LogError("{Message}", stamped);
                    break;
                case LogLevel.Debug:
                    _logger.LogDebug("{Message}", stamped);
                    break;
                case LogLevel.Trace:
                    _logger.LogTrace("{Message}", stamped);
                    break;
                default:
                    _logger.LogInformation("{Message}", stamped);
                    break;
            }
        }

        private void LogException(string context, Exception ex)
        {
            var line = $"[{DateTime.UtcNow:O}] [RotmgApi] {context}: {ex.GetType().Name}: {ex.Message}";
            _logger.LogError(ex, "{Message}", line);
        }
    }
}