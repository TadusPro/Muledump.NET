using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Xml.Serialization;

namespace MDTadusMod.Data
{
    public class AccountData
    {
        public Guid AccountId { get; set; }
        public bool PasswordError { get; set; }
        public string LastErrorMessage { get; set; }

        // Account-wide info
        public string Name { get; set; }
        public int Credits { get; set; }
        public int Fame { get; set; }
        public int MaxNumChars { get; set; }
        public string GuildName { get; set; }
        public int GuildRank { get; set; }
        public int Star { get; set; }

        public List<Character> Characters { get; set; } = new List<Character>();
        public VaultData Vault { get; set; } = new VaultData();
        public VaultData MaterialStorage { get; set; } = new();
        
        [XmlArray("Potions")]
        [XmlArrayItem("Item")]
        public List<Item> Potions { get; set; } = new();

        [XmlArray("Gifts")]
        [XmlArrayItem("Item")]
        public List<Item> Gifts { get; set; } = new();

        [XmlArray("TemporaryGifts")]
        [XmlArrayItem("Item")]
        public List<Item> TemporaryGifts { get; set; } = new();

        // Pet-related data
        [XmlArray("Pets")]
        [XmlArrayItem("Pet")]
        public List<Pet> Pets { get; set; } = new List<Pet>();
        public PetInventoryData SeasonalPetInventory { get; set; } = new PetInventoryData();
        public PetInventoryData NonSeasonalPetInventory { get; set; } = new PetInventoryData();


        // These dictionaries are now only used during live API parsing and will NOT be serialized.
        [XmlIgnore]
        public Dictionary<string, string> UniqueItemData { get; set; } = new Dictionary<string, string>();
        [XmlIgnore]
        public Dictionary<string, string> UniqueGiftItemData { get; set; } = new Dictionary<string, string>();
        [XmlIgnore]
        public Dictionary<string, string> UniqueTemporaryGiftItemData { get; set; } = new Dictionary<string, string>();
        [XmlIgnore]
        public Dictionary<string, string> MaterialStorageItemData { get; set; } = new Dictionary<string, string>();
        [XmlIgnore]
        public Dictionary<string, string> UniquePetItemData { get; set; } = new Dictionary<string, string>();


        public void RehydrateAllItems()
        {
            // Rehydrate Character Equipment
            if (Characters != null)
            {
                foreach (var character in Characters)
                {
                    character.RehydrateEquipment();
                }
            }

            // Rehydrate Vault Items
            if (Vault?.Items != null)
            {
                foreach (var item in Vault.Items)
                {
                    item.ParseEnchantments();
                }
            }

            // Rehydrate Material Storage Items
            if (MaterialStorage?.Items != null)
            {
                foreach (var item in MaterialStorage.Items)
                {
                    item.ParseEnchantments();
                }
            }

            // Rehydrate Gifts
            if (Gifts != null)
            {
                foreach (var item in Gifts)
                {
                    item.ParseEnchantments();
                }
            }

            // Rehydrate Temporary Gifts
            if (TemporaryGifts != null)
            {
                foreach (var item in TemporaryGifts)
                {
                    item.ParseEnchantments();
                }
            }

            // Rehydrate Potions (safe to call, does nothing if no enchant data)
            if (Potions != null)
            {
                foreach (var item in Potions)
                {
                    item.ParseEnchantments();
                }
            }
        }
    }

    public class VaultData
    {
        [XmlElement("Item")]
        public List<Item> Items { get; set; } = new();
    }
}