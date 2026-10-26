# Changelog

## 1.3.0

- Add an optional, per-user Start with Windows setting in the File and tray menus, disabled by default.
- Launch directly in the notification area at sign-in without flashing a window, requesting elevation, or interrupting with the first-run view chooser.
- Preserve saved preferences and automatic background-app mode, and retain restoration on Quit and Windows shutdown.
- Keep duplicate sign-in launches quiet and let users open the running app from its tray icon.
- Remove the installed copy's startup entry on uninstall while preserving entries for other copies and original-settings recovery records.
- Translate startup controls and messages into all four supported languages and add startup, hidden-window lifetime, recovery, and installer regression coverage.

## 1.2.2

- Clear the full switch background during repainting so old text and native checkbox pixels cannot show behind the main or smaller switches.
- Resize switches when fonts change, keep captions clear of the thumb, and handle very small bounds safely.
- Use a rounded keyboard-focus outline that follows the switch track.
- Keep the automatic-mode switch and help text visible in translated dashboards, including French.
- Add rendering, sizing, and layout regression coverage for both switch sizes across all four languages.

## 1.2.1

- Publish the complete 1.2 feature set below after fixing hosted-runner display assumptions in the battery dashboard checks.
- Keep app-rule editors usable with expanded history on small displays using a scrollable panel.
- Expose regression failures as GitHub annotations and support explicit publication through an annotated tag after verifying all release assets.

## 1.2.0 (tagged; release build did not complete)

- Add Standard and Advanced views, a first-launch chooser, and a view selector available at any time.
- Translate the interface into English, French, Spanish, and Simplified Chinese using bundled .NET resources.
- Replace Eco ON/OFF buttons and option checkboxes with clear switches, and fix header overflow.
- Automatically capture missing original settings before applying changes, preserving existing backups.
- Skip security and Windows-protected processes. Track only successful process changes so denied operations do not create false recovery obligations.
- Restore process, power-plan, and registry settings on quit. Offer Retry, Keep open, or Quit anyway when cleanup cannot finish, preserving saved system recovery records.
- Check for newly launched background processes every minute while process mode is active and immediately after resume.
- Add battery readings and an optional 60-minute history, with observed drain rates when sufficient data is available.
- Add opt-in automatic background-app mode on battery, restoration on AC, and manual overrides that preserve manually applied profiles.
- Add a live app list with search, status filters, priority and EcoQoS readings, and persistent per-executable exclusions.
- Organize application sources under `src/EcoMode/` and regression checks under `tests/EcoMode.RegressionTests/`; update CI and release packaging paths.
- Expand regression coverage to 113 local checks, including native process recovery, localization, view layouts, automation, and live app controls. Hardware-dependent power-plan checks are skipped on hosted CI.

The Windows x64 installer and portable ZIP include the .NET 10 runtime. Users do not need to install .NET separately. Battery readings depend on hardware support, and history is kept for the current app session only.
