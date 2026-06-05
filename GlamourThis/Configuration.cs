using System;
using Dalamud.Configuration;

namespace GlamourThis;

/// <summary>
/// Persisted user settings for Glamour This.
/// Loaded once on startup and saved through <see cref="Save"/> whenever the user changes an option.
/// </summary>
[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    /// <summary>Schema version of this configuration, used by Dalamud for future migrations.</summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// When true, the overlay opens automatically as soon as the in-game Glamour Dresser is opened.
    /// </summary>
    public bool OpenWithDresser { get; set; } = true;

    /// <summary>
    /// When true, duplicate items (more than one copy owned anywhere) are highlighted in the list.
    /// </summary>
    public bool HighlightDuplicates { get; set; } = true;

    /// <summary>
    /// When true, items stored in the Armoire are included in the listing and set completion. The
    /// Armoire holds an effectively unlimited number of cabinet items that cannot be pulled out the
    /// same way dresser items can, so turning this off hides that noise and focuses on the dresser.
    /// </summary>
    public bool IncludeArmoire { get; set; } = true;

    /// <summary>
    /// Cooldown, in seconds, enforced between pull actions to avoid bot-like rapid-fire input.
    /// Defaults to half a second so each press remains a deliberate single interaction.
    /// </summary>
    public double PullCooldownSeconds { get; set; } = 0.5d;

    /// <summary>The icon thumbnail size, in pixels, used when drawing the item grid.</summary>
    public float IconSize { get; set; } = 40f;

    /// <summary>
    /// Persists the current configuration to disk. Call this after mutating any option.
    /// </summary>
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
