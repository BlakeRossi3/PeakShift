using Godot;

namespace PeakShift;

/// <summary>
/// Abstract base class for all vehicle types in the momentum physics system.
/// Each vehicle defines its own friction, drag, gravity, and terrain affinity
/// parameters that feed into MomentumPhysics calculations.
/// </summary>
public abstract partial class VehicleBase : Node2D
{
    // ── Speed Limits ─────────────────────────────────────────────────

    /// <summary>Maximum horizontal speed for this vehicle (px/s).</summary>
    [Export]
    public float MaxSpeed { get; set; } = 800f;

    // ── Physics Modifiers ────────────────────────────────────────────

    /// <summary>
    /// Gravity multiplier. Greater than 1 = heavier/faster falling.
    /// Less than 1 = floatier/more airtime.
    /// </summary>
    public abstract float GravityMultiplier { get; }

    /// <summary>
    /// Drag coefficient multiplier for this vehicle.
    /// Affects aerodynamic drag (v^2 term). Lower = more aerodynamic.
    /// </summary>
    public abstract float DragModifier { get; }

    /// <summary>
    /// Rolling resistance multiplier for this vehicle.
    /// Affects constant ground friction. Lower = smoother ride.
    /// </summary>
    public abstract float RollingResistanceModifier { get; }

    /// <summary>
    /// Flip angular velocity multiplier. Higher = faster rotation.
    /// Heavier vehicles rotate slower.
    /// </summary>
    public abstract float FlipSpeedModifier { get; }

    // ── Terrain Affinity ─────────────────────────────────────────────

    /// <summary>
    /// Returns a terrain efficiency bonus/penalty (px/s^2).
    /// Positive = vehicle excels on this terrain (bonus acceleration).
    /// Negative = vehicle struggles (penalty deceleration).
    /// This is added directly to the total ground acceleration.
    /// </summary>
    public abstract float GetTerrainBonus(TerrainType terrain);

    /// <summary>
    /// Returns terrain-specific friction modifier for this vehicle.
    /// Multiplied with the terrain's base friction coefficient.
    /// Lower values = less friction on that surface.
    /// </summary>
    public abstract float GetTerrainFrictionModifier(TerrainType terrain);

    /// <summary>
    /// Returns terrain-specific drag modifier for this vehicle.
    /// Multiplied with the terrain's base drag modifier.
    /// Lower values = less drag on that surface.
    /// </summary>
    public abstract float GetTerrainDragModifier(TerrainType terrain);

    // ── Activation Hooks ─────────────────────────────────────────────

    /// <summary>Called when this vehicle becomes the active vehicle.</summary>
    public virtual void OnActivated()
    {
        Visible = true;
    }

    /// <summary>Called when this vehicle is swapped out.</summary>
    public virtual void OnDeactivated()
    {
        Visible = false;
    }
}
