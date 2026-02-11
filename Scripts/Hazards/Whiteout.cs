using Godot;
using System.Collections.Generic;

namespace PeakShift.Hazards;

/// <summary>
/// Screen overlay that reduces visibility. No collision damage.
/// </summary>
public partial class Whiteout : HazardBase
{
    private ColorRect _overlay;
    private float _targetAlpha = 0.7f;

    public Whiteout()
    {
        WarningDuration = 3.0f;
        ActiveDuration = 5.0f;
        BiomeCompatibility = new List<string> { "Summit Storm", "Frozen Lake" };
    }

    public override void _Ready()
    {
        _overlay = GetNodeOrNull<ColorRect>("ColorRect");
        if (_overlay != null)
        {
            _overlay.Color = new Color(1, 1, 1, 0);
        }
    }

    protected override void OnWarning()
    {
        GD.Print("[Whiteout] Visibility dropping...");
        if (_overlay != null)
        {
            var tween = CreateTween();
            tween.TweenProperty(_overlay, "color:a", 0.3f, WarningDuration);
        }
    }

    protected override void OnActivate()
    {
        GD.Print("[Whiteout] Whiteout conditions!");
        if (_overlay != null)
        {
            var tween = CreateTween();
            tween.TweenProperty(_overlay, "color:a", _targetAlpha, 0.5f);
        }
    }

    protected override void OnCleanup()
    {
        GD.Print("[Whiteout] Visibility returning...");
        if (_overlay != null)
        {
            var tween = CreateTween();
            tween.TweenProperty(_overlay, "color:a", 0.0f, 1.0f);
        }
    }
}
