# Contributing

Thanks for helping improve Simple Time Countdown.

## Development setup

1. Install .NET 8 SDK with Windows desktop support.
2. Open [SimpleTimeCountdown.sln](/E:/Work/Github%20Repository/Time%20Countdown/SimpleTimeCountdown.sln) in Visual Studio 2022, or build with:

```powershell
dotnet build E:\Work\Github Repository\Time Countdown\SimpleTimeCountdown.sln
```

## Project expectations

- Keep the app lightweight and desktop-native.
- Prefer WPF/.NET changes over heavier browser-style runtime additions.
- Preserve the simple floating-panel UI and avoid adding noisy chrome.
- Keep release scripts working for both `Setup.exe` and `MSIX`.

## Pull requests

- Describe the user-facing problem and the approach you took.
- Mention any UI, packaging, or installer impact.
- Include validation notes such as `dotnet build`, packaging, or manual smoke testing.
- Keep unrelated cleanup out of the same PR when possible.

## Code style

- Follow the repository `.editorconfig`.
- Do not commit generated output from `bin/`, `obj/`, or `artifacts/`.
- Update `README.md` or `docs/` when behavior or release flow changes.
