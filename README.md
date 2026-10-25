# EcoMode

⚡ **EcoMode** is a lightweight Windows utility that helps reduce power consumption and improve efficiency by applying smart system tweaks and process management rules.

![EcoMode Screenshot](docs/screenshot.png)

## ✨ Features

* **One-click Eco Mode toggle**: switch power saving on and restore your saved original settings by switching it off.
* **Standard or Advanced view**: start with a clear switch and plain-language status, or open the full set of options. Choose at first launch and switch anytime.
* **Four languages**: English, French, Spanish and Simplified Chinese, with automatic Windows language detection and a remembered choice.
* **Original settings backup**: save a baseline and restore it at any time, including after restarting EcoMode.
* **Windows power plan tweaks**: automatically adjusts *Energy Performance Preference (EPP)* and *Boost* settings.
* **Browser policies**: applies registry policies for Microsoft Edge and Google Chrome (hardware acceleration off, battery saver/efficiency mode on).
* **Per-app GPU preferences**: configure applications to use *integrated GPU (power-saving)* or *discrete GPU (performance)*.
* **EcoQoS (Quality of Service)**: throttle background processes while preserving the active foreground app's original priority.
* **New background apps are covered**: a light check every minute catches processes launched after Eco Mode starts, with an immediate check after resume.
* **Battery dashboard**: battery level, charging status, available runtime/discharge readings, and an optional recent history chart in either view.
* **Automatic on battery**: opt in to background-app Eco Mode on battery, with restoration when plugged in and a manual override.
* **Live app list**: inspect EcoMode status, priority and EcoQoS, search running apps, and save per-executable throttling exclusions.
* **Configurable bypass list**: define which processes should never be throttled.
* **Tray support**: runs in the background, minimizes to system tray instead of closing.
* **Import / export / reset profiles**: easily back up or share your configuration.
* **Built-in presets**: start with Balanced, Battery Saver, Gaming, Development, Creative Work, or Video Calls, then customize the app and bypass lists.
* **Utility-style interface**: dark panels, a clear Eco status, grouped controls, and a compact activity log. Styles live in [`UI/Controls`](src/EcoMode/UI/Controls/); layouts live in [`UI`](src/EcoMode/UI/).
* **Activity log**: console-like log viewer with color-coded log levels.

## 📥 Installation

The installer and portable ZIP include the .NET runtime. **Users do not need to install .NET 10 or the .NET Desktop Runtime.** The .NET 10 SDK is needed only to build EcoMode from source.

1. Download the latest installer from the [Releases](../../releases) page.
2. Run the setup — EcoMode will be installed with Start Menu and system tray support.
3. Launch EcoMode from the Start Menu or tray icon.

*(Installer built with Inno Setup — no external dependencies needed.)*

## 🚀 Usage

* When started without Administrator rights, EcoMode offers to restart as Administrator before opening the window. Choose **Yes** to open the Windows elevation prompt, or **No** to continue in standard mode. Canceling the Windows prompt also continues in standard mode. Already elevated launches skip this confirmation.
* On first launch, **How would you like to use EcoMode?** offers **Standard view (recommended)** or **Advanced view**. Standard is the default and includes the main switch, a summary of your setup, saved-settings status, **Restore saved settings**, and optional **View activity**. Advanced includes profiles, individual options and app rules. Use the view selector in the header or the **View** menu to change views anytime, including while Eco Mode is active. The choice is remembered separately in `%APPDATA%\EcoMode\view-settings.json`; importing or resetting a profile does not change it. Switching views preserves unsaved edits and saved originals.
* Use the **Eco Mode** switch to turn power saving **ON** or **OFF**. Green **ON** means Eco Mode is active; neutral **OFF** means no EcoMode changes remain pending.
* On startup, EcoMode automatically saves original settings for the selected options and configured GPU apps if no backup exists. Existing originals are preserved. Standard mode saves user settings; starting as Administrator also creates any missing power plan and browser policy backup. New options and apps are backed up before they are first changed. A failed backup is shown in the interface and blocks Eco Mode from applying changes until the backup succeeds.
* In Advanced view, click **Save originals** to save a new baseline for the selected options and configured GPU apps. Restore pending EcoMode changes first; replacing an existing baseline requires confirmation.
* Switch **OFF** to undo the settings EcoMode applied, regardless of the currently selected options or app list.
* An amber **RETRY** switch with **Restore pending** means cleanup is incomplete. Click it to retry restoration; it will not enable Eco Mode until recovery is complete.
* Click **Restore saved settings** in Standard view or **Restore originals** in Advanced view to reapply the entire saved baseline whenever needed. Recovery warnings remain visible in both views; the activity button highlights new warnings in Standard view.
* In Advanced view, use the small switches on the right of **Apply to** to choose which tweaks are included (Power Plan, Edge/Chrome policies, GPU prefs, EcoQoS). Green **ON** includes an option; outlined **OFF** excludes it. Click a switch or its label, then **Save profile** to keep your selections. These options take effect when you switch Eco Mode **ON**.
* In Advanced view, use **Add app** to add apps for GPU preference management, and **Never throttle** to exclude other processes.
* Right-click the **tray icon** for Show / Quit and, in Advanced view, Save.

[First-launch choice](docs/welcome.png) · [Advanced view](docs/advanced-view.png)

### Language

EcoMode initially follows your Windows display language when it supports English, French, Spanish or Chinese; other languages fall back to English. Choose **English**, **Français**, **Español** or **简体中文** on the welcome screen or in the **Language** menu. The interface updates immediately, including the Standard and Advanced views, presets, switches and restoration status. Active Eco Mode, unsaved edits and saved originals are preserved. App-generated activity messages use the selected language when logged; earlier entries and Windows-provided error details retain their original text.

The choice is stored separately in `%APPDATA%\EcoMode\language-settings.json`, so importing profiles or changing views does not replace it. Translations use .NET's built-in [resource localization](https://learn.microsoft.com/en-us/dotnet/core/extensions/resources): the English catalog is [`src/EcoMode/Localization/Strings.resx`](src/EcoMode/Localization/Strings.resx), with matching `fr`, `es` and `zh-Hans` catalogs. English supplies fallback text. These resources are included in the standalone executable; no translation service or additional package is required. Regression checks verify catalog completeness, format arguments, live switching, window layouts and standalone resource loading in CI.

### Battery and automatic mode

Both views include a **Battery** card. It shows the current percentage and power source, plus Windows' estimated remaining runtime and discharge watts when the hardware supplies them. **Show history** reveals the last 60 minutes recorded while EcoMode is running: green segments indicate Eco Mode on, gray segments indicate off, and dotted lines mark mode changes. History stays in memory and resets when EcoMode exits. Sleep, missing readings and power-source changes break the line rather than implying continuous measurements.

The screenshots use representative battery data from the UI regression fixtures. [French battery view](docs/battery-fr.png).

Observed percentage drain per hour appears after at least five minutes of continuous discharging readings and a measurable percentage drop. It describes recent usage; it does not estimate savings caused by EcoMode. Desktops and unsupported battery hardware show an unavailable/no-battery state. Battery readings use Windows' [power status](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-system_power_status) and [battery state](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-system_battery_state) APIs without another runtime dependency.

**Automatic on battery (background apps only)** is off by default. Enabling it limits background apps on battery and restores their original priority and EcoQoS settings when power is connected. It uses your current bypass list, preserves the active foreground app and security exclusions, and does not apply power-plan, browser or GPU profile settings. It also works while the window is hidden in the tray. Eco Mode checks for newly launched background apps every minute while active and immediately after resume; the foreground hook still handles active-app changes immediately.

The main Eco Mode switch and restore buttons take manual control. A manual OFF remains in effect until the power source changes or you choose **Resume automatic mode**. A manually activated profile stays under manual control across power changes; **Resume automatic mode** first restores that profile, then returns control to automation. Turning automatic mode off restores any automatic background-app limits. Failed backup or pending recovery blocks automatic activation. Quitting stops monitoring and follows the same restore/retry/quit-anyway flow as manual mode.

The opt-in preference is stored independently in `%APPDATA%\EcoMode\automation-settings.json`. Profile imports, resets, language changes and view changes preserve it. Manual overrides are temporary and are not saved across app restarts.

### Live apps

Click **Live apps** on the dashboard in either view, or choose **View → Live apps**. The separate window lists processes in your Windows session and refreshes every two seconds while visible. Search by executable name or PID, or filter for managed apps, exclusions or settings that need restoration. Hiding EcoMode in the tray hides the list; hidden and minimized lists pause polling.

**Managed by EcoMode** means EcoMode is tracking changes for that process. **Priority** and **EcoQoS** show current Windows readings; **Windows managed** means the execution-speed EcoQoS flag is controlled by Windows rather than explicitly set. An EcoQoS reading alone does not establish which program set it. Inaccessible readings are shown as unavailable, and opening or refreshing the list never changes process settings.

Select an app and click **Never throttle this app** to save an exclusion for every instance of that executable, including future launches. EcoMode immediately attempts to restore tracked settings for excluded processes. Failed cleanup stays visible as **Needs restoration**, with the existing restore/retry/quit-anyway recovery path available. **Remove exclusion** allows EcoMode to manage the app again; an active process mode checks background apps immediately. These actions save only the exclusion change and preserve other unsaved profile edits. Built-in safety exclusions and Windows-protected apps cannot be enabled for throttling from this list.

[Live app list preview](docs/live-apps.png) · [French preview](docs/live-apps-fr.png). Screenshots use representative test data.

### Preset profiles

Choose a **Preset profile** and click **Load**. Confirming replaces and saves the current profile, including any unsaved edits. Review the options, GPU paths and bypass list, then switch Eco Mode **ON** to apply it. Switch **OFF** and finish restoring any pending changes before switching presets. Loading a preset preserves your saved original settings.

| Preset | Enabled changes | Bypass list focus |
| --- | --- | --- |
| Balanced | Background EcoQoS and power-saving GPU preferences for Edge/Chrome | Common calls, audio and OBS |
| Battery Saver | Power-plan EPP/boost, Edge/Chrome policies, browser GPU preferences and EcoQoS | Common calls and audio |
| Gaming | Background EcoQoS | Launchers, Discord, OBS and overlay tools |
| Development | Background EcoQoS | IDEs, compilers, build tools, runtimes, terminals and container hosts |
| Creative Work | Background EcoQoS | Editors, renderers, encoders, recording and audio tools |
| Video Calls | Background EcoQoS | Meeting apps, browsers, WebView, OBS and audio |

Battery Saver uses the existing Eco power values (EPP 85 AC / 90 DC, CPU boost disabled) and disables Edge/Chrome hardware acceleration. It can reduce performance on demanding tasks. Power-plan and browser policy changes require Administrator privileges; browser policies need a browser restart.

Presets are starting points. **Add the executable names of your games, renderers, build tools or meeting apps to the bypass list**, especially if they must run at full speed while in the background. Foreground applications retain their original priority; the built-in system bypass list also remains active. GPU paths cover common installation locations, and missing files are skipped; use **Add app** for other locations. Gaming, Development, Creative Work and Video Calls leave power-plan, browser and GPU settings untouched.

The same presets are available as importable JSON files in [`src/EcoMode/Profiles/`](src/EcoMode/Profiles/) and are embedded in the app, so the picker also works when only the executable is copied. Release ZIPs include the preset files in `profiles/`. Creative Work uses the existing `production.json` filename. Edited presets can be saved and exported like any other profile.

User GPU originals are stored separately from profiles at `%APPDATA%\EcoMode\original-settings.json`. Machine-wide power plan and browser policy originals are kept in the protected 64-bit registry value `HKLM\SOFTWARE\ToolsLib\EcoMode\OriginalSettingsV1`, shared across Windows accounts and sessions. Only one EcoMode instance can run on a machine at a time. An administrator launch migrates machine entries from older per-user snapshots before applying new changes.

Snapshots preserve the original AC/DC EPP and boost values for each affected power plan and the exact supported policy/GPU values. Values that did not originally exist are removed on restore. Adding options, GPU apps, or another active power plan extends the appropriate snapshot before those settings are changed. Repeated Eco ON does not overwrite saved originals. GPU changes preserve unrelated preference fields. Unknown snapshot destinations, duplicate entries, and invalid value types/ranges are rejected before restoration.

Run EcoMode as Administrator to capture, apply, or restore system power settings and browser policies. Without elevation, user GPU preferences and accessible processes are still supported. Failed restores remain pending for retry, including after restarting the app. Power values are restored to their original plans without switching away from the currently active plan. Browser policy changes require a browser restart.

EcoMode always excludes known Malwarebytes, Microsoft Defender, and critical Windows processes through its internal [process exclusion list](src/EcoMode/Power/ProcessExclusions.cs). Profile imports and user bypass edits cannot remove these exclusions. Windows-protected processes, including Protected Process Light (PPL), are also detected before requesting write access; processes whose protection status cannot be read are left unchanged. Add other security or background tools to **Never throttle** as needed. EcoMode tracks successful priority and QoS changes separately and skips restoration writes when the current values already match the originals. An unsuccessful apply does not create a restore obligation for settings that were never changed; genuine partial restore failures remain pending for retry.

**Quit** shows a warning that EcoMode will restore the power plan settings, browser policies, GPU preferences, and original process priority/EcoQoS state it changed. Choose **OK** to restore and exit, or **Cancel** to keep running. Restoration includes processes subsequently added to the bypass list and settings whose options were subsequently unchecked. If any restoration fails, a warning offers **Retry restoration**, **Keep EcoMode open**, or **Quit anyway**, in both views and with or without Administrator access. Retry is the default; Cancel or closing the warning keeps EcoMode open. **Quit anyway** closes even when a process is protected, access is denied, cleanup throws, or saved recovery data is unreadable. It preserves saved power plan and registry originals for a later restore, without pretending that failed changes were restored. Process originals are held only in memory: blocked priority/EcoQoS settings may remain until those processes restart, and their restore records are released when you quit. Reopen as Administrator for pending system changes; restart browsers after restoring policies. Failed hook removal stops further process changes while Windows releases the remaining hook when EcoMode exits. Journal update failures do not prevent best-effort rollback; unchanged pending records can be retried on the next launch. [View the restore-failure warning](docs/restore-warning.png).

Closing the window with **X** still keeps EcoMode running in the tray. Windows shutdown attempts restoration without showing a modal warning. Cleanup cannot run if the process is forcibly terminated. Processes whose original throttling state cannot be queried are left unchanged. Uninstall preserves profiles and both restore journals so pending settings can still be recovered after reinstalling. Imported profiles are validated before saving; null/blank entries are normalized, and malformed startup profiles fall back to defaults without blocking the recovery UI.

## 🛠 Tech

* **C# (.NET 10 LTS WinForms)**
* **P/Invoke (Win32 APIs)** for EcoQoS and power throttling
* **Inno Setup** for installer packaging

## Build and Run (development)

Open `EcoMode.slnx` to load the app and regression projects together. Application sources live under `src/EcoMode/`, grouped into `Application`, `Configuration`, `Power`, `Settings`, `Interop`, `UI` and `Localization`. Embedded icons and presets are kept with the app. The executable remains `ecomode.exe`, and existing profiles and restore journals retain their formats and locations.

```text
src/EcoMode/                     Windows app, resources and publish profile
tests/EcoMode.RegressionTests/   Application regression and UI checks
tests/ReleaseScripts.Tests.ps1   Release packaging and publication checks
installer/EcoMode.iss            Inno Setup installer
scripts/                        Release automation
docs/                           User-facing screenshots
artifacts/                      Generated previews and packages (ignored)
```

`Directory.Build.props` shares nullable checks, implicit usings, deterministic builds and warnings-as-errors across projects. `.editorconfig` defines formatting; CI checks C# whitespace after building the solution.

Requires Windows and the .NET 10 SDK version specified in `global.json`. Compatible patches in the same SDK feature band can be used locally when that exact version is unavailable. CI and release workflows install the specified version to keep SDK selection reproducible.

```
dotnet build EcoMode.slnx -c Release
dotnet run --project src/EcoMode/EcoMode.csproj
```

Run the regression checks on Windows with:

```
dotnet run --project tests/EcoMode.RegressionTests/EcoMode.RegressionTests.csproj
```

The checks use temporary snapshot files, disposable registry keys, and dedicated child processes/windows. Native power writes are exercised on a duplicated inactive plan, which is restored and deleted; the active plan is left unchanged. The form is rendered offscreen to check the button layout. Pass `-- --skip-power-read` on hosted VMs to omit the hardware-dependent power read/write checks. [CI](.github/workflows/ci.yml) runs the remaining checks and release-script tests on branch pushes and pull requests with read-only repository permissions.

## Deployment

Build the standalone Windows x64 executable with the checked-in publish profile:

```powershell
dotnet publish src/EcoMode/EcoMode.csproj -c Release -r win-x64 -p:PublishProfile=Standalone
```

The output is `artifacts/standalone/win-x64/ecomode.exe`. This single executable bundles the app, .NET runtime, Windows Forms libraries, native runtime libraries, and built-in presets. It can be copied to another Windows x64 PC without installing .NET. Native libraries are extracted automatically when it starts.

In Visual Studio, choose **Publish → Standalone** with the **Release** configuration. The installer script in `installer/EcoMode.iss` also uses this standalone output by default. The automated release script uses the same profile to build the portable ZIP and installer.

Files directly under `src/EcoMode/bin/Debug/` or `src/EcoMode/bin/Release/` are development builds and can still require an installed runtime. Distribute the published executable or the release packages.

## Automated releases

Push a version tag such as `v1.2.3` (or `1.2.3`) to run [the release workflow](.github/workflows/release.yml). Prerelease tags such as `v1.2.3-rc.1` are supported. The workflow checks the tag, runs regression checks, and produces:

* `EcoMode-1.2.3-win-x64.zip`: portable, self-contained app with profiles and documentation.
* `EcoMode-1.2.3-win-x64-setup.exe`: self-contained Inno Setup installer.
* `EcoMode-1.2.3-win-x64-SHA256SUMS.txt`: SHA-256 checksums for both packages.

The tag determines the app version, Windows file version, installer version, and filenames. The workflow creates a **draft release** with the packages attached. Review it and publish it when ready. Publishing a release directly also triggers the build for its tag. Existing complete published packages are preserved when the publish event triggers a second run; draft or incomplete mutable releases can be retried. If immutable releases are enabled, let the tag build finish attaching all packages before publishing the draft.

Packages are also available as workflow artifacts for 30 days. Uploads use the built-in `GITHUB_TOKEN` with `contents: write`; no personal access token is needed. Binaries are unsigned unless code signing is configured separately.

The workflow uses the .NET 10 SDK specified in `global.json`, Windows Server 2025, and Inno Setup 7.1.0. GitHub actions are pinned to verified release commits, and [Dependabot](.github/dependabot.yml) checks weekly for updates. Hardware-dependent power-plan read/write checks are skipped on hosted VMs; the remaining process, registry, snapshot, and UI regression checks still run.

To build the same packages locally with PowerShell 7 and an installed Inno Setup compiler:

```powershell
./scripts/Build-Release.ps1 -Version 1.2.3 -IsccPath 'C:\Program Files\Inno Setup 7\ISCC.exe'
```

Output is written to `artifacts/release/`. Test version handling and publication logic without contacting GitHub with `./tests/ReleaseScripts.Tests.ps1`.

## Code signing

Sign your binaries with a code signing certificate to ensure integrity and authenticity.

```
signtool.exe sign /f "path\to\your\certificate.pfx" /p "your_certificate_password" EcoModeSetup.exe
```

## Notes & tweaks

Tuning values: Eco EPP/Boost values are defined in `WindowsSystemSettings.GetTargets`. Eco OFF restores the saved values instead of applying fixed performance defaults.

## 🙏 Thanks

This project was inspired by and builds upon ideas from
[**EnergyStar by imbushuo**](https://github.com/imbushuo/EnergyStar).
Big thanks for making their work available to the community! 💚

## 📜 License

This project is licensed under the [CC BY-NC-ND 4.0 License](LICENSE).

You are free to share the software for non-commercial use **as-is** with attribution.  
You may not modify, rebrand, or use the name **EcoMode** for derivative works.  
