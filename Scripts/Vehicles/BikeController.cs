using Godot;

namespace PeakShift;

/// <summary>
/// Mountain bike vehicle. Excels on dirt, struggles on snow.
/// Heavy and grounded (high gravity multiplier, higher rolling resistance).
/// Lower max speed than skis but better traction.
/// Cannot tuck — tuck is ski-only.
/// </summary>
public partial class BikeController : VehicleBase
{
    public BikeController()
    {
        MaxSpeed = 1200f;
    }

    // ── Physics Properties ───────────────────────────────────────────

    /// <summary>Moderate air weight — still grounded feel but much less punishing.</summary>
    public override float GravityMultiplier => 0.75f;

    /// <summary>Moderate drag — slightly less aerodynamic than skis.</summary>
    public override float DragModifier => 1.0f;

    /// <summary>Moderate rolling resistance — good tires.</summary>
    public override float RollingResistanceModifier => 0.9f;

    /// <summary>Heavier vehicle rotates slower in flips.</summary>
    public override float FlipSpeedModifier => 0.8f;

    // ── Terrain Affinity ─────────────────────────────────────────────

    /// <summary>
    /// Bike terrain bonuses:
    ///   Dirt: +120 px/s^2 (excels — tires grip well)
    ///   Snow: -15 px/s^2 (mild penalty — can still climb)
    ///   Ice:  -10 px/s^2 (slight struggle)
    /// </summary>
    public override float GetTerrainBonus(TerrainType terrain) => terrain switch
    {
        TerrainType.Dirt => 120f,
        TerrainType.Snow => -15f,
        TerrainType.Ice => -10f,
        _ => 0f
    };

    /// <summary>
    /// Bike friction modifiers:
    ///   Dirt: 0.8 (good grip, less wasted friction)
    ///   Snow: 1.05 (slightly more resistance but manageable)
    ///   Ice:  1.1 (slightly less traction)
    /// </summary>
    public override float GetTerrainFrictionModifier(TerrainType terrain) => terrain switch
    {
        TerrainType.Dirt => 0.8f,
        TerrainType.Snow => 1.05f,
        TerrainType.Ice => 1.1f,
        _ => 1.0f
    };

    /// <summary>
    /// Bike drag modifiers per terrain:
    ///   Dirt: 0.9 (slightly reduced — at home)
    ///   Snow: 1.0 (neutral — bike handles snow fine)
    ///   Ice:  1.0 (neutral)
    /// </summary>
    public override float GetTerrainDragModifier(TerrainType terrain) => terrain switch
    {
        TerrainType.Dirt => 0.9f,
        TerrainType.Snow => 1.0f,
        TerrainType.Ice => 1.0f,
        _ => 1.0f
    };

    public override void OnActivated()
    {
        base.OnActivated();
        GD.Print("[BikeController] Activated");
    }
}
