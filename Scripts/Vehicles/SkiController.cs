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
        MaxSpeed = 6000f;
    }

    // ── Physics Properties ───────────────────────────────────────────

    /// <summary>Light — slightly extended airtime but still responsive.</summary>
    public override float GravityMultiplier => 0.85f;

    /// <summary>Very aerodynamic — minimal drag.</summary>
    public override float DragModifier => 0.55f;

    /// <summary>Ultra-low rolling resistance — skis glide freely.</summary>
    public override float RollingResistanceModifier => 0.35f;

    /// <summary>Lighter vehicle rotates faster in flips.</summary>
    public override float FlipSpeedModifier => 1.2f;

    // ── Terrain Affinity ─────────────────────────────────────────────

    /// <summary>Skis can slide backwards down hills.</summary>
    public override bool CanMoveBackwards => true;

    /// <summary>
    /// Ski terrain bonuses:
    ///   Snow: +100 px/s^2 (excels — designed for snow)
    ///   Dirt: -500 px/s^2 (barely moves — skis catch and scrape on dirt)
    ///   Ice:  +60 px/s^2 (good — low friction surface matches skis)
    /// </summary>
    public override float GetTerrainBonus(TerrainType terrain) => terrain switch
    {
        TerrainType.Snow => 150f,
        TerrainType.Dirt => -500f,
        TerrainType.Ice => 100f,
        _ => 0f
    };

    /// <summary>
    /// Ski friction modifiers:
    ///   Snow: 0.6 (smooth glide)
    ///   Dirt: 5.0 (massive friction — skis scrape and dig in)
    ///   Ice:  0.4 (very low friction)
    /// </summary>
    public override float GetTerrainFrictionModifier(TerrainType terrain) => terrain switch
    {
        TerrainType.Snow => 0.6f,
        TerrainType.Dirt => 5.0f,
        TerrainType.Ice => 0.4f,
        _ => 1.0f
    };

    /// <summary>
    /// Ski drag modifiers per terrain:
    ///   Snow: 0.8 (reduced — at home)
    ///   Dirt: 3.0 (debris and scraping increases drag heavily)
    ///   Ice:  0.7 (very clean surface)
    /// </summary>
    public override float GetTerrainDragModifier(TerrainType terrain) => terrain switch
    {
        TerrainType.Snow => 0.8f,
        TerrainType.Dirt => 3.0f,
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
