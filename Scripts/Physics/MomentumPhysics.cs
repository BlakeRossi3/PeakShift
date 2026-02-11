using Godot;

namespace PeakShift.Physics;

/// <summary>
/// Pure math physics engine for the momentum-driven downhill system.
/// Stateless — all methods are pure functions that take current state and return deltas.
/// No Godot node dependencies. Fully deterministic.
/// </summary>
public static class MomentumPhysics
{
    // ── Core Downhill/Uphill Acceleration ────────────────────────────

    /// <summary>
    /// Computes gravitational acceleration along the slope.
    /// Positive slopeAngle = downhill (player accelerates with full gravity).
    /// Negative slopeAngle = uphill (player decelerates with reduced gravity).
    ///
    /// Uphill gravity is scaled by UphillGravityScale so the player carries
    /// momentum over small hills instead of rapidly decelerating.
    /// </summary>
    /// <param name="slopeAngleRad">Signed slope angle in radians. Positive = downhill.</param>
    /// <returns>Acceleration component from gravity along slope (px/s^2).</returns>
    public static float GravitySlopeAcceleration(float slopeAngleRad)
    {
        float gravityScale = slopeAngleRad < 0f ? PhysicsConstants.UphillGravityScale : 1.0f;
        return PhysicsConstants.Gravity * Mathf.Sin(slopeAngleRad) * gravityScale;
    }

    /// <summary>
    /// Computes velocity-dependent aerodynamic drag deceleration.
    /// Formula: a_drag = -sign(v) * dragCoeff * v^2
    /// Always opposes motion direction.
    /// </summary>
    /// <param name="speed">Current scalar speed (px/s). Always positive.</param>
    /// <param name="dragCoefficient">Effective drag coefficient (after terrain/tuck modifiers).</param>
    /// <returns>Deceleration from drag (px/s^2, always negative or zero).</returns>
    public static float DragDeceleration(float speed, float dragCoefficient)
    {
        return -dragCoefficient * speed * speed;
    }

    /// <summary>
    /// Computes constant rolling resistance deceleration.
    /// Formula: a_roll = -rollingResistance * frictionCoeff
    /// </summary>
    /// <param name="rollingResistance">Base rolling resistance (px/s^2).</param>
    /// <param name="frictionCoefficient">Terrain friction multiplier.</param>
    /// <returns>Deceleration from rolling resistance (px/s^2, always negative).</returns>
    public static float RollingResistanceDeceleration(float rollingResistance, float frictionCoefficient)
    {
        return -rollingResistance * frictionCoefficient;
    }

    /// <summary>
    /// Computes the total ground acceleration for a single physics frame.
    ///
    /// a_total = g * sin(theta) - dragCoeff * v^2 - rollingResistance * friction + vehicleBonus
    ///
    /// This is the core momentum equation. On downhill slopes, gravity accelerates.
    /// On uphill slopes, gravity decelerates. Drag and friction always oppose motion.
    /// Vehicle terrain bonus adds/subtracts based on vehicle-terrain match.
    /// </summary>
    public static float ComputeGroundAcceleration(
        float speed,
        float slopeAngleRad,
        float dragCoefficient,
        float rollingResistance,
        float frictionCoefficient,
        float vehicleTerrainBonus)
    {
        float gravityComponent = GravitySlopeAcceleration(slopeAngleRad);
        float dragComponent = DragDeceleration(speed, dragCoefficient);
        float rollComponent = RollingResistanceDeceleration(rollingResistance, frictionCoefficient);

        return gravityComponent + dragComponent + rollComponent + vehicleTerrainBonus;
    }

    /// <summary>
    /// Applies acceleration to speed and clamps to terminal velocity.
    /// Returns the new speed (always >= 0).
    /// </summary>
    public static float IntegrateSpeed(float currentSpeed, float acceleration, float dt, float terminalVelocity)
    {
        float newSpeed = currentSpeed + acceleration * dt;
        return Mathf.Clamp(newSpeed, PhysicsConstants.MinimumSpeed, terminalVelocity);
    }

    // ── Jump / Airborne Physics ─────────────────────────────────────

    /// <summary>
    /// Computes launch velocity components from terrain ramp.
    ///
    /// The launch direction is the terrain surface tangent at the ramp edge.
    /// v_x = speed * cos(launchAngle)
    /// v_y = speed * sin(launchAngle)  (negative = upward in Godot)
    ///
    /// launchAngle is derived from the terrain normal: the angle between
    /// the surface tangent and horizontal.
    /// </summary>
    /// <param name="speed">Scalar speed at moment of launch (px/s).</param>
    /// <param name="terrainNormal">The terrain surface normal at launch point.</param>
    /// <returns>Velocity vector (x, y) at launch. Y is negative for upward.</returns>
    public static Vector2 ComputeLaunchVelocity(float speed, Vector2 terrainNormal)
    {
        // Surface tangent is perpendicular to normal, pointing in travel direction (right)
        // Normal points "up" from surface. Tangent = rotate normal 90 degrees clockwise.
        Vector2 tangent = new Vector2(terrainNormal.Y, -terrainNormal.X);

        // Ensure tangent points rightward (travel direction)
        if (tangent.X < 0f)
            tangent = -tangent;

        // Scale by speed
        return tangent * speed * PhysicsConstants.LaunchAngleScale;
    }

    /// <summary>
    /// Computes airborne position delta for a single frame.
    ///
    /// x(t) = v_x * dt
    /// y(t) = v_y * dt + 0.5 * g * dt^2
    ///
    /// Also applies small air drag to horizontal velocity.
    /// </summary>
    /// <param name="velocity">Current velocity vector (px/s).</param>
    /// <param name="dt">Delta time (seconds).</param>
    /// <param name="gravityMultiplier">Vehicle-specific gravity scaling.</param>
    /// <returns>Updated velocity after one frame of airborne physics.</returns>
    public static Vector2 IntegrateAirborne(Vector2 velocity, float dt, float gravityMultiplier)
    {
        // Apply gravity (positive Y = downward in Godot)
        float gravityAccel = PhysicsConstants.Gravity * gravityMultiplier;
        velocity.Y += gravityAccel * dt;

        // Apply air drag to horizontal component (small, preserves most momentum)
        float airDrag = PhysicsConstants.AirDragCoefficient * velocity.X * velocity.X;
        velocity.X -= airDrag * dt;

        // Ensure X never goes negative (player always moves forward)
        if (velocity.X < PhysicsConstants.MinimumSpeed)
            velocity.X = PhysicsConstants.MinimumSpeed;

        return velocity;
    }

    // ── Flip Physics ────────────────────────────────────────────────

    /// <summary>
    /// Computes angular velocity for a flip based on launch speed and vehicle modifier.
    ///
    /// omega = baseAngularVelocity * (speed / referenceSpeed) * vehicleMod
    /// Clamped to max angular velocity.
    /// </summary>
    /// <param name="speed">Speed at time of flip initiation (px/s).</param>
    /// <param name="vehicleFlipModifier">Vehicle-specific flip speed modifier.</param>
    /// <returns>Angular velocity in radians/second.</returns>
    public static float ComputeFlipAngularVelocity(float speed, float vehicleFlipModifier)
    {
        float speedRatio = speed / PhysicsConstants.FlipSpeedReference;
        float omega = PhysicsConstants.FlipBaseAngularVelocity * speedRatio * vehicleFlipModifier;
        return Mathf.Min(omega, PhysicsConstants.FlipMaxAngularVelocity);
    }

    /// <summary>
    /// Integrates angular rotation for one frame.
    /// Returns the new cumulative rotation angle (radians).
    /// </summary>
    public static float IntegrateFlipRotation(float currentAngle, float angularVelocity, float dt)
    {
        return currentAngle + angularVelocity * dt;
    }

    /// <summary>
    /// Checks if a flip has completed a full 360-degree rotation.
    /// </summary>
    public static bool IsFlipComplete(float totalRotation)
    {
        return Mathf.Abs(totalRotation) >= Mathf.Tau;
    }

    /// <summary>
    /// Checks if the landing angle is within tolerance for a safe landing.
    /// The sprite angle should be near 0 (upright) or a full multiple of 2*PI.
    /// </summary>
    /// <param name="spriteAngleRad">Current sprite rotation in radians.</param>
    /// <returns>True if landing is safe.</returns>
    public static bool IsLandingSafe(float spriteAngleRad)
    {
        // Normalize angle to [0, 2*PI)
        float normalized = spriteAngleRad % Mathf.Tau;
        if (normalized < 0f) normalized += Mathf.Tau;

        // Check if near 0 or near 2*PI (both are "upright")
        float toleranceRad = Mathf.DegToRad(PhysicsConstants.FlipLandingTolerance);
        return normalized <= toleranceRad || normalized >= (Mathf.Tau - toleranceRad);
    }

    // ── Tuck Modifiers ──────────────────────────────────────────────

    /// <summary>
    /// Returns the effective drag coefficient with tuck and terrain modifiers applied.
    /// </summary>
    public static float GetEffectiveDragCoefficient(
        bool isTucking,
        float terrainDragModifier,
        float vehicleDragModifier)
    {
        float drag = PhysicsConstants.BaseDragCoefficient * terrainDragModifier * vehicleDragModifier;

        if (isTucking)
            drag *= PhysicsConstants.TuckDragMultiplier;

        return drag;
    }

    /// <summary>
    /// Returns the effective rolling resistance with tuck and terrain modifiers applied.
    /// </summary>
    public static float GetEffectiveRollingResistance(
        bool isTucking,
        float terrainFrictionCoefficient,
        float vehicleFrictionModifier)
    {
        float resistance = PhysicsConstants.BaseRollingResistance
                           * terrainFrictionCoefficient
                           * vehicleFrictionModifier;

        if (isTucking)
            resistance *= PhysicsConstants.TuckRollingResistanceMultiplier;

        return resistance;
    }

    /// <summary>
    /// Returns the effective terminal velocity, accounting for tuck.
    /// </summary>
    public static float GetEffectiveTerminalVelocity(bool isTucking, float vehicleMaxSpeed)
    {
        float cap = Mathf.Min(vehicleMaxSpeed, PhysicsConstants.TerminalVelocity);
        if (isTucking)
            cap *= PhysicsConstants.TuckTerminalVelocityMultiplier;
        return cap;
    }

    // ── Slope Utilities ─────────────────────────────────────────────

    /// <summary>
    /// Computes the signed slope angle from a floor normal vector.
    /// Positive = downhill (surface slopes downward to the right).
    /// Negative = uphill (surface slopes upward to the right).
    /// Zero = flat.
    ///
    /// Uses: angle = atan2(normal.X, normal.Y)
    /// When normal points straight up: (0, -1) → angle = 0 (flat).
    /// When surface tilts right-upward (uphill ramp): normal.X &lt; 0 → negative angle.
    /// When surface tilts right-downward (downhill slope): normal.X &gt; 0 → positive angle.
    /// </summary>
    public static float ComputeSignedSlopeAngle(Vector2 floorNormal)
    {
        // In Godot 2D, floor normal for flat ground is (0, -1).
        // atan2(normal.X, -normal.Y) gives signed angle:
        //   positive when surface descends to the right (downhill)
        //   negative when surface ascends to the right (uphill)
        return Mathf.Atan2(floorNormal.X, -floorNormal.Y);
    }

    /// <summary>
    /// Returns terrain-specific friction coefficient.
    /// </summary>
    public static float GetTerrainFriction(TerrainType terrain)
    {
        return terrain switch
        {
            TerrainType.Snow => PhysicsConstants.FrictionSnow,
            TerrainType.Dirt => PhysicsConstants.FrictionDirt,
            TerrainType.Ice => PhysicsConstants.FrictionIce,
            _ => 1.0f
        };
    }

    /// <summary>
    /// Returns terrain-specific drag modifier.
    /// </summary>
    public static float GetTerrainDragModifier(TerrainType terrain)
    {
        return terrain switch
        {
            TerrainType.Snow => PhysicsConstants.DragModSnow,
            TerrainType.Dirt => PhysicsConstants.DragModDirt,
            TerrainType.Ice => PhysicsConstants.DragModIce,
            _ => 1.0f
        };
    }

    // ── Jump Clearance — Trajectory Prediction ─────────────────────

    /// <summary>
    /// Result of a trajectory simulation over a gap.
    /// </summary>
    public struct GapClearanceResult
    {
        /// <summary>True if the predicted trajectory clears the gap.</summary>
        public bool Clears;
        /// <summary>Predicted landing X position (world coordinates).</summary>
        public float LandingX;
        /// <summary>Predicted landing Y position (world coordinates).</summary>
        public float LandingY;
        /// <summary>Horizontal distance traveled during the jump.</summary>
        public float JumpDistance;
    }

    /// <summary>
    /// Simulates the player's airborne trajectory from the gap lip to predict
    /// whether they will clear the gap. Uses the same IntegrateAirborne physics
    /// as the actual flight to ensure prediction matches reality.
    ///
    /// The simulation runs forward in time, stepping the velocity through
    /// gravity and air drag. At each step, it checks if the player has
    /// descended to or below the landing zone Y level. If they reach the
    /// landing zone X before that, the gap is cleared.
    /// </summary>
    /// <param name="launchPos">World position at gap lip (start of gap).</param>
    /// <param name="launchVelocity">Velocity vector at moment of launch.</param>
    /// <param name="gravityMultiplier">Vehicle gravity multiplier.</param>
    /// <param name="gapEndX">World X of the far edge of the gap.</param>
    /// <param name="landingY">World Y of the landing zone surface.</param>
    /// <param name="clearanceRatio">Fraction of gap that must be crossed (0.0–1.0).</param>
    /// <param name="landingForgiveness">Extra px past gapEndX that count as landed.</param>
    /// <param name="verticalTolerance">Max px below landingY that still count.</param>
    /// <returns>GapClearanceResult with prediction details.</returns>
    public static GapClearanceResult PredictGapClearance(
        Vector2 launchPos,
        Vector2 launchVelocity,
        float gravityMultiplier,
        float gapStartX,
        float gapEndX,
        float landingY,
        float clearanceRatio,
        float landingForgiveness,
        float verticalTolerance)
    {
        float dt = PhysicsConstants.TrajectorySimDt;
        int steps = PhysicsConstants.TrajectorySimSteps;

        Vector2 pos = launchPos;
        Vector2 vel = launchVelocity;

        float gapWidth = gapEndX - gapStartX;
        // Minimum X the player must reach to survive
        float requiredX = gapStartX + gapWidth * clearanceRatio;

        for (int i = 0; i < steps; i++)
        {
            vel = IntegrateAirborne(vel, dt, gravityMultiplier);
            pos += vel * dt;

            // Player has descended to landing zone level or below
            if (pos.Y >= landingY - verticalTolerance)
            {
                bool cleared = pos.X >= requiredX;
                return new GapClearanceResult
                {
                    Clears = cleared,
                    LandingX = pos.X,
                    LandingY = pos.Y,
                    JumpDistance = pos.X - launchPos.X
                };
            }

            // Player has passed well beyond the gap — they cleared it
            if (pos.X > gapEndX + landingForgiveness)
            {
                return new GapClearanceResult
                {
                    Clears = true,
                    LandingX = pos.X,
                    LandingY = pos.Y,
                    JumpDistance = pos.X - launchPos.X
                };
            }
        }

        // Simulation ran out of steps — treat as failure (player floated too long)
        return new GapClearanceResult
        {
            Clears = false,
            LandingX = pos.X,
            LandingY = pos.Y,
            JumpDistance = pos.X - launchPos.X
        };
    }

    // ── Terrain Curvature & Centripetal Launch ──────────────────────

    /// <summary>
    /// Computes the signed curvature of the terrain at a point using
    /// three sampled heights (finite differences).
    ///
    /// κ = h''(x) / (1 + h'(x)²)^(3/2)
    ///
    /// Positive κ = convex crest (surface curves away from player, launch candidate).
    /// Negative κ = concave valley (surface curves toward player, stay grounded).
    /// </summary>
    /// <param name="heightLeft">Terrain Y at (x - delta).</param>
    /// <param name="heightCenter">Terrain Y at x.</param>
    /// <param name="heightRight">Terrain Y at (x + delta).</param>
    /// <param name="sampleDelta">Distance between sample points (px).</param>
    /// <returns>Signed curvature (1/px). Positive = convex.</returns>
    public static float ComputeTerrainCurvature(
        float heightLeft, float heightCenter, float heightRight, float sampleDelta)
    {
        float firstDeriv = (heightRight - heightLeft) / (2f * sampleDelta);
        float secondDeriv = (heightRight - 2f * heightCenter + heightLeft)
                            / (sampleDelta * sampleDelta);

        float denominator = Mathf.Pow(1f + firstDeriv * firstDeriv, 1.5f);
        return secondDeriv / denominator;
    }

    /// <summary>
    /// Determines whether the player should detach from the terrain surface
    /// based on centripetal force vs gravity (Tiny Wings / Sonic style).
    ///
    /// On a convex crest with curvature κ, the player's speed creates centripetal
    /// acceleration = v²κ. When this exceeds the gravity component pushing them
    /// into the surface, they launch naturally.
    ///
    /// Condition: v²κ > gravity * gravityMultiplier * launchScale
    /// </summary>
    /// <param name="speed">Current scalar speed (px/s).</param>
    /// <param name="curvature">Terrain curvature at player position (1/px).</param>
    /// <param name="gravityMultiplier">Vehicle gravity multiplier.</param>
    /// <returns>True if the player should launch from the surface.</returns>
    public static bool ShouldLaunchFromSurface(float speed, float curvature, float gravityMultiplier)
    {
        // Only launch on convex surfaces (positive curvature in our coordinate system)
        if (curvature <= 0f)
            return false;

        // Speed must exceed minimum launch threshold
        if (speed < PhysicsConstants.MinLaunchSpeed)
            return false;

        float centripetalAccel = speed * speed * curvature;
        float gravityThreshold = PhysicsConstants.Gravity * gravityMultiplier
                                 * PhysicsConstants.LaunchCentripetalScale;

        return centripetalAccel > gravityThreshold;
    }
}
