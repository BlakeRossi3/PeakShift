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
        MaxSpeed = 2000f;
    }

    // ── Physics Properties ───────────────────────────────────────────

    /// <summary>Heavy — snappy air control, grounded feel.</summary>
    public override float GravityMultiplier => 0.95f;

    /// <summary>Low drag — streamlined rider position.</summary>
    public override float DragModifier => 0.7f;

    /// <summary>Low rolling resistance — good tires on smooth terrain.</summary>
    public override float RollingResistanceModifier => 0.5f;

    /// <summary>Heavier vehicle rotates slower in flips.</summary>
    public override float FlipSpeedModifier => 0.8f;

    // ── Terrain Affinity ─────────────────────────────────────────────

    /// <summary>Bike never moves backwards.</summary>
    public override bool CanMoveBackwards => false;

    /// <summary>
    /// Bike terrain bonuses:
    ///   Dirt: +250 px/s^2 (excels — tires grip and propel)
    ///   Snow: -400 px/s^2 (barely moves — wheels sink into snow)
    ///   Ice:  -10 px/s^2 (slight struggle)
    /// </summary>
    public override float GetTerrainBonus(TerrainType terrain) => terrain switch
    {
        TerrainType.Dirt => 500f,
        TerrainType.Snow => -400f,
        TerrainType.Ice => -10f,
        _ => 0f
    };

    /// <summary>
    /// Bike friction modifiers:
    ///   Dirt: 0.5 (excellent grip, minimal wasted friction)
    ///   Snow: 4.0 (wheels sink — massive rolling resistance)
    ///   Ice:  1.1 (slightly less traction)
    /// </summary>
    public override float GetTerrainFrictionModifier(TerrainType terrain) => terrain switch
    {
        TerrainType.Dirt => 0.5f,
        TerrainType.Snow => 4.0f,
        TerrainType.Ice => 1.1f,
        _ => 1.0f
    };

    /// <summary>
    /// Bike drag modifiers per terrain:
    ///   Dirt: 0.7 (reduced — at home, aerodynamic efficiency)
    ///   Snow: 2.5 (snow spray and sinking increases drag)
    ///   Ice:  1.0 (neutral)
    /// </summary>
    public override float GetTerrainDragModifier(TerrainType terrain) => terrain switch
    {
        TerrainType.Dirt => 0.7f,
        TerrainType.Snow => 2.5f,
        TerrainType.Ice => 1.0f,
        _ => 1.0f
    };

    public override void OnActivated()
    {
        base.OnActivated();
        GD.Print("[BikeController] Activated");
    }
}
