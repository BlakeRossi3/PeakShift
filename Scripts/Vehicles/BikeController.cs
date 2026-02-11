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
        MaxSpeed = 700f;
    }

    // ── Physics Properties ───────────────────────────────────────────

    /// <summary>Heavy — falls fast, grounded feel.</summary>
    public override float GravityMultiplier => 1.25f;

    /// <summary>Higher drag profile — less aerodynamic than skis.</summary>
    public override float DragModifier => 1.2f;

    /// <summary>Higher rolling resistance — chunky tires.</summary>
    public override float RollingResistanceModifier => 1.15f;

    /// <summary>Heavier vehicle rotates slower in flips.</summary>
    public override float FlipSpeedModifier => 0.8f;

    // ── Terrain Affinity ─────────────────────────────────────────────

    /// <summary>
    /// Bike terrain bonuses:
    ///   Dirt: +120 px/s^2 (excels — tires grip well)
    ///   Snow: -80 px/s^2 (struggles — tires sink)
    ///   Ice:  -30 px/s^2 (poor traction)
    /// </summary>
    public override float GetTerrainBonus(TerrainType terrain) => terrain switch
    {
        TerrainType.Dirt => 120f,
        TerrainType.Snow => -80f,
        TerrainType.Ice => -30f,
        _ => 0f
    };

    /// <summary>
    /// Bike friction modifiers:
    ///   Dirt: 0.8 (good grip, less wasted friction)
    ///   Snow: 1.4 (tires dig in, high resistance)
    ///   Ice:  1.3 (no traction, fights the surface)
    /// </summary>
    public override float GetTerrainFrictionModifier(TerrainType terrain) => terrain switch
    {
        TerrainType.Dirt => 0.8f,
        TerrainType.Snow => 1.4f,
        TerrainType.Ice => 1.3f,
        _ => 1.0f
    };

    /// <summary>
    /// Bike drag modifiers per terrain:
    ///   Dirt: 0.9 (slightly reduced — at home)
    ///   Snow: 1.2 (snow spray increases drag)
    ///   Ice:  1.0 (neutral)
    /// </summary>
    public override float GetTerrainDragModifier(TerrainType terrain) => terrain switch
    {
        TerrainType.Dirt => 0.9f,
        TerrainType.Snow => 1.2f,
        TerrainType.Ice => 1.0f,
        _ => 1.0f
    };

    public override void OnActivated()
    {
        base.OnActivated();
        GD.Print("[BikeController] Activated");
    }
}
