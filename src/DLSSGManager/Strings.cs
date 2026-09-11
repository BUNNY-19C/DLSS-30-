using System.Collections.Generic;

namespace DLSSGManager;

/// <summary>
/// The interface text, keyed by a stable identifier and split per language.
///
/// The two tables are kept as separate files so a missing translation is visible in review. Keys must
/// match exactly; <see cref="MissingKeys"/> exists so a test can assert parity rather than relying on
/// a user noticing a stray key name in the interface.
/// </summary>
public static partial class Strings
{
    private static readonly Dictionary<string, Dictionary<string, string>> Tables = new()
    {
        [Languages.ChineseSimplified] = ChineseSimplified(),
        [Languages.English] = English(),
    };

    /// <summary>The table for a language; falls back to the default when the code is unknown.</summary>
    public static Dictionary<string, string> For(string language)
    {
        var normalized = Languages.Normalize(language);
        return Tables.TryGetValue(normalized, out var table)
            ? table
            : Tables[Languages.ChineseSimplified];
    }

    /// <summary>Keys present in one table but not the other, for both directions.</summary>
    public static (List<string> OnlyInChinese, List<string> OnlyInEnglish) MissingKeys()
    {
        var zh = For(Languages.ChineseSimplified).Keys;
        var en = For(Languages.English).Keys;

        var onlyZh = zh.Where(k => !en.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var onlyEn = en.Where(k => !zh.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        return (onlyZh, onlyEn);
    }

    /// <summary>Every key, for tests that verify lookups resolve.</summary>
    public static IEnumerable<string> AllKeys => For(Languages.ChineseSimplified).Keys;
}
