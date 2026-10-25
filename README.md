# EcoMode

⚡ **EcoMode** is a lightweight Windows utility that helps reduce power consumption and improve efficiency by applying smart system tweaks and process management rules.

![EcoMode Screenshot](docs/screenshot.png)

## ✨ Features

* **One-click Eco Mode toggle**: enable power-saving with *Eco ON* and restore your saved original settings with *Eco OFF*.
* **Original settings backup**: save a baseline and restore it at any time, including after restarting EcoMode.
* **Windows power plan tweaks**: automatically adjusts *Energy Performance Preference (EPP)* and *Boost* settings.
* **Browser policies**: applies registry policies for Microsoft Edge and Google Chrome (hardware acceleration off, battery saver/efficiency mode on).
* **Per-app GPU preferences**: configure applications to use *integrated GPU (power-saving)* or *discrete GPU (performance)*.
* **EcoQoS (Quality of Service)**: throttle background processes while keeping the active foreground app boosted.
* **Configurable bypass list**: define which processes should never be throttled.
* **Tray support**: runs in the background, minimizes to system tray instead of closing.
* **Import / export / reset profiles**: easily back up or share your configuration.
* **Activity log**: console-like log viewer with color-coded log levels.

## 📥 Installation

1. Download the latest installer from the [Releases](../../releases) page.
2. Run the setup — EcoMode will be installed with Start Menu and system tray support.
3. Launch EcoMode from the Start Menu or tray icon.

*(Installer built with Inno Setup — no external dependencies needed.)*

## 🚀 Usage

* Click **🔋 Eco ON** to enable Eco Mode.
* On the first **Eco ON**, accept the prompt to save your original settings. Choosing **No** leaves Eco Mode off.
* Click **Save Original Settings** to save a new baseline for the selected options and configured GPU apps. Restore pending EcoMode changes first; replacing an existing baseline requires confirmation.
* Click **Eco OFF** to undo the settings EcoMode applied, regardless of the currently selected options or app list.
* Click **Restore Original Settings** to reapply the entire saved baseline whenever needed.
* Use the **check boxes** to control which tweaks are applied (Power Plan, Edge/Chrome policies, GPU prefs, EcoQoS).
* Use **Browse…** to add apps for GPU preference management.
* Add process names to the **Bypass list** if they should never be throttled.
* Right-click the **tray icon** for quick Save / Quit options.

The original settings snapshot is stored separately from profiles at `%APPDATA%\EcoMode\original-settings.json`. It preserves the original AC/DC EPP and boost values for each affected power plan, plus the exact browser policy and GPU preference values and registry types. Values that did not originally exist are removed on restore. Adding options, GPU apps, or another active power plan extends the snapshot before those settings are changed. Repeated Eco ON does not overwrite saved originals.

Run EcoMode as Administrator to capture, apply, or restore system power settings and browser policies. Without elevation, user GPU preferences and accessible processes are still supported. Failed restores remain pending for retry, including after restarting the app. Power values are restored to their original plans without switching away from the currently active plan. Browser policy changes require a browser restart.

**Quit** shows a warning that EcoMode will restore the power plan settings, browser policies, GPU preferences, and original process priority/EcoQoS state it changed. Choose **OK** to restore and exit, or **Cancel** to keep running. Restoration includes processes subsequently added to the bypass list and settings whose options were subsequently unchecked. If a restore fails, EcoMode stays open, warns you, and retains pending changes for retry; restoring system settings may require Administrator privileges. Closing the window with **X** still keeps EcoMode running in the tray. Windows shutdown also attempts restoration without showing a modal warning; any failed system restores remain saved for the next launch. Cleanup cannot run if the process is forcibly terminated. Processes whose original throttling state cannot be queried are left unchanged.

## 🛠 Tech

* **C# (.NET WinForms)**
* **P/Invoke (Win32 APIs)** for EcoQoS and power throttling
* **Inno Setup** for installer packaging

## Build and Run (development)

```
dotnet build
dotnet run
```

Run the regression checks on Windows with:

```
dotnet run --project tests/EcoMode.RegressionTests.csproj
```

The checks use temporary snapshot files, a disposable user registry key, and dedicated child processes. They read the active power plan without changing it and render the form offscreen to check the button layout.

## Deployment

```
# if you want to rely on user's .NET Desktop Runtime (smaller):
dotnet publish -c Release -r win-x64 --self-contained false

# OR ship everything (bigger, no runtime needed):
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

..then build it with Inno Setup Compiler.

## Automated releases

Push a version tag such as `v1.2.3` (or `1.2.3`) to run [the release workflow](.github/workflows/release.yml). Prerelease tags such as `v1.2.3-rc.1` are supported. The workflow checks the tag, runs regression checks, and produces:

* `EcoMode-1.2.3-win-x64.zip`: portable, self-contained app with profiles and documentation.
* `EcoMode-1.2.3-win-x64-setup.exe`: self-contained Inno Setup installer.
* `EcoMode-1.2.3-win-x64-SHA256SUMS.txt`: SHA-256 checksums for both packages.

The tag determines the app version, Windows file version, installer version, and filenames. The workflow creates a **draft release** with the packages attached. Review it and publish it when ready. Publishing a release directly also triggers the build for its tag. Existing complete published packages are preserved when the publish event triggers a second run; draft or incomplete mutable releases can be retried. If immutable releases are enabled, let the tag build finish attaching all packages before publishing the draft.

Packages are also available as workflow artifacts for 30 days. Uploads use the built-in `GITHUB_TOKEN` with `contents: write`; no personal access token is needed. Binaries are unsigned unless code signing is configured separately.

The workflow uses the latest matching .NET 9 SDK, Windows Server 2025, and Inno Setup 7.1.0. GitHub actions are pinned to verified release commits, and [Dependabot](.github/dependabot.yml) checks weekly for updates. The hardware-dependent active power-plan read check is skipped on hosted VMs; the remaining process, registry, snapshot, and UI regression checks still run.

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
