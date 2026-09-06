using System;

namespace dietsetup.Rules;
public readonly struct CompiledValue
{
    private readonly float flat;
    private readonly CurveAnchor[]? curve;

    private CompiledValue(float flat, CurveAnchor[]? curve)
    {
        this.flat = flat;
        this.curve = curve;
    }

    public static CompiledValue Flat(float value) => new(value, null);
    public static CompiledValue FromCurve(CurveAnchor[] anchors) => new(0f, (CurveAnchor[])anchors.Clone());

    public float Evaluate(float spoilLevel)
    {
        if (curve == null || curve.Length == 0) return flat;

        int last = curve.Length - 1;
        if (spoilLevel <= curve[0].Spoil) return curve[0].Value;
        if (spoilLevel >= curve[last].Spoil) return curve[last].Value;

        for (int i = 0; i < last; i++)
        {
            CurveAnchor a = curve[i];
            CurveAnchor b = curve[i + 1];
            if (spoilLevel < a.Spoil || spoilLevel > b.Spoil) continue;

            float t = (spoilLevel - a.Spoil) / (b.Spoil - a.Spoil);
            return (float)((double)a.Value + ((double)b.Value - a.Value) * t);
        }

        return curve[last].Value;
    }

    public bool IsCurve => curve != null && curve.Length > 0;
    public bool CanBePositive => curve == null ? flat > 0f : Array.Exists(curve, a => a.Value > 0f);
}
