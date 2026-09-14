using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System;
using System.Linq;
using System.Numerics;

namespace PlaytimeTracker.Windows;

public class MainWindow : Window, IDisposable
{

    private readonly string goatImagePath;
    private readonly Plugin plugin;

    // month page
    private int statsMonthOffset = 0;
    private int statsPage = 0;


    // jobs page
    private int jobsMonthOffset = 0;
    private int jobsPage = 0;

    // pagination 

    private const int DaysPerPage = 10;

    // We give this window a hidden ID using ##.
    // The user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public MainWindow(Plugin plugin, string goatImagePath)
        : base("Playtime Tracker##With a hidden ID", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.goatImagePath = goatImagePath;
        this.plugin = plugin;
    }

    // On window open https://dalamud.dev/api/Dalamud.Interface.Windowing/Classes/Window/
    public override void OnOpen()
    {
        plugin.RefreshPlaytimeHistoryInDatabase();

        Plugin.Chat.Print("Playtime Tracker was opened.");
    }

    public void Dispose() { }

    private void TabOverview()
    {
        // Example for other services that Dalamud provides.
        // PlayerState provides a wrapper filled with information about the player character.

        var playerState = Plugin.PlayerState;
        if (!playerState.IsLoaded)
        {
            ImGui.Text("Our local player is currently not logged in.");
            return;
        }

        if (!playerState.ClassJob.IsValid)
        {
            ImGui.Text("Our current job is currently not valid.");
            return;
        }

        ImGui.AlignTextToFramePadding();


        // Scaling hardcoded pixel values is important, as otherwise users with HUD scales above or below 100%
        // won't be able to see everything.

        // Get the icon id from a known offset + the class jobs id
        // Character Name
        var characterName = playerState.CharacterName.ToString();
        ImGui.Text($"{characterName}"); 
        ImGui.SameLine();
        var jobIconId = 62100 + playerState.ClassJob.RowId;
        var iconTexture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(jobIconId)).GetWrapOrEmpty();
        ImGui.Image(iconTexture.Handle, new Vector2(28, 28) * ImGuiHelpers.GlobalScale);
        ImGui.SameLine();

        // If you want to see the Macro representation of this SeString use `.ToMacroString()`
        // More info about SeStrings: https://dalamud.dev/plugin-development/sestring/
        ImGui.Text(playerState.ClassJob.Value.Abbreviation.ToString());

        ImGui.SameLine();
        ImGui.Text($" [Level {playerState.Level}]");
        // Playtime
        var playTime = plugin.Configuration.TodayPlaytime;
        ImGui.Text($"Playtime Today: {(int)playTime.TotalHours:D2}:{playTime.Minutes:D2}:{playTime.Seconds:D2}");
        var sumPlaytime = plugin.PlaytimeHistory.Values.Aggregate(TimeSpan.Zero, (sum, time) => sum + time);
        ImGui.Text($"Total recorded playtime: {(int)sumPlaytime.TotalDays}:{sumPlaytime.Hours:D2}:{sumPlaytime.Minutes:D2}:{sumPlaytime.Seconds:D2}");
        ImGui.Text($"Total recorded playtime (hours): {sumPlaytime.TotalHours:F2}h");


        // Example for querying Lumina, getting the name of our current area.
        var territoryId = Plugin.ClientState.TerritoryType;
        if (Plugin.DataManager.GetExcelSheet<TerritoryType>().TryGetRow(territoryId, out var territoryRow))
        {
            ImGui.Text($"Location:");
            ImGui.SameLine(120 * ImGuiHelpers.GlobalScale);
            ImGui.Text(territoryRow.PlaceName.Value.Name.ToString());
        }
        else
        {
            ImGui.Text("Invalid territory.");
        }
    }

    private void TabPlaytime()
    {
        using var child = ImRaii.Child(
            "PlaytimeStatsChild",
            Vector2.Zero,
            true);

        if (!child.Success)
            return;

        var today = DateTime.Today;

        // Selected month

        var selectedMonth = new DateTime(
            today.Year,
            today.Month,
            1).AddMonths(statsMonthOffset);

        var nextMonth = selectedMonth.AddMonths(1);

        var monthEntries = plugin.PlaytimeHistory
            .Where(e => e.Key >= selectedMonth && e.Key < nextMonth)
            .OrderByDescending(e => e.Key)
            .ToList();

        // Month navigation

        if (ImGui.Button("<"))
        {
            statsMonthOffset--;
            statsPage = 0;
        }

        ImGui.SameLine();

        ImGui.Text(
            $"  {selectedMonth:MMMM yyyy}  ");

        ImGui.SameLine();

        bool isCurrentMonth =
            selectedMonth.Year == today.Year &&
            selectedMonth.Month == today.Month;

        if (!isCurrentMonth)
        {
            if (ImGui.Button(">"))
            {
                statsMonthOffset++;
                statsPage = 0;
            }
        }

        ImGui.Separator();

        // Month statistics

        var totalMonthPlaytime =
            monthEntries
                .Select(e => e.Value)
                .Aggregate(
                    TimeSpan.Zero,
                    (sum, time) => sum + time);

        var activeDays = monthEntries.Count;

        var averagePlaytime = activeDays > 0
            ? TimeSpan.FromTicks(
                totalMonthPlaytime.Ticks / activeDays)
            : TimeSpan.Zero;

        ImGui.Text("Total");

        ImGui.SameLine(100 * ImGuiHelpers.GlobalScale);

        ImGui.Text(
            $"{(int)totalMonthPlaytime.TotalHours:D2}:" +
            $"{totalMonthPlaytime.Minutes:D2}:" +
            $"{totalMonthPlaytime.Seconds:D2}");

        ImGui.Text("Average");

        ImGui.SameLine(100 * ImGuiHelpers.GlobalScale);

        ImGui.Text(
            $"{(int)averagePlaytime.TotalHours:D2}:" +
            $"{averagePlaytime.Minutes:D2}:" +
            $"{averagePlaytime.Seconds:D2}");

        ImGui.Text($"Active Days: {activeDays}");

        ImGui.Separator();

        if (monthEntries.Count == 0)
        {
            ImGui.Text("No playtime recorded this month.");
            return;
        }
        
        // Pagination

        int totalPages = Math.Max(
            1,
            (int)Math.Ceiling(
                monthEntries.Count / (double)DaysPerPage));

        statsPage = Math.Clamp(
            statsPage,
            0,
            totalPages - 1);

        if (ImGui.Button("Previous") && statsPage > 0)
            statsPage--;

        ImGui.SameLine();

        ImGui.Text(
            $"Page {statsPage + 1} / {totalPages}");

        ImGui.SameLine();

        if (ImGui.Button("Next") &&
            statsPage < totalPages - 1)
        {
            statsPage++;
        }

        ImGui.Separator();
        
        // Daily Playtime 

        var maxHours = monthEntries
            .Max(x => x.Value.TotalHours);

        var pageEntries = monthEntries
            .Skip(statsPage * DaysPerPage)
            .Take(DaysPerPage);

        foreach (var entry in pageEntries)
        {
            var date = entry.Key;
            var time = entry.Value;

            float hours = (float)time.TotalHours;

            ImGui.Text(date.ToString("ddd MM/dd"));

            ImGui.SameLine(
                100 * ImGuiHelpers.GlobalScale);

            ImGui.ProgressBar(
                maxHours > 0
                    ? hours / (float)maxHours
                    : 0,
                new Vector2(
                    180 * ImGuiHelpers.GlobalScale,
                    18 * ImGuiHelpers.GlobalScale),
                $"{hours:F1}h");
        }
    }

    private void TabJobPlaytime()
    {
        using var child = ImRaii.Child(
            "JobPlaytimeChild",
            Vector2.Zero,
            true);

        if (!child.Success)
            return;

        var today = DateTime.Today;

        // Selected month for job playtime

        var selectedMonth = new DateTime(
            today.Year,
            today.Month,
            1).AddMonths(jobsMonthOffset);

        var nextMonth = selectedMonth.AddMonths(1);

        var monthEntries = plugin.PlaytimeHistory
            .Where(e => e.Key >= selectedMonth && e.Key < nextMonth)
            .OrderByDescending(e => e.Key)
            .ToList();


        // Month navigation

        if (ImGui.Button("<"))
        {
            jobsMonthOffset--;
            jobsPage = 0;
        }

        ImGui.SameLine();

        ImGui.Text($"  {selectedMonth:MMMM yyyy}  ");

        ImGui.SameLine();

        bool isCurrentMonth =
            selectedMonth.Year == today.Year &&
            selectedMonth.Month == today.Month;

        if (!isCurrentMonth)
        {
            if (ImGui.Button(">"))
            {
                jobsMonthOffset++;
                jobsPage = 0;
            }
        }

        ImGui.Separator();

        if (monthEntries.Count == 0)
        {
            ImGui.Text("No playtime recorded this month.");
            return;
        }
        
        // By month statistics

        var totalMonthPlaytime =
            monthEntries
                .Select(e => e.Value)
                .Aggregate(
                    TimeSpan.Zero,
                    (sum, time) => sum + time);

        var activeDays = monthEntries.Count;

        var averagePlaytime = activeDays > 0
            ? TimeSpan.FromTicks(
                totalMonthPlaytime.Ticks / activeDays)
            : TimeSpan.Zero;

        ImGui.Text(
            $"Total: {(int)totalMonthPlaytime.TotalHours:D2}:" +
            $"{totalMonthPlaytime.Minutes:D2}:" +
            $"{totalMonthPlaytime.Seconds:D2}");

        ImGui.SameLine(230 * ImGuiHelpers.GlobalScale);

        ImGui.Text($"Average: {(int)averagePlaytime.TotalHours:D2}:" +
            $"{averagePlaytime.Minutes:D2}:" +
            $"{averagePlaytime.Seconds:D2}");

        ImGui.Separator();
        
        // Pagination

        int totalPages = Math.Max(
            1,
            (int)Math.Ceiling(
                monthEntries.Count / (double)DaysPerPage));

        jobsPage = Math.Clamp(
            jobsPage,
            0,
            totalPages - 1);

        if (ImGui.Button("Previous") && jobsPage > 0)
            jobsPage--;

        ImGui.SameLine();

        ImGui.Text(
            $"Page {jobsPage + 1} / {totalPages}");

        ImGui.SameLine();

        if (ImGui.Button("Next") &&
            jobsPage < totalPages - 1)
        {
            jobsPage++;
        }

        ImGui.Separator();
           
        // Daily Job Playtime

        var pageEntries = monthEntries
            .Skip(jobsPage * DaysPerPage)
            .Take(DaysPerPage);

        foreach (var entry in pageEntries)
        {
            var date = entry.Key;
            var totalTime = entry.Value;

            ImGui.Text(
                date.ToString("ddd, MMM dd"));

            ImGui.SameLine(
                150 * ImGuiHelpers.GlobalScale);

            ImGui.Text(
                $"Total: {(int)totalTime.TotalHours:D2}:" +
                $"{totalTime.Minutes:D2}:" +
                $"{totalTime.Seconds:D2}");

            var jobs = plugin.GetPlaytimeByJob(date);

            foreach (var job in jobs)
            {
                ImGui.Indent(
                    20 * ImGuiHelpers.GlobalScale);

                ImGui.Text(job.Key);

                ImGui.SameLine(
                    80 * ImGuiHelpers.GlobalScale);

                ImGui.Text(
                    $"{(int)job.Value.TotalHours:D2}:" +
                    $"{job.Value.Minutes:D2}:" +
                    $"{job.Value.Seconds:D2}");

                ImGui.Unindent(
                    20 * ImGuiHelpers.GlobalScale);
            }

            ImGui.Separator();
        }
    }

    public override void Draw()
    {
        ImGui.Spacing();

        if (ImGui.BeginTabBar("SessionTabs"))
        {
            if (ImGui.BeginTabItem("Overview"))

            {
                TabOverview();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Stats"))

            {
                TabPlaytime();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Jobs"))
            {
                TabJobPlaytime();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Goat"))

            {
                var goatImage = Plugin.TextureProvider.GetFromFile(goatImagePath).GetWrapOrDefault();
                if (goatImage != null)
                {
                    using (ImRaii.PushIndent(55f))
                    {
                        ImGui.Image(goatImage.Handle, goatImage.Size);
                    }
                }
                else
                {
                    ImGui.Text("Image not found.");
                }

                ImGuiHelpers.ScaledDummy(20.0f);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Settings"))

            {
                ImGui.Text($"Server Info Bar: {plugin.Configuration.ServerInfoBarSetting}");

                if (ImGui.Button("Show Settings"))
                {
                    plugin.ToggleConfigUi();
                }
                ImGui.EndTabItem();
            }
        }

        // Reference code 

        // Normally a BeginChild() would have to be followed by an unconditional EndChild(),
        // ImRaii takes care of this after the scope ends.
        // This works for all ImGui functions that require specific handling, examples are BeginTable() or Indent().
        /*
        using (var child = ImRaii.Child("SomeChildWithAScrollbar", Vector2.Zero, true))
        {
            // Check if this child is drawing
            if (child.Success)
            {


                ImGuiHelpers.ScaledDummy(10.0f);

                
            }
        }*/
    }
}
