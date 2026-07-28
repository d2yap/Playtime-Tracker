using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using PlaytimeTracker.Windows;
using System;
using System.Collections.Generic;
using System.IO;

namespace PlaytimeTracker;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;

    private const string CommandName = "/ptimetrack"; 



    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("SamplePlugin");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    private DateTime lastUpdateTime = DateTime.Now;
    private DateTime lastSaveTime = DateTime.Now;
    // Server info bar 
    private IDtrBarEntry? playtimeEntry;
    // SQLite database
    private PlaytimeDatabase playtimeDb = null!;
    public Dictionary<DateTime, TimeSpan> PlaytimeHistory { get; private set; } = new();

    public string Name => PluginInterface.Manifest.Name;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // You might normally want to embed resources and load them from the manifest stream
        var goatImagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "goat.png");
        playtimeDb = new PlaytimeDatabase(Path.Combine(PluginInterface.ConfigDirectory.FullName, "playtime.db"));
        RefreshPlaytimeHistory();
        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this, goatImagePath);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        playtimeEntry = DtrBar.Get("PlaytimeTracker");
        playtimeEntry.Text = "00:00:00";
        playtimeEntry.Shown = true;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens playtime tracker window."
        });

        // Tell the UI system that we want our windows to be drawn through the window system
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;

        // This adds a button to the plugin installer entry of this plugin which allows
        // toggling the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

        // Adds another button doing the same but for the main ui of the plugin
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Framework.Update += OnFrameworkUpdate;

        // Add a simple message to the log with level set to information
        // Use /xllog to open the log window in-game
        // Example Output: 00:57:54.959 | INF | [SamplePlugin] ===A cool log message from Sample Plugin===
        Log.Information($"===A cool log message from {PluginInterface.Manifest.Name}===");
    }

    public void Dispose()
    {
        // Save before logging out/etc
        Configuration.Save();
        playtimeDb.SaveTodayPlaytime(Configuration.LastTrackedDate.Date, Configuration.TodayPlaytime);

        Framework.Update -= OnFrameworkUpdate;

        // Unregister all actions to not leak anything during disposal of plugin
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        
        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();
        playtimeEntry?.Remove();

        CommandManager.RemoveHandler(CommandName);
    }
    

    private void RefreshPlaytimeHistory()
    {
        PlaytimeHistory = playtimeDb.GetAllPlaytime();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if(Configuration.LastTrackedDate.Date != DateTime.Today)
        {
            playtimeDb.SaveTodayPlaytime(Configuration.LastTrackedDate.Date, Configuration.TodayPlaytime);
            Configuration.TodayPlaytime = TimeSpan.Zero;
            Configuration.LastTrackedDate = DateTime.Today;
            RefreshPlaytimeHistory();
        }

        var now = DateTime.Now;
        var delta = now - lastUpdateTime;
        lastUpdateTime = now;

        if (PlayerState.IsLoaded)
        {
            Configuration.TodayPlaytime += delta;
        }

        if((now - lastSaveTime).TotalSeconds > 60)
        {
            playtimeDb.SaveTodayPlaytime(DateTime.Today, Configuration.TodayPlaytime);
            Configuration.Save();
            lastSaveTime = now;
        }

        var playtime = Configuration.TodayPlaytime;
        if (playtimeEntry != null)
        {
            playtimeEntry.Text = $"{(int)playtime.TotalHours:D2}:{playtime.Minutes:D2}:{playtime.Seconds:D2}";
        }
    }
    private void OnCommand(string command, string args)
    {
        // In response to the slash command, toggle the display status of our main ui
        MainWindow.Toggle();
    }
    
    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
