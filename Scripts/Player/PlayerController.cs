using Godot;

namespace PeakShift;

/// <summary>
/// Main player controller. CharacterBody2D with a state machine that handles
/// auto-forward movement, vehicle switching (bike/ski), input, and collisions.
/// </summary>
public partial class PlayerController : CharacterBody2D
{
    /// <summary>Player vehicle states.</summary>
    public enum PlayerState
    {
        /// <summary>Riding the mountain bike.</summary>
        Biking,

        /// <summary>Riding skis.</summary>
        Skiing
    }

    // ── Signals ──────────────────────────────────────────────────

    /// <summary>Emitted when the player swaps vehicles.</summary>
    [Signal]
    public delegate void VehicleSwappedEventHandler(int newState);

    /// <summary>Emitted on a terrain-optimal swap (snow+ski or dirt+bike).</summary>
    [Signal]
    public delegate void PerfectSwapEventHandler();

    /// <summary>Emitted when the player crashes.</summary>
    [Signal]
    public delegate void PlayerCrashedEventHandler();

    // ── Exports ──────────────────────────────────────────────────

    /// <summary>Base horizontal speed before vehicle modifiers.</summary>
    [Export]
    public float BaseSpeed { get; set; } = 300f;

    /// <summary>Gravity applied each physics frame (pixels/s^2).</summary>
    [Export]
    public float Gravity { get; set; } = 980f;

    /// <summary>Jump impulse velocity (negative = upward).</summary>
    [Export]
    public float JumpForce { get; set; } = -400f;

    /// <summary>Duration of the swap cooldown in seconds.</summary>
    [Export]
    public float SwapCooldown { get; set; } = 1.0f;

    // ── Node references ──────────────────────────────────────────

    /// <summary>Reference to the currently active vehicle controller.</summary>
    public VehicleBase CurrentVehicle { get; private set; }

    /// <summary>The bike vehicle node (assign in the editor or via code).</summary>
    [Export]
    public BikeController BikeNode { get; set; }

    /// <summary>The ski vehicle node (assign in the editor or via code).</summary>
    [Export]
    public SkiController SkiNode { get; set; }

    // ── State ────────────────────────────────────────────────────

    /// <summary>Current player/vehicle state.</summary>
    public PlayerState CurrentState { get; private set; } = PlayerState.Biking;

    /// <summary>The terrain type currently under the player (set externally by TerrainManager).</summary>
    public TerrainType CurrentTerrain;

    private float _swapTimer;
    private bool _canSwap = true;

    // ── Lifecycle ────────────────────────────────────────────────

    public override void _Ready()
    {
        CurrentState = PlayerState.Biking;
        CurrentVehicle = BikeNode;
        CurrentVehicle?.OnActivated();
        GD.Print("[PlayerController] Ready — state: Biking");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Tap → Jump
        if (@event.IsActionPressed("ui_accept") && IsOnFloor())
        {
            var vel = Velocity;
            vel.Y = JumpForce;
            Velocity = vel;
            GD.Print("[PlayerController] Jump");
        }

        // Swipe down → Tuck / Duck (stub)
        if (@event.IsActionPressed("ui_down"))
        {
            if (CurrentState == PlayerState.Skiing && SkiNode != null)
            {
                SkiNode.IsTucking = true;
            }
            GD.Print("[PlayerController] Tuck/Duck");
        }

        if (@event.IsActionReleased("ui_down"))
        {
            if (CurrentState == PlayerState.Skiing && SkiNode != null)
            {
                SkiNode.IsTucking = false;
            }
        }

        // Hold → Boost (stub)
        if (@event.IsActionPressed("ui_up"))
        {
            GD.Print("[PlayerController] Boost (stub)");
        }

        // Swap vehicle
        if (@event.IsActionPressed("ui_select") && _canSwap)
        {
            SwapVehicle();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // Swap cooldown timer
        if (!_canSwap)
        {
            _swapTimer -= dt;
            if (_swapTimer <= 0f)
            {
                _canSwap = true;
            }
        }

        // Apply gravity scaled by vehicle multiplier
        float gravityMultiplier = CurrentVehicle?.GetGravityMultiplier() ?? 1.0f;
        var velocity = Velocity;

        if (!IsOnFloor())
        {
            velocity.Y += Gravity * gravityMultiplier * dt;
        }

        // Auto-forward movement
        float speedModifier = CurrentVehicle?.GetSpeedModifier(CurrentTerrain) ?? 1.0f;
        float tuckBonus = 1.0f;
        if (CurrentState == PlayerState.Skiing && SkiNode is { IsTucking: true })
        {
            tuckBonus = 1.2f;
        }

        velocity.X = BaseSpeed * speedModifier * tuckBonus;
        Velocity = velocity;

        MoveAndSlide();

        // Collision detection stub
        for (int i = 0; i < GetSlideCollisionCount(); i++)
        {
            var collision = GetSlideCollision(i);
            if (collision.GetCollider() is Node2D collider && collider.IsInGroup("hazard"))
            {
                OnCrash();
                break;
            }
        }
    }

    // ── Public API ───────────────────────────────────────────────

    /// <summary>
    /// Swap between bike and ski. Enforces a cooldown between swaps.
    /// Emits VehicleSwapped, and PerfectSwap if terrain matches the new vehicle.
    /// </summary>
    public void SwapVehicle()
    {
        if (!_canSwap)
            return;

        // Deactivate old vehicle
        CurrentVehicle?.OnDeactivated();

        // Toggle state
        if (CurrentState == PlayerState.Biking)
        {
            CurrentState = PlayerState.Skiing;
            CurrentVehicle = SkiNode;
        }
        else
        {
            CurrentState = PlayerState.Biking;
            CurrentVehicle = BikeNode;
        }

        // Activate new vehicle
        CurrentVehicle?.OnActivated();

        // Start cooldown
        _canSwap = false;
        _swapTimer = SwapCooldown;

        EmitSignal(SignalName.VehicleSwapped, (int)CurrentState);
        GD.Print($"[PlayerController] Swapped to {CurrentState}");

        // Check for perfect swap (snow+ski or dirt+bike)
        bool isPerfect =
            (CurrentState == PlayerState.Skiing && CurrentTerrain == TerrainType.Snow) ||
            (CurrentState == PlayerState.Biking && CurrentTerrain == TerrainType.Dirt);

        if (isPerfect)
        {
            EmitSignal(SignalName.PerfectSwap);
            GD.Print("[PlayerController] Perfect swap!");
        }
    }

    // ── Private helpers ──────────────────────────────────────────

    /// <summary>Handle a player crash (stub).</summary>
    private void OnCrash()
    {
        EmitSignal(SignalName.PlayerCrashed);
        GD.Print("[PlayerController] Crashed!");
    }
}
