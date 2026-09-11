using System.IO;
using System.Text.RegularExpressions;

namespace DLSSGManager;

/// <summary>
/// Checks the two language tables against each other by parsing their source files.
///
/// <see cref="Strings.MissingKeys"/> already compares the loaded dictionaries, but a key that exists
/// in neither table (a typo like <c>loc.T("Deploy.Sucess")</c>) only shows up as a raw key in the
/// interface at runtime. This scans the C# and XAML sources for every lookup key and asserts it is
/// defined, which turns that class of mistake into a test failure.
/// </summary>
public static class LocalizationAudit
{
    /// <summary>Keys used in code or XAML that no table defines.</summary>
    public static List<string> UndefinedKeysUsed(IEnumerable<string> sourceFiles)
    {
        var defined = new HashSet<string>(Strings.AllKeys, StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in sourceFiles)
        {
            string text;
            try { text = File.ReadAllText(file); }
            catch { continue; }

            // Code: Loc.T("Key") / Loc.T("Key", args). Skip the definition file itself and anything
            // that is clearly not a lookup, so the audit does not flag its own test fixtures.
            foreach (Match m in Regex.Matches(text, @"Loc\.T\(\s*""([^""]+)"""))
            {
                if (!LooksLikeKey(m.Groups[1].Value)) continue;
                used.Add(m.Groups[1].Value);
            }

            // XAML: {loc:Tr Key} — only in markup files, and only when the value is a bare key.
            if (Path.GetExtension(file).Equals(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match m in Regex.Matches(text, @"\{loc:Tr\s+([A-Za-z0-9_.]+)\s*\}"))
                    used.Add(m.Groups[1].Value);
            }
        }

        return used.Where(k => !defined.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// True when a string looks like a resource key: dot-separated segments, no spaces, at least one
    /// dot. Filters out documentation placeholders such as <c>"Key"</c> and deliberate misspellings
    /// used in tests.
    /// </summary>
    private static bool LooksLikeKey(string value)
    {
        if (value.Length < 3 || value.Contains(' ') || value.Contains('\n')) return false;
        if (!value.Contains('.')) return false;

        // Must start with a letter and contain only identifier characters plus dots.
        if (!char.IsLetter(value[0])) return false;
        return value.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-');
    }

    /// <summary>
    /// Placeholders ({0}, {1}, …) present in one language but not the other. A mismatch means
    /// <c>string.Format</c> throws or silently drops a value for one of the languages.
    /// </summary>
    public static List<string> PlaceholderMismatches()
    {
        var zh = Strings.For(Languages.ChineseSimplified);
        var en = Strings.For(Languages.English);
        var problems = new List<string>();

        foreach (var key in zh.Keys)
        {
            if (!en.TryGetValue(key, out var english)) continue;

            var zhSlots = Slots(zh[key]);
            var enSlots = Slots(english);

            if (!zhSlots.SetEquals(enSlots))
            {
                problems.Add($"{key}: 中文 {{{string.Join(",", zhSlots.OrderBy(x => x))}}} " +
                             $"vs 英文 {{{string.Join(",", enSlots.OrderBy(x => x))}}}");
            }
        }

        return problems;
    }

    private static HashSet<int> Slots(string template) =>
        Regex.Matches(template, @"\{(\d+)(?:[:}])")
            .Select(m => int.Parse(m.Groups[1].Value))
            .ToHashSet();

    /// <summary>Source files that may contain lookups, relative to the repository root.</summary>
    public static List<string> SourceFiles(string repositoryRoot)
    {
        var result = new List<string>();
        foreach (var dir in new[] { Path.Combine(repositoryRoot, "src"), Path.Combine(repositoryRoot, "test") })
        {
            if (!Directory.Exists(dir)) continue;
            result.AddRange(Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                            && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")));
            result.AddRange(Directory.EnumerateFiles(dir, "*.xaml", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                            && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")));
        }

        return result;
    }
}
