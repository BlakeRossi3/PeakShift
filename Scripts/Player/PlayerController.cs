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
	public float Gravity { get; set; } = 1600f;

	[Export]
	public float JumpForce { get; set; } = -580f;

	[Export]
	public float SwapCooldown { get; set; } = 1.0f;

	[Export]
	public float FallMultiplier { get; set; } = 2.0f;

	[Export]
	public float LowJumpMultiplier { get; set; } = 3.0f;

	[Export]
	public float CoyoteTime { get; set; } = 0.08f;

	[Export]
	public float JumpBufferTime { get; set; } = 0.1f;

	[Export]
	public float FallDeathThreshold { get; set; } = 1200f;

	[Export]
	public float BikeWallClimbForce { get; set; } = 400f;

	[Export]
	public float SkiSlopeAssist { get; set; } = 50f;

	[Export]
	public float FlipJumpForce { get; set; } = -400f;

	[Export]
	public float FlipDuration { get; set; } = 0.5f;

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

	private float _coyoteTimer;
	private float _jumpBufferTimer;
	private bool _isJumping;
	private bool _jumpHeld;
	private bool _wasOnFloor;

	/// <summary>Current horizontal speed (pixels/s).</summary>
	private float _currentSpeed;

	/// <summary>Starting position, used to reset on new game.</summary>
	private Vector2 _startPosition;

	// Flip jump state
	private bool _canFlipJump;
	private bool _isFlipping;
	private float _flipTimer;
	private Sprite2D _playerSprite;

	// ── Lifecycle ────────────────────────────────────────────────

	public override void _Ready()
	{
		// Resolve vehicle nodes by path if not assigned via Export
		BikeNode ??= GetNodeOrNull<BikeController>("Bike");
		SkiNode ??= GetNodeOrNull<SkiController>("Skis");

		CurrentState = PlayerState.Biking;
		CurrentVehicle = BikeNode;
		CurrentVehicle?.OnActivated();

		// Cache the starting position for resets
		_startPosition = Position;
		_currentSpeed = BaseSpeed;

		// Find the GameManager sibling
		_gameManager = GetNodeOrNull<GameManager>("../GameManager");

		// Find sprite for flip rotation
		_playerSprite = GetNodeOrNull<Sprite2D>("Sprite2D");

		GD.Print("[PlayerController] Ready — state: Biking");
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		// Only accept gameplay input when the game is playing
		if (_gameManager == null || _gameManager.CurrentState != GameManager.GameState.Playing)
			return;

		// Jump press — only allow flip while airborne; no standard ground jump
		if (@event.IsActionPressed("jump"))
		{
			if (!IsOnFloor() && _coyoteTimer <= 0 && _canFlipJump && !_isFlipping)
			{
				ExecuteFlipJump();
			}
		}

		// Tuck (ski only, Down arrow)
		if (@event.IsActionPressed("tuck"))
		{
			if (CurrentState == PlayerState.Skiing && SkiNode != null)
				SkiNode.IsTucking = true;
		}

		if (@event.IsActionReleased("tuck"))
		{
			if (CurrentState == PlayerState.Skiing && SkiNode != null)
				SkiNode.IsTucking = false;
		}

		// Swap vehicle (Left Shift)
		if (@event.IsActionPressed("swap_vehicle") && _canSwap)
		{
			SwapVehicle();
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		// Only process movement when the game is playing
		if (_gameManager == null || _gameManager.CurrentState != GameManager.GameState.Playing)
		{
			Velocity = Vector2.Zero;
			return;
		}

		float dt = (float)delta;

		// Swap cooldown timer
		if (!_canSwap)
		{
			_swapTimer -= dt;
			if (_swapTimer <= 0f)
				_canSwap = true;
		}

		var velocity = Velocity;

		// ── Coyote time ──────────────────────────────────────────
		if (IsOnFloor())
		{
			_coyoteTimer = CoyoteTime;
			_isJumping = false;
		}
		else
		{
			_coyoteTimer -= dt;
		}

		// Note: standard ground jump removed — jumps now come from terrain/ramp momentum.

		// ── Gravity with jump-feel multipliers ───────────────────
		float vehicleGravMult = CurrentVehicle?.GetGravityMultiplier() ?? 1.0f;
		if (!IsOnFloor())
		{
			float jumpGravMult = 1.0f;

			if (velocity.Y > 0f)
				jumpGravMult = FallMultiplier;           // Falling — snappy

			velocity.Y += Gravity * vehicleGravMult * jumpGravMult * dt;
		}

		// ── Flip rotation ────────────────────────────────────────
		if (_isFlipping)
		{
			_flipTimer -= dt;
			if (_flipTimer <= 0f)
			{
				_isFlipping = false;
				if (_playerSprite != null) _playerSprite.Rotation = 0f;
			}
			else if (_playerSprite != null)
			{
				// Full 360 rotation over FlipDuration
				_playerSprite.Rotation += (Mathf.Tau / FlipDuration) * dt;
			}
		}

		// Reset flip state on landing
		if (IsOnFloor() && _isFlipping)
		{
			_isFlipping = false;
			_canFlipJump = false;
			if (_playerSprite != null) _playerSprite.Rotation = 0f;
		}

		// ── Auto-forward movement with acceleration ──────────────

		// Get acceleration from current vehicle and terrain
		float acceleration = CurrentVehicle?.GetAcceleration(CurrentTerrain) ?? 0f;

		// Ice mechanic: can only speed up or maintain speed, never slow down
		if (CurrentTerrain == TerrainType.Ice && acceleration < 0f)
		{
			acceleration = 0f;  // Prevent deceleration on ice
		}

		// Apply acceleration to current speed
		_currentSpeed += acceleration * dt;

		// Clamp to vehicle's max speed
		float maxSpeed = CurrentVehicle?.MaxSpeed ?? 500f;

		// Apply tuck bonus to max speed if skiing
		if (CurrentState == PlayerState.Skiing && SkiNode is { IsTucking: true })
			maxSpeed *= 1.2f;

		_currentSpeed = Mathf.Clamp(_currentSpeed, BaseSpeed * 0.5f, maxSpeed);

		velocity.X = _currentSpeed;

		// ── Slope climbing assistance ────────────────────────────
		// Bikes can climb walls, skis struggle on steep slopes
		if (IsOnFloor())
		{
			Vector2 floorNormal = GetFloorNormal();
			float slopeAngle = Mathf.RadToDeg(Mathf.Acos(floorNormal.Dot(Vector2.Up)));

			// If on an upward slope (moving right and slope goes up)
			if (slopeAngle > 5f && floorNormal.X < 0f)  // Upward slope to the right
			{
				float slopeAssist = 0f;

				if (CurrentState == PlayerState.Biking)
				{
					// Bikes get strong upward force - can climb near-vertical walls
					slopeAssist = BikeWallClimbForce * (slopeAngle / 90f);
				}
				else
				{
					// Skis get minimal assist - struggle on steep slopes
					slopeAssist = SkiSlopeAssist * (slopeAngle / 90f);
				}

				velocity.Y -= slopeAssist * dt;
			}
		}

		Velocity = velocity;

		MoveAndSlide();

		// Detect leaving the ground this frame (launched from terrain) to enable flip
		if (_wasOnFloor && !IsOnFloor())
		{
			_isJumping = true;
			_canFlipJump = true;
		}

		_wasOnFloor = IsOnFloor();

		// Fall detection - player fell into a gap
		if (Position.Y > FallDeathThreshold)
		{
			OnCrash();
			return;
		}

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
		_currentSpeed = BaseSpeed;

		// Reset vehicle visuals
		CurrentVehicle?.OnDeactivated();
		CurrentVehicle = BikeNode;
		CurrentVehicle?.OnActivated();

		_canSwap = true;
		_swapTimer = 0f;
		_coyoteTimer = 0f;
		_jumpBufferTimer = 0f;
		_isJumping = false;
		_jumpHeld = false;
		_canFlipJump = false;
		_isFlipping = false;
		_flipTimer = 0f;
		if (_playerSprite != null) _playerSprite.Rotation = 0f;
		CurrentTerrain = TerrainType.Snow;

		GD.Print("[PlayerController] Reset for new run");
	}

	// ── Private helpers ──────────────────────────────────────────

	private void ExecuteFlipJump()
	{
		// Speed-scaled flip jump — smaller than normal jump but still rewards speed
		float speedBonus = Mathf.Sqrt(_currentSpeed / BaseSpeed);
		Velocity = new Vector2(Velocity.X, FlipJumpForce * speedBonus);
		_canFlipJump = false;
		_isFlipping = true;
		_flipTimer = FlipDuration;
		GD.Print("[PlayerController] Flip jump!");
	}

	private void OnCrash()
	{
		_isFlipping = false;
		_canFlipJump = false;
		if (_playerSprite != null) _playerSprite.Rotation = 0f;
		EmitSignal(SignalName.PlayerCrashed);
		GD.Print("[PlayerController] Crashed!");
	}
}
