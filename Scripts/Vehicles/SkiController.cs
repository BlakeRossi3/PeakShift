using Godot;

namespace PeakShift;

/// <summary>
/// Ski vehicle. Excels on snow and ice, struggles on dirt.
/// Light and aerodynamic (low gravity multiplier, low drag).
/// Higher max speed than bike. Can tuck for drag reduction.
/// </summary>
public partial class SkiController : VehicleBase
{
    /// <summary>
    /// When true, the skier is tucking — reduces drag, increases terminal velocity,
    /// reduces steering authority. Toggled by player input.
    /// </summary>
    public bool IsTucking { get; set; }

    public SkiController()
    {
        MaxSpeed = 950f;
    }

    // ── Physics Properties ───────────────────────────────────────────

    /// <summary>Light — more airtime, floatier jumps.</summary>
    public override float GravityMultiplier => 0.85f;

    /// <summary>Aerodynamic profile — less drag than bike.</summary>
    public override float DragModifier => 0.75f;

    /// <summary>Low rolling resistance — skis glide.</summary>
    public override float RollingResistanceModifier => 0.6f;

    /// <summary>Lighter vehicle rotates faster in flips.</summary>
    public override float FlipSpeedModifier => 1.2f;

    // ── Terrain Affinity ─────────────────────────────────────────────

    /// <summary>
    /// Ski terrain bonuses:
    ///   Snow: +100 px/s^2 (excels — designed for snow)
    ///   Dirt: -100 px/s^2 (struggles — skis catch on dirt)
    ///   Ice:  +60 px/s^2 (good — low friction surface matches skis)
    /// </summary>
    public override float GetTerrainBonus(TerrainType terrain) => terrain switch
    {
        TerrainType.Snow => 100f,
        TerrainType.Dirt => -100f,
        TerrainType.Ice => 60f,
        _ => 0f
    };

    /// <summary>
    /// Ski friction modifiers:
    ///   Snow: 0.6 (smooth glide)
    ///   Dirt: 1.5 (high friction — skis scrape)
    ///   Ice:  0.4 (very low friction)
    /// </summary>
    public override float GetTerrainFrictionModifier(TerrainType terrain) => terrain switch
    {
        TerrainType.Snow => 0.6f,
        TerrainType.Dirt => 1.5f,
        TerrainType.Ice => 0.4f,
        _ => 1.0f
    };

    /// <summary>
    /// Ski drag modifiers per terrain:
    ///   Snow: 0.8 (reduced — at home)
    ///   Dirt: 1.3 (debris increases drag)
    ///   Ice:  0.7 (very clean surface)
    /// </summary>
    public override float GetTerrainDragModifier(TerrainType terrain) => terrain switch
    {
        TerrainType.Snow => 0.8f,
        TerrainType.Dirt => 1.3f,
        TerrainType.Ice => 0.7f,
        _ => 1.0f
    };

    public override void OnActivated()
    {
        base.OnActivated();
        IsTucking = false;
        GD.Print("[SkiController] Activated");
    }
}
