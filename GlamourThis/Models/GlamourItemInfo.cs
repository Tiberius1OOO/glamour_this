using System.Collections.Generic;

namespace GlamourThis.Models;

/// <summary>
/// A single resolved glamour item as shown in the overlay.
/// This is a plain data model built from raw game inventory data joined with the
/// static <c>Item</c> Excel sheet, so the UI never has to touch game memory directly.
/// </summary>
public sealed class GlamourItemInfo
{
    /// <summary>The item's row id in the <c>Item</c> Excel sheet (always the normal-quality id).</summary>
    public uint ItemId { get; init; }

    /// <summary>The display name resolved from the <c>Item</c> sheet.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The icon id used to draw the item's thumbnail.</summary>
    public ushort IconId { get; init; }

    /// <summary>The item level, used for sorting and display.</summary>
    public uint ItemLevel { get; init; }

    /// <summary>The level required to equip the item, used to bucket it into an expansion band.</summary>
    public uint RequiredLevel { get; init; }

    /// <summary>The equipment slot category row id (head, body, hands, ...), used for slot filtering.</summary>
    public uint EquipSlotCategory { get; init; }

    /// <summary>A short, human-readable slot label (for example "Head" or "Body").</summary>
    public string SlotLabel { get; init; } = string.Empty;

    /// <summary>True when this specific stored copy is high quality.</summary>
    public bool IsHighQuality { get; init; }

    /// <summary>
    /// True when the item is eligible to be condensed into an outfit glamour
    /// (the in-game "Outfit Glamour-ready Item" flag). Drives the set badge in the UI.
    /// </summary>
    public bool IsOutfitGlamourReady { get; init; }

    /// <summary>
    /// The outfit set id this item belongs to, or <c>null</c> when the item is not part of any set.
    /// Items sharing a set id are grouped together in the set view.
    /// </summary>
    public uint? SetId { get; init; }

    /// <summary>
    /// Every storage this item was found in, together with how many copies live there.
    /// A piece can appear in more than one place (for example the dresser and a bag),
    /// which is exactly what duplicate highlighting keys off of.
    /// </summary>
    public List<GlamourItemLocation> Locations { get; init; } = new();

    /// <summary>
    /// The Glamour Dresser slot indices (0-799) that currently hold a copy of this item.
    /// Restoring a copy targets one of these slots. Empty for items that are not in the dresser.
    /// </summary>
    public List<int> DresserSlots { get; init; } = new();

    /// <summary>The total number of copies of this item across every tracked storage.</summary>
    public int TotalCount
    {
        get
        {
            var total = 0;
            foreach (var location in Locations)
                total += location.Count;
            return total;
        }
    }

    /// <summary>True when more than one copy of this item exists anywhere we track.</summary>
    public bool IsDuplicate => TotalCount > 1;

    /// <summary>True when at least one copy currently sits in the Glamour Dresser.</summary>
    public bool IsInDresser => IsIn(GlamourLocation.GlamourDresser);

    /// <summary>True when at least one copy is held outside the dresser (bags or armoury chest).</summary>
    public bool IsInInventory => IsIn(GlamourLocation.Inventory) || IsIn(GlamourLocation.ArmouryChest);

    /// <summary>
    /// Returns true when at least one copy of this item is stored in the given location.
    /// </summary>
    /// <param name="location">The storage location to test for.</param>
    public bool IsIn(GlamourLocation location)
    {
        foreach (var entry in Locations)
            if (entry.Location == location)
                return true;
        return false;
    }

    /// <summary>
    /// A short, comma-separated summary of every place this item is currently held,
    /// for example "Dresser" or "Dresser, Bags". Used for the location column in the UI.
    /// </summary>
    public string LocationSummary
    {
        get
        {
            var seen = new List<string>(Locations.Count);
            foreach (var entry in Locations)
            {
                var label = entry.Location.ToLabel();
                if (!seen.Contains(label))
                    seen.Add(label);
            }
            return string.Join(", ", seen);
        }
    }
}

/// <summary>
/// A count of a single item within one specific storage location.
/// </summary>
/// <param name="Location">Which storage the copies were found in.</param>
/// <param name="Count">How many copies live in that storage.</param>
public readonly record struct GlamourItemLocation(GlamourLocation Location, int Count);
