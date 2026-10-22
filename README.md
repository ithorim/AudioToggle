# AudioToggle

Install the **[.NET 10 Desktop Runtime for Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)**, then run `AudioToggle.exe`. Choose **.NET Desktop Runtime**, which includes Windows Forms support. The SDK also satisfies this requirement.

Press **Ctrl+Alt+F9** to switch playback devices for all three roles. A second launch exits immediately. Version **1.0.0**, by **ithorim**. Built on **.NET 10**.

Right-click the tray icon → **Settings** to choose playback devices from the dropdowns. Start with two devices, add more with **Add device**, remove extras, and use the arrows to change their order. **Save** applies and remembers changes; **Cancel** discards them. Two devices alternate; larger lists cycle 1 → 2 → 3 → 1. If the current default is outside the list, the next press selects the first entry. At least two different devices are required.

The initial pair is HyperX Cloud Flight / Realtek USB speakers. The initial Realtek match uses the `Speakers (Realtek USB Audio` prefix so Digital Output is excluded. Later selections use exact endpoint IDs. Unavailable devices stay in your list; if the next device is unavailable, a warning appears and the default stays unchanged. **Refresh devices** updates the dropdowns after connecting hardware.

If a switch fails after changing some audio roles, the app attempts to restore their previous devices. If restoration also fails, the warning reports that some roles could not be restored.

Settings are stored in `%LOCALAPPDATA%\AudioToggle\settings.json`. The hotkey and popup duration remain constants at the top of `Program.cs`. Tray → **About** shows the version and author; **Exit** closes the app.

Settings and About use dark custom headers. Drag the header to move a window; click × or press Esc to close it. Closing Settings without Save discards edits.

A small popup appears above the primary screen's taskbar without taking focus. Each switch replaces its text immediately and restarts the 1.3-second timer. Click it to dismiss early. Notifications do not queue.

Opening `AudioToggle.exe` automatically registers it to run when you sign in, for your Windows account only. No administrator rights or startup shortcut are needed. It uses one `AudioToggle` entry in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, skips writing when the path already matches, and updates the same entry if you move the app and run it from its new location. You can disable it in Windows **Startup apps**. If you previously added a shortcut in `shell:startup`, you can remove that shortcut.

Build with the .NET 10 SDK or newer on Windows. From the folder containing `build.ps1`:

```powershell
.\build.ps1
```

Close the running app using **Tray → Exit** first. The script publishes the single executable to `publish/AudioToggle.exe` beside the source folders. It uses your installed SDK and normal NuGet restore; it does not install tools or maintain its own dependency cache. The optional checks do not switch your real audio devices. You can also run `dotnet publish -c Release` directly from the `AudioToggle` project folder.

The executable uses the separately installed .NET 10 Desktop Runtime. Setting defaults uses the undocumented Windows `IPolicyConfig` COM interface.

Uses [NAudio.Wasapi 2.3.0](https://www.nuget.org/packages/NAudio.Wasapi/2.3.0) and its NAudio.Core dependency under the MIT license; see `THIRD-PARTY-NOTICES.txt`. The source archive includes the consolidated checks in `AudioToggle.Tests`; see that folder's README for running them.
