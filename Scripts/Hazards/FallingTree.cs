using Godot;
using System.Collections.Generic;

namespace PeakShift.Hazards;

/// <summary>
/// Tips over from the side with narrow collision area.
/// </summary>
public partial class FallingTree : HazardBase
{
    [Export] public float TipSpeed { get; set; } = 2.0f;

    private float _currentRotation = 0f;
    private bool _isFalling = false;

    public FallingTree()
    {
        WarningDuration = 1.5f;
        ActiveDuration = 2.0f;
        BiomeCompatibility = new List<string> { "Pine Forest", "Alpine Meadow" };
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        if (_isFalling && _currentRotation < Mathf.Pi / 2)
        {
            _currentRotation += TipSpeed * (float)delta;
            Rotation = _currentRotation;
        }
    }

    protected override void OnWarning()
    {
        GD.Print("[FallingTree] Tree creaking...");
    }

    protected override void OnActivate()
    {
        _isFalling = true;
        GD.Print("[FallingTree] Timber!");
    }

    protected override void OnCleanup()
    {
        _isFalling = false;
        GD.Print("[FallingTree] Tree settled.");
    }
}
