using System.Collections.Generic;
using Godot;
using PeakShift.Core;

namespace PeakShift;

/// <summary>
/// Run-based procedural terrain generator. Each "run" is a repeating cycle:
///
///   Descent (long downhill) → Ramp (smooth upward curve) → Gap (airborne)
///
/// The descent uses a cosine S-curve (flat → steep → flat at bottom).
/// The ramp uses a cosine quarter-curve (flat at bottom → steep upward at lip).
/// This guarantees every gap has a proper ski-jump ramp and the game always
/// feels downhill. Terrain type changes between runs.
/// </summary>
public partial class TerrainManager : Node2D
{
    // ── Signals ──────────────────────────────────────────────────

    [Signal]
    public delegate void TerrainChangedEventHandler(int newTerrainType);

    [Signal]
    public delegate void ChunkEnteredEventHandler();

    // ── Exports ──────────────────────────────────────────────────

    [Export]
    public int ChunksAhead { get; set; } = 10;

    [Export]
    public float DespawnDistance { get; set; } = 2000f;

    /// <summary>Reference to the player node, set by GameManager.</summary>
    public CharacterBody2D PlayerNode { get; set; }

    // ── Constants ────────────────────────────────────────────────

    private const float MaxChunkWidth = 512f;
    private const float BaseGroundY = 200f;
    private const float PointSpacing = 32f;
    private const float SpawnX = -256f;

    // Intro run — long steep descent (5-10s) flowing into first ramp launch
    private const float IntroDescentLength = 5000f;
    private const float IntroDescentDrop = 8000f;
    private const float IntroRampLength = 500f;
    private const float IntroRampRise = 350f;
    private const float IntroGapWidth = 250f;

    // Extra downhill gradient added per pixel of descent length
    private const float DownhillGradient = 0.14f;

    // Gap sizing — base range before steepness scaling
    private const float MinGapBase = 120f;
    private const float MaxGapBase = 280f;

    // Steepness-to-gap multiplier: gap = base + steepness * this
    private const float GapSteepnessScale = 2000f;

    // Fill polygon extends this far below the deepest surface point in each chunk
    private const float FillDepthBelowSurface = 1000f;

    // ── Run definition ──────────────────────────────────────────

    private class TerrainRun
    {
        public float DescentStartX;
        public float DescentStartY;
        public float DescentLength;
        public float DescentDrop;   // total Y increase (going down on screen)
        public float RampLength;
        public float RampRise;      // Y decrease (going up on screen)
        public float GapWidth;
        public TerrainType Type;

        // Computed positions
        public float RampStartX => DescentStartX + DescentLength;
        public float RampStartY => DescentStartY + DescentDrop;
        public float RampEndY => RampStartY - RampRise;
        public float GapStartX => RampStartX + RampLength;
        public float GapEndX => GapStartX + GapWidth;
    }

    // ── State ────────────────────────────────────────────────────

    public List<ChunkInstance> ActiveChunks { get; } = new();

    private float _nextSpawnX;
    private readonly RandomNumberGenerator _rng = new();
    private BiomeManager _biomeManager;

    // Run generation
    private readonly List<TerrainRun> _runs = new();
    private float _nextRunStartX;
    private float _nextRunStartY;
    private int _runCount;
    private int _currentSpawnRunIndex;

    // Terrain type sections (multiple runs of same type)
    private int _runsUntilTypeChange;
    private TerrainType _currentSectionType = TerrainType.Snow;

    // Chunk entry tracking (separate from generation)
    private TerrainType _lastEnteredType = TerrainType.Snow;

    // ── Inner types ──────────────────────────────────────────────

    public class ChunkInstance
    {
        public TerrainType Type { get; init; }
        public float WorldX { get; init; }
        public float Width { get; init; }
        public Node2D SceneNode { get; init; }
        public bool IsGap { get; init; }
    }

    // ── Lifecycle ────────────────────────────────────────────────

    public override void _Ready()
    {
        _rng.Randomize();
        _nextRunStartX = SpawnX;
        _nextRunStartY = BaseGroundY;
        _nextSpawnX = SpawnX;
        _currentSpawnRunIndex = 0;
        _runsUntilTypeChange = 0;
        _biomeManager = GetNodeOrNull<BiomeManager>("../BiomeManager");

        EnsureRunsTo(_nextSpawnX + MaxChunkWidth * (ChunksAhead + 4));
        for (int i = 0; i < ChunksAhead; i++)
            SpawnNextChunk();

        GD.Print("[TerrainManager] Initialized with run-based terrain");
    }

    public override void _PhysicsProcess(double delta)
    {
        float playerX = PlayerNode?.GlobalPosition.X ?? 0f;

        RecycleChunks(playerX);

        while (ActiveChunks.Count < ChunksAhead)
            SpawnNextChunk();

        CheckChunkEntry(playerX);
    }

    // ── Run generation ──────────────────────────────────────────

    private void EnsureRunsTo(float worldX)
    {
        float target = worldX + MaxChunkWidth * (ChunksAhead + 4);
        while (_runs.Count == 0 || _runs[^1].GapEndX < target)
            GenerateNextRun();
    }

    private void GenerateNextRun()
    {
        bool isFirst = _runCount == 0;

        // Terrain type — changes every few runs
        if (_runsUntilTypeChange <= 0)
        {
            _currentSectionType = SelectTerrainType();
            int min = _runCount < 3 ? 3 : 2;
            int max = _runCount < 3 ? 5 : 4;
            _runsUntilTypeChange = _rng.RandiRange(min, max);
        }
        _runsUntilTypeChange--;

        float descentLength, descentBaseDrop, rampLength, rampRise, gapWidth;

        if (isFirst)
        {
            // ── Intro run — long steep descent into first ramp ────
            descentLength = IntroDescentLength;
            descentBaseDrop = IntroDescentDrop;
            rampLength = IntroRampLength;
            rampRise = IntroRampRise;
            gapWidth = IntroGapWidth;
        }
        else
        {
            // ── Regular runs (significantly steeper) ─────────────
            descentLength = _rng.RandfRange(1200f, 3000f);
            descentBaseDrop = _rng.RandfRange(500f, 1100f);
            rampLength = _rng.RandfRange(200f, 550f);
            rampRise = _rng.RandfRange(100f, 400f);

            float rampSteepness = rampRise / rampLength;
            float gapBase = _rng.RandfRange(MinGapBase, MaxGapBase);
            gapWidth = gapBase + rampSteepness * GapSteepnessScale;
        }

        // Downhill gradient compounds steepness on longer descents
        float descentDrop = descentBaseDrop + descentLength * DownhillGradient;

        var run = new TerrainRun
        {
            DescentStartX = _nextRunStartX,
            DescentStartY = _nextRunStartY,
            DescentLength = descentLength,
            DescentDrop = descentDrop,
            RampLength = rampLength,
            RampRise = rampRise,
            GapWidth = gapWidth,
            Type = _currentSectionType,
        };
        _runs.Add(run);

        // Next run landing: slightly below the ramp lip
        float landingOffset = _rng.RandfRange(30f, 80f);
        _nextRunStartX = run.GapEndX;
        _nextRunStartY = run.RampEndY + landingOffset;

        _runCount++;
    }

    // ── Terrain height function ─────────────────────────────────

    /// <summary>
    /// Returns the terrain surface Y at the given world X position.
    /// Uses run-based segments: cosine S-curve descents and cosine ramps.
    /// Public so PlayerController can query slope geometry.
    /// </summary>
    public float GetTerrainHeight(float worldX)
    {
        EnsureRunsTo(worldX);

        for (int i = 0; i < _runs.Count; i++)
        {
            var run = _runs[i];

            if (worldX < run.DescentStartX) continue;
            if (worldX >= run.GapEndX) continue;

            // ── Descent: cosine S-curve (flat → steep → flat) ───
            if (worldX < run.RampStartX)
            {
                float t = (worldX - run.DescentStartX) / run.DescentLength;
                return run.DescentStartY + run.DescentDrop * (1f - Mathf.Cos(t * Mathf.Pi)) / 2f;
            }

            // ── Ramp: cosine quarter-curve (flat at bottom → steep upward at lip)
            if (worldX < run.GapStartX)
            {
                float t = (worldX - run.RampStartX) / run.RampLength;
                return run.RampStartY - run.RampRise * (1f - Mathf.Cos(t * Mathf.Pi / 2f));
            }

            // ── Gap: return lip height (used for fall detection) ─
            return run.RampEndY;
        }

        return BaseGroundY;
    }

    /// <summary>
    /// Returns the approximate terrain surface normal at a given world X position.
    /// Computed by sampling two nearby points and finding the perpendicular.
    /// Used by PlayerController for launch angle calculations.
    /// </summary>
    public Vector2 GetTerrainNormalAt(float worldX)
    {
        const float sampleDelta = 4f;
        float yLeft = GetTerrainHeight(worldX - sampleDelta);
        float yRight = GetTerrainHeight(worldX + sampleDelta);

        Vector2 tangent = new Vector2(sampleDelta * 2f, yRight - yLeft).Normalized();
        Vector2 normal = new Vector2(-tangent.Y, tangent.X);

        if (normal.Y > 0f)
            normal = -normal;

        return normal;
    }

    /// <summary>
    /// Returns the Y position of the terrain surface at the start of the world.
    /// Used by PlayerController to position the player correctly on reset.
    /// </summary>
    public float GetStartingSurfaceY()
    {
        return GetTerrainHeight(SpawnX);
    }

    // ── Chunk spawning ──────────────────────────────────────────

    public void SpawnNextChunk()
    {
        EnsureRunsTo(_nextSpawnX + MaxChunkWidth * ChunksAhead);

        // Advance to the current run
        while (_currentSpawnRunIndex < _runs.Count - 1
               && _nextSpawnX >= _runs[_currentSpawnRunIndex].GapEndX)
        {
            _currentSpawnRunIndex++;
        }

        var run = _runs[_currentSpawnRunIndex];

        // ── In a gap: create gap instance and skip past it ──────
        if (_nextSpawnX >= run.GapStartX && _nextSpawnX < run.GapEndX)
        {
            float gapRemaining = run.GapEndX - _nextSpawnX;
            ActiveChunks.Add(new ChunkInstance
            {
                Type = run.Type,
                WorldX = _nextSpawnX,
                Width = gapRemaining,
                SceneNode = null,
                IsGap = true
            });
            _nextSpawnX = run.GapEndX;
            return;
        }

        // ── Determine chunk width (shorter if gap is nearby) ────
        float distToGap = run.GapStartX - _nextSpawnX;
        float chunkWidth = Mathf.Min(MaxChunkWidth, distToGap);

        // Skip tiny slivers
        if (chunkWidth < 32f)
        {
            _nextSpawnX = run.GapStartX;
            return;
        }

        int resolution = Mathf.Max(3, (int)(chunkWidth / PointSpacing) + 1);
        CreateTerrainChunk(_nextSpawnX, chunkWidth, resolution, run.Type);
        _nextSpawnX += chunkWidth;
    }

    private void CreateTerrainChunk(float worldX, float width, int resolution, TerrainType terrainType)
    {
        var body = new StaticBody2D();
        body.Position = new Vector2(worldX, 0);
        body.ZIndex = -1;

        float spacing = width / (resolution - 1);

        // Generate surface height points
        var surfacePoints = new Vector2[resolution];
        float maxSurfaceY = float.MinValue;
        for (int i = 0; i < resolution; i++)
        {
            float localX = i * spacing;
            float wx = worldX + localX;
            float y = GetTerrainHeight(wx);
            surfacePoints[i] = new Vector2(localX, y);
            if (y > maxSurfaceY) maxSurfaceY = y;
        }

        // Dynamic fill depth — always well below the deepest surface point
        float fillDepth = maxSurfaceY + FillDepthBelowSurface;

        // Build closed polygon: surface points + deep fill
        var polyPoints = new Vector2[resolution + 2];
        for (int i = 0; i < resolution; i++)
            polyPoints[i] = surfacePoints[i];
        polyPoints[resolution] = new Vector2(width, fillDepth);
        polyPoints[resolution + 1] = new Vector2(0, fillDepth);

        // Visual fill
        var polygon = new Polygon2D
        {
            Polygon = polyPoints,
            Color = GetFillColor(terrainType)
        };
        body.AddChild(polygon);

        // Surface edge line
        var line = new Line2D();
        line.Points = surfacePoints;
        line.Width = 4f;
        line.DefaultColor = GetSurfaceColor(terrainType);
        body.AddChild(line);

        // Physics collision
        var collision = new CollisionPolygon2D { Polygon = polyPoints };
        body.AddChild(collision);

        AddChild(body);

        ActiveChunks.Add(new ChunkInstance
        {
            Type = terrainType,
            WorldX = worldX,
            Width = width,
            SceneNode = body,
            IsGap = false
        });
    }

    // ── Recycling ───────────────────────────────────────────────

    public void RecycleChunks(float playerX)
    {
        ActiveChunks.RemoveAll(chunk =>
        {
            bool shouldRemove = (playerX - (chunk.WorldX + chunk.Width)) > DespawnDistance;
            if (shouldRemove)
                chunk.SceneNode?.QueueFree();
            return shouldRemove;
        });

        // Trim old runs the player has long passed
        while (_runs.Count > 2 && _runs[0].GapEndX < playerX - DespawnDistance)
        {
            _runs.RemoveAt(0);
            _currentSpawnRunIndex = Mathf.Max(0, _currentSpawnRunIndex - 1);
        }
    }

    /// <summary>Clear all chunks and reset for a new run.</summary>
    public void Reset()
    {
        foreach (var chunk in ActiveChunks)
            chunk.SceneNode?.QueueFree();
        ActiveChunks.Clear();
        _runs.Clear();

        _nextRunStartX = SpawnX;
        _nextRunStartY = BaseGroundY;
        _nextSpawnX = SpawnX;
        _currentSpawnRunIndex = 0;
        _runCount = 0;
        _runsUntilTypeChange = 0;
        _lastEnteredType = TerrainType.Snow;

        EnsureRunsTo(_nextSpawnX + MaxChunkWidth * (ChunksAhead + 4));
        for (int i = 0; i < ChunksAhead; i++)
            SpawnNextChunk();
    }

    // ── Terrain type selection ──────────────────────────────────

    private TerrainType SelectTerrainType()
    {
        if (_biomeManager?.CurrentBiome == null)
            return _rng.Randf() > 0.5f ? TerrainType.Snow : TerrainType.Dirt;

        var ratios = _biomeManager.CurrentBiome.GetTerrainRatios();
        float roll = _rng.Randf();
        float cumulative = 0f;

        foreach (var (type, ratio) in ratios)
        {
            cumulative += ratio;
            if (roll <= cumulative)
                return type;
        }

        return TerrainType.Snow;
    }

    // ── Chunk entry detection ───────────────────────────────────

    private void CheckChunkEntry(float playerX)
    {
        foreach (var chunk in ActiveChunks)
        {
            float chunkEnd = chunk.WorldX + chunk.Width;
            if (playerX >= chunk.WorldX && playerX < chunkEnd)
            {
                if (chunk.Type != _lastEnteredType)
                {
                    _lastEnteredType = chunk.Type;
                    EmitSignal(SignalName.TerrainChanged, (int)chunk.Type);
                }
                break;
            }
        }
    }

    // ── Terrain colors ──────────────────────────────────────────

    private static Color GetFillColor(TerrainType type) => type switch
    {
        TerrainType.Snow  => new Color(0.90f, 0.93f, 0.98f),
        TerrainType.Dirt  => new Color(0.50f, 0.33f, 0.18f),
        TerrainType.Ice   => new Color(0.70f, 0.85f, 0.95f),
        _                 => new Color(0.80f, 0.80f, 0.85f)
    };

    private static Color GetSurfaceColor(TerrainType type) => type switch
    {
        TerrainType.Snow  => new Color(1.0f, 1.0f, 1.0f),
        TerrainType.Dirt  => new Color(0.65f, 0.45f, 0.25f),
        TerrainType.Ice   => new Color(0.85f, 0.95f, 1.0f),
        _                 => Colors.White
    };
}
