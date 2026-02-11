using Godot;
using PeakShift.Physics;

namespace PeakShift;

/// <summary>
/// Main player controller. CharacterBody2D driven by momentum-based physics.
///
/// State machine: Grounded → Airborne → Flipping → (landing) → Grounded
///                Grounded ↔ Tucking (ski only, while grounded)
///
/// Core loop each frame:
///   1. Update state machine transitions
///   2. Compute physics (slope accel, drag, gravity, air physics)
///   3. Apply velocity via MoveAndSlide
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
		Tucking,
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
	public delegate void PerfectSwapEventHandler();

	[Signal]
	public delegate void PlayerCrashedEventHandler();

	[Signal]
	public delegate void FlipCompletedEventHandler();

	[Signal]
	public delegate void FlipFailedEventHandler();

	[Signal]
	public delegate void SpeedChangedEventHandler(float speed);

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
	private Vector2 _startPosition;

	// Vehicle swap
	private float _swapTimer;
	private bool _canSwap = true;

	// Coyote time (brief window after leaving ground where we don't immediately transition)
	private float _coyoteTimer;
	private bool _wasOnFloor;

	// Airborne state
	private Vector2 _airVelocity;
	private Vector2 _launchNormal;

	// Flip state
	private float _flipRotation;
	private float _flipAngularVelocity;
	private bool _flipCompleted;

	// Post-flip bonus window
	private float _flipBonusTimer;

	// Tuck state
	private bool _tuckInputHeld;

	// Brake state (bike only)
	private bool _brakeInputHeld;

	// Terrain-hugging: collision shape half-height for snap offset
	private float _collisionHalfHeight = 32f;

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

		// Configure CharacterBody2D for terrain hugging
		FloorSnapLength = 48f;
		FloorMaxAngle = Mathf.DegToRad(70f);

		// Detect collision shape half-height for snap offset
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

		// ── Flip input (airborne only) ──────────────────────────────
		if (@event.IsActionPressed("jump"))
		{
			if (CurrentMoveState == MoveState.Airborne)
			{
				EnterFlipping();
			}
		}

		// ── Tuck input (ski only) ───────────────────────────────────
		if (@event.IsActionPressed("tuck"))
		{
			_tuckInputHeld = true;
		}
		if (@event.IsActionReleased("tuck"))
		{
			_tuckInputHeld = false;
		}

		// ── Brake input (bike only) ─────────────────────────────────
		if (@event.IsActionPressed("brake"))
		{
			_brakeInputHeld = true;
		}
		if (@event.IsActionReleased("brake"))
		{
			_brakeInputHeld = false;
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
		UpdateTuckState();

		// ── State machine dispatch ──────────────────────────────────
		switch (CurrentMoveState)
		{
			case MoveState.Grounded:
			case MoveState.Tucking:
				ProcessGrounded(dt);
				break;

			case MoveState.Airborne:
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

		// Hazard collision
		for (int i = 0; i < GetSlideCollisionCount(); i++)
		{
			var collision = GetSlideCollision(i);
			if (collision.GetCollider() is Node2D collider && collider.IsInGroup("hazard"))
			{
				OnCrash("Hazard collision");
				return;
			}
		}

		_wasOnFloor = IsOnFloor();
	}

	// ── Grounded Physics ────────────────────────────────────────────

	private void ProcessGrounded(float dt)
	{
		bool isTucking = CurrentMoveState == MoveState.Tucking;

		// ── Compute slope from floor normal or terrain query ────────
		Vector2 floorNormal;
		float slopeAngleRad;

		if (IsOnFloor())
		{
			floorNormal = GetFloorNormal();
			slopeAngleRad = MomentumPhysics.ComputeSignedSlopeAngle(floorNormal);
			_coyoteTimer = PhysicsConstants.CoyoteTime;
		}
		else if (_terrainManager != null)
		{
			// Not on floor but may be close — use terrain data for physics
			floorNormal = _terrainManager.GetTerrainNormalAt(GlobalPosition.X);
			slopeAngleRad = MomentumPhysics.ComputeSignedSlopeAngle(floorNormal);
		}
		else
		{
			floorNormal = Vector2.Up;
			slopeAngleRad = 0f;
		}

		// ── Gather modifiers ────────────────────────────────────────
		float terrainFriction = MomentumPhysics.GetTerrainFriction(CurrentTerrain);
		float terrainDragMod = MomentumPhysics.GetTerrainDragModifier(CurrentTerrain);

		float vehicleDragMod = CurrentVehicle?.DragModifier ?? 1.0f;
		float vehicleFrictionMod = CurrentVehicle?.GetTerrainFrictionModifier(CurrentTerrain) ?? 1.0f;
		float vehicleDragTerrainMod = CurrentVehicle?.GetTerrainDragModifier(CurrentTerrain) ?? 1.0f;
		float vehicleTerrainBonus = CurrentVehicle?.GetTerrainBonus(CurrentTerrain) ?? 0f;

		// ── Compute effective coefficients ──────────────────────────
		float effectiveDrag = MomentumPhysics.GetEffectiveDragCoefficient(
			isTucking, terrainDragMod * vehicleDragTerrainMod, vehicleDragMod);

		float effectiveRollingResistance = MomentumPhysics.GetEffectiveRollingResistance(
			isTucking, terrainFriction, vehicleFrictionMod);

		// Apply post-flip bonus drag reduction
		if (_flipBonusTimer > 0f)
			effectiveDrag *= PhysicsConstants.FlipSuccessDragMultiplier;

		// ── Brake (bike only) ───────────────────────────────────────
		bool isBraking = _brakeInputHeld && CurrentVehicleType == VehicleType.Bike;
		if (isBraking)
			effectiveDrag *= PhysicsConstants.BrakeDragMultiplier;

		// ── Tuck downhill acceleration bonus ────────────────────────
		float tuckAccelBonus = 1.0f;
		if (isTucking && slopeAngleRad > 0f)
			tuckAccelBonus = PhysicsConstants.TuckDownhillAccelBonus;

		// ── Compute total ground acceleration ───────────────────────
		float acceleration = MomentumPhysics.ComputeGroundAcceleration(
			MomentumSpeed,
			slopeAngleRad * tuckAccelBonus,
			effectiveDrag,
			effectiveRollingResistance,
			1.0f, // friction already baked into effectiveRollingResistance
			vehicleTerrainBonus);

		// ── Terminal velocity ───────────────────────────────────────
		float maxSpeed = CurrentVehicle?.MaxSpeed ?? PhysicsConstants.TerminalVelocity;
		float terminalVel = MomentumPhysics.GetEffectiveTerminalVelocity(isTucking, maxSpeed);

		// ── Integrate speed ─────────────────────────────────────────
		MomentumSpeed = MomentumPhysics.IntegrateSpeed(MomentumSpeed, acceleration, dt, terminalVel);

		// ── Brake minimum speed ─────────────────────────────────────
		if (isBraking && MomentumSpeed < PhysicsConstants.BrakeMinSpeed)
			MomentumSpeed = PhysicsConstants.BrakeMinSpeed;

		// ── Build velocity vector along surface ─────────────────────
		// MomentumSpeed is treated as horizontal speed. Vertical component
		// is derived from the slope ratio so forward progress stays consistent
		// regardless of slope steepness (no "lagging" on steep downhills).
		Vector2 tangent = new Vector2(floorNormal.Y, -floorNormal.X);
		if (tangent.X < 0f) tangent = -tangent;

		if (tangent.X > 0.1f)
		{
			float slopeRatio = tangent.Y / tangent.X;
			Velocity = new Vector2(MomentumSpeed, MomentumSpeed * slopeRatio);
		}
		else
		{
			// Near-vertical slope fallback — use tangent direction directly
			Velocity = tangent * MomentumSpeed;
		}

		// ── Centripetal launch check ────────────────────────────────
		// Before MoveAndSlide, check if the player should detach from the
		// terrain at a convex crest (like Tiny Wings / Sonic).
		if (_terrainManager != null)
		{
			float curvature = ComputeCurvatureAtPlayer();
			float gravMult = CurrentVehicle?.GravityMultiplier ?? 1.0f;

			if (MomentumPhysics.ShouldLaunchFromSurface(MomentumSpeed, curvature, gravMult))
			{
				// Disable floor snap for this launch
				FloorSnapLength = 0f;
				TransitionToAirborne();
				MoveAndSlide();
				FloorSnapLength = 48f;
				return;
			}
		}

		MoveAndSlide();

		// ── Post-move terrain hugging ───────────────────────────────
		// If MoveAndSlide lost floor contact (small bump, polygon edge),
		// snap the player back to the terrain surface if they're close.
		if (!IsOnFloor() && _terrainManager != null)
		{
			float terrainY = _terrainManager.GetTerrainHeight(GlobalPosition.X);
			float targetY = terrainY - _collisionHalfHeight;
			float distToSurface = Mathf.Abs(Position.Y - targetY);

			if (distToSurface < PhysicsConstants.GroundSnapDistance)
			{
				// Snap to surface — player flows over the bump
				Position = new Vector2(Position.X, targetY);
				_coyoteTimer = PhysicsConstants.CoyoteTime;
			}
			else
			{
				// Too far from surface — over a gap or genuine air
				_coyoteTimer -= dt;
				if (_coyoteTimer <= 0f)
				{
					TransitionToAirborne();
					ProcessAirborne(dt);
					return;
				}
			}
		}
	}

	// ── Airborne Physics ────────────────────────────────────────────

	private void ProcessAirborne(float dt)
	{
		float gravMult = CurrentVehicle?.GravityMultiplier ?? 1.0f;

		// ── Integrate airborne trajectory ───────────────────────────
		// x(t) = v_x * dt  (with air drag)
		// y(t) = v_y * dt + 0.5 * g * dt^2
		_airVelocity = MomentumPhysics.IntegrateAirborne(_airVelocity, dt, gravMult);

		Velocity = _airVelocity;
		MoveAndSlide();

		// ── Landing detection ───────────────────────────────────────
		if (IsOnFloor())
		{
			OnLanding();
		}
	}

	// ── Flipping Physics ────────────────────────────────────────────

	private void ProcessFlipping(float dt)
	{
		float gravMult = CurrentVehicle?.GravityMultiplier ?? 1.0f;

		// ── Airborne trajectory continues during flip ───────────────
		_airVelocity = MomentumPhysics.IntegrateAirborne(_airVelocity, dt, gravMult);
		Velocity = _airVelocity;

		// ── Integrate rotation ──────────────────────────────────────
		_flipRotation = MomentumPhysics.IntegrateFlipRotation(_flipRotation, _flipAngularVelocity, dt);

		// Apply visual rotation
		if (_playerSprite != null)
			_playerSprite.Rotation = _flipRotation;

		// Check if flip completed full rotation
		if (!_flipCompleted && MomentumPhysics.IsFlipComplete(_flipRotation))
		{
			_flipCompleted = true;
		}

		MoveAndSlide();

		// ── Landing detection ───────────────────────────────────────
		if (IsOnFloor())
		{
			OnFlipLanding();
		}
	}

	// ── State Transitions ───────────────────────────────────────────

	private void TransitionToAirborne()
	{
		CurrentMoveState = MoveState.Airborne;

		// Use current velocity as air velocity
		_airVelocity = Velocity;

		// If velocity is mostly horizontal (launched from ramp), use terrain normal
		// to compute proper launch arc
		if (_airVelocity.Y >= -10f && _terrainManager != null)
		{
			Vector2 terrainNormal = _terrainManager.GetTerrainNormalAt(GlobalPosition.X);
			_airVelocity = MomentumPhysics.ComputeLaunchVelocity(MomentumSpeed, terrainNormal);
			_launchNormal = terrainNormal;
		}

		// Exit tuck on launch
		if (SkiNode != null) SkiNode.IsTucking = false;
		_tuckInputHeld = false;
	}

	private void EnterFlipping()
	{
		if (CurrentMoveState != MoveState.Airborne)
			return;

		CurrentMoveState = MoveState.Flipping;
		_flipRotation = 0f;
		_flipCompleted = false;

		float flipMod = CurrentVehicle?.FlipSpeedModifier ?? 1.0f;
		_flipAngularVelocity = MomentumPhysics.ComputeFlipAngularVelocity(MomentumSpeed, flipMod);

		// Air-jump: boost upward when initiating flip
		_airVelocity.Y = Mathf.Min(_airVelocity.Y, PhysicsConstants.FlipLaunchImpulse);

		GD.Print($"[PlayerController] Flip initiated — angular vel: {_flipAngularVelocity:F1} rad/s, air impulse: {_airVelocity.Y:F0}");
	}

	private void OnLanding()
	{
		CurrentMoveState = MoveState.Grounded;

		// Recover momentum speed from horizontal air velocity
		MomentumSpeed = Mathf.Abs(_airVelocity.X);
		_airVelocity = Vector2.Zero;

		// Reset sprite rotation
		if (_playerSprite != null)
			_playerSprite.Rotation = 0f;
	}

	private void OnFlipLanding()
	{
		// Check landing angle
		bool landingSafe = MomentumPhysics.IsLandingSafe(_flipRotation);

		if (_flipCompleted && landingSafe)
		{
			// Successful flip — momentum boost + drag reduction window
			MomentumSpeed = Mathf.Abs(_airVelocity.X) * PhysicsConstants.FlipSuccessSpeedBoost;
			_flipBonusTimer = PhysicsConstants.FlipSuccessDragWindowDuration;
			EmitSignal(SignalName.FlipCompleted);
			GD.Print($"[PlayerController] Flip success! Speed boosted to {MomentumSpeed:F0}");
		}
		else
		{
			// Failed flip — crash
			EmitSignal(SignalName.FlipFailed);
			OnCrash(_flipCompleted ? "Bad landing angle" : "Incomplete rotation");
			return;
		}

		// Reset flip state
		_flipRotation = 0f;
		_flipAngularVelocity = 0f;
		_flipCompleted = false;
		_airVelocity = Vector2.Zero;

		if (_playerSprite != null)
			_playerSprite.Rotation = 0f;

		CurrentMoveState = MoveState.Grounded;
	}

	// ── Tuck Management ─────────────────────────────────────────────

	private void UpdateTuckState()
	{
		// Tuck only available for skis while grounded
		bool canTuck = CurrentVehicleType == VehicleType.Ski
					   && (CurrentMoveState == MoveState.Grounded || CurrentMoveState == MoveState.Tucking)
					   && _tuckInputHeld;

		if (canTuck && CurrentMoveState == MoveState.Grounded)
		{
			CurrentMoveState = MoveState.Tucking;
			if (SkiNode != null) SkiNode.IsTucking = true;
		}
		else if (!canTuck && CurrentMoveState == MoveState.Tucking)
		{
			CurrentMoveState = MoveState.Grounded;
			if (SkiNode != null) SkiNode.IsTucking = false;
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
			// Exit tuck when switching to bike
			if (SkiNode != null) SkiNode.IsTucking = false;
			_tuckInputHeld = false;
			if (CurrentMoveState == MoveState.Tucking)
				CurrentMoveState = MoveState.Grounded;
		}

		CurrentVehicle?.OnActivated();

		_canSwap = false;
		_swapTimer = PhysicsConstants.SwapCooldown;

		EmitSignal(SignalName.VehicleSwapped, (int)CurrentVehicleType);

		// Perfect swap detection
		bool isPerfect =
			(CurrentVehicleType == VehicleType.Ski && CurrentTerrain == TerrainType.Snow) ||
			(CurrentVehicleType == VehicleType.Bike && CurrentTerrain == TerrainType.Dirt);

		if (isPerfect)
		{
			EmitSignal(SignalName.PerfectSwap);
			GD.Print("[PlayerController] Perfect swap!");
		}
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

		_canSwap = true;
		_swapTimer = 0f;
		_coyoteTimer = 0f;
		_wasOnFloor = false;

		_airVelocity = Vector2.Zero;
		_launchNormal = Vector2.Up;

		_flipRotation = 0f;
		_flipAngularVelocity = 0f;
		_flipCompleted = false;
		_flipBonusTimer = 0f;

		_tuckInputHeld = false;
		_brakeInputHeld = false;
		if (SkiNode != null) SkiNode.IsTucking = false;

		if (_playerSprite != null) _playerSprite.Rotation = 0f;

		CurrentTerrain = TerrainType.Snow;

		GD.Print("[PlayerController] Reset for new run");
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
		if (_playerSprite != null) _playerSprite.Rotation = 0f;
		_flipRotation = 0f;
		_flipAngularVelocity = 0f;
		_flipCompleted = false;
		if (SkiNode != null) SkiNode.IsTucking = false;
		_tuckInputHeld = false;
		_brakeInputHeld = false;

		CurrentMoveState = MoveState.Grounded;
		EmitSignal(SignalName.PlayerCrashed);
		GD.Print($"[PlayerController] Crashed — {reason}");
	}
}
