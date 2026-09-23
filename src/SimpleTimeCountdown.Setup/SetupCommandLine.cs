namespace TimeCountdown.Setup;

/// <summary>
/// Parsed command line. Unknown switches are collected in <see cref="Errors"/> instead of being
/// ignored, so "/S" or "--install-dir C:\x" typos fail loudly rather than install somewhere
/// unexpected. Switches that take a value accept both "--name=value" and "--name value".
/// </summary>
internal sealed class SetupCommandLine
{
    public bool Silent { get; private set; }

    public bool Uninstall { get; private set; }

    public bool RemoveData { get; private set; }

    public bool Launch { get; private set; }

    public bool CreateDesktopShortcut { get; private set; } = true;

    public bool ShowHelp { get; private set; }

    public string? InstallDirectory { get; private set; }

    public string? LogPath { get; private set; }

    public string? Language { get; private set; }

    public List<string> Errors { get; } = [];

    public static SetupCommandLine Parse(IReadOnlyList<string> args)
    {
        var commandLine = new SetupCommandLine();
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index].Trim();
            var separator = argument.IndexOf('=');
            var name = (separator > 0 ? argument[..separator] : argument).ToLowerInvariant();
            string? inlineValue = separator > 0 ? argument[(separator + 1)..] : null;

            switch (name)
            {
                case "--silent":
                    commandLine.Silent = true;
                    break;
                case "--uninstall":
                    commandLine.Uninstall = true;
                    break;
                case "--remove-data":
                    commandLine.RemoveData = true;
                    break;
                case "--launch":
                    commandLine.Launch = true;
                    break;
                case "--no-launch":
                    // Setup 2.0 launched the app after a silent install unless told not to; that is
                    // now the default, and the switch is still accepted so existing scripts keep working.
                    commandLine.Launch = false;
                    break;
                case "--no-desktop-shortcut":
                    commandLine.CreateDesktopShortcut = false;
                    break;
                case "--help" or "-h" or "/?" or "-?":
                    commandLine.ShowHelp = true;
                    break;
                case "--install-dir":
                    commandLine.InstallDirectory = commandLine.TakeValue(argument, inlineValue, args, ref index);
                    break;
                case "--log":
                    commandLine.LogPath = commandLine.TakeValue(argument, inlineValue, args, ref index);
                    break;
                case "--lang":
                    var language = commandLine.TakeValue(argument, inlineValue, args, ref index);
                    if (language is not null && !InstallerText.IsSupportedLanguage(language))
                    {
                        commandLine.Errors.Add(argument);
                    }
                    else
                    {
                        commandLine.Language = language;
                    }

                    break;
                default:
                    commandLine.Errors.Add(argument);
                    break;
            }
        }

        return commandLine;
    }

    private string? TakeValue(string argument, string? inlineValue, IReadOnlyList<string> args, ref int index)
    {
        var value = inlineValue;
        if (value is null && index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = args[++index];
        }

        value = value?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(value))
        {
            Errors.Add(argument);
            return null;
        }

        return value;
    }
}
