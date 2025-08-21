using System.Text.Json.Serialization;

namespace MDTadusMod.Data;

public class GlobalSettings
{
    [SettingDescription("Show additional display options for item containers.")]
    public bool ShowExtendedItemContainerDisplay { get; set; } = false;
    
    [SettingDescription("Item summaries will group by their rarity.")]
    public bool GroupRarities { get; set; } = false;
    
    [SettingDescription("Automatically check for application updates.")]
    public bool CheckForUpdates { get; set; } = true;

    [SettingDescription("Pixel width of one card/container column. Affects how many fit per row.")]
    public int CardWidthPx { get; set; } = 180;
}

[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class SettingDescriptionAttribute : Attribute
{
    public string Description { get; }

    public SettingDescriptionAttribute(string description)
    {
        Description = description;
    }
}