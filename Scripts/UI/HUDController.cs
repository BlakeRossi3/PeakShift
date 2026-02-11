using Godot;
using PeakShift.Physics;

namespace PeakShift.UI;

public partial class HUDController : CanvasLayer
{
    // Uses SignalBus.ScoreUpdated ("score_updated"), VehicleSwapped ("vehicle_swapped"), TerrainChanged ("terrain_changed")

    private Label _distanceLabel;
    private Label _scoreLabel;
    private Label _multiplierLabel;
    private TextureRect _vehicleIcon;
    private BaseButton _swapButton;
    private ColorRect[] _terrainPreview = new ColorRect[5];

    private float _swapCooldown = 0f;
    private const float SwapCooldownTime = 1.0f;

    // ── Debug overlay ───────────────────────────────────────────
    private Label _debugLabel;
    private bool _debugVisible;

    /// <summary>
    /// Reference to the player controller, set by GameManager.
    /// When set, the debug overlay pulls live physics state each frame.
    /// </summary>
    public PlayerController PlayerRef { get; set; }

    /// <summary>
    /// Reference to the terrain manager, set by GameManager.
    /// Used for module debug info in the overlay.
    /// </summary>
    public TerrainManager TerrainRef { get; set; }

    public override void _Ready()
    {
        _distanceLabel = GetNodeOrNull<Label>("%DistanceLabel");
        _scoreLabel = GetNodeOrNull<Label>("%ScoreLabel");
        _multiplierLabel = GetNodeOrNull<Label>("%MultiplierLabel");
        _vehicleIcon = GetNodeOrNull<TextureRect>("%VehicleIcon");
        _swapButton = GetNodeOrNull<BaseButton>("%SwapButton");

        for (int i = 0; i < 5; i++)
        {
            _terrainPreview[i] = GetNodeOrNull<ColorRect>($"%TerrainPreview{i}");
        }

        if (_swapButton != null)
        {
            _swapButton.Pressed += OnSwapPressed;
        }

        // Create debug label (top-left, monospace, semi-transparent background)
        _debugLabel = new Label
        {
            Position = new Vector2(12, 12),
            Visible = false,
        };
        _debugLabel.AddThemeColorOverride("font_color", new Color(0.0f, 1.0f, 0.4f));
        _debugLabel.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_debugLabel);

        GD.Print("[HUD] Initialized - found labels and buttons (debug overlay: F3)");
    }

    public override void _Process(double delta)
    {
        if (_swapCooldown > 0)
        {
            _swapCooldown -= (float)delta;
            if (_swapButton != null)
                _swapButton.Modulate = new Color(1, 1, 1, 0.5f);
        }
        else if (_swapButton != null)
        {
            _swapButton.Modulate = Colors.White;
        }

        // Update debug overlay
        if (_debugVisible && PlayerRef != null)
            UpdateDebugOverlay();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Toggle debug overlay with F3
        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.F3)
        {
            _debugVisible = !_debugVisible;
            _debugLabel.Visible = _debugVisible;
        }
    }

    public void UpdateDistance(float distance)
    {
        if (_distanceLabel != null)
            _distanceLabel.Text = $"{distance:F0}m";
    }

    public void UpdateScore(int score, float multiplier)
    {
        if (_scoreLabel != null)
            _scoreLabel.Text = $"{score}";
        if (_multiplierLabel != null)
            _multiplierLabel.Text = $"x{multiplier:F1}";
    }

    public void UpdateVehicleIcon(bool isBike)
    {
        if (_vehicleIcon == null) return;
        // Swap texture based on vehicle — uses placeholder icons
        var path = isBike ? "res://Assets/Art/UI/bike_icon.png" : "res://Assets/Art/UI/ski_icon.png";
        _vehicleIcon.Texture = GD.Load<Texture2D>(path);
    }

    public void UpdateTerrainPreview(Color[] colors)
    {
        for (int i = 0; i < Mathf.Min(colors.Length, _terrainPreview.Length); i++)
        {
            if (_terrainPreview[i] != null)
                _terrainPreview[i].Color = colors[i];
        }
    }

    private void UpdateDebugOverlay()
    {
        var p = PlayerRef;
        string airState = p.DebugIsAirborne ? "AIRBORNE" : "GROUNDED";
        string gapState = p.DebugOverGap ? " [OVER GAP]" : "";

        string clearanceInfo = "";
        if (p.LastClearanceValid)
        {
            var r = p.LastClearanceResult;
            clearanceInfo = r.Clears
                ? $"\nGap: CLEAR ({r.JumpDistance:F0}px jump)"
                : $"\nGap: FAIL (landed {r.LandingX:F0}, needed further)";
        }

        string moduleInfo = "";
        if (TerrainRef != null)
        {
            moduleInfo = $"\nModule: {TerrainRef.DebugModuleInfoAt(p.GlobalPosition.X)}" +
                         $"\nModules: {TerrainRef.DebugPlacedModuleCount} placed" +
                         $"\nGenDist: {TerrainRef.DebugTotalDistance:F0}px" +
                         $"\nPool: {TerrainRef.DebugPoolAvailable}/{TerrainRef.DebugPoolTotal}";
        }

        string tuckInfo = "";
        if (p.DebugIsTucking)
        {
            if (p.DebugIsAirborne)
            {
                tuckInfo = $"\nTuck: AERIAL DIVE (grav x{PhysicsConstants.TuckAerialGravityMultiplier:F1}, " +
                    $"dive +{PhysicsConstants.TuckAerialDiveAcceleration:F0})";
            }
            else
            {
                tuckInfo = $"\nTuck: GROUNDED (launch x{PhysicsConstants.TuckLaunchThresholdMultiplier:F1}, " +
                    $"snap +{PhysicsConstants.TuckExtraSnapDistance:F0}px)";
            }
        }

        _debugLabel.Text =
            $"Speed: {p.MomentumSpeed:F0} px/s\n" +
            $"Fwd Vel: {p.DebugForwardVelocity:F0} px/s\n" +
            $"Vert Vel: {p.DebugVerticalVelocity:F0} px/s\n" +
            $"Slope: {p.DebugSlopeAngleDeg:F1}°\n" +
            $"State: {airState}{gapState}\n" +
            $"Terrain: {p.DebugTerrainType}\n" +
            $"Vehicle: {p.CurrentVehicleType}" +
            tuckInfo +
            clearanceInfo +
            moduleInfo;
    }

    private void OnSwapPressed()
    {
        if (_swapCooldown > 0) return;
        _swapCooldown = SwapCooldownTime;
        // Uses SignalBus.VehicleSwapped ("vehicle_swapped") — would emit via SignalBus
        GD.Print("[HUD] Swap button pressed");
    }
}
