namespace dietsetup.Rules;

/// <summary>An authored tooltip line: a lang key that applies from <see cref="Spoil"/> up to the next line's
/// spoil. Shown only for food that perishes.</summary>
public readonly record struct CompiledLine(float Spoil, string Key);
