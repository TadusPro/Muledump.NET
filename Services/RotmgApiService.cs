using MDTadusMod.Data;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Web;
using System.Linq;
using System.Text;

namespace MDTadusMod.Services
{
    public class RotmgApiService
    {
        private readonly HttpClient _httpClient;
        private const string ClientToken = "0";

        public RotmgApiService(HttpClient httpClient) => _httpClient = httpClient;

        public async Task<AccountData> GetAccountDataAsync(Account account, AccountData existingAccountData = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Starting data fetch for {account.Email}...");
            
            // Create a temporary object to hold new data.
            var newAccountData = new AccountData { AccountId = account.Id };
            
            var accessToken = await VerifyAccountAndGetToken(account, newAccountData);

            // If password is wrong, return the original, unmodified data.
            if (newAccountData.PasswordError)
            {
                sb.AppendLine($"Token verification failed: {newAccountData.LastErrorMessage}");
                Debug.WriteLine(sb.ToString());
                // Return the old data if it exists, otherwise the new object with the error.
                return existingAccountData ?? newAccountData;
            }

            if (string.IsNullOrEmpty(accessToken))
            {
                sb.AppendLine($"Token verification failed: {newAccountData.LastErrorMessage}");
                Debug.WriteLine(sb.ToString());
                return existingAccountData ?? newAccountData;
            }
            sb.AppendLine("Successfully obtained access token.");


            var charListResponse = await _httpClient.PostAsync("https://www.realmofthemadgod.com/char/list", new FormUrlEncodedContent(new []
            {
                new KeyValuePair<string, string>("accessToken", accessToken),
                new KeyValuePair<string, string>("muleDump", "true")
            }));

            if (charListResponse.IsSuccessStatusCode)
            {
                sb.AppendLine("Successfully fetched character list.");
                var content = await charListResponse.Content.ReadAsStringAsync();
                // Parse the new data into our temporary object.
                ParseCharListXml(content, newAccountData);
            }
            else
            {
                var errorMsg = $"Failed to fetch character list: {charListResponse.ReasonPhrase}";
                sb.AppendLine(errorMsg);
                newAccountData.LastErrorMessage = errorMsg;
                // On failure, return the old data.
                return existingAccountData ?? newAccountData;
            }

            sb.AppendLine("Data fetch process finished.");
            Debug.WriteLine(sb.ToString());
            // On success, return the newly fetched data.
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
                accountData.Credits = (int?)accountElement.Element("Credits") ?? 0;
                accountData.Fame = (int?)accountElement.Element("Fame") ?? 0;
                accountData.MaxNumChars = (int?)charsElement.Attribute("maxNumChars") ?? 0;
                var guildElement = accountElement.Element("Guild");
                if (guildElement != null)
                {
                    accountData.GuildName = guildElement.Element("Name")?.Value;
                    accountData.GuildRank = (int?)guildElement.Element("Rank") ?? 0;
                }

                // --- Class Stats & Stars ---
                var statsElement = accountElement.Element("Stats");
                if (statsElement != null)
                {
                    int totalStars = 0;
                    foreach (var classStats in statsElement.Elements("ClassStats"))
                    {
                        var bestFame = (int?)classStats.Element("BestBaseFame") ?? 0;
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
                        Id = (int)c.Attribute("id"),
                        ObjectType = (int?)c.Element("ObjectType") ?? 0,
                        Skin = (int?)c.Element("Texture") ?? 0,
                        Level = (int?)c.Element("Level") ?? 0,
                        Exp = (int?)c.Element("Exp") ?? 0,
                        CurrentFame = (int?)c.Element("CurrentFame") ?? 0,
                        EquipQS = c.Element("EquipQS")?.Value,
                        MaxHitPoints = (int?)c.Element("MaxHitPoints") ?? 0,
                        MaxMagicPoints = (int?)c.Element("MaxMagicPoints") ?? 0,
                        Attack = (int?)c.Element("Attack") ?? 0,
                        Defense = (int?)c.Element("Defense") ?? 0,
                        Speed = (int?)c.Element("Speed") ?? 0,
                        Dexterity = (int?)c.Element("Dexterity") ?? 0,
                        Vitality = (int?)c.Element("HpRegen") ?? 0,
                        Wisdom = (int?)c.Element("MpRegen") ?? 0,
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
                            var itemId = int.Parse(itemStr.Split('#')[0]);
                            character.UniqueItemData.TryGetValue(itemId.ToString(), out var enchantList);
                            character.EquipmentList.Add(new Item(itemId, enchantList?.FirstOrDefault()));
                        }
                    }

                    character.ParsePCStats();

                    // --- Pet Data ---
                    var petElement = c.Element("Pet");
                    if (petElement != null)
                    {
                        var instanceId = (int)petElement.Attribute("instanceId");
                        if (existingPetInstanceIds.Add(instanceId)) // True if the pet is new
                        {
                            var pet = new Pet
                            {
                                InstanceId = instanceId,
                                Name = (string)petElement.Attribute("name"),
                                ObjectType = (int)petElement.Attribute("type"),
                                Rarity = (int)petElement.Attribute("rarity"),
                                MaxAbilityPower = (int)petElement.Attribute("maxAbilityPower"),
                                Skin = (int)petElement.Attribute("skin"),
                                Shader = (int)petElement.Attribute("shader"),
                                CreatedOn = (string)petElement.Attribute("createdOn"),
                                Abilities = petElement.Element("Abilities")?.Elements("Ability")
                                    .Select(ab => new PetAbility
                                    {
                                        Type = (int)ab.Attribute("type"),
                                        Power = (int)ab.Attribute("power"),
                                        Points = (int)ab.Attribute("points")
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
                                        // invParts[0] = "0,8" (slot info)
                                        // invParts[1] = "X" (delimiter)
                                        // invParts[2] = actual items
                                        var itemsString = invParts[2];
                                        if (!string.IsNullOrEmpty(itemsString))
                                        {
                                            var itemStrings = itemsString.Split(',').Where(s => !string.IsNullOrWhiteSpace(s));
                                            foreach (var itemStr in itemStrings)
                                            {
                                                var parts = itemStr.Split('#');
                                                var itemId = int.Parse(parts[0]);
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
                        var itemId = int.Parse(parts[0]);
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
                        var itemId = int.Parse(parts[0]);
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
                        var itemId = int.Parse(parts[0]);
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
                        var itemId = int.Parse(parts[0]);
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
                    var itemIds = potionsElement.Value.Split(',').Where(s => !string.IsNullOrWhiteSpace(s)).Select(int.Parse);
                    foreach (var itemId in itemIds)
                    {
                        // Potions don't have enchantments, so the second parameter is null.
                        accountData.Potions.Add(new Item(itemId, null));
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
            else // email
            {
                content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("guid", account.Email),
                    new KeyValuePair<string, string>("password", account.Password)
                });
            }

            var response = await _httpClient.PostAsync(uriBuilder.ToString(), content);
            var responseString = await response.Content.ReadAsStringAsync();

            try
            {
                var xml = XDocument.Parse(responseString);
                if (xml.Root?.Name == "Error" && xml.Root.Value == "WebChangePasswordDialog.passwordError")
                {
                    accountData.PasswordError = true;
                    accountData.LastErrorMessage = "Password error";

                    return null;
                }
                else if(xml.Root?.Name == "Error")
                {
                    accountData.LastErrorMessage = xml.Root.Value;

                }

                if (response.IsSuccessStatusCode)
                {
                    accountData.LastErrorMessage = "";
                    return xml.Root?.Element("AccessToken")?.Value;
                }
            }
            catch (System.Xml.XmlException)
            {
                // Response was not valid XML, proceed to throw general exception
            }

            // If we've reached here, the request failed for a non-specific reason.
            if (!response.IsSuccessStatusCode)
            {
                 throw new Exception($"Verification failed: {response.ReasonPhrase} - {responseString}");
            }

            return null; // Should not be reached, but covers all paths
        }

    }
}