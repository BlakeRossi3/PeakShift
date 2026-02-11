namespace PeakShift.Physics;

/// <summary>
/// All tunable physics constants for the momentum-driven downhill system.
/// Organized by subsystem. All values are in pixels and seconds unless noted.
/// </summary>
public static class PhysicsConstants
{
    // ── Gravity ──────────────────────────────────────────────────────
    /// <summary>Gravitational acceleration (px/s^2). Tuned for 2D at ~60fps.</summary>
    public const float Gravity = 2200f;

    /// <summary>
    /// Gravity multiplier when traveling uphill (slope angle negative).
    /// Reduces deceleration on inclines so player carries momentum over small hills.
    /// 0.35 = only 35% of gravity opposes the player going uphill.
    /// </summary>
    public const float UphillGravityScale = 0.35f;

    // ── Drag & Resistance ────────────────────────────────────────────
    /// <summary>Base aerodynamic drag coefficient. Applied as: drag = coeff * v^2.</summary>
    public const float BaseDragCoefficient = 0.0004f;

    /// <summary>Rolling resistance (constant force opposing motion, px/s^2).</summary>
    public const float BaseRollingResistance = 8f;

    /// <summary>Air drag while airborne (much lower than ground drag).</summary>
    public const float AirDragCoefficient = 0.00008f;

    // ── Speed Limits ─────────────────────────────────────────────────
    /// <summary>Absolute minimum speed — player always moves forward at least this fast (px/s).</summary>
    public const float MinimumSpeed = 100f;

    /// <summary>Global terminal velocity cap (px/s).</summary>
    public const float TerminalVelocity = 1800f;

    /// <summary>Starting speed for new runs (px/s).</summary>
    public const float StartingSpeed = 350f;

    // ── Tuck Modifiers (Grounded) ─────────────────────────────────────
    /// <summary>Drag multiplier when tucking (0.0–1.0, lower = less drag).</summary>
    public const float TuckDragMultiplier = 0.35f;

    /// <summary>Rolling resistance multiplier when tucking.</summary>
    public const float TuckRollingResistanceMultiplier = 0.7f;

    /// <summary>Steering authority multiplier when tucking (reduced control).</summary>
    public const float TuckSteeringMultiplier = 0.5f;

    /// <summary>Acceleration bonus multiplier on downhill while tucking.</summary>
    public const float TuckDownhillAccelBonus = 1.15f;

    /// <summary>Terminal velocity multiplier when tucking (allows higher top speed).</summary>
    public const float TuckTerminalVelocityMultiplier = 1.25f;

    // ── Tuck Downforce (Grounded Path Adherence) ────────────────────
    /// <summary>
    /// Multiplier applied to the centripetal launch threshold when grounded-tucking.
    /// Higher = harder to accidentally launch. At 2.5, the player needs 2.5x the
    /// centripetal force to detach from the surface while tucking.
    /// This is the core "stay on path" mechanic — tuck prevents premature lift.
    /// </summary>
    public const float TuckLaunchThresholdMultiplier = 2.5f;

    /// <summary>
    /// Extra ground snap distance (px) when tucking. Adds to GroundSnapDistance
    /// so the player sticks through bigger bumps while tucked.
    /// </summary>
    public const float TuckExtraSnapDistance = 40f;

    /// <summary>
    /// Downward velocity applied each frame while grounded-tucking to counteract
    /// any upward bounce from terrain undulations (px/s).
    /// </summary>
    public const float TuckGroundedDownforce = 600f;

    // ── Tuck Aerial Dive ────────────────────────────────────────────
    /// <summary>
    /// Downward acceleration applied while tucking in air (px/s^2).
    /// Added on top of normal gravity. Makes the player dive toward the slope.
    /// </summary>
    public const float TuckAerialDiveAcceleration = 1800f;

    /// <summary>
    /// Gravity scale multiplier while aerial-tucking. Stacks with vehicle gravity.
    /// At 1.6, effective gravity is 60% stronger than normal while diving.
    /// </summary>
    public const float TuckAerialGravityMultiplier = 1.6f;

    /// <summary>
    /// Maximum upward (negative Y) velocity allowed while aerial-tucking (px/s).
    /// Clamps positive vertical velocity to prevent continued rise.
    /// Set to a small negative value so the player can still arc slightly upward
    /// but won't gain height. 0 = immediately kills all upward momentum.
    /// </summary>
    public const float TuckAerialMaxUpwardVelocity = -50f;

    /// <summary>
    /// Maximum downward velocity while aerial-tucking (px/s, positive = down).
    /// Prevents the dive from becoming an uncontrollable plummet.
    /// </summary>
    public const float TuckAerialMaxDownwardVelocity = 1600f;

    // ── Jump / Airborne ──────────────────────────────────────────────
    /// <summary>Minimum speed required to get any meaningful launch (px/s).</summary>
    public const float MinLaunchSpeed = 200f;

    /// <summary>Launch angle is derived from terrain normal. This scales the vertical component.</summary>
    public const float LaunchAngleScale = 1.0f;

    /// <summary>How far below the terrain surface (px) before fall death triggers.</summary>
    public const float FallDeathBelowTerrain = 800f;

    // ── Terrain Hugging & Centripetal Launch ─────────────────────────
    /// <summary>
    /// Max distance (px) the player can be from the terrain surface and still snap back.
    /// Keeps the player glued over small bumps and undulations.
    /// </summary>
    public const float GroundSnapDistance = 50f;

    /// <summary>Sampling delta (px) for computing terrain curvature via finite differences.</summary>
    public const float CurvatureSampleDelta = 16f;

    /// <summary>
    /// Multiplier on gravity for the centripetal launch threshold.
    /// Lower = player launches more easily from convex crests.
    /// At 0.7, the player detaches when centripetal force reaches 70% of gravity.
    /// </summary>
    public const float LaunchCentripetalScale = 0.7f;

    // ── Flip Mechanics ───────────────────────────────────────────────
    /// <summary>Base angular velocity for flips (radians/s). Scales with speed.</summary>
    public const float FlipBaseAngularVelocity = 8.0f;

    /// <summary>Speed at which flip angular velocity is at base rate (px/s).</summary>
    public const float FlipSpeedReference = 400f;

    /// <summary>Max angular velocity cap (rad/s) to prevent impossible spins.</summary>
    public const float FlipMaxAngularVelocity = 14.0f;

    /// <summary>Landing angle tolerance from upright (degrees). Outside = crash.</summary>
    public const float FlipLandingTolerance = 35f;

    /// <summary>Momentum boost after successful flip (multiplied to speed).</summary>
    public const float FlipSuccessSpeedBoost = 1.12f;

    /// <summary>Duration of drag reduction window after successful flip (seconds).</summary>
    public const float FlipSuccessDragWindowDuration = 2.0f;

    /// <summary>Drag multiplier during post-flip bonus window.</summary>
    public const float FlipSuccessDragMultiplier = 0.5f;

    /// <summary>Upward velocity impulse applied when initiating a flip (px/s). Acts like an air-jump.</summary>
    public const float FlipLaunchImpulse = -350f;

    // ── Jump Clearance ─────────────────────────────────────────────
    // These parameters govern the "must-clear-the-gap" momentum rule.
    // If the player's trajectory won't reach the landing zone, the run ends.

    /// <summary>
    /// Fraction of the gap the player must clear to survive (0.0–1.0).
    /// At 0.85, the player must land within the last 15% of the gap or beyond.
    /// Provides a small forgiveness buffer so brushing the far edge doesn't kill.
    /// </summary>
    public const float GapClearanceRatio = 0.85f;

    /// <summary>
    /// Extra horizontal distance (px) past the gap end that still counts as a valid landing.
    /// Prevents pixel-perfect frustration on close calls.
    /// </summary>
    public const float LandingForgivenessPx = 40f;

    /// <summary>
    /// Maximum vertical distance (px) below the landing zone surface that still counts
    /// as a successful landing. Handles slight terrain dips at the gap edge.
    /// </summary>
    public const float LandingVerticalTolerancePx = 60f;

    /// <summary>
    /// Number of simulation steps for trajectory prediction during gap crossing.
    /// Higher = more accurate but slightly more CPU. 60 ≈ 1 second of flight at 60fps.
    /// </summary>
    public const int TrajectorySimSteps = 120;

    /// <summary>
    /// Time step (seconds) per trajectory simulation step.
    /// Matches physics tick rate for accuracy.
    /// </summary>
    public const float TrajectorySimDt = 1f / 60f;

    // ── Terrain Friction Coefficients ────────────────────────────────
    // These multiply rolling resistance. Lower = less friction = faster.
    public const float FrictionSnow = 0.7f;
    public const float FrictionDirt = 1.0f;
    public const float FrictionIce = 0.3f;

    // ── Terrain Drag Modifiers ───────────────────────────────────────
    // These multiply aerodynamic drag coefficient on ground.
    public const float DragModSnow = 0.9f;
    public const float DragModDirt = 1.1f;
    public const float DragModIce = 0.6f;

    // ── Coyote Time & Buffers ────────────────────────────────────────
    public const float CoyoteTime = 0.1f;
    public const float JumpBufferTime = 0.12f;

    // ── Brake ──────────────────────────────────────────────────────────
    /// <summary>Drag multiplier when braking (bike only). Much higher drag to slow down.</summary>
    public const float BrakeDragMultiplier = 6.0f;

    /// <summary>Minimum speed while braking — can't fully stop.</summary>
    public const float BrakeMinSpeed = 40f;

    // ── Slope Thresholds ─────────────────────────────────────────────
    /// <summary>Minimum slope angle (degrees) to count as "downhill" for acceleration.</summary>
    public const float MinSlopeAngle = 2f;

    // ── Vehicle Swap ─────────────────────────────────────────────────
    public const float SwapCooldown = 0.8f;

    /// <summary>Speed penalty multiplier on swap to wrong terrain (e.g. bike on snow).</summary>
    public const float BadSwapSpeedPenalty = 0.9f;
}
