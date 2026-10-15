namespace LocalScenarioSetup;

/// <summary>Finds an executable on a PATH string, honouring PATHEXT on Windows.</summary>
internal static class ToolLocator
{
    public static string? Find(string name, string? pathVariable, string? pathExt)
    {
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return null;
        }

        var extensions = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            // A name that already carries an extension is tried as-is first.
            if (Path.HasExtension(name))
            {
                extensions.Add(string.Empty);
            }

            var exts = string.IsNullOrWhiteSpace(pathExt) ? ".COM;.EXE;.BAT;.CMD" : pathExt;
            extensions.AddRange(exts.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        else
        {
            extensions.Add(string.Empty);
        }

        foreach (var dir in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var cleaned = dir.Trim('"');
            foreach (var ext in extensions)
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(cleaned, name + ext);
                }
                catch (ArgumentException)
                {
                    break;
                }

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
