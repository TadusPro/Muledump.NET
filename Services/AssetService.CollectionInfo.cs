using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RotMGAssetExtractor.Model;

namespace MDTadusMod.Services
{
    // New partial for collection-based grouping (replaces old SetType usage)
    public partial class AssetService
    {
        public record EquipmentCollectionInfo(int TypeId, int CollectionIcon);

        /// <summary>
        /// Returns (TypeId, CollectionIcon) pairs for the provided equipment ids.
        /// Non-equipment ids are skipped.
        /// </summary>
        public async Task<IReadOnlyList<EquipmentCollectionInfo>> GetEquipmentCollectionInfoAsync(IEnumerable<int> typeIds)
        {
            var list = new List<EquipmentCollectionInfo>();
            if (typeIds == null) return list;
            if (!await Ready()) return list;

            foreach (var id in typeIds.Distinct())
            {
                if (_itemModelsById.TryGetValue(id, out var model) && model is Equipment eq)
                {
                    list.Add(new EquipmentCollectionInfo(id, eq.CollectionIcon));
                }
            }
            return list;
        }
    }
}