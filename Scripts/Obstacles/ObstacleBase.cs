using Godot;

namespace PeakShift.Obstacles;

/// <summary>
/// Base class for static terrain obstacles (rocks, trees, logs, etc.).
/// Obstacles are pooled and placed by the terrain generation system.
///
/// Collision types:
///   - Hard: Player crashes immediately on contact
///   - Slow: Player loses speed but continues
///   - Jump: Player can jump over but crashes if they hit it
/// </summary>
public abstract partial class ObstacleBase : Area2D
{
    // ── Collision behavior ──────────────────────────────────────────

    public enum CollisionType
    {
        Hard,   // Instant crash
        Slow,   // Speed penalty
        Jump    // Can jump over, crash if grounded hit
    }

    /// <summary>How this obstacle affects the player on collision.</summary>
    [Export]
    public CollisionType Behavior { get; set; } = CollisionType.Hard;

    /// <summary>Speed loss when hit with Slow behavior (px/s).</summary>
    [Export]
    public float SpeedPenalty { get; set; } = 200f;

    // ── Placement metadata ──────────────────────────────────────────

    /// <summary>Visual size category for placement logic.</summary>
    public enum SizeCategory { Small, Medium, Large }

    [Export]
    public SizeCategory Size { get; set; } = SizeCategory.Medium;

    /// <summary>Terrain types this obstacle can spawn on.</summary>
    public TerrainType[] AllowedTerrains { get; set; } = { TerrainType.Snow, TerrainType.Dirt, TerrainType.Ice };

    // ── Lifecycle ───────────────────────────────────────────────────

    private bool _isActive;

    public override void _Ready()
    {
        // Connect collision signals
        BodyEntered += OnBodyEntered;
        _isActive = true;

        OnInitialize();
    }

    /// <summary>Called when the obstacle is first created or reset from pool.</summary>
    protected virtual void OnInitialize() { }

    /// <summary>Activate this obstacle at a specific world position.</summary>
    public virtual void Activate(Vector2 worldPosition, TerrainType terrain)
    {
        GlobalPosition = worldPosition;
        _isActive = true;
        Visible = true;

        OnActivate(terrain);
    }

    /// <summary>Called when activated from the pool.</summary>
    protected virtual void OnActivate(TerrainType terrain) { }

    /// <summary>Deactivate and prepare for return to pool.</summary>
    public virtual void Deactivate()
    {
        _isActive = false;
        Visible = false;

        OnDeactivate();
    }

    /// <summary>Called when returned to pool.</summary>
    protected virtual void OnDeactivate() { }

    // ── Collision handling ──────────────────────────────────────────

    private void OnBodyEntered(Node2D body)
    {
        if (!_isActive) return;

        // Check if it's the player
        if (body is PlayerController player)
        {
            HandlePlayerCollision(player);
        }
    }

    private void HandlePlayerCollision(PlayerController player)
    {
        switch (Behavior)
        {
            case CollisionType.Hard:
                // Instant crash
                player.EmitSignal(PlayerController.SignalName.PlayerCrashed);
                break;

            case CollisionType.Slow:
                // Apply speed penalty without crashing
                player.ApplySpeedPenalty(SpeedPenalty);
                break;

            case CollisionType.Jump:
                // Only crash if player is grounded
                if (player.CurrentMoveState == PlayerController.MoveState.Grounded ||
                    player.CurrentMoveState == PlayerController.MoveState.Tucking)
                {
                    player.EmitSignal(PlayerController.SignalName.PlayerCrashed);
                }
                // If airborne, player clears the obstacle safely
                break;
        }

        OnPlayerHit(player);
    }

    /// <summary>Called when player collides with this obstacle.</summary>
    protected virtual void OnPlayerHit(PlayerController player) { }

    // ── Terrain compatibility ───────────────────────────────────────

    public bool IsCompatibleWithTerrain(TerrainType terrain)
    {
        foreach (var allowed in AllowedTerrains)
        {
            if (allowed == terrain)
                return true;
        }
        return false;
    }
}
