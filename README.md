# Playtime Tracker

Track your playtime, locally and by job!

_This plugin uses a SQLite database to save your data._

![Image of a FINAL FANTASY XIV screenshot showcasing the plugin](/Screenshots/plugin.png)

Also, show your current day playtime in the server bar!

![Image of a FINAL FANTASY XIV screenshot showcasing the server bar](/Screenshots/dtr.png)

## How To Use

### Prerequisites

Playtime Tracker assumes all the following prerequisites are met:

- XIVLauncher, FINAL FANTASY XIV, and Dalamud have all been installed and the game has been run with Dalamud at least once.
- XIVLauncher is installed to its default directories and configurations.
    - If a custom path is required for Dalamud's dev directory, it must be set with the `DALAMUD_HOME` environment variable.
- A .NET Core 8 SDK has been installed and configured, or is otherwise available. (In most cases, the IDE will take care of this.)

### Building

1. Open up `PlaytimeTracker.sln` in your C# editor of choice (likely [Visual Studio](https://visualstudio.microsoft.com) or [JetBrains Rider](https://www.jetbrains.com/rider/)).
2. Build the solution. By default, this will build a `Debug` build, but you can switch to `Release` in your IDE.
3. The resulting plugin can be found at `SamplePlugin/bin/x64/Debug/PlaytimeTracker.dll` (or `Release` if appropriate.)

### Activating in-game

1. Launch the game and use `/xlsettings` in chat or `xlsettings` in the Dalamud Console to open up the Dalamud settings.
    - In here, go to `Experimental`, and add the full path to the `PlaytimeTracker.dll` to the list of Dev Plugin Locations.
2. Next, use `/xlplugins` (chat) or `xlplugins` (console) to open up the Plugin Installer.
    - In here, go to `Dev Tools > Installed Dev Plugins`, and the `PlaytimeTracker` should be visible. Enable it.
3. You should now be able to use `/ptimetrack` (chat) or `ptimetrack` (console)!

Note that you only need to add it to the Dev Plugin Locations once (Step 1); it is preserved afterwards. You can disable, enable, or load your plugin on startup through the Plugin Installer.
