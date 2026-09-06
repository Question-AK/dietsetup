using System;
using System.Collections.Generic;

namespace dietsetup;

public class DietSetupConfig
{
    // Disables diet math, consequences, rot intake and this mod's edibility grants; grant-definition edits require restart.
    public bool EnableDietSystem { get; set; } = true;
    public bool EnableRotIntakeTracking { get; set; } = true;
    public double RotIntakePerBite { get; set; } = 0.08;
    public Dictionary<string, double> IntakeHalfLifeHours { get; set; } = new() { ["rot"] = 48.0 };
    public double RotIntakeCap { get; set; } = 1.0;
    public bool RecordLastConsumption { get; set; } = true;
    public float CapacityFloor { get; set; } = 0.05f;

    public void Validate()
    {
        if (!float.IsFinite(CapacityFloor) || CapacityFloor <= 0 || !float.IsFinite(1f / CapacityFloor))
            throw new ArgumentException("CapacityFloor must be finite, positive, and have a finite reciprocal.");
        if (!double.IsFinite(RotIntakeCap) || RotIntakeCap < 0) throw new ArgumentException("RotIntakeCap must be finite and non-negative.");
        if (!double.IsFinite(RotIntakePerBite) || RotIntakePerBite < 0) throw new ArgumentException("RotIntakePerBite must be finite and non-negative.");
        if (IntakeHalfLifeHours == null || !IntakeHalfLifeHours.ContainsKey("rot")) throw new ArgumentException("IntakeHalfLifeHours must contain rot.");
        foreach (var (tag, hours) in IntakeHalfLifeHours)
            if (!double.IsFinite(hours) || hours <= 0) throw new ArgumentException($"IntakeHalfLifeHours.{tag} must be finite and positive.");
    }
}
