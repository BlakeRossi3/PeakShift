using System.Collections.Generic;
using Godot;

namespace PeakShift;

/// <summary>
/// Manages terrain chunk spawning and recycling. Keeps a buffer of chunks
/// ahead of the player and removes chunks that fall behind.
/// </summary>
public partial class TerrainManager : Node2D
{
    // ── Signals ──────────────────────────────────────────────────

    /// <summary>Emitted when the terrain type changes under the player.</summary>
    [Signal]
    public delegate void TerrainChangedEventHandler(int newTerrainType);

    /// <summary>Emitted when the player enters any new chunk.</summary>
    [Signal]
    public delegate void ChunkEnteredEventHandler();

    // ── Exports ──────────────────────────────────────────────────

    /// <summary>Number of chunks to keep spawned ahead of the player.</summary>
    [Export]
    public int ChunksAhead { get; set; } = 3;

    /// <summary>Distance behind the player at which chunks are despawned.</summary>
    [Export]
    public float DespawnDistance { get; set; } = 1000f;

    /// <summary>Available chunk resources for weighted random selection.</summary>
    [Export]
    public TerrainChunk[] AvailableChunks { get; set; } = System.Array.Empty<TerrainChunk>();

    /// <summary>Reference to the player node, set by GameManager.</summary>
    public CharacterBody2D PlayerNode { get; set; }

    // ── State ────────────────────────────────────────────────────

    /// <summary>Currently active chunk instances in the world.</summary>
    public List<ChunkInstance> ActiveChunks { get; } = new();

    private float _nextSpawnX;
    private TerrainType _lastTerrainType;
    private readonly RandomNumberGenerator _rng = new();

    // ── Inner types ──────────────────────────────────────────────

    /// <summary>Runtime data for a spawned chunk.</summary>
    public class ChunkInstance
    {
        /// <summary>The resource definition.</summary>
        public TerrainChunk Data { get; init; }

        /// <summary>World X position of the chunk's left edge.</summary>
        public float WorldX { get; init; }
    }

    // ── Lifecycle ────────────────────────────────────────────────

    public override void _Ready()
    {
        _rng.Randomize();
        _nextSpawnX = 0f;
        _lastTerrainType = TerrainType.Snow;

        // Seed initial chunks
        for (int i = 0; i < ChunksAhead; i++)
        {
            SpawnNextChunk();
        }

        GD.Print("[TerrainManager] Initialized with initial chunks");
    }

    public override void _PhysicsProcess(double delta)
    {
        // Use the parent or player X as reference. For now, use 0 as stub.
        // In a real game, pass the player's X position.
        float playerX = GetPlayerX();

        RecycleChunks(playerX);

        // Spawn more chunks if we don't have enough ahead
        while (ActiveChunks.Count < ChunksAhead)
        {
            SpawnNextChunk();
        }

        // Check if the player has entered a new chunk
        CheckChunkEntry(playerX);
    }

    // ── Public API ───────────────────────────────────────────────

    /// <summary>
    /// Spawn the next terrain chunk using weighted random selection
    /// from available chunk resources.
    /// </summary>
    public void SpawnNextChunk()
    {
        if (AvailableChunks.Length == 0)
        {
            // Fallback: create a default snow chunk
            var fallback = new TerrainChunk { Type = TerrainType.Snow, Length = 512f };
            var instance = new ChunkInstance { Data = fallback, WorldX = _nextSpawnX };
            ActiveChunks.Add(instance);
            _nextSpawnX += fallback.Length;
            GD.Print($"[TerrainManager] Spawned fallback chunk at X={instance.WorldX}");
            return;
        }

        // Weighted random: higher difficulty chunks are less likely
        TerrainChunk selected = SelectWeightedRandom();
        var chunk = new ChunkInstance { Data = selected, WorldX = _nextSpawnX };
        ActiveChunks.Add(chunk);
        _nextSpawnX += selected.Length;

        GD.Print($"[TerrainManager] Spawned {selected.Type} chunk at X={chunk.WorldX} (len={selected.Length})");
    }

    /// <summary>
    /// Remove chunks that are far behind the player's current position.
    /// </summary>
    /// <param name="playerX">The player's current X world position.</param>
    public void RecycleChunks(float playerX)
    {
        ActiveChunks.RemoveAll(chunk =>
        {
            bool shouldRemove = (playerX - (chunk.WorldX + chunk.Data.Length)) > DespawnDistance;
            if (shouldRemove)
            {
                GD.Print($"[TerrainManager] Recycled chunk at X={chunk.WorldX}");
            }
            return shouldRemove;
        });
    }

    // ── Private helpers ──────────────────────────────────────────

    /// <summary>Get the player's current X position.</summary>
    private float GetPlayerX()
    {
        return PlayerNode?.GlobalPosition.X ?? 0f;
    }

    /// <summary>Check if the player has entered a new chunk and emit signals.</summary>
    private void CheckChunkEntry(float playerX)
    {
        foreach (var chunk in ActiveChunks)
        {
            float chunkEnd = chunk.WorldX + chunk.Data.Length;
            if (playerX >= chunk.WorldX && playerX < chunkEnd)
            {
                // Emit terrain changed if the type is different
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

    /// <summary>Select a chunk resource using inverse-difficulty weighting.</summary>
    private TerrainChunk SelectWeightedRandom()
    {
        // Inverse difficulty weighting: difficulty 1 → weight 5, difficulty 5 → weight 1
        float totalWeight = 0f;
        foreach (var chunk in AvailableChunks)
        {
            totalWeight += 6f - chunk.Difficulty; // Range: 5 down to 1
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

        // Fallback to last
        return AvailableChunks[^1];
    }
}
