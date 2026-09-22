using System.Text.Json;

namespace Copper;

public sealed class ContactConfig
{
    public string Name { get; set; } = "";
    public string ChatName { get; set; } = "";
    public string ProfileFile { get; set; } = "";
}

public sealed class AppConfig
{
    public string BridgeDir { get; set; } = "";
    public string Model { get; set; } = "claude-opus-5";
    /// <summary>Jason's own profile — Copper models both sides of the bridge.</summary>
    public string SelfFile { get; set; } = "people/jason.md";
    public ContactConfig Contact { get; set; } = new();

    /// <summary>Directory that copper.json was loaded from; people/ paths resolve against it.</summary>
    public string BaseDir { get; set; } = "";

    public static AppConfig Load()
    {
        var configPath = FindConfig()
            ?? throw new FileNotFoundException(
                "copper.json not found. Run copper from its project directory, or put copper.json next to the executable.");

        var config = JsonSerializer.Deserialize<AppConfig>(
                File.ReadAllText(configPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException($"Could not parse {configPath}.");

        config.BaseDir = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
        if (!Path.IsPathRooted(config.BridgeDir))
            config.BridgeDir = Path.GetFullPath(Path.Combine(config.BaseDir, config.BridgeDir));
        return config;
    }

    private static string? FindConfig()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            for (var i = 0; dir is not null && i < 6; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "copper.json");
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    public string PeopleDir => Path.Combine(BaseDir, "people");

    /// <summary>The whole people network, small files loaded in full: {slug → contents}.</summary>
    public IReadOnlyDictionary<string, string> LoadPeople()
    {
        var people = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(PeopleDir)) return people;
        foreach (var file in Directory.EnumerateFiles(PeopleDir, "*.md"))
            people[Path.GetFileNameWithoutExtension(file)] = File.ReadAllText(file);
        return people;
    }

    /// <summary>Contact's slug, derived from its profile file name (e.g. "people/marat.md" → "marat").</summary>
    public string ContactSlug => Path.GetFileNameWithoutExtension(Contact.ProfileFile);

    /// <summary>Append a dated note to a person's file, creating the file for a new person.</summary>
    public void AppendPersonNote(string person, string category, string note)
    {
        var slug = Slugify(person);
        Directory.CreateDirectory(PeopleDir);
        var path = Path.Combine(PeopleDir, slug + ".md");
        if (!File.Exists(path))
            File.WriteAllText(path, $"# {person}{Environment.NewLine}{Environment.NewLine}## Learned{Environment.NewLine}");
        File.AppendAllText(path, $"- [{DateTime.Now:yyyy-MM-dd}] ({category}) {note}{Environment.NewLine}");
    }

    /// <summary>Date of a person's grounding statement, or null if they don't have one yet.</summary>
    public DateTime? GroundingAsOf(string slug)
    {
        var path = Path.Combine(PeopleDir, slug + ".md");
        if (!File.Exists(path)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(path), @"\*\*As of (\d{4}-\d{2}-\d{2})\.\*\*");
        return m.Success && DateTime.TryParse(m.Groups[1].Value, out var d) ? d : null;
    }

    /// <summary>Replace (or insert) the "## Grounding" section with a fresh dated statement.</summary>
    public void ReplaceGrounding(string slug, string statement)
    {
        var path = Path.Combine(PeopleDir, slug + ".md");
        if (!File.Exists(path)) return;
        var text = File.ReadAllText(path);
        var section = $"## Grounding{Environment.NewLine}**As of {DateTime.Now:yyyy-MM-dd}.** {statement.Trim()}{Environment.NewLine}";

        var start = text.IndexOf("## Grounding", StringComparison.Ordinal);
        if (start >= 0)
        {
            var next = text.IndexOf("\n## ", start + 4, StringComparison.Ordinal);
            text = next >= 0
                ? text[..start] + section + text[(next + 1)..]
                : text[..start] + section;
        }
        else
        {
            // Insert right after the "# Title" line.
            var firstBreak = text.IndexOf('\n');
            text = firstBreak >= 0
                ? text[..(firstBreak + 1)] + Environment.NewLine + section + text[(firstBreak + 1)..]
                : text + Environment.NewLine + Environment.NewLine + section;
        }
        File.WriteAllText(path, text);
    }

    private static string Slugify(string name)
    {
        var slug = new string(name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        return slug.Length > 0 ? slug : "unknown";
    }
}
