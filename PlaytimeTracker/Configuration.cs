using Dalamud.Configuration;
using System;

namespace PlaytimeTracker;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;
    public bool SomePropertyToBeSavedAndWithADefault { get; set; } = true;

    // Track the current playtime
    public TimeSpan TodayPlaytime { get; set; } = TimeSpan.Zero; 
    public DateTime LastTrackedDate { get; set; } = DateTime.Today;

    // The below exists just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
