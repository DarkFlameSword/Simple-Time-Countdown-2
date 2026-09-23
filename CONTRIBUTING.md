# Contributing

Thanks for helping improve Simple Time Countdown.

## Development setup

1. Use Windows 10 (2004 or later) or Windows 11.
2. Install the .NET SDK pinned in [global.json](global.json) (8.0.419, or a later 8.0.4xx patch). Running `dotnet --version` from the repository root must succeed.
3. Open [SimpleTimeCountdown.sln](SimpleTimeCountdown.sln) in Visual Studio 2022 with the ".NET desktop development" workload, or use the CLI from the repository root:

```powershell
dotnet restore .\SimpleTimeCountdown.sln
dotnet build .\SimpleTimeCountdown.sln
dotnet test .\SimpleTimeCountdown.sln
dotnet run --project .\src\SimpleTimeCountdown.App\SimpleTimeCountdown.App.csproj
```

4. The Windows 10/11 SDK is only needed to build the MSIX or to sign (`makeappx`, `makepri`, `signtool`).

Don't try installs with the installer project itself: started with `dotnet run` it has no payload and cannot install (with `--uninstall` it still removes your own installation). Build `Setup.exe` with `scripts\Build-SetupExe.ps1`; it performs a real per-user install, so try it in a virtual machine (`scripts\Test-SetupExe.ps1` automates an install, reinstall and uninstall there) or uninstall it from Settings > Apps afterwards. The [README](README.md#release-scripts) describes the release scripts and signing; run them with Windows PowerShell 5.1 (`powershell.exe`).

## Project expectations

- Keep the app lightweight and desktop-native; prefer WPF/.NET changes over heavier browser-style runtimes.
- Preserve the simple floating-panel UI and avoid adding noisy chrome.
- Follow the Casebook design language in `src/SimpleTimeCountdown.App/Themes/VictorianTheme.xaml`: paper, ink and one oxblood accent. Use its brushes with `{DynamicResource ...}` (they are swapped at runtime for Windows contrast themes) and its shared styles, rather than hard-coded colours, fonts or sizes. Rendered text is never smaller than 12 DIP.
- Put every user-facing string in `Services/LocalizationService.cs`, in both the English and the Simplified Chinese table. No UI text as literals in XAML or C#.
- Text must always display completely: wrap it, or trim it with an ellipsis and expose the full text in a tooltip and to screen readers. Give every text input a `MaxLength`, and let dialogs scroll rather than grow past the screen.
- Keep everything usable from the keyboard with a visible focus ring, give icon-only buttons an `AutomationProperties.Name` and a tooltip, and check new UI in a High Contrast theme and at 150% scaling.
- Never let a file or registry failure crash the app; log it with `AppLog`.
- Keep the release scripts working for the portable zip, `Setup.exe` and the MSIX. If you touch packaging, run `scripts\Build-SetupExe.ps1`; CI also runs `scripts\Test-SetupExe.ps1` and `scripts\Publish-MSIX.ps1`.

## Versions and icons

- Change the product version only in [Directory.Build.props](Directory.Build.props) (`<VersionPrefix>`); everything else derives from it.
- The icon's design source is `src/SimpleTimeCountdown.App/Assets/AppIcon.svg`. After editing it, re-export `AppIcon-16.png` to `AppIcon-1024.png`, run `.\scripts\Generate-AppAssets.ps1 -Force`, and commit the regenerated `AppIcon.ico` and `packaging/msix/Assets` files.

## Pull requests

- Describe the user-facing problem and the approach you took.
- Mention any UI, packaging, or installer impact, with before/after screenshots for UI changes (English and Chinese where the layout differs).
- Include validation notes such as `dotnet test`, packaging, or manual smoke testing. For every pull request CI builds and runs the tests, builds the portable zip, `Setup.exe` and the MSIX, and installs, reinstalls and uninstalls `Setup.exe` on a clean runner.
- Keep unrelated cleanup out of the same PR when possible.

## Code style

- Follow the repository `.editorconfig`.
- Do not commit generated output from `bin/`, `obj/`, or `artifacts/`.
- Update `README.md` and `README.zh-CN.md` together when behaviour or the release flow changes.
