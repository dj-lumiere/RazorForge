namespace Builder.Declaration;

/// <summary>
/// The module path of a source file that has no <c>module</c> header. The build driver and the manifest's
/// executable-module index both use it, so a header-less entry file resolves to the same module name in
/// both places.
/// </summary>
internal static class ModulePathDerivation
{
    /// <summary>
    /// Derives a module path for a file with no <c>module</c> header, from its location relative to
    /// the project root (the config.toml directory). Path segments are PascalCased (whitespace
    /// removed, each word's first letter capitalized), <c>.</c>/<c>..</c> segments are dropped, the
    /// file extension is stripped, and segments are joined with <c>/</c>. E.g.
    /// <c>../SomeFolder/SomeMoreFolder/file a.rf</c> -> <c>SomeFolder/SomeMoreFolder/FileA</c>.
    /// </summary>
    internal static string FromFile(string projectRoot, string filePath)
    {
        string rel = Path.GetRelativePath(relativeTo: projectRoot, path: filePath);
        var segments = rel.Split(separator:
                               ['/', '\\'],
                               options: StringSplitOptions.RemoveEmptyEntries)
                          .Where(predicate: s => s != "." && s != "..")
                          .ToList();

        if (segments.Count == 0)
        {
            return PascalCaseSegment(segment: Path.GetFileNameWithoutExtension(path: filePath));
        }

        // Strip the extension from the final segment (the file name).
        segments[^1] = Path.GetFileNameWithoutExtension(path: segments[^1]);
        return string.Join(separator: '/', values: segments.Select(selector: PascalCaseSegment));
    }

    /// <summary>
    /// PascalCases one path segment: splits on whitespace, capitalizes the first letter of each word
    /// (preserving the rest), and concatenates. <c>file a</c> -> <c>FileA</c>; an already-cased
    /// <c>SomeFolder</c> stays <c>SomeFolder</c>.
    /// </summary>
    private static string PascalCaseSegment(string segment)
    {
        string[] words = segment.Split(separator: (char[]?)null,
            options: StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return segment;
        }

        var sb = new System.Text.StringBuilder();
        foreach (string w in words)
        {
            sb.Append(value: char.ToUpperInvariant(c: w[index: 0]));
            if (w.Length > 1)
            {
                sb.Append(value: w[1..]);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Reads the first "module X" declaration from a source file.
    /// </summary>
    internal static string? ReadDeclaredModule(string filePath)
    {
        try
        {
            foreach (string line in File.ReadLines(path: filePath))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith(value: "module "))
                {
                    string name = trimmed["module ".Length..]
                       .Trim();
                    int commentIdx = name.IndexOf(value: '#');
                    if (commentIdx >= 0)
                    {
                        name = name[..commentIdx]
                           .Trim();
                    }

                    return name;
                }

                // Skip comments, empty lines, and imports — stop at first real declaration
                if (!string.IsNullOrWhiteSpace(value: trimmed) &&
                    !trimmed.StartsWith(value: '#') && !trimmed.StartsWith(value: "import "))
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                value:
                $"Warning: Could not read or parse '{filePath}' for module name extraction: {ex.Message}");
        }

        return null;
    }
}
