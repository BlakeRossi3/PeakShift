using Godot;

namespace PeakShift;

/// <summary>
/// Main player controller. CharacterBody2D with a state machine that handles
/// auto-forward movement, vehicle switching (bike/ski), input, and collisions.
/// Only processes movement and input when the game is in the Playing state.
/// </summary>
public partial class PlayerController : CharacterBody2D
{
    public enum PlayerState
    {
        Biking,
        Skiing
    }

    // ── Signals ──────────────────────────────────────────────────

    [Signal]
    public delegate void VehicleSwappedEventHandler(int newState);

    [Signal]
    public delegate void PerfectSwapEventHandler();

    [Signal]
    public delegate void PlayerCrashedEventHandler();

    // ── Exports ──────────────────────────────────────────────────

    [Export]
    public float BaseSpeed { get; set; } = 300f;

    [Export]
    public float Gravity { get; set; } = 980f;

    [Export]
    public float JumpForce { get; set; } = -400f;

    [Export]
    public float SwapCooldown { get; set; } = 1.0f;

    // ── Node references ──────────────────────────────────────────

    public VehicleBase CurrentVehicle { get; private set; }

    [Export]
    public BikeController BikeNode { get; set; }

    [Export]
    public SkiController SkiNode { get; set; }

    // ── State ────────────────────────────────────────────────────

    public PlayerState CurrentState { get; private set; } = PlayerState.Biking;
    public TerrainType CurrentTerrain;

    private float _swapTimer;
    private bool _canSwap = true;
    private GameManager _gameManager;

    /// <summary>Starting position, used to reset on new game.</summary>
    private Vector2 _startPosition;

    // ── Lifecycle ────────────────────────────────────────────────

    public override void _Ready()
    {
        CurrentState = PlayerState.Biking;
        CurrentVehicle = BikeNode;
        CurrentVehicle?.OnActivated();

        // Cache the starting position for resets
        _startPosition = Position;

        // Find the GameManager sibling
        _gameManager = GetNodeOrNull<GameManager>("../GameManager");

        GD.Print("[PlayerController] Ready — state: Biking");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Only accept gameplay input when the game is playing
        if (_gameManager == null || _gameManager.CurrentState != GameManager.GameState.Playing)
            return;

        // Jump
        if (@event.IsActionPressed("ui_accept") && IsOnFloor())
        {
            var vel = Velocity;
            vel.Y = JumpForce;
            Velocity = vel;
        }

        // Tuck (ski only)
        if (@event.IsActionPressed("ui_down"))
        {
            if (CurrentState == PlayerState.Skiing && SkiNode != null)
            {
                SkiNode.IsTucking = true;
            }
        }

        if (@event.IsActionReleased("ui_down"))
        {
            if (CurrentState == PlayerState.Skiing && SkiNode != null)
            {
                SkiNode.IsTucking = false;
            }
        }

        // Swap vehicle
        if (@event.IsActionPressed("ui_select") && _canSwap)
        {
            SwapVehicle();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        // Only process movement when the game is playing
        if (_gameManager == null || _gameManager.CurrentState != GameManager.GameState.Playing)
        {
            // Keep velocity zeroed when not playing so player doesn't drift
            Velocity = Vector2.Zero;
            return;
        }

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

        // Apply gravity
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

        // Collision detection for hazards
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

    public void SwapVehicle()
    {
        if (!_canSwap)
            return;

        CurrentVehicle?.OnDeactivated();

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

        CurrentVehicle?.OnActivated();

        _canSwap = false;
        _swapTimer = SwapCooldown;

        EmitSignal(SignalName.VehicleSwapped, (int)CurrentState);
        GD.Print($"[PlayerController] Swapped to {CurrentState}");

        bool isPerfect =
            (CurrentState == PlayerState.Skiing && CurrentTerrain == TerrainType.Snow) ||
            (CurrentState == PlayerState.Biking && CurrentTerrain == TerrainType.Dirt);

        if (isPerfect)
        {
            EmitSignal(SignalName.PerfectSwap);
            GD.Print("[PlayerController] Perfect swap!");
        }
    }

    /// <summary>Reset position and state for a new run.</summary>
    public void ResetForNewRun()
    {
        Position = _startPosition;
        Velocity = Vector2.Zero;
        CurrentState = PlayerState.Biking;

        // Reset vehicle visuals
        CurrentVehicle?.OnDeactivated();
        CurrentVehicle = BikeNode;
        CurrentVehicle?.OnActivated();

        _canSwap = true;
        _swapTimer = 0f;
        CurrentTerrain = TerrainType.Snow;

        GD.Print("[PlayerController] Reset for new run");
    }

    // ── Private helpers ──────────────────────────────────────────

    private void OnCrash()
    {
        EmitSignal(SignalName.PlayerCrashed);
        GD.Print("[PlayerController] Crashed!");
    }
}
