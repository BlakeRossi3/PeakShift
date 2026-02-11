using Godot;
using System.Collections.Generic;
using PeakShift;

namespace PeakShift.Data;

[GlobalClass]
public partial class BiomeData : Resource
{
    [Export] public string Name { get; set; } = "";
    [Export] public float SnowRatio { get; set; } = 0.33f;
    [Export] public float DirtRatio { get; set; } = 0.33f;
    [Export] public float IceRatio { get; set; } = 0.34f;
    [Export] public float HazardFrequency { get; set; } = 1.0f;
    [Export] public Color BackgroundTopColor { get; set; } = Colors.SkyBlue;
    [Export] public Color BackgroundBottomColor { get; set; } = Colors.White;
    [Export] public string MusicMood { get; set; } = "neutral";

    public Dictionary<TerrainType, float> GetTerrainRatios()
    {
        return new Dictionary<TerrainType, float>
        {
            { TerrainType.Snow, SnowRatio },
            { TerrainType.Dirt, DirtRatio },
            { TerrainType.Ice, IceRatio }
        };
    }

    public static BiomeData AlpineMeadow() => new()
    {
        Name = "Alpine Meadow",
        SnowRatio = 0.4f, DirtRatio = 0.6f, IceRatio = 0.0f,
        HazardFrequency = 0.5f,
        BackgroundTopColor = new Color("87CEEB"),
        BackgroundBottomColor = new Color("90EE90"),
        MusicMood = "calm"
    };

    public static BiomeData PineForest() => new()
    {
        Name = "Pine Forest",
        SnowRatio = 0.5f, DirtRatio = 0.4f, IceRatio = 0.1f,
        HazardFrequency = 1.0f,
        BackgroundTopColor = new Color("4A6741"),
        BackgroundBottomColor = new Color("2E4A2E"),
        MusicMood = "adventurous"
    };

    public static BiomeData FrozenLake() => new()
    {
        Name = "Frozen Lake",
        SnowRatio = 0.4f, DirtRatio = 0.0f, IceRatio = 0.6f,
        HazardFrequency = 0.8f,
        BackgroundTopColor = new Color("B0C4DE"),
        BackgroundBottomColor = new Color("E0FFFF"),
        MusicMood = "tense"
    };

    public static BiomeData RockyRidge() => new()
    {
        Name = "Rocky Ridge",
        SnowRatio = 0.4f, DirtRatio = 0.5f, IceRatio = 0.1f,
        HazardFrequency = 1.5f,
        BackgroundTopColor = new Color("808080"),
        BackgroundBottomColor = new Color("A0522D"),
        MusicMood = "intense"
    };

    public static BiomeData SummitStorm() => new()
    {
        Name = "Summit Storm",
        SnowRatio = 0.7f, DirtRatio = 0.0f, IceRatio = 0.3f,
        HazardFrequency = 2.0f,
        BackgroundTopColor = new Color("2F4F4F"),
        BackgroundBottomColor = new Color("696969"),
        MusicMood = "epic"
    };
}
