using System.Collections.Generic;
using Godot;
using PeakShift.Core;

namespace PeakShift;

/// <summary>
/// Generates procedural terrain with rolling hills, manages chunk spawning and
/// recycling, and selects terrain types (snow/dirt/ice/slush) based on the
/// current biome. Each chunk is a StaticBody2D with Polygon2D visuals,
/// Line2D surface edge, and CollisionPolygon2D physics.
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
    public int ChunksAhead { get; set; } = 8;

    [Export]
    public float DespawnDistance { get; set; } = 1000f;

    /// <summary>Reference to the player node, set by GameManager.</summary>
    public CharacterBody2D PlayerNode { get; set; }

    // ── Terrain generation constants ─────────────────────────────

    private const float ChunkWidth = 512f;
    private const float BaseGroundY = 300f;
    private const float TerrainFillDepth = 700f;
    private const int SurfaceResolution = 17;  // one point every 32px
    private const float PointSpacing = ChunkWidth / (SurfaceResolution - 1);

    // Gap generation - gaps only spawn at end of downhill slopes (natural ski jumps)
    private const float GapProbability = 0.20f;
    private const float MinGapWidth = 200f;
    private const float MaxGapWidth = 400f;

    // Terrain section sizing - how many chunks before terrain type changes
    private const int MinSectionChunks = 4;   // At least 4 chunks (~2048px) per section
    private const int MaxSectionChunks = 8;   // Up to 8 chunks (~4096px) per section
    private const int EarlyGameSectionChunks = 10; // Even bigger sections at start

    // ── State ────────────────────────────────────────────────────

    public List<ChunkInstance> ActiveChunks { get; } = new();

    private float _nextSpawnX;
    private TerrainType _lastTerrainType;
    private int _chunksUntilTypeChange;
    private readonly RandomNumberGenerator _rng = new();
    private float _phaseOffset;
    private BiomeManager _biomeManager;

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
        _phaseOffset = _rng.RandfRange(0f, 1000f);
        _nextSpawnX = -256f;
        _lastTerrainType = TerrainType.Snow;
        _chunksUntilTypeChange = EarlyGameSectionChunks;
        _biomeManager = GetNodeOrNull<BiomeManager>("../BiomeManager");

        for (int i = 0; i < ChunksAhead; i++)
            SpawnNextChunk();

        GD.Print("[TerrainManager] Initialized with procedural terrain");
    }

    public override void _PhysicsProcess(double delta)
    {
        float playerX = PlayerNode?.GlobalPosition.X ?? 0f;

        RecycleChunks(playerX);

        while (ActiveChunks.Count < ChunksAhead)
            SpawnNextChunk();

        CheckChunkEntry(playerX);
    }

    // ── Chunk spawning ───────────────────────────────────────────

    public void SpawnNextChunk()
    {
        // Check if we should spawn a gap: only on downhill slopes (natural ski-jump ramps)
        if (_nextSpawnX > 2000f && _rng.Randf() < GapProbability && IsSlopeDownhill(_nextSpawnX))
        {
            float gapWidth = _rng.RandfRange(MinGapWidth, MaxGapWidth);

            // First, spawn a short steep uphill ramp so players can be launched off it
            float rampHeight = 220f; // steepness of the launch ramp

            var rampBody = new StaticBody2D();
            rampBody.Position = new Vector2(_nextSpawnX, 0);
            rampBody.ZIndex = -1;

            var rampSurfacePoints = new Vector2[SurfaceResolution];
            float baseHeight = GetTerrainHeight(_nextSpawnX);
            for (int i = 0; i < SurfaceResolution; i++)
            {
                float localX = i * PointSpacing;
                // create a short, steep uphill that rises toward the end of the chunk
                float t = (float)i / (SurfaceResolution - 1);
                rampSurfacePoints[i] = new Vector2(localX, baseHeight - rampHeight * t);
            }

            var rampPoly = new Vector2[SurfaceResolution + 2];
            for (int i = 0; i < SurfaceResolution; i++)
                rampPoly[i] = rampSurfacePoints[i];
            rampPoly[SurfaceResolution] = new Vector2(ChunkWidth, TerrainFillDepth);
            rampPoly[SurfaceResolution + 1] = new Vector2(0, TerrainFillDepth);

            var rampPolygon = new Polygon2D
            {
                Polygon = rampPoly,
                Color = GetFillColor(_lastTerrainType)
            };
            rampBody.AddChild(rampPolygon);

            var rampLine = new Line2D();
            rampLine.Points = rampSurfacePoints;
            rampLine.Width = 4f;
            rampLine.DefaultColor = GetSurfaceColor(_lastTerrainType);
            rampBody.AddChild(rampLine);

            var rampCollision = new CollisionPolygon2D { Polygon = rampPoly };
            rampBody.AddChild(rampCollision);

            AddChild(rampBody);

            ActiveChunks.Add(new ChunkInstance
            {
                Type = _lastTerrainType,
                WorldX = _nextSpawnX,
                Width = ChunkWidth,
                SceneNode = rampBody,
                IsGap = false
            });

            _nextSpawnX += ChunkWidth;

            // Then add the actual empty gap after the ramp
            ActiveChunks.Add(new ChunkInstance
            {
                Type = TerrainType.Snow,
                WorldX = _nextSpawnX,
                Width = gapWidth,
                SceneNode = null,
                IsGap = true
            });

            // Force terrain type to change after the gap so players often have to swap in-air
            _chunksUntilTypeChange = 0;

            _nextSpawnX += gapWidth;
            return;
        }

        // Terrain type only changes after enough chunks in current section
        if (_chunksUntilTypeChange <= 0)
        {
            _lastTerrainType = SelectTerrainType();
            // Bigger sections early, smaller sections later
            int min = _nextSpawnX < 5000f ? EarlyGameSectionChunks : MinSectionChunks;
            int max = _nextSpawnX < 5000f ? EarlyGameSectionChunks + 4 : MaxSectionChunks;
            _chunksUntilTypeChange = _rng.RandiRange(min, max);
        }

        var terrainType = _lastTerrainType;
        _chunksUntilTypeChange--;

        var body = new StaticBody2D();
        body.Position = new Vector2(_nextSpawnX, 0);
        body.ZIndex = -1;

        // Generate surface height points
        var surfacePoints = new Vector2[SurfaceResolution];
        for (int i = 0; i < SurfaceResolution; i++)
        {
            float localX = i * PointSpacing;
            float worldX = _nextSpawnX + localX;
            surfacePoints[i] = new Vector2(localX, GetTerrainHeight(worldX));
        }

        // Build closed polygon: surface points + deep fill
        var polyPoints = new Vector2[SurfaceResolution + 2];
        for (int i = 0; i < SurfaceResolution; i++)
            polyPoints[i] = surfacePoints[i];
        polyPoints[SurfaceResolution] = new Vector2(ChunkWidth, TerrainFillDepth);
        polyPoints[SurfaceResolution + 1] = new Vector2(0, TerrainFillDepth);

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
            WorldX = _nextSpawnX,
            Width = ChunkWidth,
            SceneNode = body,
            IsGap = false
        });

        _nextSpawnX += ChunkWidth;
    }

    // ── Recycling ────────────────────────────────────────────────

    public void RecycleChunks(float playerX)
    {
        ActiveChunks.RemoveAll(chunk =>
        {
            bool shouldRemove = (playerX - (chunk.WorldX + chunk.Width)) > DespawnDistance;
            if (shouldRemove)
                chunk.SceneNode?.QueueFree();
            return shouldRemove;
        });
    }

    /// <summary>Clear all chunks and reset for a new run.</summary>
    public void Reset()
    {
        foreach (var chunk in ActiveChunks)
            chunk.SceneNode?.QueueFree();
        ActiveChunks.Clear();

        _nextSpawnX = -256f;
        _lastTerrainType = TerrainType.Snow;
        _chunksUntilTypeChange = EarlyGameSectionChunks;
        _phaseOffset = _rng.RandfRange(0f, 1000f); // new terrain each run

        for (int i = 0; i < ChunksAhead; i++)
            SpawnNextChunk();
    }

    // ── Terrain height function ──────────────────────────────────

    /// <summary>
    /// Returns the terrain surface Y at the given world X position.
    /// Uses layered sine waves for dramatic hills with screen-height variation.
    /// Creates larger hills with bigger drops, fewer small bumps.
    /// Public so PlayerController can query slope geometry.
    /// </summary>
    public float GetTerrainHeight(float worldX)
    {
        // Large hills with screen-height variation (removed small bumps for smoother, bigger hills)
        float variation =
            Mathf.Sin(worldX * 0.0015f + _phaseOffset) * 280f +      // Very large, slow hills with big drops
            Mathf.Sin(worldX * 0.004f + _phaseOffset * 1.7f) * 150f; // Medium-large hills

        // Blend from flat near x=0 to full variation by x=400
        float blend = Mathf.Clamp(worldX / 400f, 0f, 1f);

        return BaseGroundY + variation * blend;
    }

    /// <summary>
    /// Returns the approximate terrain surface normal at a given world X position.
    /// Computed by sampling two nearby points and finding the perpendicular.
    /// Used by PlayerController for launch angle calculations when floor normal
    /// is unavailable (e.g. at ramp edges).
    /// </summary>
    public Vector2 GetTerrainNormalAt(float worldX)
    {
        const float sampleDelta = 4f;
        float yLeft = GetTerrainHeight(worldX - sampleDelta);
        float yRight = GetTerrainHeight(worldX + sampleDelta);

        // Tangent points right along the surface
        Vector2 tangent = new Vector2(sampleDelta * 2f, yRight - yLeft).Normalized();

        // Normal is perpendicular to tangent, pointing "up" (negative Y in Godot)
        Vector2 normal = new Vector2(-tangent.Y, tangent.X);

        // Ensure normal points upward (Y < 0)
        if (normal.Y > 0f)
            normal = -normal;

        return normal;
    }

    /// <summary>
    /// Returns true if the terrain at worldX is going downhill (Y increasing = lower on screen).
    /// Gaps should only spawn here — creates natural ski-jump ramps.
    /// </summary>
    private bool IsSlopeDownhill(float worldX)
    {
        float heightHere = GetTerrainHeight(worldX);
        float heightAhead = GetTerrainHeight(worldX - ChunkWidth);
        return heightHere > heightAhead; // Y increases downward, so higher Y = lower elevation = downhill
    }

    // ── Terrain type selection ───────────────────────────────────

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

    // ── Chunk entry detection ────────────────────────────────────

    private void CheckChunkEntry(float playerX)
    {
        foreach (var chunk in ActiveChunks)
        {
            float chunkEnd = chunk.WorldX + chunk.Width;
            if (playerX >= chunk.WorldX && playerX < chunkEnd)
            {
                if (chunk.Type != _lastTerrainType)
                {
                    _lastTerrainType = chunk.Type;
                    EmitSignal(SignalName.TerrainChanged, (int)chunk.Type);
                }
                break;
            }
        }
    }

    // ── Terrain colors ───────────────────────────────────────────

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
