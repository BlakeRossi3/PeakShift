using Godot;

namespace PeakShift;

/// <summary>
/// Ski vehicle. Excels on snow and ice, struggles on dirt.
/// Light and floaty (low gravity multiplier).
/// Higher max speed than bike.
/// </summary>
public partial class SkiController : VehicleBase
{
    /// <summary>
    /// When true, the skier is tucking — reduces drag and increases speed by 1.2x.
    /// Toggled by player input (swipe down / hold down).
    /// </summary>
    public bool IsTucking { get; set; }

    /// <summary>
    /// Aerial steering factor (0 = no air control, 1 = full).
    /// Stub for future aerial mechanics.
    /// </summary>
    [Export]
    public float AirControlFactor { get; set; } = 0.5f;

    public SkiController()
    {
        MaxSpeed = 650f;  // Higher max speed for skis
    }

    /// <inheritdoc/>
    public override float GetAcceleration(TerrainType terrain)
    {
        return terrain switch
        {
            TerrainType.Snow  => 200f,   // Fast acceleration on snow
            TerrainType.Dirt  => -180f,  // Strong deceleration on dirt
            TerrainType.Ice   => 120f,   // Good acceleration on ice
            _                 => 0f
        };
    }

    /// <inheritdoc/>
    public override float GetSpeedModifier(TerrainType terrain)
    {
        return terrain switch
        {
            TerrainType.Snow  => 1.5f,
            TerrainType.Dirt  => 0.6f,
            TerrainType.Ice   => 1.2f,
            _                 => 1.0f
        };
    }

    /// <inheritdoc/>
    public override float GetGravityMultiplier()
    {
        return 0.7f; // Floaty
    }

    /// <inheritdoc/>
    public override void OnActivated()
    {
        base.OnActivated();
        IsTucking = false;
        // TODO: Show ski sprite / animation
        GD.Print("[SkiController] Ski sprite shown");
    }
}
