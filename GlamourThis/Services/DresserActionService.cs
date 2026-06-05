using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using GlamourThis.Models;

namespace GlamourThis.Services;

/// <summary>
/// Performs the "pull out" (restore) actions against the Glamour Dresser.
///
/// The game only allows items to move while the dresser window is open, so every action is gated
/// behind a live check of the dresser addon. Reading, searching and sorting never depend on this
/// service; it exists purely for the interactive moves.
///
/// Restoring is quirky: the dresser loads an item's full data lazily, so the very first call for a
/// freshly-seen item often bails with a spurious "inventory full" result and only succeeds on a
/// later attempt. To make a single click "just work", a restore is retried across a short window of
/// frames until it is accepted or the budget runs out. On success the in-memory snapshot is updated
/// optimistically so the item greys out immediately, without waiting for the asynchronous server
/// round-trip that would otherwise keep it looking pullable.
/// </summary>
public sealed class DresserActionService : IDisposable
{
    private const string DresserAddonName = "MiragePrismPrismBox";
    private const string ArmoireAddonName = "Cabinet";

    /// <summary>The id offset the game adds to mark a stored dresser item as high quality.</summary>
    private const uint HighQualityItemIdOffset = 1_000_000;

    /// <summary>How many frames to keep retrying a restore before giving up (~1.5s at 60fps).</summary>
    private const int RetryFrames = 90;

    /// <summary>
    /// Shorter retry budget (~0.3s) used when the item is also stored in the Armoire. Such items are
    /// one-of-a-kind cabinet pieces, so the game refuses to release a second copy from the dresser; a
    /// brief window still covers the lazy per-item load, after which we fail fast with a clear reason
    /// instead of hammering a request the game will keep rejecting.
    /// </summary>
    private const int ArmoireRetryFrames = 20;

    private readonly IGameGui gameGui;
    private readonly IFramework framework;
    private readonly INotificationManager notifications;
    private readonly IPluginLog log;
    private readonly Configuration configuration;

    private readonly List<PendingRestore> pending = new();
    private DateTime lastActionTime = DateTime.MinValue;

    private bool dresserWasOpen;
    private int lastDresserSignature;

    /// <summary>
    /// Invoked on the framework thread when the dresser's contents change while it is open and no pull
    /// is in flight (for example after the player stores a set as an outfit). The owner uses this to
    /// rebuild its snapshot so finished sets fall out of the list without a manual refresh.
    /// </summary>
    public Action? OnDresserContentsChanged { get; set; }

    /// <summary>
    /// Creates the action service and subscribes to the framework tick used to drive restore retries.
    /// </summary>
    /// <param name="gameGui">Used to detect whether the dresser or armoire window is currently open.</param>
    /// <param name="framework">Framework service whose update tick drives queued restore retries.</param>
    /// <param name="notifications">Used to surface action results to the user.</param>
    /// <param name="configuration">User configuration (read for the pull cooldown).</param>
    /// <param name="log">Plugin log sink.</param>
    public DresserActionService(
        IGameGui gameGui,
        IFramework framework,
        INotificationManager notifications,
        Configuration configuration,
        IPluginLog log)
    {
        this.gameGui = gameGui;
        this.framework = framework;
        this.notifications = notifications;
        this.configuration = configuration;
        this.log = log;

        this.framework.Update += OnFrameworkUpdate;
    }

    /// <summary>Stops driving the retry queue when the plugin unloads.</summary>
    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
        pending.Clear();
    }

    /// <summary>
    /// True when the in-game Glamour Dresser window is open, which is required for any dresser move.
    /// </summary>
    public bool IsDresserOpen => gameGui.GetAddonByName(DresserAddonName) != nint.Zero;

    /// <summary>
    /// True when the in-game Armoire window is open, which is required for any armoire move.
    /// </summary>
    public bool IsArmoireOpen => gameGui.GetAddonByName(ArmoireAddonName) != nint.Zero;

    /// <summary>
    /// Queues a single copy of an item to be pulled out of the Glamour Dresser and back into the
    /// player's inventory. Only ever the dresser copy is moved: the slot is resolved purely from the
    /// dresser's own contents (see <see cref="FindDresserSlot"/>), never from the Armoire, so for an
    /// item that exists in both places this pulls from the dresser as the player expects. One call
    /// moves exactly one item. The work is finished over the next few frames (see the class summary)
    /// so this returns immediately; results are reported via notifications and an optimistic update of
    /// the item's location data.
    /// </summary>
    /// <param name="item">The dresser item to restore.</param>
    public void RestoreItem(GlamourItemInfo item)
    {
        if (!IsDresserOpen)
        {
            Warn("Open the Glamour Dresser to pull items out.");
            return;
        }

        if (IsOnCooldown)
            return;

        if (IsQueued(item))
            return;

        // Items the player also holds in the Armoire are unique cabinet pieces; the game will not let
        // a second copy leave the dresser, so use the shorter budget and fail fast with a clear reason.
        var budget = item.IsIn(GlamourLocation.Armoire) ? ArmoireRetryFrames : RetryFrames;

        lastActionTime = DateTime.UtcNow;
        pending.Add(new PendingRestore(item, budget));
    }

    /// <summary>True while a configured pull cooldown is still elapsing (always false when disabled).</summary>
    private bool IsOnCooldown =>
        configuration.PullCooldownSeconds > 0 &&
        (DateTime.UtcNow - lastActionTime).TotalSeconds < configuration.PullCooldownSeconds;

    /// <summary>Returns true when the given item already has a pending restore queued.</summary>
    private bool IsQueued(GlamourItemInfo item)
    {
        foreach (var entry in pending)
            if (ReferenceEquals(entry.Item, item))
                return true;
        return false;
    }

    /// <summary>
    /// Drives queued restores once per frame: each pending item is re-attempted until the game accepts
    /// it, its dresser copy disappears, or its retry budget is exhausted.
    /// </summary>
    private unsafe void OnFrameworkUpdate(IFramework _)
    {
        var mirage = MirageManager.Instance();
        var loaded = mirage != null && mirage->PrismBoxLoaded;

        DriveRestores(mirage, loaded);
        DetectDresserChange(mirage, loaded);
    }

    /// <summary>Re-attempts each queued restore until it is accepted, vanishes, or runs out of budget.</summary>
    private unsafe void DriveRestores(MirageManager* mirage, bool loaded)
    {
        if (pending.Count == 0)
            return;

        for (var i = pending.Count - 1; i >= 0; i--)
        {
            var entry = pending[i];

            if (!IsDresserOpen)
            {
                pending.RemoveAt(i);
                continue;
            }

            if (!loaded)
            {
                if (--entry.FramesLeft <= 0)
                {
                    pending.RemoveAt(i);
                    Warn($"Could not pull {entry.Item.Name} (the dresser did not finish loading).");
                }
                else
                {
                    pending[i] = entry;
                }

                continue;
            }

            var slot = FindDresserSlot(mirage, entry.Item);
            if (slot < 0)
            {
                // The copy is no longer in the dresser, so the pull already went through.
                pending.RemoveAt(i);
                continue;
            }

            if (mirage->RestorePrismBoxItem((uint)slot))
            {
                pending.RemoveAt(i);
                ApplyRestored(entry.Item, slot);
                log.Information($"Glamour This: restored '{entry.Item.Name}' from dresser slot {slot}.");
                Inform($"Pulled {entry.Item.Name} into your inventory.");
            }
            else if (--entry.FramesLeft <= 0)
            {
                pending.RemoveAt(i);
                if (entry.Item.IsIn(GlamourLocation.Armoire))
                    Warn($"Could not pull {entry.Item.Name}: a copy is in your Armoire, and the game treats it as one-of-a-kind. Remove the Armoire copy first if you want this one in your bags.");
                else
                    Warn($"Could not pull {entry.Item.Name} (inventory full, or you already own this unique item).");
            }
            else
            {
                pending[i] = entry;
            }
        }
    }

    /// <summary>
    /// Watches for changes to the dresser's contents while it is open and idle, and raises
    /// <see cref="OnDresserContentsChanged"/> when they change. Detection is suppressed while a pull is
    /// pending so the optimistic UI update is not fought by a reconciling refresh mid-action, and the
    /// baseline is captured (without firing) on the frame the dresser opens so the initial load alone
    /// does not count as a change.
    /// </summary>
    private unsafe void DetectDresserChange(MirageManager* mirage, bool loaded)
    {
        if (!IsDresserOpen || !loaded)
        {
            dresserWasOpen = false;
            return;
        }

        if (pending.Count > 0)
            return;

        var signature = ComputeDresserSignature(mirage);

        if (!dresserWasOpen)
        {
            dresserWasOpen = true;
            lastDresserSignature = signature;
            return;
        }

        if (signature != lastDresserSignature)
        {
            lastDresserSignature = signature;
            OnDresserContentsChanged?.Invoke();
        }
    }

    /// <summary>
    /// Builds a cheap order-sensitive signature of the dresser's stored item ids, used only to notice
    /// when the contents change (such as after storing a set as an outfit).
    /// </summary>
    private static unsafe int ComputeDresserSignature(MirageManager* mirage)
    {
        var ids = mirage->PrismBoxItemIds;
        var hash = 17;
        for (var i = 0; i < ids.Length; i++)
        {
            if (ids[i] != 0)
                hash = (hash * 31) + (int)ids[i] + i;
        }

        return hash;
    }

    /// <summary>
    /// Locates a dresser slot that currently holds the given item, preferring the slots recorded during
    /// the last scan but always re-validating against live dresser memory so a stale index can never
    /// restore the wrong item. Returns the slot index, or -1 when no matching slot is found.
    /// </summary>
    /// <param name="mirage">The live mirage manager.</param>
    /// <param name="item">The item we want to restore.</param>
    private static unsafe int FindDresserSlot(MirageManager* mirage, GlamourItemInfo item)
    {
        var rawId = item.IsHighQuality ? item.ItemId + HighQualityItemIdOffset : item.ItemId;
        var ids = mirage->PrismBoxItemIds;

        foreach (var candidate in item.DresserSlots)
        {
            if (candidate >= 0 && candidate < ids.Length && ids[candidate] == rawId)
                return candidate;
        }

        for (var slot = 0; slot < ids.Length; slot++)
        {
            if (ids[slot] == rawId)
                return slot;
        }

        return -1;
    }

    /// <summary>
    /// Optimistically updates a freshly-restored item so the UI reflects the move immediately: the
    /// pulled dresser slot is dropped and the dresser copy count is reduced, with one copy attributed
    /// to the player's bags. A later full refresh reconciles this with the authoritative game state.
    /// </summary>
    /// <param name="item">The item that was restored.</param>
    /// <param name="slot">The dresser slot that was pulled.</param>
    private static void ApplyRestored(GlamourItemInfo item, int slot)
    {
        item.DresserSlots.Remove(slot);

        for (var i = 0; i < item.Locations.Count; i++)
        {
            if (item.Locations[i].Location != GlamourLocation.GlamourDresser)
                continue;

            var remaining = item.Locations[i].Count - 1;
            if (remaining > 0)
                item.Locations[i] = item.Locations[i] with { Count = remaining };
            else
                item.Locations.RemoveAt(i);
            break;
        }

        for (var i = 0; i < item.Locations.Count; i++)
        {
            if (item.Locations[i].Location == GlamourLocation.Inventory)
            {
                item.Locations[i] = item.Locations[i] with { Count = item.Locations[i].Count + 1 };
                return;
            }
        }

        item.Locations.Add(new GlamourItemLocation(GlamourLocation.Inventory, 1));
    }

    /// <summary>Shows an informational notification to the user.</summary>
    private void Inform(string message) => notifications.AddNotification(new()
    {
        Title = "Glamour This",
        Content = message,
        Type = Dalamud.Interface.ImGuiNotification.NotificationType.Info,
    });

    /// <summary>Shows a warning notification to the user and logs it.</summary>
    private void Warn(string message)
    {
        log.Warning($"Glamour This: {message}");
        notifications.AddNotification(new()
        {
            Title = "Glamour This",
            Content = message,
            Type = Dalamud.Interface.ImGuiNotification.NotificationType.Warning,
        });
    }

    /// <summary>A restore waiting to be accepted by the game, with its remaining retry budget.</summary>
    private struct PendingRestore
    {
        public PendingRestore(GlamourItemInfo item, int framesLeft)
        {
            Item = item;
            FramesLeft = framesLeft;
        }

        public GlamourItemInfo Item { get; }

        public int FramesLeft { get; set; }
    }
}
