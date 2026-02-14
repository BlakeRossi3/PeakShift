using System.Collections.Generic;
using Godot;
using PeakShift.Terrain;
using PeakShift.Obstacles;

namespace PeakShift;

/// <summary>
/// Modular track generation manager. Delegates to ModuleTrackGenerator for
/// module selection and placement, then renders terrain chunks with object pooling.
///
/// Public API is preserved from the original TerrainManager so PlayerController,
/// GameManager, and HUD continue to work without changes.
///
/// Debug: Press F4 to preview-spawn the next N modules in the console log.
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
    public int ChunksAhead { get; set; } = 30;

    [Export]
    public float DespawnDistance { get; set; } = 5000f;

    [Export]
    public int PreviewModuleCount { get; set; } = 10;

    [Export]
    public int PoolPrewarmCount { get; set; } = 20;

    /// <summary>Reference to the player node, set by GameManager.</summary>
    public CharacterBody2D PlayerNode { get; set; }

    // ── Constants ────────────────────────────────────────────────

    private const float MaxChunkWidth = 512f;
    private const float BaseGroundY = 200f;
    private const float PointSpacing = 32f;
    private const float SpawnX = -256f;
    private const float FillDepthBelowSurface = 5000f;

    // ── Module system ────────────────────────────────────────────

    private ModuleTrackGenerator _generator;
    private DifficultyProfile _difficulty;
    private ModulePool _pool;
    private ObstaclePool _obstaclePool;

    // ── Chunk tracking ───────────────────────────────────────────

    public List<ChunkInstance> ActiveChunks { get; } = new();
    private List<ObstacleInstance> _activeObstacles = new();

    private float _nextSpawnX;

    // Terrain type change detection
    private TerrainType _lastEnteredType = TerrainType.Snow;

    // ── Debug ────────────────────────────────────────────────────

    /// <summary>Last placed module count (for debug overlay).</summary>
    public int DebugPlacedModuleCount => _generator?.PlacedModules.Count ?? 0;

    /// <summary>Current generation distance (for debug overlay).</summary>
    public float DebugTotalDistance => _generator?.TotalDistance ?? 0f;

    /// <summary>Pool stats for debug overlay.</summary>
    public int DebugPoolAvailable => _pool?.Available ?? 0;
    public int DebugPoolTotal => _pool?.TotalCreated ?? 0;

    /// <summary>Returns info about the current module at the given world X.</summary>
    public string DebugModuleInfoAt(float worldX)
    {
        if (_generator == null) return "N/A";

        foreach (var mod in _generator.PlacedModules)
        {
            if (worldX >= mod.WorldStartX && worldX < mod.WorldEndX)
            {
                if (mod.Compound != null)
                {
                    float localX = worldX - mod.WorldStartX;
                    int secIdx = mod.Compound.FindSubSectionIndex(localX);
                    secIdx = Mathf.Clamp(secIdx, 0, mod.Compound.Sections.Count - 1);
                    var sec = mod.Compound.Sections[secIdx];
                    return $"Compound:{sec.Type} D{mod.Compound.Difficulty} " +
                           $"{mod.Compound.EntryTerrain}" +
                           (mod.Compound.HasTerrainTransition ? $"→{mod.Compound.ExitTerrain}" : "") +
                           $" [GAP {mod.ScaledGapWidth:F0}px]";
                }

                return $"{mod.Template.Shape} D{mod.Template.Difficulty} " +
                       $"{mod.Template.EntryTerrain}" +
                       (mod.Template.IsTransition ? $"→{mod.Template.ExitTerrain}" : "") +
                       (mod.Template.HasJump ? $" [GAP {mod.ScaledGapWidth:F0}px]" : "");
            }
        }

        if (_generator.IsOverGap(worldX))
            return "AIRBORNE (gap)";

        return "?";
    }

    // ── Inner types ──────────────────────────────────────────────

    public class ChunkInstance
    {
        public TerrainType Type { get; init; }
        public float WorldX { get; init; }
        public float Width { get; init; }
        public StaticBody2D SceneNode { get; init; }
        public bool IsGap { get; init; }
    }

    private class ObstacleInstance
    {
        public ObstacleBase Obstacle { get; init; }
        public float WorldX { get; init; }
    }

    // ── Lifecycle ────────────────────────────────────────────────

    public override void _Ready()
    {
        // Build module system
        _difficulty = new DifficultyProfile();
        _generator = new ModuleTrackGenerator(_difficulty)
        {
            LookaheadModules = 30,
            DespawnBehind = DespawnDistance
        };
        _pool = new ModulePool(this, PoolPrewarmCount);
        _obstaclePool = new ObstaclePool(this, prewarmCountPerType: 10);

        // Initialize generator
        _generator.Initialize(SpawnX, BaseGroundY);

        // Spawn initial chunks
        _nextSpawnX = SpawnX;

        for (int i = 0; i < ChunksAhead; i++)
            SpawnNextChunk();

        GD.Print("[TerrainManager] Initialized with compound module track generator");
        GD.Print($"[TerrainManager] Pool: {_pool.TotalCreated} terrain chunks, " +
                 $"{_obstaclePool.TotalCreated} obstacles pre-warmed");
    }

    public override void _PhysicsProcess(double delta)
    {
        float playerX = PlayerNode?.GlobalPosition.X ?? 0f;

        // Update generator (generates ahead, trims behind)
        _generator.Update(playerX);

        // Recycle rendered chunks behind the player
        RecycleChunks(playerX);
        RecycleObstacles(playerX);

        // Spawn new chunks
        while (ActiveChunks.Count < ChunksAhead)
            SpawnNextChunk();

        // Detect terrain type changes
        CheckTerrainChange(playerX);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // F4: Preview mode — log the next N modules
        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.F4)
        {
            var preview = _generator.PreviewGenerate(PreviewModuleCount);
            GD.Print($"[TerrainManager] ═══ Preview: next {preview.Count} modules ═══");
            foreach (var mod in preview)
            {
                if (mod.Compound != null)
                {
                    var c = mod.Compound;
                    string sections = string.Join(", ", c.Sections.ConvertAll(s => s.Type.ToString()));
                    GD.Print($"  [{mod.SequenceIndex}] Compound D{c.Difficulty} " +
                             $"{c.EntryTerrain}" +
                             (c.HasTerrainTransition ? $"→{c.ExitTerrain}" : "") +
                             $" | X: {mod.WorldStartX:F0}→{mod.WorldEndX:F0} " +
                             $"Y: {mod.EntryY:F0}→{mod.ExitY:F0} " +
                             $"[GAP {mod.ScaledGapWidth:F0}px] [{sections}]");
                }
                else
                {
                    GD.Print($"  [{mod.SequenceIndex}] {mod.Template.Shape} " +
                             $"D{mod.Template.Difficulty} " +
                             $"{mod.Template.EntryTerrain}" +
                             (mod.Template.IsTransition ? $"→{mod.Template.ExitTerrain}" : "") +
                             $" | X: {mod.WorldStartX:F0}→{mod.WorldEndX:F0} " +
                             $"Y: {mod.EntryY:F0}→{mod.ExitY:F0}" +
                             (mod.Template.HasJump ? $" [GAP {mod.ScaledGapWidth:F0}px]" : ""));
                }
            }
            GD.Print("[TerrainManager] ═══════════════════════════════════");
        }
    }

    // ── Public API (preserved for PlayerController compatibility) ──

    /// <summary>
    /// Returns the terrain surface Y at the given world X position.
    /// Delegates to the modular track generator.
    /// </summary>
    public float GetTerrainHeight(float worldX)
    {
        return _generator.GetHeight(worldX);
    }

    /// <summary>
    /// Returns the approximate terrain surface normal at a given world X position.
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
    /// </summary>
    public float GetStartingSurfaceY()
    {
        return GetTerrainHeight(SpawnX);
    }

    // ── Gap query API ────────────────────────────────────────────

    public struct GapInfo
    {
        public bool Found;
        public float GapStartX;
        public float GapEndX;
        public float LipY;
        public float LandingY;
        public float Width;
        public TerrainType Type;
    }

    public GapInfo GetCurrentOrNextGap(float worldX)
    {
        var genGap = _generator.GetCurrentOrNextGap(worldX);
        return new GapInfo
        {
            Found = genGap.Found,
            GapStartX = genGap.GapStartX,
            GapEndX = genGap.GapEndX,
            LipY = genGap.LipY,
            LandingY = genGap.LandingY,
            Width = genGap.Width,
            Type = genGap.Type
        };
    }

    public bool IsOverGap(float worldX)
    {
        return _generator.IsOverGap(worldX);
    }

    /// <summary>
    /// Returns the terrain surface tangent (travel direction) at a given world X.
    /// Always points rightward (positive X).
    /// </summary>
    public Vector2 GetTerrainTangentAt(float worldX)
    {
        const float sampleDelta = 4f;
        float yLeft = GetTerrainHeight(worldX - sampleDelta);
        float yRight = GetTerrainHeight(worldX + sampleDelta);
        return new Vector2(sampleDelta * 2f, yRight - yLeft).Normalized();
    }

    /// <summary>
    /// Returns the terrain type at the given world X position.
    /// </summary>
    public TerrainType GetTerrainType(float worldX)
    {
        return _generator.GetTerrainTypeAt(worldX);
    }

    // ── Chunk spawning (renders modules as terrain geometry) ─────

    public void SpawnNextChunk()
    {
        if (_generator.PlacedModules.Count == 0) return;

        // Find which module contains _nextSpawnX
        ModuleTrackGenerator.PlacedModule currentMod = null;
        for (int i = 0; i < _generator.PlacedModules.Count; i++)
        {
            var mod = _generator.PlacedModules[i];

            // Check if we're in this module's surface area
            if (_nextSpawnX >= mod.WorldStartX && _nextSpawnX < mod.WorldEndX)
            {
                currentMod = mod;
                break;
            }

            // Check if we're in this module's gap
            if (mod.HasJump && _nextSpawnX >= mod.GapStartX && _nextSpawnX < mod.GapEndX)
            {
                // Spawn gap chunk and skip past it
                float gapRemaining = mod.GapEndX - _nextSpawnX;
                ActiveChunks.Add(new ChunkInstance
                {
                    Type = mod.ModuleExitTerrain,
                    WorldX = _nextSpawnX,
                    Width = gapRemaining,
                    SceneNode = null,
                    IsGap = true
                });
                _nextSpawnX = mod.GapEndX;
                return;
            }
        }

        if (currentMod == null)
        {
            // We're past all placed modules — advance to find one
            if (_generator.PlacedModules.Count > 0)
            {
                var last = _generator.PlacedModules[^1];
                if (_nextSpawnX < last.GapEndX)
                {
                    // Find the right module
                    foreach (var mod in _generator.PlacedModules)
                    {
                        if (mod.WorldStartX > _nextSpawnX)
                        {
                            _nextSpawnX = mod.WorldStartX;
                            currentMod = mod;
                            break;
                        }
                    }
                }
            }

            if (currentMod == null)
                return;  // Nothing to spawn yet
        }

        // Determine chunk width (shorter if module end or gap is nearby)
        float distToEnd = currentMod.WorldEndX - _nextSpawnX;
        float chunkWidth = Mathf.Min(MaxChunkWidth, distToEnd);

        if (chunkWidth < 32f)
        {
            _nextSpawnX = currentMod.WorldEndX;
            return;
        }

        // Determine terrain type at this position
        TerrainType terrainType = currentMod.ActiveTerrainAt(_nextSpawnX);

        // For gap modules, don't render surface geometry (legacy intro modules only)
        if (currentMod.Template?.Shape == TrackModule.ModuleShape.Gap)
        {
            ActiveChunks.Add(new ChunkInstance
            {
                Type = terrainType,
                WorldX = _nextSpawnX,
                Width = chunkWidth,
                SceneNode = null,
                IsGap = true
            });
            _nextSpawnX += chunkWidth;
            return;
        }

        int resolution = Mathf.Max(3, (int)(chunkWidth / PointSpacing) + 1);
        CreateTerrainChunk(_nextSpawnX, chunkWidth, resolution, terrainType, currentMod);

        // Obstacles disabled
        // if (currentMod.Template.ObstacleDensity > 0f)
        // {
        //     SpawnObstaclesForChunk(_nextSpawnX, chunkWidth, terrainType, currentMod);
        // }

        _nextSpawnX += chunkWidth;
    }

    private void CreateTerrainChunk(float worldX, float width, int resolution,
                                     TerrainType terrainType,
                                     ModuleTrackGenerator.PlacedModule module)
    {
        var body = _pool.Acquire();
        body.Position = new Vector2(worldX, 0);

        float spacing = width / (resolution - 1);

        // Generate surface height points from the module's curve
        var surfacePoints = new Vector2[resolution];
        float maxSurfaceY = float.MinValue;
        for (int i = 0; i < resolution; i++)
        {
            float localX = i * spacing;
            float wx = worldX + localX;
            float y = module.HeightAt(wx);
            surfacePoints[i] = new Vector2(localX, y);
            if (y > maxSurfaceY) maxSurfaceY = y;
        }

        float fillDepth = maxSurfaceY + FillDepthBelowSurface;

        // Build closed polygon
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

        // Physics collision removed — path-following uses math queries, not collision polygons

        ActiveChunks.Add(new ChunkInstance
        {
            Type = terrainType,
            WorldX = worldX,
            Width = width,
            SceneNode = body,
            IsGap = false
        });
    }

    // ── Obstacle spawning ────────────────────────────────────────

    private void SpawnObstaclesForChunk(float chunkWorldX, float chunkWidth,
                                         TerrainType terrainType,
                                         ModuleTrackGenerator.PlacedModule module)
    {
        float obsDensity = module.Template?.ObstacleDensity ?? 0f;
        if (obsDensity <= 0f) return;

        // Determine obstacle types to use
        var allowedTypes = module.Template?.AllowedObstacleTypes;
        if (allowedTypes == null || allowedTypes.Length == 0)
        {
            // Default: all types allowed based on terrain
            allowedTypes = terrainType switch
            {
                TerrainType.Snow => new[] { "Rock", "Tree", "Log" },
                TerrainType.Dirt => new[] { "Rock", "Tree", "Log" },
                TerrainType.Ice => new[] { "Rock" }, // Only rocks on ice
                _ => new[] { "Rock" }
            };
        }

        // Probability-based spawning: each chunk rolls against density.
        // With density ~0.1 and ~8 chunks per module, yields 0-2 obstacles per module.
        if (GD.Randf() > obsDensity) return;

        // Spawn exactly 1 obstacle in this chunk
        float localX = GD.Randf() * chunkWidth;
        float worldX = chunkWorldX + localX;
        float terrainY = module.HeightAt(worldX);

        string obstacleType = allowedTypes[(int)(GD.Randf() * allowedTypes.Length)];
        var obstacle = _obstaclePool.Acquire(obstacleType);
        if (obstacle == null) return;

        Vector2 spawnPos = new Vector2(worldX, terrainY - 16f);
        obstacle.Activate(spawnPos, terrainType);

        _activeObstacles.Add(new ObstacleInstance
        {
            Obstacle = obstacle,
            WorldX = worldX
        });
    }

    // ── Recycling ───────────────────────────────────────────────

    public void RecycleChunks(float playerX)
    {
        ActiveChunks.RemoveAll(chunk =>
        {
            bool shouldRemove = (playerX - (chunk.WorldX + chunk.Width)) > DespawnDistance;
            if (shouldRemove && chunk.SceneNode != null)
                _pool.Release(chunk.SceneNode);
            return shouldRemove;
        });
    }

    private void RecycleObstacles(float playerX)
    {
        _activeObstacles.RemoveAll(obs =>
        {
            bool shouldRemove = (playerX - obs.WorldX) > DespawnDistance;
            if (shouldRemove && obs.Obstacle != null)
                _obstaclePool.Release(obs.Obstacle);
            return shouldRemove;
        });
    }

    /// <summary>Clear all chunks and reset for a new run.</summary>
    public void Reset()
    {
        foreach (var chunk in ActiveChunks)
        {
            if (chunk.SceneNode != null)
                _pool.Release(chunk.SceneNode);
        }
        ActiveChunks.Clear();

        // Clear all obstacles
        foreach (var obs in _activeObstacles)
        {
            if (obs.Obstacle != null)
                _obstaclePool.Release(obs.Obstacle);
        }
        _activeObstacles.Clear();

        // Re-initialize generator
        _generator.Initialize(SpawnX, BaseGroundY);
        _nextSpawnX = SpawnX;

        _lastEnteredType = TerrainType.Snow;

        for (int i = 0; i < ChunksAhead; i++)
            SpawnNextChunk();

        GD.Print("[TerrainManager] Reset with modular track generator");
    }

    // ── Terrain type change detection ────────────────────────────

    private void CheckTerrainChange(float playerX)
    {
        var terrainType = _generator.GetTerrainTypeAt(playerX);
        if (terrainType != _lastEnteredType)
        {
            _lastEnteredType = terrainType;
            EmitSignal(SignalName.TerrainChanged, (int)terrainType);
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
