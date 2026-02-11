using Godot;

namespace PeakShift;

/// <summary>
/// Resource describing a single terrain chunk: its type, difficulty,
/// biome tags, obstacle spawn points, and length.
/// </summary>
[GlobalClass]
public partial class TerrainChunk : Resource
{
    /// <summary>The surface type of this chunk.</summary>
    [Export]
    public TerrainType Type { get; set; } = TerrainType.Snow;

    /// <summary>Difficulty rating from 1 (easy) to 5 (extreme).</summary>
    [Export(PropertyHint.Range, "1,5,1")]
    public int Difficulty { get; set; } = 1;

    /// <summary>Tags describing the biome (e.g. "alpine", "forest").</summary>
    [Export]
    public string[] BiomeTags { get; set; } = System.Array.Empty<string>();

    /// <summary>Predefined positions where obstacles may spawn within this chunk.</summary>
    [Export]
    public Vector2[] ObstacleSpawnPoints { get; set; } = System.Array.Empty<Vector2>();

    /// <summary>Horizontal length of this chunk in pixels.</summary>
    [Export]
    public float Length { get; set; } = 512f;
}
