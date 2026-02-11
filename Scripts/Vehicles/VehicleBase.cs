using Godot;

namespace PeakShift;

/// <summary>
/// Abstract base class for all vehicle types. Provides speed/gravity modifiers
/// and activation hooks. Extend this for each specific vehicle.
/// </summary>
public abstract partial class VehicleBase : Node2D
{
    /// <summary>Base horizontal speed for this vehicle (pixels/s).</summary>
    [Export]
    public float BaseSpeed { get; set; } = 300f;

    /// <summary>Base gravity value (pixels/s^2).</summary>
    [Export]
    public float BaseGravity { get; set; } = 980f;

    // ── Abstract API ─────────────────────────────────────────────

    /// <summary>
    /// Returns the speed multiplier for the given terrain type.
    /// Values greater than 1 mean faster; less than 1 mean slower.
    /// </summary>
    /// <param name="terrain">The current terrain surface.</param>
    /// <returns>Speed modifier float.</returns>
    public abstract float GetSpeedModifier(TerrainType terrain);

    /// <summary>
    /// Returns the gravity multiplier for this vehicle.
    /// Greater than 1 = heavier, less than 1 = floatier.
    /// </summary>
    /// <returns>Gravity modifier float.</returns>
    public abstract float GetGravityMultiplier();

    // ── Virtual hooks ────────────────────────────────────────────

    /// <summary>Called when this vehicle becomes the active vehicle.</summary>
    public virtual void OnActivated()
    {
        Visible = true;
        GD.Print($"[{GetType().Name}] Activated");
    }

    /// <summary>Called when this vehicle is swapped out.</summary>
    public virtual void OnDeactivated()
    {
        Visible = false;
        GD.Print($"[{GetType().Name}] Deactivated");
    }
}
