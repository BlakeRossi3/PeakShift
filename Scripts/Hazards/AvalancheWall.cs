using Godot;

namespace PeakShift.Hazards;

/// <summary>
/// Persistent avalanche wall that chases the player from behind.
/// Moves forward at an accelerating speed — if the player slows down
/// or stalls, the wall catches them and ends the run.
///
/// Visuals: solid white snow mass behind the wall front, with a
/// semi-transparent fog gradient at the leading edge and snow
/// particles tumbling along the terrain slope.
/// </summary>
public partial class AvalancheWall : Node2D
{
    [Signal]
    public delegate void AvalancheCaughtPlayerEventHandler();

    // ── Tuning ──────────────────────────────────────────────────

    /// <summary>How far behind the player the wall starts (px).</summary>
    [Export] public float InitialDistance { get; set; } = 800f;

    /// <summary>Starting speed of the avalanche wall (px/s).</summary>
    [Export] public float StartSpeed { get; set; } = 280f;

    /// <summary>Speed acceleration (px/s²) — wall gets faster over time.</summary>
    [Export] public float SpeedAcceleration { get; set; } = 2f;

    /// <summary>Maximum wall speed cap (px/s).</summary>
    [Export] public float MaxSpeed { get; set; } = 1400f;

    /// <summary>Width of the fog/gradient leading edge (px).</summary>
    [Export] public float FogWidth { get; set; } = 200f;

    /// <summary>How far back behind WallX the solid mass extends (px).</summary>
    [Export] public float MassTrailLength { get; set; } = 3000f;

    /// <summary>How high above the terrain the snow mass rises (px).</summary>
    [Export] public float SnowHeightAboveTerrain { get; set; } = 600f;

    // ── State ───────────────────────────────────────────────────

    /// <summary>Current world X position of the avalanche front edge.</summary>
    public float WallX { get; private set; }

    /// <summary>Current wall speed (px/s).</summary>
    public float WallSpeed { get; private set; }

    private bool _active;
    private float _runTime;

    // ── References ──────────────────────────────────────────────

    public PlayerController PlayerRef { get; set; }
    public TerrainManager TerrainRef { get; set; }

    private CpuParticles2D _snowParticles;

    // ── Lifecycle ───────────────────────────────────────────────

    public override void _Ready()
    {
        // Create snow particles at the leading edge
        _snowParticles = new CpuParticles2D
        {
            Emitting = false,
            Amount = 60,
            Lifetime = 1.5f,
            SpeedScale = 1.2f,
            Explosiveness = 0f,
            Direction = new Vector2(1f, 0.5f),
            Spread = 30f,
            InitialVelocityMin = 80f,
            InitialVelocityMax = 200f,
            Gravity = new Vector2(0f, 400f),
            ScaleAmountMin = 2f,
            ScaleAmountMax = 6f,
            Color = new Color(1f, 1f, 1f, 0.7f),
            EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle,
            EmissionRectExtents = new Vector2(10f, 300f),
        };
        AddChild(_snowParticles);

        // Z-index: render behind the player but in front of terrain fill
        ZIndex = -1;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_active || PlayerRef == null) return;

        float dt = (float)delta;
        _runTime += dt;

        // Accelerate the wall
        WallSpeed = Mathf.Min(WallSpeed + SpeedAcceleration * dt, MaxSpeed);

        // Advance the wall
        WallX += WallSpeed * dt;

        // Check if the wall caught the player
        if (PlayerRef.GlobalPosition.X <= WallX)
        {
            EmitSignal(SignalName.AvalancheCaughtPlayer);
            _active = false;
            return;
        }

        // Update particle position to the leading edge
        if (TerrainRef != null)
        {
            float terrainY = TerrainRef.GetTerrainHeight(WallX);
            _snowParticles.GlobalPosition = new Vector2(WallX, terrainY - SnowHeightAboveTerrain * 0.5f);

            // Angle particles along the terrain slope
            Vector2 normal = TerrainRef.GetTerrainNormalAt(WallX);
            Vector2 tangent = new Vector2(normal.Y, -normal.X);
            if (tangent.X < 0f) tangent = -tangent;
            _snowParticles.Direction = tangent + new Vector2(0f, 0.3f);
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_active || TerrainRef == null) return;

        // ── Sample terrain heights along the leading region ─────
        const int sampleCount = 20;
        float sampleStart = WallX - MassTrailLength;
        float sampleEnd = WallX + FogWidth;
        float sampleStep = (sampleEnd - sampleStart) / (sampleCount - 1);

        // Deep below all terrain (bottom of screen + buffer)
        float bottomY = 10000f;

        // ── Draw solid snow mass (behind WallX) ────────────────
        // Polygon: from far left, follows terrain surface + height above,
        // down to bottom, back to start
        var massPoints = new Vector2[sampleCount + 2];
        int massIdx = 0;

        // Top edge following terrain (from left to WallX)
        int massSamples = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            float wx = sampleStart + i * sampleStep;
            if (wx > WallX) break;

            float terrainY = TerrainRef.GetTerrainHeight(wx);
            float topY = terrainY - SnowHeightAboveTerrain;
            // Convert to local coordinates
            massPoints[massIdx++] = ToLocal(new Vector2(wx, topY));
            massSamples++;
        }

        if (massSamples >= 2)
        {
            // Close the polygon: go down to bottom, then back to start
            var closedMass = new Vector2[massSamples + 2];
            for (int i = 0; i < massSamples; i++)
                closedMass[i] = massPoints[i];
            closedMass[massSamples] = ToLocal(new Vector2(WallX, bottomY));
            closedMass[massSamples + 1] = ToLocal(new Vector2(sampleStart, bottomY));

            DrawPolygon(closedMass, new Color[] { new Color(0.92f, 0.94f, 0.98f, 1f) });
        }

        // ── Draw fog gradient (WallX to WallX + FogWidth) ─────
        // Multiple semi-transparent strips fading out
        const int fogStrips = 8;
        float stripWidth = FogWidth / fogStrips;

        for (int s = 0; s < fogStrips; s++)
        {
            float stripStart = WallX + s * stripWidth;
            float stripEnd = stripStart + stripWidth;
            float alpha = Mathf.Lerp(0.7f, 0f, (float)s / fogStrips);

            // Sample terrain at strip edges
            float y1 = TerrainRef.GetTerrainHeight(stripStart);
            float y2 = TerrainRef.GetTerrainHeight(stripEnd);
            float top1 = y1 - SnowHeightAboveTerrain;
            float top2 = y2 - SnowHeightAboveTerrain;

            var stripPoly = new Vector2[]
            {
                ToLocal(new Vector2(stripStart, top1)),
                ToLocal(new Vector2(stripEnd, top2)),
                ToLocal(new Vector2(stripEnd, bottomY)),
                ToLocal(new Vector2(stripStart, bottomY))
            };

            var fogColor = new Color(0.95f, 0.96f, 1f, alpha);
            DrawPolygon(stripPoly, new Color[] { fogColor });
        }
    }

    // ── Public API ──────────────────────────────────────────────

    public void Activate()
    {
        if (PlayerRef == null) return;

        WallX = PlayerRef.GlobalPosition.X - InitialDistance;
        WallSpeed = StartSpeed;
        _runTime = 0f;
        _active = true;

        if (_snowParticles != null)
            _snowParticles.Emitting = true;

        QueueRedraw();
    }

    public void Deactivate()
    {
        _active = false;
        if (_snowParticles != null)
            _snowParticles.Emitting = false;
        QueueRedraw();
    }

    public void Reset()
    {
        Deactivate();
        Activate();
    }
}
