using MDTadusMod.Data;
using RotMGAssetExtractor.Model;
using RotMGAssetExtractor.ModelHelpers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Diagnostics;
using System.Reflection;

namespace MDTadusMod.Services
{
    public partial class AssetService
    {
        // --- static shared state (unchanged pattern) ---
        private static Task? _initializationTask;
        private static readonly object _initLock = new();
        private static bool _initSucceeded = false;
        private static Exception? _initError;
        private static string? _dataDir; // <- set from DI

        private static Dictionary<int, string> _classIdToNameMap = new();
        private static Dictionary<int, Dictionary<string, int>> _maxStatsPerClass = new();
        private static Dictionary<int, string> _pcStatIdToNameMap = new();
        private static Dictionary<string, int> _pcStatNameToIdMap = new();
        private static Dictionary<int, FameBonus> _fameBonuses = new();
        private static Dictionary<int, object> _itemModelsById = new();

        // DI ctor: capture the chosen data directory
        public AssetService(IAppPaths paths)
        {
            _dataDir ??= paths.DataDir; // set once for the static init
            Initialize();
        }

        private void Initialize()
        {
            lock (_initLock)
            {
                if (_initializationTask != null) return;

                _initializationTask = Task.Run(async () =>
                {
                    try
                    {
                        // Use the installer-selected data dir
                        await RotMGAssetExtractor.RotMGAssetExtractor.InitAsync(_dataDir!);
                        await BuildAssetData();
                        _initSucceeded = true;
                    }
                    catch (Exception ex)
                    {
                        _initError = ex;
                        _initSucceeded = false;
                        Debug.WriteLine("[AssetService] Receiving GameData failed: " + ex);
                    }
                });
            }
        }

        private static async Task<bool> Ready()
        {
            var t = _initializationTask;
            if (t == null) return false;
            try { await t; } catch { }
            return _initSucceeded;
        }

        private static Task BuildAssetData()
        {
            _maxStatsPerClass = new();
            _classIdToNameMap = new();
            _itemModelsById = new();
            _pcStatIdToNameMap = new();
            _pcStatNameToIdMap = new();
            _fameBonuses = new();

            var itemTypes = new[] { "Player", "Equipment", "Skin", "Dye", "Emote", "Entrance", "PetSkin", "PetAbility" };
            foreach (var itemType in itemTypes)
            {
                if (RotMGAssetExtractor.RotMGAssetExtractor.BuildModelsByType.TryGetValue(itemType, out var itemObjects))
                {
                    foreach (var item in itemObjects.Cast<RotMGAssetExtractor.Model.Object>())
                    {
                        _itemModelsById[item.type] = item;

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
                                { "Vitality", player.HpRegen.Max }, // HpRegen -> Vitality
                                { "Wisdom", player.MpRegen.Max }    // MpRegen -> Wisdom
                            };
                            _maxStatsPerClass[player.type] = classStats;
                            _classIdToNameMap[player.type] = player.id;
                        }
                    }
                }
            }

            if (RotMGAssetExtractor.RotMGAssetExtractor.BuildModelsByType.TryGetValue("PlayerStat", out var statObjects))
            {
                foreach (var stat in statObjects.Cast<PlayerStat>())
                {
                    var statName = !string.IsNullOrEmpty(stat.displayName) ? stat.displayName : stat.id;
                    if (stat.dungeon) statName = stat.dungeonId;
                    _pcStatIdToNameMap[stat.index] = statName;
                    if (!string.IsNullOrEmpty(stat.id))
                        _pcStatNameToIdMap[stat.id] = stat.index;
                }
                Debug.WriteLine($"[AssetService] Loaded {_pcStatIdToNameMap.Count} PC stat names.");
            }
            else
            {
                Debug.WriteLine("[AssetService] 'PlayerStat' models not found.");
            }

            if (RotMGAssetExtractor.RotMGAssetExtractor.BuildModelsByType.TryGetValue("FameBonus", out var fameBonusObjects))
            {
                foreach (var bonus in fameBonusObjects.Cast<FameBonus>())
                    _fameBonuses[bonus.code] = bonus;
                Debug.WriteLine($"[AssetService] Loaded {_fameBonuses.Count} FameBonuses.");
            }
            else
            {
                Debug.WriteLine("[AssetService] 'FameBonus' models not found.");
            }

            if (_itemModelsById.Count > 0)
                Debug.WriteLine($"[AssetService] Loaded {_itemModelsById.Count} textures.");
            else
                Debug.WriteLine("[AssetService] 'texture' models not found.");

            return Task.CompletedTask;
        }

        public static async Task<string> GetPetAbilityNameById(int id)
        {
            if (!await Ready()) return "Unknown";
            if (_itemModelsById.TryGetValue(id, out var m) && m is RotMGAssetExtractor.Model.PetAbility pa)
                return pa.id;
            return $"Ability #{id}";
        }

        public static async Task<string> GetPetSkinDisplayNameById(int skinId)
        {
            if (!await Ready()) return "Unknown";
            if (_itemModelsById.TryGetValue(skinId, out var m) && m is PetSkin ps)
                return ps.DisplayId;
            return "Unknown";
        }

        public static async Task<bool> IsStatMaxed(Character character, string statName)
        {
            if (!await Ready() || character == null) return false;

            var maxStats = await GetMaxStatsForClass(character.ObjectType);
            if (maxStats == null) return false;

            var statMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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

            if (!statMap.TryGetValue(statName, out var mapped)) return false;
            if (!maxStats.TryGetValue(mapped, out var max)) return false;

            int current = statName.ToLowerInvariant() switch
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
            return current >= max;
        }

        public static async Task<int> GetMaxedStatsCount(Character character)
        {
            if (!await Ready() || character == null) return 0;
            var names = new[] { "health", "magic", "attack", "defense", "speed", "dexterity", "vitality", "wisdom" };
            var count = 0;
            foreach (var n in names)
                if (await IsStatMaxed(character, n)) count++;
            return count;
        }

        public static async Task<Dictionary<string, int>?> GetMaxStatsForClass(int classType)
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
            await (_initializationTask ?? Task.CompletedTask);

            if (_pcStatIdToNameMap.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name))
                return name;

            // Fallback: find the string id whose index matches, then humanize it
            var kv = _pcStatNameToIdMap.FirstOrDefault(p => p.Value == id);
            if (!string.IsNullOrWhiteSpace(kv.Key))
            {
                var friendly = HumanizeStatKey(kv.Key);
                _pcStatIdToNameMap[id] = friendly; // cache for future calls
                return friendly;
            }

            return $"#{id}";
        }

        private static string HumanizeStatKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return "Stat";

            // Replace separators
            var s = key.Replace('_', ' ').Replace('-', ' ');

            // Insert spaces before capitals in CamelCase
            var sb = new System.Text.StringBuilder(s.Length * 2);
            char prev = '\0';
            foreach (var c in s)
            {
                if (char.IsUpper(c) && prev != '\0' && prev != ' ' && !char.IsUpper(prev))
                    sb.Append(' ');
                sb.Append(c);
                prev = c;
            }

            // Normalize spacing and decode any HTML entities
            var normalized = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
            return System.Net.WebUtility.HtmlDecode(normalized);
        }

        public static async Task<Dictionary<string, int>> GetPCStatNameToIdMap()
        {
            await (_initializationTask ?? Task.CompletedTask);
            return _pcStatNameToIdMap;
        }

        public static async Task<FameBonus?> GetFameBonusByCode(int code)
        {
            await (_initializationTask ?? Task.CompletedTask);
            _fameBonuses.TryGetValue(code, out var bonus);
            return bonus;
        }

        public static async Task<ICollection<FameBonus>> GetAllFameBonuses()
        {
            await (_initializationTask ?? Task.CompletedTask);
            return _fameBonuses.Values;
        }

        public async Task<string?> GetItemImageAsBase64Async(int itemId)
        {
            if (!await Ready())
            {
                Debug.WriteLine($"[AssetService] Init not complete. Cannot retrieve item image {itemId}");
                return null;
            }

            if (!_itemModelsById.TryGetValue(itemId, out var itemModel))
            {
                Debug.WriteLine($"[AssetService] Item model not found for ID: {itemId}");
                return null;
            }

            ITexture? texture = null;
            var modelType = itemModel.GetType();

            var animatedTextureProp = modelType.GetProperty("AnimatedTexture");
            if (animatedTextureProp?.GetValue(itemModel) is ITexture at && !string.IsNullOrEmpty(at.File))
                texture = at;

            if (texture == null)
            {
                var textureProp = modelType.GetProperty("Texture");
                if (textureProp?.GetValue(itemModel) is ITexture t)
                    texture = t;
            }

            if (texture == null || string.IsNullOrEmpty(texture.File))
            {
                Debug.WriteLine($"[AssetService] Texture missing/invalid for item {itemId} ({itemModel.GetType().Name})");
                return null;
            }

            var image = RotMGAssetExtractor.Flatc.ImageBuffer.GetImage(texture, itemId);
            if (image == null) return null;

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
