using Godot;
using PeakShift.Physics;
using static PeakShift.Physics.MomentumPhysics;

namespace PeakShift;

/// <summary>
/// Main player controller. CharacterBody2D driven by momentum-based physics.
///
/// State machine: Grounded → Airborne → Flipping → (landing) → Grounded
///                Grounded ↔ Tucking (both vehicles, grounded downforce)
///                Airborne ↔ AirborneTucking (aerial dive, anti-lift)
///
/// Core loop each frame:
///   1. Update state machine transitions
///   2. Compute physics (slope accel, drag, gravity, air physics)
///   3. Grounded: path-follow (position from terrain math). Airborne: Euler integration.
///   4. Check failure conditions
///
/// All physics math is delegated to MomentumPhysics (pure, stateless, deterministic).
/// All tunable constants live in PhysicsConstants.
/// </summary>
public partial class PlayerController : CharacterBody2D
{
	// ── Movement State Machine ──────────────────────────────────────

	public enum MoveState
	{
		Grounded,
		Airborne,
		Tucking,          // Grounded tuck: path adherence, downforce
		AirborneTucking,  // Aerial tuck: dive, anti-lift
		Flipping
	}

	public enum VehicleType
	{
		Bike,
		Ski
	}

	// ── Signals ──────────────────────────────────────────────────────

	[Signal]
	public delegate void VehicleSwappedEventHandler(int newState);

	[Signal]
	public delegate void PlayerCrashedEventHandler();

	[Signal]
	public delegate void FlipCompletedEventHandler();

	[Signal]
	public delegate void FlipFailedEventHandler();

	[Signal]
	public delegate void FlipPointsScoredEventHandler(int points, int flipCount);

	// ── Node References ─────────────────────────────────────────────

	[Export]
	public BikeController BikeNode { get; set; }

	[Export]
	public SkiController SkiNode { get; set; }

	public VehicleBase CurrentVehicle { get; private set; }

	// ── Public State ────────────────────────────────────────────────

	public MoveState CurrentMoveState { get; private set; } = MoveState.Grounded;
	public VehicleType CurrentVehicleType { get; private set; } = VehicleType.Bike;
	public TerrainType CurrentTerrain;

	/// <summary>Current scalar momentum speed along the ground/travel direction (px/s).</summary>
	public float MomentumSpeed { get; private set; }

	// ── Private State ───────────────────────────────────────────────

	private GameManager _gameManager;
	private TerrainManager _terrainManager;
	private Sprite2D _playerSprite;
	private Texture2D _bikerTexture;
	private Texture2D _skierTexture;
	private Vector2 _startPosition;

	// Vehicle swap
	private float _swapTimer;
	private bool _canSwap = true;

	// Airborne state
	private Vector2 _airVelocity;
	private Vector2 _launchNormal;

	// Flip state
	private float _flipRotation;
	private float _flipAngularVelocity;
	private bool _flipCompleted;
	private int _targetFlipCount;
	private int _flipCount; // Number of full 360° rotations completed

	// Post-flip bonus window
	private float _flipBonusTimer;

	// Flip cooldown (delay between successive flips)
	private float _flipCooldownTimer;

	// Tuck state
	private bool _tuckInputHeld;

	// Terrain-hugging: collision shape half-height for snap offset
	private float _collisionHalfHeight = 32f;

	// Slope-following visual rotation
	private float _visualRotation;

	// ── Debug state (exposed for HUD overlay) ──────────────────────
	public float DebugSlopeAngleDeg { get; private set; }
	public float DebugVerticalVelocity { get; private set; }
	public float DebugForwardVelocity { get; private set; }
	public bool DebugIsAirborne => CurrentMoveState is MoveState.Airborne or MoveState.AirborneTucking or MoveState.Flipping;
	public bool DebugIsTucking => CurrentMoveState is MoveState.Tucking or MoveState.AirborneTucking;
	public string DebugTerrainType => CurrentTerrain.ToString();
	public bool DebugOverGap { get; private set; }

	// ── Lifecycle ───────────────────────────────────────────────────

	public override void _Ready()
	{
		BikeNode ??= GetNodeOrNull<BikeController>("Bike");
		SkiNode ??= GetNodeOrNull<SkiController>("Skis");

		CurrentVehicleType = VehicleType.Bike;
		CurrentVehicle = BikeNode;
		CurrentVehicle?.OnActivated();

		_startPosition = Position;
		MomentumSpeed = PhysicsConstants.StartingSpeed;

		_gameManager = GetNodeOrNull<GameManager>("../GameManager");
		_terrainManager = GetNodeOrNull<TerrainManager>("../TerrainManager");
		_playerSprite = GetNodeOrNull<Sprite2D>("Sprite2D");
		_bikerTexture = GD.Load<Texture2D>("res://Assets/Art/Characters/Biker.png");
		_skierTexture = GD.Load<Texture2D>("res://Assets/Art/Characters/Skier.png");

		// Detect collision shape half-height for terrain Y offset
		var collisionShape = GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
		if (collisionShape?.Shape is RectangleShape2D rect)
			_collisionHalfHeight = rect.Size.Y / 2f;
		else if (collisionShape?.Shape is CircleShape2D circle)
			_collisionHalfHeight = circle.Radius;
		else if (collisionShape?.Shape is CapsuleShape2D capsule)
			_collisionHalfHeight = capsule.Height / 2f;

		// Position player on the terrain surface at startup
		if (_terrainManager != null)
		{
			float surfaceY = _terrainManager.GetStartingSurfaceY();
			Position = new Vector2(Position.X, surfaceY - _collisionHalfHeight);
			_startPosition = Position;
		}

		GD.Print("[PlayerController] Ready — momentum physics active");
	}

	// ── Input ───────────────────────────────────────────────────────

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_gameManager == null || _gameManager.CurrentState != GameManager.GameState.Playing)
			return;

		// ── Flip input (ground or airborne) ────────────────────────
		if (@event.IsActionPressed("jump") && _flipCooldownTimer <= 0f)
		{
			// Allow flipping from ground or air
			if (CurrentMoveState is MoveState.Grounded or MoveState.Tucking)
			{
				// Launch from ground into flip
				TransitionToAirborne();
				EnterFlipping();
			}
			else if (CurrentMoveState is MoveState.Airborne or MoveState.AirborneTucking)
			{
				// Exit aerial tuck to flip
				if (CurrentMoveState == MoveState.AirborneTucking)
				{
					_tuckInputHeld = false;
					SetSkiTucking(false);
				}
				EnterFlipping();
			}
			// If already flipping, queue another flip (double-jump style)
			else if (CurrentMoveState == MoveState.Flipping)
			{
				_targetFlipCount++;
				_airVelocity.Y = Mathf.Min(_airVelocity.Y, PhysicsConstants.FlipLaunchImpulse);
				_flipCooldownTimer = PhysicsConstants.FlipCooldown;
				GD.Print($"[PlayerController] Queued flip #{_targetFlipCount} — air boost applied");
			}
		}

		// ── Tuck input (both vehicles) ─────────────────────────────
		if (@event.IsActionPressed("tuck"))
		{
			_tuckInputHeld = true;
		}
		if (@event.IsActionReleased("tuck"))
		{
			_tuckInputHeld = false;
		}

		// ── Vehicle swap ────────────────────────────────────────────
		if (@event.IsActionPressed("swap_vehicle") && _canSwap)
		{
			SwapVehicle();
		}
	}

	// ── Physics Process ─────────────────────────────────────────────

	public override void _PhysicsProcess(double delta)
	{
		if (_gameManager == null || _gameManager.CurrentState != GameManager.GameState.Playing)
		{
			Velocity = Vector2.Zero;
			return;
		}

		float dt = (float)delta;

		UpdateSwapCooldown(dt);
		UpdateFlipBonusTimer(dt);
		UpdateFlipCooldown(dt);
		UpdateTuckState();

		// ── State machine dispatch ──────────────────────────────────
		switch (CurrentMoveState)
		{
			case MoveState.Grounded:
			case MoveState.Tucking:
				ProcessGrounded(dt);
				break;

			case MoveState.Airborne:
			case MoveState.AirborneTucking:
				ProcessAirborne(dt);
				break;

			case MoveState.Flipping:
				ProcessFlipping(dt);
				break;
		}

		// ── Post-move checks ────────────────────────────────────────

		// Fall death — relative to terrain surface so downhill progression doesn't kill you
		if (_terrainManager != null)
		{
			float terrainY = _terrainManager.GetTerrainHeight(GlobalPosition.X);
			if (Position.Y > terrainY + PhysicsConstants.FallDeathBelowTerrain)
			{
				OnCrash("Fell into gap");
				return;
			}
		}

		UpdateDebugState();
	}

	// ── Grounded Physics ────────────────────────────────────────────

	private void ProcessGrounded(float dt)
	{
		bool isTucking = CurrentMoveState == MoveState.Tucking;
		bool isBraking = isTucking && CurrentVehicleType == VehicleType.Bike;
		bool isActuallyTucking = isTucking && !isBraking;
		float playerX = GlobalPosition.X;

		// ── Query terrain math directly ─────────────────────────────
		Vector2 terrainNormal = _terrainManager != null
			? _terrainManager.GetTerrainNormalAt(playerX)
			: Vector2.Up;
		float slopeAngleRad = MomentumPhysics.ComputeSignedSlopeAngle(terrainNormal);
		DebugSlopeAngleDeg = Mathf.RadToDeg(slopeAngleRad);

		// ── Update terrain type at current position ─────────────────
		if (_terrainManager != null)
			CurrentTerrain = _terrainManager.GetTerrainType(playerX);

		// ── Centripetal launch check ────────────────────────────────
		// Tucking/braking suppresses launch — player sticks to the ground.
		// Don't launch when going backwards or near-stopped
		if (!isTucking && MomentumSpeed > 0f && _terrainManager != null)
		{
			float curvature = ComputeCurvatureAtPlayer();
			float gravMult = CurrentVehicle?.GravityMultiplier ?? 1.0f;

			if (MomentumPhysics.ShouldLaunchFromSurface(MomentumSpeed, curvature, gravMult, isActuallyTucking))
			{
				TransitionToAirborne();
				return;
			}
		}

		// ── Gather modifiers ────────────────────────────────────────
		float terrainFriction = MomentumPhysics.GetTerrainFriction(CurrentTerrain);
		float terrainDragMod = MomentumPhysics.GetTerrainDragModifier(CurrentTerrain);

		float vehicleDragMod = CurrentVehicle.DragModifier;
		float vehicleFrictionMod = CurrentVehicle.GetTerrainFrictionModifier(CurrentTerrain);
		float vehicleDragTerrainMod = CurrentVehicle.GetTerrainDragModifier(CurrentTerrain);
		float vehicleTerrainBonus = CurrentVehicle.GetTerrainBonus(CurrentTerrain);

		// When going backwards, terrain bonus always pushes toward forward (opposes backward motion)
		if (MomentumSpeed < 0f)
			vehicleTerrainBonus = Mathf.Abs(vehicleTerrainBonus);

		// ── Compute effective coefficients ──────────────────────────
		float effectiveDrag = MomentumPhysics.GetEffectiveDragCoefficient(
			isActuallyTucking, terrainDragMod * vehicleDragTerrainMod, vehicleDragMod);

		float effectiveRollingResistance = MomentumPhysics.GetEffectiveRollingResistance(
			isActuallyTucking, terrainFriction, vehicleFrictionMod)
			* CurrentVehicle.RollingResistanceModifier;

		// ── Brake: bike uses tuck input as powerful brake ────────────
		if (isBraking)
		{
			effectiveDrag *= PhysicsConstants.BrakeDragMultiplier;
		}

		// Apply post-flip bonus drag reduction
		if (_flipBonusTimer > 0f)
			effectiveDrag *= PhysicsConstants.FlipSuccessDragMultiplier;

		// ── Tuck downhill acceleration bonus (ski only) ─────────────
		float tuckAccelBonus = 1.0f;
		if (isActuallyTucking && slopeAngleRad > 0f)
			tuckAccelBonus = PhysicsConstants.TuckDownhillAccelBonus;

		// ── Compute total ground acceleration ───────────────────────
		float acceleration = MomentumPhysics.ComputeGroundAcceleration(
			MomentumSpeed,
			slopeAngleRad * tuckAccelBonus,
			effectiveDrag,
			effectiveRollingResistance,
			1.0f,
			vehicleTerrainBonus);

		// Extra constant brake deceleration force
		if (isBraking && MomentumSpeed > 0f)
			acceleration -= PhysicsConstants.BrakeDeceleration;

		// ── Terminal velocity ───────────────────────────────────────
		float maxSpeed = CurrentVehicle?.MaxSpeed ?? PhysicsConstants.TerminalVelocity;
		float terminalVel = MomentumPhysics.GetEffectiveTerminalVelocity(isActuallyTucking, maxSpeed);

		// ── Determine min speed based on vehicle ────────────────────
		float minSpeed;
		if (CurrentVehicle.CanMoveBackwards)
			minSpeed = -PhysicsConstants.MaxBackwardSpeed;
		else if (isBraking)
			minSpeed = PhysicsConstants.BrakeMinSpeed;
		else
			minSpeed = PhysicsConstants.MinimumSpeed;

		// ── Integrate speed ─────────────────────────────────────────
		MomentumSpeed = MomentumPhysics.IntegrateSpeed(MomentumSpeed, acceleration, dt, terminalVel, minSpeed);

		// ── PATH-FOLLOWING: Position from math, not collision ───────
		float newX = playerX + MomentumSpeed * dt;

		// Check if we've reached a gap — transition to airborne at the lip
		if (_terrainManager != null && _terrainManager.IsOverGap(newX))
		{
			// If going backwards into a gap, just fall straight down
			if (MomentumSpeed < 0f)
				_airVelocity = new Vector2(0f, 0f);
			TransitionToAirborne();
			return;
		}

		// Snap Y to terrain curve minus half-height offset
		float terrainY = _terrainManager?.GetTerrainHeight(newX) ?? GlobalPosition.Y;
		float newY = terrainY - _collisionHalfHeight;
		GlobalPosition = new Vector2(newX, newY);

		// Set Velocity for debug tools and Godot's internal state
		Vector2 tangent = _terrainManager?.GetTerrainTangentAt(newX) ?? Vector2.Right;
		Velocity = tangent * MomentumSpeed;

		// ── Slope-following visual rotation ─────────────────────────
		float targetAngle = Mathf.Atan2(tangent.Y, tangent.X);
		_visualRotation = Mathf.Lerp(_visualRotation, targetAngle, 1f - Mathf.Exp(-12f * dt));
		if (_playerSprite != null)
			_playerSprite.Rotation = _visualRotation;
	}

	// ── Airborne Physics ────────────────────────────────────────────

	private void ProcessAirborne(float dt)
	{
		float gravMult = CurrentVehicle?.GravityMultiplier ?? 1.0f;
		bool isAerialTuck = CurrentMoveState == MoveState.AirborneTucking;

		// ── Integrate airborne trajectory ───────────────────────────
		if (isAerialTuck)
			_airVelocity = MomentumPhysics.IntegrateAirborneTucking(_airVelocity, dt, gravMult);
		else
			_airVelocity = MomentumPhysics.IntegrateAirborne(_airVelocity, dt, gravMult);

		// ── Move by direct position update ──────────────────────────
		GlobalPosition += _airVelocity * dt;
		Velocity = _airVelocity;

		// ── Velocity-direction visual rotation ──────────────────────
		float targetAngle = Mathf.Atan2(_airVelocity.Y, _airVelocity.X);
		_visualRotation = Mathf.Lerp(_visualRotation, targetAngle, 1f - Mathf.Exp(-8f * dt));
		if (_playerSprite != null)
			_playerSprite.Rotation = _visualRotation;

		// ── Math-based landing detection ────────────────────────────
		if (_terrainManager != null)
		{
			float terrainY = _terrainManager.GetTerrainHeight(GlobalPosition.X);
			float playerBottomY = GlobalPosition.Y + _collisionHalfHeight;
			bool overGap = _terrainManager.IsOverGap(GlobalPosition.X);

			if (!overGap && playerBottomY >= terrainY && _airVelocity.Y > 0f)
			{
				GlobalPosition = new Vector2(GlobalPosition.X, terrainY - _collisionHalfHeight);
				OnLanding();
			}
		}
	}

	// ── Flipping Physics ────────────────────────────────────────────

	private void ProcessFlipping(float dt)
	{
		float gravMult = CurrentVehicle?.GravityMultiplier ?? 1.0f;

		// ── Airborne trajectory with reduced gravity during flip ────
		float flipGravMult = gravMult * PhysicsConstants.FlipGravityMultiplier;
		_airVelocity = MomentumPhysics.IntegrateAirborne(_airVelocity, dt, flipGravMult);

		// ── Move by direct position update ──────────────────────────
		GlobalPosition += _airVelocity * dt;
		Velocity = _airVelocity;

		// ── Integrate rotation toward target flip count ─────────────
		float maxRotation = _targetFlipCount * Mathf.Tau;

		if (_flipRotation < maxRotation)
		{
			_flipRotation = MomentumPhysics.IntegrateFlipRotation(_flipRotation, _flipAngularVelocity, dt);
			if (_flipRotation >= maxRotation)
				_flipRotation = maxRotation;
		}

		if (_playerSprite != null)
			_playerSprite.Rotation = _flipRotation;

		// Track number of full 360° rotations completed
		int previousFlipCount = _flipCount;
		_flipCount = (int)(Mathf.Abs(_flipRotation) / Mathf.Tau);

		if (_flipCount > previousFlipCount)
		{
			_flipCompleted = true;
			GD.Print($"[PlayerController] Flip #{_flipCount} completed!");
		}

		// ── Math-based landing detection ────────────────────────────
		if (_terrainManager != null)
		{
			float terrainY = _terrainManager.GetTerrainHeight(GlobalPosition.X);
			float playerBottomY = GlobalPosition.Y + _collisionHalfHeight;
			bool overGap = _terrainManager.IsOverGap(GlobalPosition.X);

			if (!overGap && playerBottomY >= terrainY && _airVelocity.Y > 0f)
			{
				GlobalPosition = new Vector2(GlobalPosition.X, terrainY - _collisionHalfHeight);
				OnFlipLanding();
			}
		}
	}

	// ── State Transitions ───────────────────────────────────────────

	private void TransitionToAirborne()
	{
		bool wasTucking = _tuckInputHeld;
		CurrentMoveState = wasTucking ? MoveState.AirborneTucking : MoveState.Airborne;

		// If going backwards (ski sliding), just fall — don't compute a backwards launch
		if (MomentumSpeed < 0f)
		{
			_airVelocity = new Vector2(0f, 0f);
			_launchNormal = Vector2.Up;
			MomentumSpeed = 0f;
			SetSkiTucking(wasTucking);
			return;
		}

		// Compute launch velocity from current speed and terrain normal
		Vector2 terrainNormal = _terrainManager != null
			? _terrainManager.GetTerrainNormalAt(GlobalPosition.X)
			: Vector2.Up;
		_airVelocity = MomentumPhysics.ComputeLaunchVelocity(MomentumSpeed, terrainNormal);
		_launchNormal = terrainNormal;

		SetSkiTucking(wasTucking);
	}

	private void EnterFlipping()
	{
		if (CurrentMoveState is not (MoveState.Airborne or MoveState.AirborneTucking))
			return;

		CurrentMoveState = MoveState.Flipping;
		_flipRotation = 0f;
		_flipCompleted = false;
		_targetFlipCount = 1; // One press = one full flip
		_flipCount = 0;

		float flipMod = CurrentVehicle?.FlipSpeedModifier ?? 1.0f;
		_flipAngularVelocity = MomentumPhysics.ComputeFlipAngularVelocity(MomentumSpeed, flipMod);

		// Air-jump: boost upward when initiating flip
		_airVelocity.Y = Mathf.Min(_airVelocity.Y, PhysicsConstants.FlipLaunchImpulse);
		_flipCooldownTimer = PhysicsConstants.FlipCooldown;

		GD.Print($"[PlayerController] Flip initiated — angular vel: {_flipAngularVelocity:F1} rad/s, air impulse: {_airVelocity.Y:F0}");
	}

	private void OnLanding()
	{
		bool landIntoTuck = _tuckInputHeld;
		CurrentMoveState = landIntoTuck ? MoveState.Tucking : MoveState.Grounded;

		// Project full air velocity onto slope tangent to preserve energy from steep dives
		Vector2 landNormal = _terrainManager != null
			? _terrainManager.GetTerrainNormalAt(GlobalPosition.X)
			: Vector2.Up;
		MomentumSpeed = MomentumPhysics.ProjectLandingSpeed(_airVelocity, landNormal);
		_airVelocity = Vector2.Zero;

		SetSkiTucking(landIntoTuck);
	}

	private void OnFlipLanding()
	{
		float normalized = _flipRotation % Mathf.Tau;
		if (normalized < 0f) normalized += Mathf.Tau;
		float toleranceRad = Mathf.DegToRad(90f);
		bool landingSafe = normalized <= toleranceRad || normalized >= (Mathf.Tau - toleranceRad);

		if (_flipCount > 0 && landingSafe)
		{
			Vector2 landNormal = _terrainManager != null
				? _terrainManager.GetTerrainNormalAt(GlobalPosition.X)
				: Vector2.Up;
			float projectedSpeed = MomentumPhysics.ProjectLandingSpeed(_airVelocity, landNormal);
			float boostedSpeed = projectedSpeed * PhysicsConstants.FlipSuccessSpeedBoost;
			MomentumSpeed = boostedSpeed;
			_flipBonusTimer = PhysicsConstants.FlipSuccessDragWindowDuration;

			int points = _flipCount * 100;
			EmitSignal(SignalName.FlipCompleted);
			EmitSignal(SignalName.FlipPointsScored, points, _flipCount);
			GD.Print($"[PlayerController] Flip landed! {_flipCount} flip(s), {points} points! Speed: {boostedSpeed:F0}");

			_flipRotation = 0f;
			_flipAngularVelocity = 0f;
			_flipCompleted = false;
			_targetFlipCount = 0;
			_flipCount = 0;
			_airVelocity = Vector2.Zero;

			// Land on the ground — no bounce
			bool landIntoTuck = _tuckInputHeld;
			CurrentMoveState = landIntoTuck ? MoveState.Tucking : MoveState.Grounded;
			SetSkiTucking(landIntoTuck);
		}
		else
		{
			EmitSignal(SignalName.FlipFailed);
			OnCrash(landingSafe ? "No flips completed" : "Bad landing angle");
		}
	}

	// ── Tuck Management ─────────────────────────────────────────────

	private void UpdateTuckState()
	{
		// ── Grounded tuck: available for both vehicles ─────────────
		bool canGroundTuck = (CurrentMoveState == MoveState.Grounded || CurrentMoveState == MoveState.Tucking)
							 && _tuckInputHeld;

		if (canGroundTuck && CurrentMoveState == MoveState.Grounded)
		{
			CurrentMoveState = MoveState.Tucking;
			SetSkiTucking(true);
		}
		else if (!canGroundTuck && CurrentMoveState == MoveState.Tucking)
		{
			CurrentMoveState = MoveState.Grounded;
			SetSkiTucking(false);
		}

		// ── Aerial tuck: press/hold tuck while airborne ────────────
		if (CurrentMoveState == MoveState.Airborne && _tuckInputHeld)
		{
			CurrentMoveState = MoveState.AirborneTucking;
			SetSkiTucking(true);
		}
		else if (CurrentMoveState == MoveState.AirborneTucking && !_tuckInputHeld)
		{
			CurrentMoveState = MoveState.Airborne;
			SetSkiTucking(false);
		}
	}

	// ── Vehicle Swap ────────────────────────────────────────────────

	public void SwapVehicle()
	{
		if (!_canSwap) return;

		CurrentVehicle?.OnDeactivated();

		if (CurrentVehicleType == VehicleType.Bike)
		{
			CurrentVehicleType = VehicleType.Ski;
			CurrentVehicle = SkiNode;
		}
		else
		{
			CurrentVehicleType = VehicleType.Bike;
			CurrentVehicle = BikeNode;
		}

		CurrentVehicle?.OnActivated();
		UpdateCharacterSprite();

		_canSwap = false;
		_swapTimer = PhysicsConstants.SwapCooldown;

		EmitSignal(SignalName.VehicleSwapped, (int)CurrentVehicleType);
	}

	// ── Obstacle Collision ──────────────────────────────────────────

	/// <summary>
	/// Apply a speed penalty from a non-fatal obstacle collision (e.g., logs, small rocks).
	/// </summary>
	public void ApplySpeedPenalty(float penaltyAmount)
	{
		MomentumSpeed = Mathf.Max(PhysicsConstants.MinimumSpeed, MomentumSpeed - penaltyAmount);
	}

	// ── Reset ───────────────────────────────────────────────────────

	public void ResetForNewRun()
	{
		// Position player on top of the terrain surface at the start
		float startY = _terrainManager?.GetStartingSurfaceY() ?? _startPosition.Y;
		Position = new Vector2(_startPosition.X, startY - _collisionHalfHeight);
		Velocity = Vector2.Zero;

		CurrentVehicleType = VehicleType.Bike;
		CurrentMoveState = MoveState.Grounded;
		MomentumSpeed = PhysicsConstants.StartingSpeed;

		CurrentVehicle?.OnDeactivated();
		CurrentVehicle = BikeNode;
		CurrentVehicle?.OnActivated();
		UpdateCharacterSprite();

		_canSwap = true;
		_swapTimer = 0f;

		_airVelocity = Vector2.Zero;
		_launchNormal = Vector2.Up;

		_flipRotation = 0f;
		_flipAngularVelocity = 0f;
		_flipCompleted = false;
		_targetFlipCount = 0;
		_flipCount = 0;
		_flipBonusTimer = 0f;
		_flipCooldownTimer = 0f;

		_tuckInputHeld = false;
		SetSkiTucking(false);

		_visualRotation = 0f;
		if (_playerSprite != null) _playerSprite.Rotation = 0f;

		CurrentTerrain = TerrainType.Snow;

		GD.Print("[PlayerController] Reset for new run");
	}

	/// <summary>
	/// Updates debug-exposed state each frame. Called from _PhysicsProcess
	/// so the HUD debug overlay can read current values.
	/// </summary>
	private void UpdateDebugState()
	{
		DebugForwardVelocity = CurrentMoveState is MoveState.Airborne or MoveState.AirborneTucking or MoveState.Flipping
			? _airVelocity.X
			: MomentumSpeed;
		DebugVerticalVelocity = CurrentMoveState is MoveState.Airborne or MoveState.AirborneTucking or MoveState.Flipping
			? _airVelocity.Y
			: Velocity.Y;
		DebugOverGap = _terrainManager?.IsOverGap(GlobalPosition.X) ?? false;
	}

	// ── Helpers ─────────────────────────────────────────────────────

	private void UpdateSwapCooldown(float dt)
	{
		if (!_canSwap)
		{
			_swapTimer -= dt;
			if (_swapTimer <= 0f)
				_canSwap = true;
		}
	}

	private void UpdateFlipBonusTimer(float dt)
	{
		if (_flipBonusTimer > 0f)
			_flipBonusTimer -= dt;
	}

	private void UpdateFlipCooldown(float dt)
	{
		if (_flipCooldownTimer > 0f)
			_flipCooldownTimer -= dt;
	}

	/// <summary>
	/// Computes terrain curvature at the player's current X position.
	/// Positive = convex crest (launch candidate). Negative = concave valley.
	/// </summary>
	private float ComputeCurvatureAtPlayer()
	{
		float x = GlobalPosition.X;
		float delta = PhysicsConstants.CurvatureSampleDelta;
		float hLeft = _terrainManager.GetTerrainHeight(x - delta);
		float hCenter = _terrainManager.GetTerrainHeight(x);
		float hRight = _terrainManager.GetTerrainHeight(x + delta);
		return MomentumPhysics.ComputeTerrainCurvature(hLeft, hCenter, hRight, delta);
	}

	private void OnCrash(string reason)
	{
		// Reset visual state
		_visualRotation = 0f;
		if (_playerSprite != null) _playerSprite.Rotation = 0f;
		_flipRotation = 0f;
		_flipAngularVelocity = 0f;
		_flipCompleted = false;
		_targetFlipCount = 0;
		_flipCount = 0;
		SetSkiTucking(false);
		_tuckInputHeld = false;
		_airVelocity = Vector2.Zero;

		CurrentMoveState = MoveState.Grounded;
		EmitSignal(SignalName.PlayerCrashed);
		GD.Print($"[PlayerController] Crashed — {reason}");
	}

	// ── Helper Methods ──────────────────────────────────────────────

	private void UpdateCharacterSprite()
	{
		if (_playerSprite != null)
			_playerSprite.Texture = CurrentVehicleType == VehicleType.Bike ? _bikerTexture : _skierTexture;
	}

	/// <summary>Sets the ski tucking visual state if SkiNode exists.</summary>
	private void SetSkiTucking(bool value)
	{
		if (SkiNode != null) SkiNode.IsTucking = value;
	}
}
