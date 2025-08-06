using MDTadusMod.Data;
using RotMGAssetExtractor.Model;
using System.Diagnostics;
using RotMGAssetExtractor.Flatc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Reflection;
using RotMGAssetExtractor.ModelHelpers;

namespace MDTadusMod.Services
{
    public class AssetService
    {
        private static Task _initializationTask;
        private static readonly object _initLock = new();
        private static bool _initSucceeded = false;
        private static Exception _initError;

        private static Dictionary<int, string> _classIdToNameMap;
        private static Dictionary<int, Dictionary<string, int>> _maxStatsPerClass;
        private static Dictionary<int, string> _pcStatIdToNameMap;
        private static Dictionary<string, int> _pcStatNameToIdMap;
        private static Dictionary<int, FameBonus> _fameBonuses;
        private static Dictionary<int, object> _itemModelsById;


        public AssetService() => Initialize();

        private void Initialize()
        {
            lock (_initLock)
            {
                if (_initializationTask != null) return;

                _initializationTask = Task.Run(async () =>
                {
                    try
                    {
                        await RotMGAssetExtractor.RotMGAssetExtractor
                            .InitAsync(Microsoft.Maui.Storage.FileSystem.AppDataDirectory);
                        await BuildAssetData();
                        _initSucceeded = true;
                    }
                    catch (Exception ex)
                    {
                        _initError = ex;
                        _initSucceeded = false;
                        Debug.WriteLine("[AssetService] Reciving GameData Failed: " + ex);
                    }
                });
            }
        }
        private static async Task<bool> Ready()
        {
            if (_initializationTask == null) return false;
            try { await _initializationTask; } catch { }
            return _initSucceeded;
        }


        private static Task BuildAssetData()
        {
            // Initialize dictionaries
            _maxStatsPerClass = new Dictionary<int, Dictionary<string, int>>();
            _classIdToNameMap = new Dictionary<int, string>();
            _itemModelsById = new Dictionary<int, object>();
            _pcStatIdToNameMap = new Dictionary<int, string>();
            _pcStatNameToIdMap = new Dictionary<string, int>();
            _fameBonuses = new Dictionary<int, FameBonus>();

            // Build Item and Player Data
            var itemTypes = new[] { "Player", "Equipment", "Skin", "Dye", "Emote", "Entrance", "PetSkin", "PetAbility" };
            foreach (var itemType in itemTypes)
            {
                if (RotMGAssetExtractor.RotMGAssetExtractor.BuildModelsByType.TryGetValue(itemType, out var itemObjects))
                {
                    foreach (var item in itemObjects.Cast<RotMGAssetExtractor.Model.Object>())
                    {
                        _itemModelsById[item.type] = item;

                        // Special handling for Player types
                        if (item is Player player)
                        {
                            var classStats = new Dictionary<string, int>
                            {
                                { "MaxHitPoints", player.MaxHitPoints.Max },
                                { "MaxMagicPoints", player.MaxMagicPoints.Max },
                                { "Attack", player.Attack.Max },
                                { "Defense", player.Defense.Max },
                                { "Speed", player.Speed.Max },
                                { "Dexterity", player.Dexterity.Max },
                                { "Vitality", player.HpRegen.Max }, // Assuming HpRegen maps to Vitality
                                { "Wisdom", player.MpRegen.Max }   // Assuming MpRegen maps to Wisdom
                            };
                            _maxStatsPerClass[player.type] = classStats;
                            _classIdToNameMap[player.type] = player.id;
                        }
                    }
                }
            }

            // Build PC Stat Name Data
            if (RotMGAssetExtractor.RotMGAssetExtractor.BuildModelsByType.TryGetValue("PlayerStat", out var statObjects))
            {
                var stats = statObjects.Cast<PlayerStat>();
                foreach (var stat in stats)
                {
                    // Prioritize displayName, but fall back to id if it's empty
                    var statName = !string.IsNullOrEmpty(stat.displayName) ? stat.displayName : stat.id;
                    if (stat.dungeon)
                    {
                        statName = stat.dungeonId;
                    }
                    _pcStatIdToNameMap[stat.index] = statName;
                    if (!string.IsNullOrEmpty(stat.id))
                    {
                        _pcStatNameToIdMap[stat.id] = stat.index;
                    }
                }
                Debug.WriteLine($"[AssetService] Loaded {_pcStatIdToNameMap.Count} PC stat names.");
            }
            else
            {
                Debug.WriteLine("[AssetService] 'PlayerStat' models not found in asset data.");
            }

            // Build Fame Bonus Data
            if (RotMGAssetExtractor.RotMGAssetExtractor.BuildModelsByType.TryGetValue("FameBonus", out var fameBonusObjects))
            {
                var bonuses = fameBonusObjects.Cast<FameBonus>();
                foreach (var bonus in bonuses)
                {
                    _fameBonuses[bonus.code] = bonus;
                }
                Debug.WriteLine($"[AssetService] Loaded {_fameBonuses.Count} FameBonuses.");
            }
            else
            {
                Debug.WriteLine("[AssetService] 'FameBonus' models not found in asset data.");
            }

            if (_itemModelsById.Count > 0)
            {
                Debug.WriteLine($"[AssetService] Loaded {_itemModelsById.Count} textures.");
            }
            else
            {
                Debug.WriteLine("[AssetService] 'texture' models not found in asset data.");

            }

            return Task.CompletedTask;
        }

        public static async Task<string> GetPetAbilityNameById(int id)
        {
            if (!await Ready()) return "Unknown";

            if (_itemModelsById.TryGetValue(id, out var itemModel) && itemModel is RotMGAssetExtractor.Model.PetAbility petAbility)
            {
                return petAbility.id;
            }
            return $"Ability #{id}";
        }

        public static async Task<string> GetPetSkinDisplayNameById(int skinId)
        {
            if (!await Ready()) return "Unknown";

            if (_itemModelsById.TryGetValue(skinId, out var itemModel) && itemModel is PetSkin petSkin)
            {
                return petSkin.DisplayId; // 'id' holds the display name for PetSkin models
            }

            return "Unknown";
        }

        public static async Task<bool> IsStatMaxed(Character character, string statName)
        {
            if (!await Ready() || character == null) return false;
            

            var maxStats = await GetMaxStatsForClass(character.ObjectType);
            if (maxStats == null)
            {
                return false;
            }

            // Map lowercase stat names from conditions to PascalCase keys in the dictionary
            var statMap = new Dictionary<string, string>
            {
                { "health", "MaxHitPoints" },
                { "magic", "MaxMagicPoints" },
                { "attack", "Attack" },
                { "defense", "Defense" },
                { "speed", "Speed" },
                { "dexterity", "Dexterity" },
                { "vitality", "Vitality" },
                { "wisdom", "Wisdom" }
            };

            if (!statMap.TryGetValue(statName, out var mappedStatName))
            {
                return false;
            }

            if (!maxStats.TryGetValue(mappedStatName, out var maxStatValue))
            {
                return false;
            }

            int currentStatValue = statName switch
            {
                "health" => character.MaxHitPoints,
                "magic" => character.MaxMagicPoints,
                "attack" => character.Attack,
                "defense" => character.Defense,
                "speed" => character.Speed,
                "dexterity" => character.Dexterity,
                "vitality" => character.Vitality,
                "wisdom" => character.Wisdom,
                _ => -1
            };

            bool isMaxed = currentStatValue >= maxStatValue;
            return isMaxed;
        }

        public static async Task<int> GetMaxedStatsCount(Character character)
        {
            if (!await Ready() || character == null)
            {
                return 0;
            }

            int maxedStatsCount = 0;
            var statNames = new[] { "health", "magic", "attack", "defense", "speed", "dexterity", "vitality", "wisdom" };

            foreach (var statName in statNames)
            {
                if (await IsStatMaxed(character, statName))
                {
                    maxedStatsCount++;
                }
            }

            return maxedStatsCount;
        }

        public static async Task<Dictionary<string, int>> GetMaxStatsForClass(int classType)
        {
            if (!await Ready()) return null;
            _maxStatsPerClass.TryGetValue(classType, out var stats);
            return stats;
        }

        public static async Task<Dictionary<int, Dictionary<string, int>>> GetMaxStatsPerClass()
        {
            if (!await Ready()) return new();
            return _maxStatsPerClass;
        }

        public static async Task<string> GetClassNameById(int id)
        {
            if (!await Ready()) return "Unknown";
            return _classIdToNameMap.GetValueOrDefault(id, "Unknown");

        }

        public static async Task<string> GetPCStatName(int id)
        {
            await _initializationTask;
            return _pcStatIdToNameMap.GetValueOrDefault(id, $"#{id}");
        }

        public static async Task<Dictionary<string, int>> GetPCStatNameToIdMap()
        {
            await _initializationTask;
            return _pcStatNameToIdMap;
        }

        public static async Task<FameBonus> GetFameBonusByCode(int code)
        {
            await _initializationTask;
            _fameBonuses.TryGetValue(code, out var bonus);
            return bonus; // Will be null if not found
        }
        public static async Task<ICollection<FameBonus>> GetAllFameBonuses()
        {
            await _initializationTask;
            return _fameBonuses.Values;
        }

        public async Task<string> GetItemImageAsBase64Async(int itemId)
        {
            if (!await Ready())
            {
                Debug.WriteLine("[AssetService] Initialization not complete. Cannot retrieve item image " + itemId);
                return null;
            }
            if(itemId == 9505)
            {
                Debug.Write("[AssetService] Starting imggathering from character");
            }


            if (!_itemModelsById.TryGetValue(itemId, out var itemModel))
            {
                Debug.WriteLine($"[AssetService] Warning: Item model not found for ID: {itemId}");
                return null;
            }

            ITexture texture = null;
            var modelType = itemModel.GetType();

            // Prioritize AnimatedTexture property if it exists
            PropertyInfo animatedTextureProp = modelType.GetProperty("AnimatedTexture");
            if (animatedTextureProp != null)
            {
                var animatedTexture = animatedTextureProp.GetValue(itemModel) as ITexture;
                // Check that the texture object exists and its File property is not empty.
                if (animatedTexture != null && !string.IsNullOrEmpty(animatedTexture.File))
                {
                    texture = animatedTexture;
                }
            }

            // Fallback to Texture property if AnimatedTexture is not found or is null
            if (texture == null)
            {
                PropertyInfo textureProp = modelType.GetProperty("Texture");
                if (textureProp != null)
                {
                    texture = textureProp.GetValue(itemModel) as ITexture;
                }
            }

            if (texture == null)
            {
                Debug.WriteLine($"[AssetService] Warning: Texture object is null for item ID: {itemId} (Type: {itemModel.GetType().Name})");
                return null;
            }

            // Add a resiliency check to ensure the texture data is valid before proceeding.
            if (string.IsNullOrEmpty(texture.File))
            {
                Debug.WriteLine($"[AssetService] Warning: Texture for item ID {itemId} has an invalid (null or empty) File property. Skipping image retrieval.");
                return null;
            }

            var image = ImageBuffer.GetImage(texture, itemId);

            if (image == null)
            {
                //Debug.WriteLine($"[AssetService] Warning: ImageBuffer returned null for item ID: {itemId}");
                return null;
            }

            return ConvertImageToBase64(image);
        }

        private static string ConvertImageToBase64(Image<Rgba32> image)
        {
            using var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return $"data:image/png;base64,{Convert.ToBase64String(ms.ToArray())}";
        }
    }
}
