using Godot;
using System.Collections.Generic;

namespace PeakShift.Hazards;

/// <summary>
/// Multiple rocks fall from above with scattered collision.
/// </summary>
public partial class Rockslide : HazardBase
{
    [Export] public float RockSpeed { get; set; } = 350f;
    [Export] public int RockCount { get; set; } = 5;

    private bool _isActive = false;

    public Rockslide()
    {
        WarningDuration = 1.0f;
        ActiveDuration = 3.0f;
        BiomeCompatibility = new List<string> { "Rocky Ridge", "Summit Storm" };
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        if (_isActive)
        {
            Position += new Vector2(0, RockSpeed * (float)delta);
        }
    }

    protected override void OnWarning()
    {
        GD.Print("[Rockslide] Rocks rumbling above...");
    }

    protected override void OnActivate()
    {
        _isActive = true;
        GD.Print($"[Rockslide] {RockCount} rocks falling!");
    }

    protected override void OnCleanup()
    {
        _isActive = false;
        GD.Print("[Rockslide] Rocks settled.");
    }
}
