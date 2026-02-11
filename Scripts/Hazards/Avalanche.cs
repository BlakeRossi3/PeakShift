using Godot;
using System.Collections.Generic;

namespace PeakShift.Hazards;

/// <summary>
/// Spawns from the top of screen, moves downward with wide collision.
/// </summary>
public partial class Avalanche : HazardBase
{
    [Export] public float FallSpeed { get; set; } = 400f;

    private Sprite2D _sprite;
    private bool _isMoving = false;

    public Avalanche()
    {
        WarningDuration = 2.0f;
        ActiveDuration = 4.0f;
        BiomeCompatibility = new List<string> { "Summit Storm", "Frozen Lake", "Pine Forest" };
    }

    public override void _Ready()
    {
        _sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        if (_isMoving)
        {
            Position += new Vector2(0, FallSpeed * (float)delta);
        }
    }

    protected override void OnWarning()
    {
        GD.Print("[Avalanche] Warning! Avalanche incoming!");
        // Flash or shake screen — stub
    }

    protected override void OnActivate()
    {
        _isMoving = true;
        GD.Print("[Avalanche] Avalanche active!");
    }

    protected override void OnCleanup()
    {
        _isMoving = false;
        GD.Print("[Avalanche] Avalanche clearing...");
    }
}
