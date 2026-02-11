using Godot;

namespace PeakShift;

/// <summary>
/// Mountain bike vehicle. Excels on dirt, struggles on snow.
/// Heavy and grounded (high gravity multiplier).
/// Lower max speed than skis.
/// </summary>
public partial class BikeController : VehicleBase
{
    /// <summary>Whether the bike can wall-ride (stub, always false for now).</summary>
    public bool CanWallRide { get; set; } = false;

    public BikeController()
    {
        MaxSpeed = 450f;  // Lower max speed for bike
    }

    /// <inheritdoc/>
    public override float GetAcceleration(TerrainType terrain)
    {
        return terrain switch
        {
            TerrainType.Dirt  => 180f,   // Fast acceleration on dirt
            TerrainType.Snow  => -150f,  // Deceleration on snow
            TerrainType.Ice   => 60f,    // Slight acceleration on ice
            _                 => 0f
        };
    }

    /// <inheritdoc/>
    public override float GetSpeedModifier(TerrainType terrain)
    {
        return terrain switch
        {
            TerrainType.Dirt  => 1.5f,
            TerrainType.Snow  => 0.6f,
            TerrainType.Ice   => 0.8f,
            _                 => 1.0f
        };
    }

    /// <inheritdoc/>
    public override float GetGravityMultiplier()
    {
        return 1.3f; // Heavy, grounded
    }

    /// <inheritdoc/>
    public override void OnActivated()
    {
        base.OnActivated();
        // TODO: Show bike sprite / animation
        GD.Print("[BikeController] Bike sprite shown");
    }
}
