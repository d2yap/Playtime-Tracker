
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
    [PluginService] public static IChatGui Chat { get; private set; } = null!;

    private const string CommandName = "/ptimetrack";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("PlaytimeTracker");

    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    private DateTime lastUpdateTime = DateTime.Now;
    private DateTime lastSaveTime = DateTime.Now;

    // Server info bar
    private IDtrBarEntry? playtimeEntry;

    // SQLite database
    private PlaytimeDatabase playtimeDb = null!;

    // Total playtime history
    public Dictionary<DateTime, TimeSpan> PlaytimeHistory { get; private set; } = new();

    // Current day's playtime for each job.
    // Key = job abbreviation
    // Value = job name + accumulated playtime
    private Dictionary<string, (string Name, TimeSpan Playtime)> TodayJobPlaytime { get; set; } = new();

    public string Name => PluginInterface.Manifest.Name;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration
                       ?? new Configuration();

        var goatImagePath = Path.Combine(
            PluginInterface.AssemblyLocation.Directory?.FullName!,
            "goat.png");

        playtimeDb = new PlaytimeDatabase(
            Path.Combine(
                PluginInterface.ConfigDirectory.FullName,
                "playtime.db"));

        // If the saved date is not today, reset tracking
        if (Configuration.LastTrackedDate.Date != DateTime.Today)
        {
            Configuration.LastTrackedDate = DateTime.Today;
        }

        RefreshPlaytimeHistory();
        RefreshTodayJobPlaytime();

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this, goatImagePath);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        playtimeEntry = DtrBar.Get("PlaytimeTracker");
        playtimeEntry.Text = "00:00:00";
        playtimeEntry.Shown = true;

        CommandManager.AddHandler(
            CommandName,
            new CommandInfo(OnCommand)
            {
                HelpMessage = "Opens playtime tracker window."
            });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Framework.Update += OnFrameworkUpdate;

        Log.Information(
            $"===A cool message from {PluginInterface.Manifest.Name}===");

    }

    public void Dispose()
    {
        // Save the current state before the plugin unloads.
        SaveJobPlaytime(Configuration.LastTrackedDate.Date);

        Configuration.Save();

        Framework.Update -= OnFrameworkUpdate;

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

        // Also refresh today's playtime from the database
        // This ensures we don't restore deleted data or carry over stale cached values
        Configuration.TodayPlaytime = playtimeDb.GetPlaytimeForDate(DateTime.Today);
    }

    public Dictionary<string, TimeSpan> GetPlaytimeByJob(DateTime date)
    {
        return playtimeDb.GetPlaytimeByJob(date);
    }

    private void RefreshTodayJobPlaytime()
    {
        TodayJobPlaytime.Clear();

        var jobs = playtimeDb.GetPlaytimeByJobWithNames(DateTime.Today);

        foreach (var job in jobs)
        {
            TodayJobPlaytime[job.Key] =
                (
                    job.Value.Name,
                    job.Value.Playtime
                );
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var now = DateTime.Now;

        var delta = now - lastUpdateTime;
        lastUpdateTime = now;

        // When new day happens

        if (Configuration.LastTrackedDate.Date != DateTime.Today)
        {
            // Save yesterday's job totals before resetting.
            SaveJobPlaytime(Configuration.LastTrackedDate.Date);

            // Reset today's in-memory job data.
            TodayJobPlaytime.Clear();

            Configuration.TodayPlaytime = TimeSpan.Zero;
            Configuration.LastTrackedDate = DateTime.Today;

            RefreshPlaytimeHistory();
            RefreshTodayJobPlaytime();
        }

        // Playtime tracking

        if (PlayerState.IsLoaded)
        {
            // Overall playtime
            Configuration.TodayPlaytime += delta;

            // Current job
            var job = PlayerState.ClassJob;

            if (job.RowId != 0)
            {
                var abbreviation =
                    job.Value.Abbreviation.ToString();

                var name =
                    job.Value.Name.ToString();

                if (TodayJobPlaytime.TryGetValue(
                        abbreviation,
                        out var existing))
                {
                    TodayJobPlaytime[abbreviation] =
                        (
                            existing.Name,
                            existing.Playtime + delta
                        );
                }
                else
                {
                    TodayJobPlaytime[abbreviation] =
                        (
                            name,
                            delta
                        );
                }
            }
        }

        // save every 60 seconds

        if ((now - lastSaveTime).TotalSeconds > 60)
        {
            SaveJobPlaytime(Configuration.LastTrackedDate.Date);

            Configuration.Save();

            RefreshPlaytimeHistory();

            lastSaveTime = now;
        }

        // DTR bar update

        UpdateDtrBar();
    }

    private void SaveJobPlaytime(DateTime date)
    {
        foreach (var job in TodayJobPlaytime)
        {
            playtimeDb.SaveJobPlaytime(
                date.Date,
                job.Value.Name,
                job.Key,
                job.Value.Playtime);
        }
    }

    private void UpdateDtrBar()
    {
        var playtime = Configuration.TodayPlaytime;
        var serverInfoSetting =
            Configuration.ServerInfoBarSetting;

        if (playtimeEntry != null && serverInfoSetting)
        {
            playtimeEntry.Shown = true;

            playtimeEntry.Text =
                $"{(int)playtime.TotalHours:D2}:" +
                $"{playtime.Minutes:D2}:" +
                $"{playtime.Seconds:D2}";
        }
        else if (playtimeEntry != null)
        {
            playtimeEntry.Text = "";
            playtimeEntry.Shown = false;
        }
    }

    private void OnCommand(string command, string args)
    {
        MainWindow.Toggle();
    }

    public void RefreshPlaytimeHistoryInDatabase()
    {
        SaveJobPlaytime(Configuration.LastTrackedDate.Date);

        PlaytimeHistory =
            playtimeDb.GetAllPlaytime();

        Configuration.Save();
    }

    // windows

    public void ToggleConfigUi() =>
        ConfigWindow.Toggle();

    public void ToggleMainUi() =>
        MainWindow.Toggle();

}
