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
	private int _targetFlipCount;
	private int _flipCount; // Number of full 360° rotations completed

	// Post-flip bonus window
	private float _flipBonusTimer;

	// Tuck state
	private bool _tuckInputHeld;

	// Terrain-hugging: collision shape half-height for snap offset
	private float _collisionHalfHeight = 32f;

	// Slope-following visual rotation
	private float _visualRotation;

	// Smoothed floor normal to prevent jitter at chunk boundaries
	private Vector2 _smoothedFloorNormal = Vector2.Up;


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

		// ── Flip input (ground or airborne) ────────────────────────
		if (@event.IsActionPressed("jump"))
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
			// If already flipping, queue another flip
			else if (CurrentMoveState == MoveState.Flipping)
			{
				_targetFlipCount++;
				GD.Print($"[PlayerController] Queued flip #{_targetFlipCount}");
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
		UpdateDebugState();
	}

	// ── Grounded Physics ────────────────────────────────────────────

	private void ProcessGrounded(float dt)
	{
		bool isTucking = CurrentMoveState == MoveState.Tucking;

		// ── Compute slope from floor normal or terrain query ────────
		// Smooth the floor normal to eliminate jitter at chunk boundaries
		Vector2 rawNormal;

		if (IsOnFloor())
		{
			rawNormal = GetFloorNormal();
			_coyoteTimer = PhysicsConstants.CoyoteTime;
		}
		else if (_terrainManager != null)
		{
			rawNormal = _terrainManager.GetTerrainNormalAt(GlobalPosition.X);
		}
		else
		{
			rawNormal = Vector2.Up;
		}

		// Exponential smoothing — converges fast (20/s) but prevents single-frame spikes
		_smoothedFloorNormal = _smoothedFloorNormal.Lerp(rawNormal, 1f - Mathf.Exp(-20f * dt)).Normalized();
		Vector2 floorNormal = _smoothedFloorNormal;
		float slopeAngleRad = MomentumPhysics.ComputeSignedSlopeAngle(floorNormal);

		DebugSlopeAngleDeg = Mathf.RadToDeg(slopeAngleRad);

		// ── Gather modifiers ────────────────────────────────────────
		float terrainFriction = MomentumPhysics.GetTerrainFriction(CurrentTerrain);
		float terrainDragMod = MomentumPhysics.GetTerrainDragModifier(CurrentTerrain);

		float vehicleDragMod = CurrentVehicle.DragModifier;
		float vehicleFrictionMod = CurrentVehicle.GetTerrainFrictionModifier(CurrentTerrain);
		float vehicleDragTerrainMod = CurrentVehicle.GetTerrainDragModifier(CurrentTerrain);
		float vehicleTerrainBonus = CurrentVehicle.GetTerrainBonus(CurrentTerrain);

		// ── Compute effective coefficients ──────────────────────────
		float effectiveDrag = MomentumPhysics.GetEffectiveDragCoefficient(
			isTucking, terrainDragMod * vehicleDragTerrainMod, vehicleDragMod);

		float effectiveRollingResistance = MomentumPhysics.GetEffectiveRollingResistance(
			isTucking, terrainFriction, vehicleFrictionMod)
			* CurrentVehicle.RollingResistanceModifier;

		// Apply post-flip bonus drag reduction
		if (_flipBonusTimer > 0f)
			effectiveDrag *= PhysicsConstants.FlipSuccessDragMultiplier;

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

		// ── Tuck downforce (grounded) ──────────────────────────────
		// When tucking on ground, apply downward velocity component to
		// counteract small upward bounces from terrain undulations.
		if (isTucking)
		{
			Velocity = new Vector2(Velocity.X,
				Mathf.Max(Velocity.Y, PhysicsConstants.TuckGroundedDownforce * dt));
		}

		// ── Centripetal launch check ────────────────────────────────
		// Before MoveAndSlide, check if the player should detach from the
		// terrain at a convex crest (like Tiny Wings / Sonic).
		// Tuck raises the launch threshold — prevents premature lift.
		if (_terrainManager != null)
		{
			float curvature = ComputeCurvatureAtPlayer();
			float gravMult = CurrentVehicle?.GravityMultiplier ?? 1.0f;

			if (MomentumPhysics.ShouldLaunchFromSurface(MomentumSpeed, curvature, gravMult, isTucking))
			{
				// TransitionToAirborne handles disabling floor snap
				TransitionToAirborne();
				MoveAndSlide();
				return;
			}
		}

		// ── Slope-following visual rotation ─────────────────────────
		float targetAngle = Mathf.Atan2(tangent.Y, tangent.X); // angle of surface tangent
		_visualRotation = Mathf.Lerp(_visualRotation, targetAngle, 1f - Mathf.Exp(-12f * dt));
		if (_playerSprite != null)
			_playerSprite.Rotation = _visualRotation;

		MoveAndSlide();

		// ── Post-move terrain hugging ───────────────────────────────
		// If MoveAndSlide lost floor contact (small bump, polygon edge),
		// snap the player back to the terrain surface if they're close.
		// Tuck increases snap distance for better path adherence.
		if (!IsOnFloor() && _terrainManager != null)
		{
			float terrainY = _terrainManager.GetTerrainHeight(GlobalPosition.X);
			float targetY = terrainY - _collisionHalfHeight;
			float distToSurface = Mathf.Abs(Position.Y - targetY);
			float snapDist = MomentumPhysics.GetEffectiveSnapDistance(isTucking);

			if (distToSurface < snapDist)
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
		bool isAerialTuck = CurrentMoveState == MoveState.AirborneTucking;

		// ── Integrate airborne trajectory ───────────────────────────
		if (isAerialTuck)
		{
			// Aerial tuck: boosted gravity + dive acceleration + upward velocity clamp
			_airVelocity = MomentumPhysics.IntegrateAirborneTucking(_airVelocity, dt, gravMult);
		}
		else
		{
			// Normal airborne: standard gravity + light air drag
			_airVelocity = MomentumPhysics.IntegrateAirborne(_airVelocity, dt, gravMult);
		}

		Velocity = _airVelocity;

		// ── Velocity-direction visual rotation ──────────────────────
		float targetAngle = Mathf.Atan2(_airVelocity.Y, _airVelocity.X);
		_visualRotation = Mathf.Lerp(_visualRotation, targetAngle, 1f - Mathf.Exp(-8f * dt));
		if (_playerSprite != null)
			_playerSprite.Rotation = _visualRotation;

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

		// ── Airborne trajectory with reduced gravity during flip ────
		// Apply flip gravity multiplier for floatier, more controllable flips
		float flipGravMult = gravMult * PhysicsConstants.FlipGravityMultiplier;
		_airVelocity = MomentumPhysics.IntegrateAirborne(_airVelocity, dt, flipGravMult);
		Velocity = _airVelocity;

		// ── Integrate rotation toward target flip count ─────────────
		float maxRotation = _targetFlipCount * Mathf.Tau;

		if (_flipRotation < maxRotation)
		{
			_flipRotation = MomentumPhysics.IntegrateFlipRotation(_flipRotation, _flipAngularVelocity, dt);
			// Clamp to target to prevent overshoot
			if (_flipRotation >= maxRotation)
				_flipRotation = maxRotation;
		}

		// Apply visual rotation
		if (_playerSprite != null)
			_playerSprite.Rotation = _flipRotation;

		// Track number of full 360° rotations completed
		int previousFlipCount = _flipCount;
		_flipCount = (int)(Mathf.Abs(_flipRotation) / Mathf.Tau);

		// Check if we just completed a new flip
		if (_flipCount > previousFlipCount)
		{
			_flipCompleted = true;
			GD.Print($"[PlayerController] Flip #{_flipCount} completed!");
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
		// If tuck is held, transition to aerial tuck (dive) instead of normal airborne
		bool wasTucking = _tuckInputHeld;
		CurrentMoveState = wasTucking ? MoveState.AirborneTucking : MoveState.Airborne;

		// Disable floor snap while airborne to prevent premature re-attachment to terrain
		FloorSnapLength = 0f;

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

		// Update visual tuck state
		SetSkiTucking(wasTucking);

		// Gap clearance is no longer predicted — player flies the trajectory
		// and dies naturally from FallDeathBelowTerrain if they miss.
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

		GD.Print($"[PlayerController] Flip initiated — angular vel: {_flipAngularVelocity:F1} rad/s, air impulse: {_airVelocity.Y:F0}");
	}

	private void OnLanding()
	{
		// Re-enable floor snap now that we're grounded
		FloorSnapLength = 48f;

		// If landing while aerial-tucking and tuck is still held, go straight to grounded tuck
		bool landIntoTuck = _tuckInputHeld;
		CurrentMoveState = landIntoTuck ? MoveState.Tucking : MoveState.Grounded;

		// Project full air velocity onto slope tangent to preserve energy from steep dives
		Vector2 landNormal = IsOnFloor() ? GetFloorNormal() : Vector2.Up;
		MomentumSpeed = MomentumPhysics.ProjectLandingSpeed(_airVelocity, landNormal);
		_airVelocity = Vector2.Zero;

		// Update visual tuck state
		SetSkiTucking(landIntoTuck);

		// _visualRotation will be smoothly updated by ProcessGrounded next frame
	}

	private void OnFlipLanding()
	{
		// Check landing angle - made more forgiving (90 degrees instead of 35)
		float normalized = _flipRotation % Mathf.Tau;
		if (normalized < 0f) normalized += Mathf.Tau;
		float toleranceRad = Mathf.DegToRad(90f); // Much more forgiving!
		bool landingSafe = normalized <= toleranceRad || normalized >= (Mathf.Tau - toleranceRad);

		if (_flipCount > 0 && landingSafe)
		{
			// Successful flip — project speed onto slope, apply boost, then bounce
			Vector2 landNormal = IsOnFloor() ? GetFloorNormal() : Vector2.Up;
			float projectedSpeed = MomentumPhysics.ProjectLandingSpeed(_airVelocity, landNormal);
			float boostedSpeed = projectedSpeed * PhysicsConstants.FlipSuccessSpeedBoost;
			MomentumSpeed = boostedSpeed;
			_flipBonusTimer = PhysicsConstants.FlipSuccessDragWindowDuration;

			// Award points: 100 per flip
			int points = _flipCount * 100;
			EmitSignal(SignalName.FlipCompleted);
			EmitSignal(SignalName.FlipPointsScored, points, _flipCount);
			GD.Print($"[PlayerController] Flip bounce! {_flipCount} flip(s), {points} points! Speed: {boostedSpeed:F0}");

			// Reset flip state
			_flipRotation = 0f;
			_flipAngularVelocity = 0f;
			_flipCompleted = false;
			_targetFlipCount = 0;
			_flipCount = 0;

			// Bounce: stay airborne with an upward impulse instead of snapping to ground
			_airVelocity = new Vector2(boostedSpeed, -PhysicsConstants.FlipBounceImpulse);
			CurrentMoveState = MoveState.Airborne;
			FloorSnapLength = 0f;

			// Don't snap visual rotation — ProcessGrounded will lerp it on the bounce landing
		}
		else
		{
			// Failed flip — crash
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
		_coyoteTimer = 0f;
		_wasOnFloor = false;

		_airVelocity = Vector2.Zero;
		_launchNormal = Vector2.Up;

		_flipRotation = 0f;
		_flipAngularVelocity = 0f;
		_flipCompleted = false;
		_targetFlipCount = 0;
		_flipCount = 0;
		_flipBonusTimer = 0f;

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
