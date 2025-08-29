using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RotMGAssetExtractor.Model;

namespace MDTadusMod.Services
{
    public partial class AssetService
    {
        public record EquipmentSetInfo(int TypeId, int SetType, string SetName);

        public async Task<IReadOnlyList<EquipmentSetInfo>> GetEquipmentSetInfoAsync(IEnumerable<int> typeIds)
        {
            if (!await Ready()) return new List<EquipmentSetInfo>();
            var result = new List<EquipmentSetInfo>();
            foreach (var id in typeIds.Distinct())
            {
                if (_itemModelsById.TryGetValue(id, out var m) && m is Equipment eq)
                {
                    result.Add(new EquipmentSetInfo(id, eq.SetType, eq.SetName ?? string.Empty));
                }
            }
            return result;
        }
    }
}