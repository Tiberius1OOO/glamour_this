using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using GlamourThis.Models;
using GlamourThis.Services;
using Dalamud.Bindings.ImGui;

namespace GlamourThis.Windows;

/// <summary>
/// The main Glamour This overlay: a searchable, sortable browser over the player's dresser,
/// armoire and (optionally) the rest of their inventory.
///
/// The window is purely a view over the snapshot held by <see cref="InventoryDataService"/>; it
/// never reads game memory itself. User actions are routed to <see cref="DresserActionService"/>,
/// which enforces the "must be at a dresser" rules.
/// </summary>
public sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;
    private readonly InventoryDataService data;
    private readonly DresserActionService actions;

    private string searchText = string.Empty;
    private SortMode sortMode = SortMode.Name;
    private int slotFilterIndex;
    private int locationFilterIndex;
    private int expansionFilterIndex;
    private bool duplicatesOnly;
    private bool glamourReadyOnly;

    private static readonly string[] SlotFilterOptions =
    {
        "All slots", "Weapon", "Shield", "Head", "Body", "Hands", "Legs", "Feet",
        "Earrings", "Necklace", "Bracelets", "Ring",
    };

    private static readonly string[] LocationFilterOptions =
    {
        "Dresser", "Armoire", "Bags / Armoury", "Everywhere",
    };

    private static readonly string[] ExpansionFilterOptions =
    {
        "All expansions", "A Realm Reborn", "Heavensward", "Stormblood",
        "Shadowbringers", "Endwalker", "Dawntrail", "Special (Lv 1)",
    };

    private static readonly ExpansionBand[] ExpansionFilterBands =
    {
        ExpansionBand.ARealmReborn, ExpansionBand.Heavensward, ExpansionBand.Stormblood,
        ExpansionBand.Shadowbringers, ExpansionBand.Endwalker, ExpansionBand.Dawntrail, ExpansionBand.Special,
    };

    /// <summary>Orange used to flag Armoire-only items, which cannot be pulled like dresser items.</summary>
    private static readonly Vector4 ArmoireColor = new(1f, 0.65f, 0.2f, 1f);

    /// <summary>
    /// Creates the main window.
    /// </summary>
    /// <param name="plugin">The owning plugin, used for shared services and configuration.</param>
    public MainWindow(Plugin plugin) : base("Glamour This###GlamourThisMain")
    {
        this.plugin = plugin;
        configuration = plugin.Configuration;
        data = plugin.InventoryDataService;
        actions = plugin.DresserActionService;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 360),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Size = new Vector2(760, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    /// <summary>No unmanaged resources to release; present to satisfy the windowing contract.</summary>
    public void Dispose()
    {
    }

    /// <summary>Draws the toolbar and the Items / Sets tabs.</summary>
    public override void Draw()
    {
        DrawToolbar();
        ImGui.Separator();

        using var tabBar = new TabBarScope("GlamourThisTabs");
        if (!tabBar.Success)
            return;

        if (ImGui.BeginTabItem("Items"))
        {
            DrawItemsTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Sets"))
        {
            DrawSetsTab();
            ImGui.EndTabItem();
        }
    }

    /// <summary>
    /// Draws the top toolbar: search box, slot filter, duplicate / glamour-ready toggles, sort
    /// selector and a manual refresh button.
    /// </summary>
    private void DrawToolbar()
    {
        ImGui.SetNextItemWidth(220f);
        ImGui.InputTextWithHint("##search", "Search by name...", ref searchText, 256);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(130f);
        ImGui.Combo("##location", ref locationFilterIndex, LocationFilterOptions, LocationFilterOptions.Length);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Which storage to list items from. Defaults to the Glamour Dresser.");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(120f);
        ImGui.Combo("##slot", ref slotFilterIndex, SlotFilterOptions, SlotFilterOptions.Length);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f);
        ImGui.Combo("##expansion", ref expansionFilterIndex, ExpansionFilterOptions, ExpansionFilterOptions.Length);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Filter by expansion, approximated from each item's required level.\nLevel 1 cosmetic / event gear is grouped under \"Special (Lv 1)\".");

        ImGui.SameLine();
        ImGui.Checkbox("Duplicates", ref duplicatesOnly);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Show only items you hold more than one of (across the dresser, bags and armoury).");

        ImGui.SameLine();
        ImGui.Checkbox("Outfit-ready", ref glamourReadyOnly);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Items tab: show only Outfit Glamour-ready pieces.\nSets tab: show only complete sets you can still store as an outfit.");

        ImGui.SameLine();
        var includeArmoire = configuration.IncludeArmoire;
        if (ImGui.Checkbox("Armoire", ref includeArmoire))
        {
            configuration.IncludeArmoire = includeArmoire;
            configuration.Save();
            plugin.RefreshData();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Include Armoire (cabinet) items. Turn off to hide the endless armoire and focus on the dresser.");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(140f);
        var sortIndex = (int)sortMode;
        if (ImGui.Combo("##sort", ref sortIndex, SortModeLabels, SortModeLabels.Length))
            sortMode = (SortMode)sortIndex;

        ImGui.SameLine();
        if (ImGui.Button("Refresh"))
            plugin.RefreshData();

        if (!actions.IsDresserOpen)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(dresser closed - actions disabled)");
        }
    }

    /// <summary>Draws the filtered, sorted item grid.</summary>
    private void DrawItemsTab()
    {
        var items = ApplyFilters(data.Items);

        ImGui.Text($"{items.Count} items");
        ImGui.SameLine();
        ImGui.TextDisabled($"of {data.Items.Count} owned");

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY |
                                      ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable("##items", 6, flags))
            return;

        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 0.4f);
        ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthStretch, 0.12f);
        ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthStretch, 0.16f);
        ImGui.TableSetupColumn("iLvl", ImGuiTableColumnFlags.WidthStretch, 0.08f);
        ImGui.TableSetupColumn("Count", ImGuiTableColumnFlags.WidthStretch, 0.08f);
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthStretch, 0.16f);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        foreach (var item in items)
            DrawItemRow(item);

        ImGui.EndTable();
    }

    /// <summary>Draws a single item row, including the duplicate highlight and action buttons.</summary>
    private void DrawItemRow(GlamourItemInfo item)
    {
        ImGui.TableNextRow();

        if (configuration.HighlightDuplicates && item.IsDuplicate)
        {
            var highlight = ImGui.GetColorU32(new Vector4(0.85f, 0.65f, 0.10f, 0.25f));
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, highlight);
        }

        ImGui.TableNextColumn();
        DrawIcon(item.IconId, configuration.IconSize);
        ImGui.SameLine();
        var label = item.IsHighQuality ? $"{item.Name} (HQ)" : item.Name;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        if (item.IsOutfitGlamourReady)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.45f, 0.8f, 1f, 1f), "[set]");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Outfit Glamour-ready Item");
        }

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(item.SlotLabel);

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        var armoireOnly = item.IsIn(GlamourLocation.Armoire) && !item.IsInDresser;
        if (armoireOnly)
            ImGui.TextColored(ArmoireColor, item.LocationSummary);
        else
            ImGui.TextUnformatted(item.LocationSummary);

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(item.ItemLevel > 0 ? item.ItemLevel.ToString() : "-");

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(item.TotalCount.ToString());

        ImGui.TableNextColumn();
        DrawItemActions(item);
    }

    /// <summary>
    /// Draws the pull-out button for a row, disabled when not at a dresser or when the item is not
    /// currently sitting in the dresser. Putting items back is intentionally left to the native dresser
    /// window, so no Store action is offered here.
    /// </summary>
    private void DrawItemActions(GlamourItemInfo item)
    {
        using (new DisabledScope(!actions.IsDresserOpen || !item.IsInDresser))
        {
            if (ImGui.SmallButton($"Pull##{item.ItemId}_{item.IsHighQuality}"))
                actions.RestoreItem(item);
        }
    }

    /// <summary>Draws the outfit-set view: one collapsible group per set with its pieces.</summary>
    private void DrawSetsTab()
    {
        var sets = data.Sets;
        var search = searchText.Trim();

        var visible = new List<OutfitSetInfo>();
        foreach (var set in sets)
        {
            if (search.Length > 0 && !set.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!MatchesSetFilter(set))
                continue;

            visible.Add(set);
        }

        ImGui.Text($"{visible.Count} sets");
        ImGui.SameLine();
        ImGui.TextDisabled($"of {sets.Count}");
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "complete = you own every piece of the set (so it can be stored as one outfit glamour).\n" +
                "stored = the set is already condensed into a single outfit slot in your dresser.\n" +
                "x/y = how many of the set's pieces you currently own.\n\n" +
                "With \"Outfit-ready\" ticked this lists only sets you own in full and have not stored yet,\n" +
                "and a set drops off the list once you have stored it as an outfit.");
        }

        if (!ImGui.BeginChild("##sets"))
        {
            ImGui.EndChild();
            return;
        }

        foreach (var set in visible)
            DrawSetGroup(set);

        ImGui.EndChild();
    }

    /// <summary>
    /// Decides whether a set is shown in the Sets tab. With the shared "Outfit-ready" toggle on, only
    /// sets that can be stored as an outfit right now are listed: those you own in full
    /// (<see cref="OutfitSetInfo.IsComplete"/>) and have not already condensed into a single slot
    /// (<see cref="OutfitSetInfo.IsStoredAsOutfit"/>). Pulling a piece into your bags keeps the set
    /// here because the piece is still owned, while storing the finished outfit removes it.
    /// </summary>
    /// <param name="set">The set to test against the active toggle.</param>
    private bool MatchesSetFilter(OutfitSetInfo set)
    {
        if (!glamourReadyOnly)
            return true;

        return set.IsComplete && !set.IsStoredAsOutfit;
    }

    /// <summary>Draws a single outfit set as a collapsible header with its pieces and actions.</summary>
    private void DrawSetGroup(OutfitSetInfo set)
    {
        var completion = $"{set.OwnedCount}/{set.TotalCount}";
        var header = set.IsComplete ? $"{set.Name}  -  {completion} (complete)" : $"{set.Name}  -  {completion}";

        var headerColor = set.IsComplete
            ? new Vector4(0.5f, 1f, 0.5f, 1f)
            : new Vector4(1f, 1f, 1f, 1f);

        ImGui.PushStyleColor(ImGuiCol.Text, headerColor);
        var open = ImGui.CollapsingHeader($"{header}###set{set.SetId}");
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(set.IsComplete
                ? "You own all pieces of this set."
                : $"You own {set.OwnedCount} of {set.TotalCount} pieces of this set.");
        }

        if (set.IsStoredAsOutfit)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.45f, 0.8f, 1f, 1f), "[stored]");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("This set is currently condensed into a single outfit slot in your dresser.");
        }

        if (!open)
            return;

        ImGui.Indent();

        if (set.IsStoredAsOutfit)
        {
            ImGui.TextDisabled("Stored as one outfit - restore it from the dresser window.");
        }
        else
        {
            ImGui.TextDisabled("Pull pulls one piece per click into your inventory.");
        }

        foreach (var piece in set.Pieces)
            DrawSetPiece(piece);

        ImGui.Unindent();
    }

    /// <summary>
    /// Draws one piece of a set: an owned/missing marker, the piece name, and a per-piece Pull button
    /// when the piece is sitting in the dresser. Each Pull moves exactly one piece.
    /// </summary>
    /// <param name="piece">The set piece to draw.</param>
    private void DrawSetPiece(OutfitSetPiece piece)
    {
        var owned = piece.OwnedItem;
        var inDresser = owned is not null && owned.IsInDresser;

        using (new DisabledScope(!actions.IsDresserOpen || !inDresser))
        {
            if (ImGui.SmallButton($"Pull##piece{piece.ItemId}") && owned is not null)
                actions.RestoreItem(owned);
        }

        ImGui.SameLine();

        var armoireOnly = owned is not null && owned.IsIn(GlamourLocation.Armoire) && !owned.IsInDresser && !owned.IsInInventory;

        Vector4 color;
        if (!piece.IsOwned)
            color = new Vector4(0.6f, 0.6f, 0.6f, 1f);
        else if (armoireOnly)
            color = ArmoireColor;
        else
            color = new Vector4(1f, 1f, 1f, 1f);

        var marker = piece.IsOwned ? "+" : "-";
        var suffix = armoireOnly ? "  (Armoire)" : string.Empty;
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(color, $"{marker} [{piece.SlotLabel}] {piece.Name}{suffix}");
        if (piece.IsOwned && ImGui.IsItemHovered())
            ImGui.SetTooltip($"In: {owned!.LocationSummary}");
    }

    /// <summary>
    /// Applies the active search text, slot filter and toggles to the snapshot, then sorts the
    /// result according to the selected <see cref="SortMode"/>.
    /// </summary>
    /// <param name="source">The full owned-item snapshot.</param>
    private List<GlamourItemInfo> ApplyFilters(IReadOnlyList<GlamourItemInfo> source)
    {
        var search = searchText.Trim();
        var slotLabel = slotFilterIndex == 0 ? null : SlotFilterOptions[slotFilterIndex];

        var result = new List<GlamourItemInfo>();
        foreach (var item in source)
        {
            if (!MatchesLocationFilter(item))
                continue;
            if (!MatchesExpansionFilter(item))
                continue;
            if (search.Length > 0 && !item.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                continue;
            if (slotLabel is not null && item.SlotLabel != slotLabel)
                continue;
            if (duplicatesOnly && !item.IsDuplicate)
                continue;
            if (glamourReadyOnly && !item.IsOutfitGlamourReady)
                continue;

            result.Add(item);
        }

        result.Sort(CompareItems);
        return result;
    }

    /// <summary>
    /// Returns true when the item belongs in the currently selected location filter. The default
    /// "Dresser" view is what most dresser-management work needs; the other options widen the scope.
    /// </summary>
    /// <param name="item">The item to test against the active location filter.</param>
    private bool MatchesLocationFilter(GlamourItemInfo item) => locationFilterIndex switch
    {
        0 => item.IsInDresser,
        1 => item.IsIn(GlamourLocation.Armoire),
        2 => item.IsInInventory,
        _ => true,
    };

    /// <summary>
    /// Returns true when the item falls in the selected expansion band. Index 0 ("All expansions")
    /// matches everything; the remaining options map to a single <see cref="ExpansionBand"/>.
    /// </summary>
    /// <param name="item">The item to test against the active expansion filter.</param>
    private bool MatchesExpansionFilter(GlamourItemInfo item)
    {
        if (expansionFilterIndex <= 0)
            return true;

        return Expansions.Classify(item.RequiredLevel) == ExpansionFilterBands[expansionFilterIndex - 1];
    }

    /// <summary>Comparison used for sorting the item list according to the current sort mode.</summary>
    private int CompareItems(GlamourItemInfo a, GlamourItemInfo b) => sortMode switch
    {
        SortMode.Slot => string.CompareOrdinal(a.SlotLabel, b.SlotLabel),
        SortMode.ItemLevel => b.ItemLevel.CompareTo(a.ItemLevel),
        SortMode.DuplicatesFirst => b.TotalCount.CompareTo(a.TotalCount),
        _ => string.CompareOrdinal(a.Name, b.Name),
    };

    /// <summary>Draws a game item icon at the given square size, skipping gracefully when missing.</summary>
    private static void DrawIcon(ushort iconId, float size)
    {
        if (iconId == 0)
        {
            ImGui.Dummy(new Vector2(size, size));
            return;
        }

        var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
        ImGui.Image(texture.Handle, new Vector2(size, size));
    }

    private static readonly string[] SortModeLabels = { "Sort: Name", "Sort: Slot", "Sort: iLvl", "Sort: Dupes" };

    /// <summary>The available sort orders for the item list.</summary>
    private enum SortMode
    {
        Name = 0,
        Slot = 1,
        ItemLevel = 2,
        DuplicatesFirst = 3,
    }

    /// <summary>
    /// Small helper that pushes ImGui's disabled state for the lifetime of a <c>using</c> block.
    /// </summary>
    private readonly struct DisabledScope : IDisposable
    {
        private readonly bool disabled;

        public DisabledScope(bool disabled)
        {
            this.disabled = disabled;
            if (disabled)
                ImGui.BeginDisabled();
        }

        public void Dispose()
        {
            if (disabled)
                ImGui.EndDisabled();
        }
    }

    /// <summary>
    /// Small helper that scopes an ImGui tab bar to a <c>using</c> block and reports whether it began.
    /// </summary>
    private readonly struct TabBarScope : IDisposable
    {
        public readonly bool Success;

        public TabBarScope(string id)
        {
            Success = ImGui.BeginTabBar(id);
        }

        public void Dispose()
        {
            if (Success)
                ImGui.EndTabBar();
        }
    }
}
