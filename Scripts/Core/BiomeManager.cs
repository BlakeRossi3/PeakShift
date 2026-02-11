using Godot;
using PeakShift.Data;

namespace PeakShift.Core;

public partial class BiomeManager : Node
{
    // Uses SignalBus.BiomeTransition ("biome_transition")
    [Signal] public delegate void BiomeTransitionEventHandler(string biomeName);

    [Export] public float TransitionDuration { get; set; } = 2.0f;

    private BiomeData _currentBiome;
    private readonly BiomeData[] _biomeSequence;
    private int _biomeIndex = 0;
    private float _distanceTraveled = 0f;
    private float _nextBiomeThreshold = 500f;
    private const float BiomeInterval = 500f;

    public BiomeData CurrentBiome => _currentBiome;

    public BiomeManager()
    {
        _biomeSequence = new BiomeData[]
        {
            BiomeData.AlpineMeadow(),
            BiomeData.PineForest(),
            BiomeData.FrozenLake(),
            BiomeData.RockyRidge(),
            BiomeData.SummitStorm()
        };
    }

    public override void _Ready()
    {
        _currentBiome = _biomeSequence[0];
    }

    public override void _Process(double delta)
    {
        // Distance tracking would be driven by player speed — stub for now
    }

    /// <summary>
    /// Called by the game loop when distance updates.
    /// </summary>
    public void UpdateDistance(float totalDistance)
    {
        _distanceTraveled = totalDistance;

        if (_distanceTraveled >= _nextBiomeThreshold && _biomeIndex < _biomeSequence.Length - 1)
        {
            _biomeIndex++;
            TransitionTo(_biomeSequence[_biomeIndex]);
            _nextBiomeThreshold += BiomeInterval;
        }
    }

    private void TransitionTo(BiomeData newBiome)
    {
        var oldBiome = _currentBiome;
        _currentBiome = newBiome;

        // Tween background colors
        var tween = CreateTween();
        tween.SetParallel(true);

        // These would target a background node — stub targets self for now
        GD.Print($"[BiomeManager] Transitioning from {oldBiome.Name} to {newBiome.Name}");

        EmitSignal(SignalName.BiomeTransition, newBiome.Name);
    }

    public void Reset()
    {
        _biomeIndex = 0;
        _distanceTraveled = 0f;
        _nextBiomeThreshold = BiomeInterval;
        _currentBiome = _biomeSequence[0];
    }
}
