using Godot;
using PeakShift.Physics;

namespace PeakShift.UI;

public partial class HUDController : CanvasLayer
{
    // Uses SignalBus.ScoreUpdated ("score_updated"), VehicleSwapped ("vehicle_swapped"), TerrainChanged ("terrain_changed")

    private Label _distanceLabel;
    private Label _scoreLabel;
    private Label _multiplierLabel;
    private Label _speedLabel;
    private TextureRect _vehicleIcon;
    private BaseButton _swapButton;
    private ColorRect[] _terrainPreview = new ColorRect[5];

    private float _swapCooldown = 0f;
    private const float SwapCooldownTime = 1.0f;

    // ── Debug overlay ───────────────────────────────────────────
    private Label _debugLabel;
    private bool _debugVisible;

    // ── Flip points display ─────────────────────────────────────
    private Label _flipPointsLabel;
    private float _flipPointsTimer;
    private const float FlipPointsDisplayDuration = 2.0f;

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
        _speedLabel = GetNodeOrNull<Label>("%SpeedLabel");
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

        // Create flip points label (center screen, large, bold, gold color)
        _flipPointsLabel = new Label
        {
            Visible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _flipPointsLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.84f, 0.0f)); // Gold
        _flipPointsLabel.AddThemeFontSizeOverride("font_size", 72);
        AddChild(_flipPointsLabel);

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

        // Update flip points display
        if (_flipPointsTimer > 0)
        {
            _flipPointsTimer -= (float)delta;

            // Position label at center of viewport
            if (_flipPointsLabel != null)
            {
                var viewportSize = GetViewport().GetVisibleRect().Size;
                _flipPointsLabel.Position = new Vector2(
                    viewportSize.X / 2 - 100, // Offset for approximate text width
                    viewportSize.Y / 2 - 100
                );

                // Fade out in last 0.5 seconds
                if (_flipPointsTimer < 0.5f)
                {
                    float alpha = _flipPointsTimer / 0.5f;
                    _flipPointsLabel.Modulate = new Color(1, 1, 1, alpha);
                }
            }

            if (_flipPointsTimer <= 0)
            {
                _flipPointsLabel.Visible = false;
            }
        }

        // Update speedometer
        if (_speedLabel != null && PlayerRef != null)
        {
            int kmh = (int)(Mathf.Abs(PlayerRef.MomentumSpeed) / 10f);
            _speedLabel.Text = $"{kmh} km/h";
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

    public void ShowFlipPoints(int points, int flipCount)
    {
        if (_flipPointsLabel == null) return;

        string flipText = flipCount == 1 ? "FLIP" : $"{flipCount}X FLIP";
        _flipPointsLabel.Text = $"+{points}\n{flipText}";
        _flipPointsLabel.Visible = true;
        _flipPointsLabel.Modulate = Colors.White; // Reset opacity
        _flipPointsTimer = FlipPointsDisplayDuration;

        GD.Print($"[HUD] Showing flip points: +{points} ({flipCount} flip(s))");
    }

    private void UpdateDebugOverlay()
    {
        var p = PlayerRef;
        string airState = p.DebugIsAirborne ? "AIRBORNE" : "GROUNDED";
        string gapState = p.DebugOverGap ? " [OVER GAP]" : "";

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
