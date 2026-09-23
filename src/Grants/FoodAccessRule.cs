using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace dietsetup.Grants;

public enum FoodAccessMode { Unrestricted, Listed, Denied }

/// <summary>Who may consume one material Diet Setup made edible (authoring contract 3). Permission is
/// physical and decides whether the mouthful may happen; it is not a dietary response, so a permitted
/// diet can still resolve the material as <c>Inedible</c> and eat it for nothing.</summary>
public sealed class FoodAccessRule
{
    private const string ListedPrefix = "listed:";
    private const string UnrestrictedWord = "unrestricted";
    private const string DeniedWord = "denied";

    public static readonly FoodAccessRule Unrestricted = new(FoodAccessMode.Unrestricted, ImmutableHashSet<string>.Empty);
    public static readonly FoodAccessRule Denied = new(FoodAccessMode.Denied, ImmutableHashSet<string>.Empty);

    public FoodAccessMode Mode { get; }
    public ImmutableHashSet<string> Diets { get; }

    private FoodAccessRule(FoodAccessMode mode, ImmutableHashSet<string> diets)
    {
        Mode = mode;
        Diets = diets;
    }

    public static FoodAccessRule Listed(IEnumerable<string> diets)
    {
        ImmutableHashSet<string> set = diets.ToImmutableHashSet(StringComparer.Ordinal);
        if (set.IsEmpty) throw new ArgumentException("A listed permission names at least one diet; 'nobody' is mode denied.", nameof(diets));
        return new FoodAccessRule(FoodAccessMode.Listed, set);
    }

    public bool Restricts => Mode != FoodAccessMode.Unrestricted;

    /// <summary>Side-effect free: reads nothing but this rule and the id. An entity with no resolved diet
    /// is not on any list, so a restricted material stays refused rather than defaulting open.</summary>
    public bool Permits(string? dietId) => Mode switch
    {
        FoodAccessMode.Unrestricted => true,
        FoodAccessMode.Listed => dietId != null && Diets.Contains(dietId),
        _ => false
    };

    /// <summary>Wire and payload form. The server resolves the rule once and the client reads back the
    /// same decision, so a client preview cannot disagree with what the server will enforce.</summary>
    public string Encode() => Mode switch
    {
        FoodAccessMode.Listed => ListedPrefix + string.Join(",", Diets.OrderBy(d => d, StringComparer.Ordinal)),
        FoodAccessMode.Denied => DeniedWord,
        _ => UnrestrictedWord
    };

    /// <summary>An unreadable value fails rather than decoding to unrestricted: a corrupted permission
    /// must not read as a licence to eat.</summary>
    public static bool TryDecode(string? raw, out FoodAccessRule rule)
    {
        rule = Unrestricted;
        if (string.IsNullOrEmpty(raw) || raw == UnrestrictedWord) return true;
        if (raw == DeniedWord) { rule = Denied; return true; }
        if (!raw.StartsWith(ListedPrefix, StringComparison.Ordinal)) return false;

        string[] diets = raw[ListedPrefix.Length..].Split(',');
        if (diets.Length == 0) return false;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string diet in diets)
        {
            if (string.IsNullOrWhiteSpace(diet) || !seen.Add(diet)) return false;
        }

        rule = Listed(seen);
        return true;
    }
}
