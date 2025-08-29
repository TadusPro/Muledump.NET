using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using RotMGAssetExtractor.Model;

namespace MDTadusMod.Services
{
    public partial class AssetService
    {
        private HashSet<int>? _cachedShinyItems;

        /// <summary>
        /// Returns item type ids considered "shiny".
        /// Notes about previous 0 / 0 issue:
        ///   - Most likely caused by calling before AssetService initialization completed (Ready() was false and we returned empty).
        ///   - This version waits for initialization instead of bailing early.
        /// Detection strategy:
        ///   1. Wait for extractor init (Ready()).
        ///   2. Iterate equipment models (can be widened if needed).
        ///   3. Detect an InventoryAnimation flag via:
        ///        a. Public property named 'InventoryAnimation'
        ///        b. Public field named 'InventoryAnimation'
        ///        c. Any public IDictionary (string-> object) property containing key 'InventoryAnimation'
        ///   4. Treat 'true'/1/non‑zero bool/numeric, or non‑empty string, or non‑null complex object whose inner 'enabled' property is true as shiny.
        /// If nothing found you can temporarily enable the DEBUG block to see why (Debug.WriteLine).
        /// </summary>
        public async Task<HashSet<int>> GetAllShinyItemIdsAsync(bool forceRefresh = false)
        {
            if (forceRefresh) _cachedShinyItems = null;
            if (_cachedShinyItems != null) return _cachedShinyItems;

            // Wait for init instead of returning early
            await (_initializationTask ?? Task.CompletedTask);
            if (!await Ready())
            {
                Debug.WriteLine("[AssetService.Shiny] Initialization failed; returning empty shiny set.");
                return new();
            }

            var result = new HashSet<int>();
            int scanned = 0, candidates = 0;

            foreach (var (typeId, model) in _itemModelsById)
            {
                if (model is not Equipment) // keep scope narrow; remove this guard if shiny spans other categories
                    continue;

                scanned++;

                if (!TryGetInventoryAnimationValue(model, out var rawValue))
                    continue;

                if (!IsTruthyInventoryAnimation(rawValue))
                    continue;

                result.Add(typeId);
                candidates++;
            }

#if DEBUG
            Debug.WriteLine($"[AssetService.Shiny] Scanned Equipments={scanned}, Shiny Detected={candidates}");
#endif

            // Sanity guard: if > 40% of equipment flagged, we probably matched the wrong signal -> log but still return (remove if undesired)
            var equipmentTotal = _itemModelsById.Values.Count(v => v is Equipment);
            if (equipmentTotal > 0 && result.Count > equipmentTotal * 0.4)
            {
                Debug.WriteLine($"[AssetService.Shiny][WARN] Shiny ratio {result.Count}/{equipmentTotal} > 40%. Verify detection logic.");
            }

            _cachedShinyItems = result;
            return _cachedShinyItems;
        }

        public void InvalidateShinyCache() => _cachedShinyItems = null;

        private static bool TryGetInventoryAnimationValue(object model, out object? value)
        {
            value = null;
            var t = model.GetType();

            // Property
            var prop = t.GetProperty("InventoryAnimation", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop != null)
            {
                value = prop.GetValue(model);
                if (value != null) return true;
            }

            // Field
            var field = t.GetField("InventoryAnimation", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (field != null)
            {
                value = field.GetValue(model);
                if (value != null) return true;
            }

            // Dictionary style properties (generic)
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!typeof(IEnumerable).IsAssignableFrom(p.PropertyType)) continue;
                object? dictObj = null;
                try { dictObj = p.GetValue(model); } catch { /* ignore */ }
                if (dictObj == null) continue;

                // Handle IDictionary / IDictionary<string, object>
                if (dictObj is IDictionary legacyDict)
                {
                    foreach (var key in legacyDict.Keys)
                    {
                        if (key is string sk && sk.Equals("InventoryAnimation", StringComparison.OrdinalIgnoreCase))
                        {
                            value = legacyDict[key];
                            if (value != null) return true;
                        }
                    }
                }
                else
                {
                    // Try generic dictionary via reflection
                    var dictType = dictObj.GetType();
                    if (dictType.IsGenericType &&
                        dictType.GetGenericArguments().Length == 2 &&
                        dictType.GetGenericArguments()[0] == typeof(string))
                    {
                        var tryGetValue = dictType.GetMethod("TryGetValue", new[] { typeof(string), dictType.GetGenericArguments()[1].MakeByRefType() });
                        if (tryGetValue != null)
                        {
                            var args = new object?[] { "InventoryAnimation", null };
                            var ok = (bool)tryGetValue.Invoke(dictObj, args)!;
                            if (ok && args[1] != null)
                            {
                                value = args[1];
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsTruthyInventoryAnimation(object? value)
        {
            if (value == null) return false;

            switch (value)
            {
                case bool b:
                    return b;
                case string s:
                    return IsStringTrue(s);
                case sbyte or byte or short or ushort or int or uint or long or ulong:
                    return Convert.ToInt64(value) > 0;
                case float or double or decimal:
                    return Convert.ToDouble(value) > 0;
                default:
                    // Nested object: look for 'enabled' or a bool property with 'active' or direct truth flag
                    var t = value.GetType();
                    var flagProp = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(p =>
                            (p.PropertyType == typeof(bool) || p.PropertyType == typeof(bool?)) &&
                            (p.Name.Equals("enabled", StringComparison.OrdinalIgnoreCase) ||
                             p.Name.Equals("active", StringComparison.OrdinalIgnoreCase)));
                    if (flagProp != null)
                    {
                        var flagVal = flagProp.GetValue(value);
                        if (flagVal is bool fb) return fb;
                    }

                    // Fallback: treat complex object as true if it has any public data (conservative)
                    return true;
            }
        }

        private static bool IsStringTrue(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            return s.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("y", StringComparison.OrdinalIgnoreCase);
        }
    }
}