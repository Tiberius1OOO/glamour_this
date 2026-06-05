using System.Collections.Generic;
using System.Linq;

namespace GlamourThis.Models;

/// <summary>
/// An outfit glamour set: the group of pieces the game allows to be condensed into a
/// single dresser slot (for example "Star Crew Attire" = jacket, gloves, trousers, boots).
/// Built by <see cref="GlamourThis.Services.OutfitSetService"/> from the
/// <c>MirageStoreSetItem</c> sheet joined with what the player currently owns.
/// </summary>
public sealed class OutfitSetInfo
{
    /// <summary>The set's row id in the <c>MirageStoreSetItem</c> sheet.</summary>
    public uint SetId { get; init; }

    /// <summary>A display name for the set, derived from the set's representative item.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The icon id used to represent the whole set in the UI.</summary>
    public ushort IconId { get; init; }

    /// <summary>
    /// Every piece that can belong to this set, in slot order, whether or not the player owns it.
    /// Pieces the player does not own are still listed so missing slots are obvious.
    /// </summary>
    public List<OutfitSetPiece> Pieces { get; init; } = new();

    /// <summary>How many distinct pieces of the set the player currently owns.</summary>
    public int OwnedCount => Pieces.Count(piece => piece.IsOwned);

    /// <summary>The total number of pieces that make up the complete set.</summary>
    public int TotalCount => Pieces.Count;

    /// <summary>True when the player owns every piece, so the set can be stored as one outfit.</summary>
    public bool IsComplete => TotalCount > 0 && OwnedCount == TotalCount;

    /// <summary>
    /// True when the player already has the set condensed into a single outfit slot in the dresser.
    /// </summary>
    public bool IsStoredAsOutfit { get; set; }
}

/// <summary>
/// One slot's worth of an outfit set, paired with the resolved item that fills it (if owned).
/// </summary>
public sealed class OutfitSetPiece
{
    /// <summary>The item id of this piece in the set.</summary>
    public uint ItemId { get; init; }

    /// <summary>The piece's display name from the <c>Item</c> sheet.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>A short slot label for the piece (for example "Hands").</summary>
    public string SlotLabel { get; init; } = string.Empty;

    /// <summary>
    /// The resolved owned item backing this slot, or <c>null</c> when the player does not own it.
    /// </summary>
    public GlamourItemInfo? OwnedItem { get; init; }

    /// <summary>True when the player owns at least one copy of this piece.</summary>
    public bool IsOwned => OwnedItem is not null;
}
