namespace MDTadusMod.Data
{
    public class AccountViewOptions
    {
        private int _charRowLength = 8; // Default value

        public bool Email { get; set; } // global
        public bool IGN { get; set; } // global
        public bool Info { get; set; } // global

        public bool Characters { get; set; } // has alot of sub options
        public CharOrder CharOrder { get; set; } // could be a list that the player selects one
        public int CharRowLength
        {
            get => _charRowLength;
            set => _charRowLength = Math.Clamp(value, 1, 40);
        }
        public bool CharDescription { get; set; }
        public bool Stats { get; set; }
        public bool Exalts { get; set; } // should be sub option of Stats  true false
        public StatsOrder StatsOrder { get; set; } // should be sub option of stats maby list
        public bool AdditionalGoals { get; set; }
        public bool AdditionalStats { get; set; }
        public bool AdditionalBonuses { get; set; }
        public bool Equipment { get; set; }
        public bool Inventory { get; set; }
        public bool Quickslots { get; set; }
        // end of characters suboptions

        public bool Vaults { get; set; } // global
        public bool Potions { get; set; } // global
        public bool Materials { get; set; } // global
        public bool Spoils { get; set; }
        public bool Gifts { get; set; } // global

        public bool Pet { get; set; } // global
        public bool PetDescription { get; set; } // sub option of Pet
        public bool PetInventory { get; set; } // sub option of Pet

        public bool Shrink { get; set; } // global

        // Container Display Options
        public ContainerDisplayType InventoryDisplayType { get; set; } = ContainerDisplayType.Chest;
        public ContainerDisplayType EquipmentDisplayType { get; set; } = ContainerDisplayType.Chest;
        public ContainerDisplayType QuickslotsDisplayType { get; set; } = ContainerDisplayType.Chest;
        public ContainerDisplayType VaultsDisplayType { get; set; } = ContainerDisplayType.Chest;
        public ContainerDisplayType PotionsDisplayType { get; set; } = ContainerDisplayType.Chest;
        public ContainerDisplayType MaterialsDisplayType { get; set; } = ContainerDisplayType.Chest;
        public ContainerDisplayType SpoilsDisplayType { get; set; } = ContainerDisplayType.Chest;
        public ContainerDisplayType GiftsDisplayType { get; set; } = ContainerDisplayType.Chest;
        public ContainerDisplayType PetInventoryDisplayType { get; set; } = ContainerDisplayType.Chest;
    }
    public enum CharOrder
    {
        Id = 0,
        Class = 3,
        PlayTime = 4,
        BaseFame = 1,
        TotalFame = 2,
        MaxedStats = 5,
    }
    public enum StatsOrder 
    {
        Base = 0,
        LeftToMax = 1,
    }

    public enum ContainerDisplayType
    {
        Chest = 0,
        Summarized = 1,
    }
}