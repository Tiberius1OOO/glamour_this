namespace GlamourThis.Models;

/// <summary>
/// Identifies the in-game storage a glamour item was read from.
/// Used to tell the user where a piece currently lives and to decide which
/// pull-out / put-back actions are valid for it.
/// </summary>
public enum GlamourLocation
{
    /// <summary>The Glamour Dresser (Glamour Chest), the main storage this plugin manages.</summary>
    GlamourDresser,

    /// <summary>The Armoire (Cabinet), which stores certain seasonal and reward outfits.</summary>
    Armoire,

    /// <summary>The player's normal inventory bags. Tracked only to detect duplicates.</summary>
    Inventory,

    /// <summary>The Armoury Chest (equipped-gear storage). Tracked only to detect duplicates.</summary>
    ArmouryChest,
}

/// <summary>
/// Display helpers for <see cref="GlamourLocation"/>.
/// </summary>
public static class GlamourLocationExtensions
{
    /// <summary>
    /// Returns a short, user-facing label for a storage location (for example "Dresser" or "Bags").
    /// </summary>
    /// <param name="location">The location to label.</param>
    public static string ToLabel(this GlamourLocation location) => location switch
    {
        GlamourLocation.GlamourDresser => "Dresser",
        GlamourLocation.Armoire => "Armoire",
        GlamourLocation.Inventory => "Bags",
        GlamourLocation.ArmouryChest => "Armoury",
        _ => "Unknown",
    };
}
