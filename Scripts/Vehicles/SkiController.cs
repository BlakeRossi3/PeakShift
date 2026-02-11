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
        MaxSpeed = 1600f;
    }

    // ── Physics Properties ───────────────────────────────────────────

    /// <summary>Very light — extended airtime, floaty jumps.</summary>
    public override float GravityMultiplier => 0.5f;

    /// <summary>Very aerodynamic — minimal drag.</summary>
    public override float DragModifier => 0.55f;

    /// <summary>Ultra-low rolling resistance — skis glide freely.</summary>
    public override float RollingResistanceModifier => 0.35f;

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
        TerrainType.Dirt => -180f,
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
        TerrainType.Dirt => 2.0f,
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
        TerrainType.Dirt => 1.6f,
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
