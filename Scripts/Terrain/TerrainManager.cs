using System.Collections.Generic;
using Godot;

namespace PeakShift;

/// <summary>
/// Manages terrain chunk spawning and recycling. Keeps a buffer of chunks
/// ahead of the player and removes chunks that fall behind.
/// Instantiates actual StaticBody2D scenes so terrain is visible and collidable.
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
    public int ChunksAhead { get; set; } = 3;

    [Export]
    public float DespawnDistance { get; set; } = 1000f;

    [Export]
    public TerrainChunk[] AvailableChunks { get; set; } = System.Array.Empty<TerrainChunk>();

    /// <summary>Reference to the player node, set by GameManager.</summary>
    public CharacterBody2D PlayerNode { get; set; }

    // ── Constants ───────────────────────────────────────────────

    /// <summary>Y position for the ground surface (player stands on top).</summary>
    private const float GroundY = 300f;

    /// <summary>Height of each terrain chunk in pixels.</summary>
    private const float ChunkHeight = 64f;

    // ── State ────────────────────────────────────────────────────

    public List<ChunkInstance> ActiveChunks { get; } = new();

    private float _nextSpawnX;
    private TerrainType _lastTerrainType;
    private readonly RandomNumberGenerator _rng = new();
    private PackedScene _chunkScene;

    // ── Inner types ──────────────────────────────────────────────

    public class ChunkInstance
    {
        public TerrainChunk Data { get; init; }
        public float WorldX { get; init; }
        /// <summary>The actual scene node in the tree.</summary>
        public Node2D SceneNode { get; init; }
    }

    // ── Lifecycle ────────────────────────────────────────────────

    public override void _Ready()
    {
        _rng.Randomize();
        _nextSpawnX = 0f;
        _lastTerrainType = TerrainType.Snow;

        // Load the chunk scene template
        _chunkScene = GD.Load<PackedScene>("res://Scenes/Terrain/TerrainChunk.tscn");

        // Seed initial chunks
        for (int i = 0; i < ChunksAhead; i++)
        {
            SpawnNextChunk();
        }

        GD.Print("[TerrainManager] Initialized with initial chunks");
    }

    public override void _PhysicsProcess(double delta)
    {
        float playerX = GetPlayerX();

        RecycleChunks(playerX);

        // Spawn more chunks if we don't have enough ahead
        while (ActiveChunks.Count < ChunksAhead)
        {
            SpawnNextChunk();
        }

        CheckChunkEntry(playerX);
    }

    // ── Public API ───────────────────────────────────────────────

    public void SpawnNextChunk()
    {
        TerrainChunk data;

        if (AvailableChunks.Length == 0)
        {
            // Fallback: create a default snow chunk
            data = new TerrainChunk { Type = TerrainType.Snow, Length = 512f };
        }
        else
        {
            data = SelectWeightedRandom();
        }

        // Instantiate the visual/physics scene node
        var node = _chunkScene.Instantiate<StaticBody2D>();

        // Position: X at left edge of chunk, Y at ground level
        // The ColorRect and collision in the scene are centered (offset -256 to +256),
        // so we position the node at the center of the chunk.
        float centerX = _nextSpawnX + data.Length / 2f;
        node.Position = new Vector2(centerX, GroundY);

        // Scale the chunk if its length differs from the default 512
        if (!Mathf.IsEqualApprox(data.Length, 512f))
        {
            float scaleX = data.Length / 512f;
            node.Scale = new Vector2(scaleX, 1f);
        }

        // Color the chunk based on terrain type
        var colorRect = node.GetNodeOrNull<ColorRect>("ColorRect");
        if (colorRect != null)
        {
            colorRect.Color = GetTerrainColor(data.Type);
        }

        AddChild(node);

        var instance = new ChunkInstance
        {
            Data = data,
            WorldX = _nextSpawnX,
            SceneNode = node
        };
        ActiveChunks.Add(instance);
        _nextSpawnX += data.Length;

        GD.Print($"[TerrainManager] Spawned {data.Type} chunk at X={instance.WorldX}");
    }

    public void RecycleChunks(float playerX)
    {
        ActiveChunks.RemoveAll(chunk =>
        {
            bool shouldRemove = (playerX - (chunk.WorldX + chunk.Data.Length)) > DespawnDistance;
            if (shouldRemove)
            {
                // Free the actual scene node
                chunk.SceneNode?.QueueFree();
                GD.Print($"[TerrainManager] Recycled chunk at X={chunk.WorldX}");
            }
            return shouldRemove;
        });
    }

    /// <summary>Clear all chunks and reset for a new run.</summary>
    public void Reset()
    {
        foreach (var chunk in ActiveChunks)
        {
            chunk.SceneNode?.QueueFree();
        }
        ActiveChunks.Clear();
        _nextSpawnX = 0f;
        _lastTerrainType = TerrainType.Snow;

        // Re-seed initial chunks
        for (int i = 0; i < ChunksAhead; i++)
        {
            SpawnNextChunk();
        }
    }

    // ── Private helpers ──────────────────────────────────────────

    private float GetPlayerX()
    {
        return PlayerNode?.GlobalPosition.X ?? 0f;
    }

    private void CheckChunkEntry(float playerX)
    {
        foreach (var chunk in ActiveChunks)
        {
            float chunkEnd = chunk.WorldX + chunk.Data.Length;
            if (playerX >= chunk.WorldX && playerX < chunkEnd)
            {
                if (chunk.Data.Type != _lastTerrainType)
                {
                    _lastTerrainType = chunk.Data.Type;
                    EmitSignal(SignalName.TerrainChanged, (int)chunk.Data.Type);
                    GD.Print($"[TerrainManager] Terrain changed to {chunk.Data.Type}");
                }
                break;
            }
        }
    }

    private TerrainChunk SelectWeightedRandom()
    {
        float totalWeight = 0f;
        foreach (var chunk in AvailableChunks)
        {
            totalWeight += 6f - chunk.Difficulty;
        }

        float roll = _rng.RandfRange(0f, totalWeight);
        float cumulative = 0f;

        foreach (var chunk in AvailableChunks)
        {
            cumulative += 6f - chunk.Difficulty;
            if (roll <= cumulative)
            {
                return chunk;
            }
        }

        return AvailableChunks[^1];
    }

    /// <summary>Get a color for the given terrain type.</summary>
    private static Color GetTerrainColor(TerrainType type)
    {
        return type switch
        {
            TerrainType.Snow => new Color(0.92f, 0.95f, 1.0f),   // white-blue
            TerrainType.Dirt => new Color(0.55f, 0.35f, 0.2f),   // brown
            TerrainType.Ice  => new Color(0.7f, 0.85f, 0.95f),   // light blue
            TerrainType.Slush => new Color(0.6f, 0.55f, 0.5f),   // gray-brown
            _ => new Color(0.8f, 0.8f, 0.85f)
        };
    }
}
