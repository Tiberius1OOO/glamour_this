using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using GlamourThis.Services;
using GlamourThis.Windows;

namespace GlamourThis;

/// <summary>
/// Entry point for the Glamour This plugin.
///
/// Responsible for acquiring Dalamud services, constructing the data/set/action services and the
/// windows, registering the slash command and UI hooks, and cleaning everything up on unload.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IGameInventory GameInventory { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static INotificationManager Notifications { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/glamthis";
    private const string CommandAlias = "/gt";
    private const string DresserAddonName = "MiragePrismPrismBox";

    private readonly WindowSystem windowSystem = new("GlamourThis");
    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;

    /// <summary>The loaded, persisted user configuration.</summary>
    public Configuration Configuration { get; }

    /// <summary>Static outfit-set definitions and lookups.</summary>
    public OutfitSetService OutfitSetService { get; }

    /// <summary>Live inventory snapshot and set grouping.</summary>
    public InventoryDataService InventoryDataService { get; }

    /// <summary>Pull-out / put-back actions against the dresser and armoire.</summary>
    public DresserActionService DresserActionService { get; }

    /// <summary>
    /// Wires up the plugin: loads config, builds services, creates windows, and registers commands
    /// and event handlers.
    /// </summary>
    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        OutfitSetService = new OutfitSetService(DataManager, Log);
        OutfitSetService.Build();

        InventoryDataService = new InventoryDataService(GameInventory, DataManager, OutfitSetService, Log);
        DresserActionService = new DresserActionService(GameGui, Framework, Notifications, Configuration, Log)
        {
            // Re-read the snapshot whenever the dresser's contents change (for example after the player
            // stores a set as an outfit), so completed-and-stored sets drop out of the list on their own.
            OnDresserContentsChanged = () => InventoryDataService.Refresh(Configuration.IncludeArmoire),
        };

        configWindow = new ConfigWindow(this);
        mainWindow = new MainWindow(this);

        windowSystem.AddWindow(configWindow);
        windowSystem.AddWindow(mainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Glamour This browser. Use /gt as a shortcut.",
        });
        CommandManager.AddHandler(CommandAlias, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Glamour This browser.",
            ShowInHelp = false,
        });

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, DresserAddonName, OnDresserOpened);
    }

    /// <summary>
    /// Unregisters every handler and disposes the windows so nothing leaks when the plugin unloads.
    /// </summary>
    public void Dispose()
    {
        AddonLifecycle.UnregisterListener(OnDresserOpened);

        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        windowSystem.RemoveAllWindows();
        mainWindow.Dispose();
        configWindow.Dispose();
        DresserActionService.Dispose();

        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(CommandAlias);
    }

    /// <summary>
    /// Refreshes the inventory snapshot on the framework thread and then opens the main window.
    /// Centralised here so both the command and the auto-open path share the same behaviour.
    /// </summary>
    public void OpenBrowser()
    {
        RefreshData();
        mainWindow.IsOpen = true;
    }

    /// <summary>
    /// Rebuilds the inventory snapshot. Scheduled onto the framework thread because it reads live
    /// game containers, which must not be touched from the UI thread.
    /// </summary>
    public void RefreshData()
    {
        Framework.RunOnFrameworkThread(() =>
            InventoryDataService.Refresh(Configuration.IncludeArmoire));
    }

    /// <summary>Handles the slash command by toggling the main window (refreshing as it opens).</summary>
    private void OnCommand(string command, string args)
    {
        if (mainWindow.IsOpen)
        {
            mainWindow.IsOpen = false;
            return;
        }

        OpenBrowser();
    }

    /// <summary>
    /// Opens the browser automatically when the in-game dresser opens, if the user enabled that.
    /// </summary>
    private void OnDresserOpened(AddonEvent type, AddonArgs args)
    {
        if (Configuration.OpenWithDresser)
            OpenBrowser();
    }

    /// <summary>Toggles the configuration window.</summary>
    public void ToggleConfigUi() => configWindow.Toggle();

    /// <summary>Toggles the main browser window.</summary>
    public void ToggleMainUi() => mainWindow.Toggle();
}
