# Simple Time Countdown

<div align="center">

[**English**](README.md) | [**简体中文**](README.zh-CN.md)

</div>

Keep every deadline in sight. Simple Time Countdown is a small countdown panel that floats on your Windows desktop and shows how much time is left for each thing you are waiting on or working towards: exams, submissions, bills, trips, birthdays.

![Main panel](docs/images/example-main.png)

- **Light on your PC**: a native Windows app, with no web browser inside it and no background service.
- **Private**: everything stays on your computer. No account, no sync, no tracking, no internet access.
- **A look of its own**: a single parchment "casebook" page, with each countdown set like a printed entry.

Works on Windows 10 (version 2004 or later) and Windows 11, in English and Simplified Chinese.

## Download and install

Download the latest version from the [Releases page](../../releases/latest) ([all versions](../../releases)). Nothing else needs to be installed first.

| File | Choose it if… |
| --- | --- |
| `SimpleTimeCountdown-Setup-win-x64.exe` | **Recommended.** You want a normal installation with Start menu and desktop shortcuts, and an entry in Settings > Apps. No administrator rights needed. |
| `SimpleTimeCountdown-Release-win-x64-portable.zip` | You don't want to install anything: unzip it anywhere (even a USB drive) and run `TimeCountdown.exe`. |
| `SimpleTimeCountdown_<version>_win-x64.msix` | You prefer apps installed and removed by Windows' own package system. It installs with a double-click only when it is signed with a certificate your PC trusts. |

To install, run the Setup file and follow the steps. By default it installs just for you, into `%LocalAppData%\Programs\Simple Time Countdown`; you can choose another folder, whether to add a desktop shortcut, and whether to start the app when Setup finishes. Setup speaks English or Chinese, following your Windows display language. Installing a newer version over an older one keeps your countdowns.

## Using Simple Time Countdown

### Add a countdown

Select **+** in the panel's top bar (or press `Ctrl+N`), then fill in:

- **Title** (required, up to 120 characters). It is always shown in full on the card; long titles simply use a slightly smaller size.
- **Note** (optional, up to 280 characters), for example a room number or what to bring.
- **Due date**, **hour** and **minute**, in any **time zone**. If a time does not exist or happens twice because of a daylight-saving change, the form tells you.
- **Reminder**: none, or 15 minutes, 1 hour, 1 day or 3 days before.
- **Pin** it to keep it at the top of the list.

![New countdown](docs/images/example-add.png)

### Read a card

- The label at the top shows the status: **Standing**, **Urgent** or **Perilous** as the deadline approaches (you choose when in Settings), then **Overdue** once it has passed.
- The large figures show the time left (days and hours, then hours and minutes, then minutes and seconds). Past the deadline they count the time since.
- The burning cigar shows how much of the time since you created the countdown has gone. When the deadline passes, only a heap of ash is left.
- The buttons under each card pin or unpin it, archive it, edit it or delete it.

### Archive, restore and search

- Archive a countdown you are done with; it gets a postmark stamp. Switch between the active list and the archive with the archive button in the top bar (or `Ctrl+E`), and restore an archived countdown at any time.
- Select the magnifier (or press `Ctrl+F`) to search titles and notes. `Esc` clears the search, and a second `Esc` closes it.

![Archive](docs/images/example-archive.png)

### Reminders

You get a Windows notification when a reminder is due and when a deadline is reached. Selecting the notification opens the panel. Reminders that fall due while your PC is off or the app is closed are shown the next time the app starts, grouped together. Notifications follow your Windows notification settings (Focus / Do not disturb included).

### Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| `Ctrl+N` | New countdown |
| `Ctrl+F` | Search |
| `Esc` | Clear the search, then close it |
| `Ctrl+E` | Show or hide the archive |
| `Ctrl+,` | Settings |

### The panel and the tray icon

- Drag the panel by any empty part of the paper, and resize it from its edges or the bottom-right corner. It remembers its size and position.
- The notification-area (tray) icon opens the panel with a single click. Its menu has Show panel, Add countdown, Settings, Always on top, Uninstall (Setup installations) and Exit.
- The close button either keeps the app running in the tray, so reminders keep coming, or exits it, as you choose in Settings. To quit completely, use **Exit** in the tray menu.

## Settings

Open Settings with the gear button in the top bar (or `Ctrl+,`). Changes apply immediately.

- **Display**: keep the panel above other windows, or keep it behind them; panel opacity (85–100%).
- **Behaviour and defaults**: language (English / 中文), launch when you sign in to Windows, keep running in the tray when the panel is closed, and the reminder and time zone new countdowns start with.
- **Status thresholds**: how close a deadline must be for a countdown to count as Perilous or Urgent.
- **Window position**: put the panel back in its default place.
- **About**: the version, a link to report a problem, and a button that opens the log folder.

![Settings](docs/images/example-setting.png)

## Accessibility

- Every action works from the keyboard, with a clearly visible focus ring.
- Screen readers get a one-sentence summary of each countdown and names for every button; the progress cigar is read as a percentage.
- Windows contrast themes are followed: the app switches to your system colours and turns off translucency and shadows.
- The app follows the Windows **Text size** setting (Settings > Accessibility > Text size), and the panel's text grows as you make the panel wider.
- Colours meet WCAG AA contrast even at the lowest panel opacity.

## Your data and privacy

- Your countdowns and settings are saved on your PC in `%AppData%\TimeCountdown\state.json`. The app keeps the previous good copy as `state.json.bak`.
- If the file ever becomes unreadable, the app restores the last good copy, keeps the unreadable file as `state.corrupt.<date and time>.json` so nothing is lost, and tells you where it is.
- Diagnostic logs (to help with problem reports) go to `%LocalAppData%\TimeCountdown\logs`; only the last 7 days are kept. **Settings > About > Open the log folder** takes you there.
- The MSIX version keeps both folders in its own private storage under `%LocalAppData%\Packages\SimpleTimeCountdown.Desktop_<id>\`.

## Uninstall

- **Setup version**: uninstall from Settings > Apps, or with **Uninstall** in the tray menu. You can choose to also delete your countdowns, settings and logs; otherwise they are kept for a later reinstall.
- **Portable version**: exit the app and delete its folder. Your countdowns remain in `%AppData%\TimeCountdown`; delete that folder too if you no longer need them.
- **MSIX version**: uninstall from Settings > Apps. Windows removes the app's data with it.

## Troubleshooting

- **The panel is gone.** Click the tray icon. If the panel is off-screen (for example after unplugging a monitor), open Settings and choose **Reset panel position**.
- **The panel disappears when I press Win+D.** With "Keep the panel behind other windows" on, *Show desktop* hides it too. Click the tray icon to bring it back.
- **I get no reminders.** Check that the countdown has a reminder, that the app is still running in the tray, and that Windows notifications (and Focus / Do not disturb) allow them.
- **Something went wrong.** Use **Settings > About > Report a problem**, and attach the latest log file from **Open the log folder**.

---

## For developers

### Build from source

Prerequisites:

- Windows 10 (2004 or later) or Windows 11
- .NET SDK 8.0.419 or a later 8.0.4xx patch; [global.json](global.json) pins it, and `dotnet --version` run from the repository root must succeed
- Optional: Visual Studio 2022 with the ".NET desktop development" workload
- Only for building the MSIX or signing: the Windows 10/11 SDK (`makeappx`, `makepri`, `signtool`)

Run these from the repository root:

```powershell
dotnet restore .\SimpleTimeCountdown.sln
dotnet build .\SimpleTimeCountdown.sln -c Release
dotnet test .\SimpleTimeCountdown.sln -c Release --no-build
dotnet run --project .\src\SimpleTimeCountdown.App\SimpleTimeCountdown.App.csproj
```

`dotnet build` only updates the build outputs under `src/**/bin/`. It does not produce the distributable packages; use the release scripts below.

The installer project (`src/SimpleTimeCountdown.Setup`) gets its payload only when `Build-SetupExe.ps1` publishes it, so a Setup started with `dotnet run` or from Visual Studio cannot install anything (with `--uninstall` it still removes the copy installed for your user). The `Setup.exe` the script produces performs a real per-user install (files under `%LocalAppData%\Programs`, shortcuts and a Settings > Apps entry): try it in a virtual machine, or uninstall it from Settings > Apps afterwards.

### Release scripts

Run the scripts with Windows PowerShell 5.1 (`powershell.exe`, as in the examples), which is what they are written and tested for. They default to `Release` and `win-x64` (override with `-Configuration` and `-RuntimeIdentifier win-x64|win-x86|win-arm64`), rebuild their output folders from scratch so no stale file can ship, and stop at the first failing step.

```powershell
# Portable zip
powershell -ExecutionPolicy Bypass -File .\scripts\Publish-Portable.ps1

# Portable zip + classic Setup.exe installer (the zip is embedded as the installer's payload)
powershell -ExecutionPolicy Bypass -File .\scripts\Build-SetupExe.ps1

# MSIX package, signed with a local development certificate (for testing on this PC only)
powershell -ExecutionPolicy Bypass -File .\scripts\Publish-MSIX.ps1 -DevCertificate
```

Outputs, relative to the repository root:

- Portable zip: `artifacts/packages/SimpleTimeCountdown-Release-win-x64-portable.zip`
- Setup EXE: `artifacts/packages/SimpleTimeCountdown-Setup-win-x64.exe`
- MSIX: `artifacts/packages/SimpleTimeCountdown_<version>_win-x64.msix`
- Debug symbols, kept out of the packages: `artifacts/symbols/<configuration>/<rid>/<package>/`, for example `artifacts/symbols/Release/win-x64/portable/`

Each package is moved into `artifacts/packages` only after it is complete and, when a certificate is configured, signed, so a failed run never leaves a partial package, or one that missed its signature, there. Without a certificate the outputs are unsigned and the scripts say so; the signing options are described in `scripts/ReleaseCommon.ps1`.

`Test-SetupExe.ps1` smoke-tests the `Setup.exe` that `Build-SetupExe.ps1` built: it installs it silently, reinstalls over it and uninstalls it through the Settings > Apps command, checking the files, shortcuts and registry after each step. It really installs the app for the current user, so CI runs it on a clean runner; locally, use a virtual machine. It refuses to run where Simple Time Countdown is already installed for your user.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Test-SetupExe.ps1
```

### Repository structure

- `.github/workflows/` — CI: build, tests, the three packages and a Setup.exe install/uninstall test
- `docs/images/` — README screenshots
- `packaging/msix/` — MSIX manifest template and logos
- `scripts/` — release scripts (portable zip, Setup.exe, MSIX, signing), the Setup.exe smoke test and icon generation
- `src/Shared/` — names the app and the installer must agree on
- `src/SimpleTimeCountdown.App/` — the WPF app
  - `Themes/VictorianTheme.xaml` — the Casebook design language: colours, type and shared control styles
  - `Controls/` — custom-drawn parts such as the cigar progress bar, the archive stamp and the card title
  - `Services/` — saving state, localization, themes and High Contrast, logging, launch at startup
  - `Models/`, `ViewModels/`, `Views/`, `Converters/`
  - `Assets/` — the icon source (`AppIcon.svg`), its PNG exports and `AppIcon.ico`
- `src/SimpleTimeCountdown.Setup/` — the Setup.exe installer
- `tests/` — automated tests
- `artifacts/` — generated output only, not committed

### Contributing and license

See [CONTRIBUTING.md](CONTRIBUTING.md). Simple Time Countdown is released under the [MIT License](LICENSE); the packages also redistribute the .NET runtime, see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
