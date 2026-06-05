using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace GlamourThis.Windows;

/// <summary>
/// The settings window for Glamour This. Every control writes straight back to the shared
/// <see cref="Configuration"/> and saves immediately, so changes take effect at once.
/// </summary>
public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;

    /// <summary>
    /// Creates the configuration window bound to the plugin's configuration.
    /// </summary>
    /// <param name="plugin">The owning plugin instance.</param>
    public ConfigWindow(Plugin plugin) : base("Glamour This - Settings###GlamourThisConfig")
    {
        configuration = plugin.Configuration;

        Size = new Vector2(380, 220);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    /// <summary>No unmanaged resources to release; present to satisfy the windowing contract.</summary>
    public void Dispose()
    {
    }

    /// <summary>Draws the settings controls and persists any change the user makes.</summary>
    public override void Draw()
    {
        var openWithDresser = configuration.OpenWithDresser;
        if (ImGui.Checkbox("Open automatically with the Glamour Dresser", ref openWithDresser))
        {
            configuration.OpenWithDresser = openWithDresser;
            configuration.Save();
        }

        var highlightDuplicates = configuration.HighlightDuplicates;
        if (ImGui.Checkbox("Highlight duplicate items", ref highlightDuplicates))
        {
            configuration.HighlightDuplicates = highlightDuplicates;
            configuration.Save();
        }

        ImGui.Spacing();

        var iconSize = configuration.IconSize;
        if (ImGui.SliderFloat("Icon size", ref iconSize, 24f, 64f, "%.0f px"))
        {
            configuration.IconSize = iconSize;
            configuration.Save();
        }
    }
}
