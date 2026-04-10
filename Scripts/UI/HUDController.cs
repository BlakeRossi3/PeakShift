using Godot;
using PeakShift.Physics;

namespace PeakShift.UI;

public partial class HUDController : CanvasLayer
{
    private Label _scoreLabel;
    private Label _distanceLabel;
    private Label _speedLabel;
    private TextureRect _vehicleIcon;
    private Label _flipPointsLabel;
    private Label _debugLabel;
    private bool _debugVisible;
    private float _flipPointsTimer;
    private const float FlipPointsDisplayDuration = 2.0f;

    /// <summary>Set by GameManager. Used for speed display and debug overlay.</summary>
    public PlayerController PlayerRef { get; set; }

    /// <summary>Set by GameManager. Used for module debug info.</summary>
    public TerrainManager TerrainRef { get; set; }

    public override void _Ready()
    {
        BuildHUD();
        GD.Print("[HUD] Initialized (debug: F3)");
    }

    // ── Build the HUD layout programmatically ───────────────────

    private void BuildHUD()
    {
        // ── Top-left: Score ─────────────────────────────────────
        var scorePanel = MakePanel(0, 0, 0, 0, 32, 24, 280, 110);
        AddChild(scorePanel);
        scorePanel.AddChild(MakeLabel("SCORE", 16, UITheme.TextMuted));
        _scoreLabel = MakeLabel("0", 36, UITheme.TextPrimary);
        scorePanel.AddChild(_scoreLabel);

        // ── Top-right: Distance ─────────────────────────────────
        var distPanel = MakePanel(1, 0, 1, 0, -280, 24, -32, 110);
        AddChild(distPanel);
        var distTitle = MakeLabel("DISTANCE", 16, UITheme.TextMuted);
        distTitle.HorizontalAlignment = HorizontalAlignment.Right;
        distPanel.AddChild(distTitle);
        _distanceLabel = MakeLabel("0m", 36, UITheme.TextPrimary);
        _distanceLabel.HorizontalAlignment = HorizontalAlignment.Right;
        distPanel.AddChild(_distanceLabel);

        // ── Bottom-left: Speed ──────────────────────────────────
        var speedPanel = MakePanel(0, 1, 0, 1, 32, -140, 300, -24);
        AddChild(speedPanel);
        _speedLabel = MakeLabel("0", 52, UITheme.TextPrimary);
        speedPanel.AddChild(_speedLabel);
        speedPanel.AddChild(MakeLabel("km/h", 16, UITheme.TextMuted));

        // ── Bottom-right: Vehicle ───────────────────────────────
        var vehiclePanel = MakePanel(1, 1, 1, 1, -120, -140, -32, -24);
        AddChild(vehiclePanel);
        vehiclePanel.Alignment = BoxContainer.AlignmentMode.End;
        _vehicleIcon = new TextureRect
        {
            CustomMinimumSize = new Vector2(56, 56),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        vehiclePanel.AddChild(_vehicleIcon);
        var swapHint = MakeLabel("SHIFT", 14, UITheme.TextMuted);
        swapHint.HorizontalAlignment = HorizontalAlignment.Center;
        vehiclePanel.AddChild(swapHint);

        // ── Center: Flip points ─────────────────────────────────
        _flipPointsLabel = new Label
        {
            Visible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 0.5f,
            AnchorTop = 0.35f,
            AnchorRight = 0.5f,
            AnchorBottom = 0.35f,
            OffsetLeft = -200,
            OffsetRight = 200,
            OffsetTop = -80,
            OffsetBottom = 80,
        };
        _flipPointsLabel.AddThemeFontSizeOverride("font_size", 72);
        _flipPointsLabel.AddThemeColorOverride("font_color", UITheme.Gold);
        _flipPointsLabel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.6f));
        _flipPointsLabel.AddThemeConstantOverride("shadow_offset_x", 3);
        _flipPointsLabel.AddThemeConstantOverride("shadow_offset_y", 3);
        _flipPointsLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_flipPointsLabel);

        // ── Debug overlay (F3) ──────────────────────────────────
        _debugLabel = new Label
        {
            Position = new Vector2(12, 12),
            Visible = false,
        };
        _debugLabel.AddThemeColorOverride("font_color", new Color(0f, 1f, 0.4f));
        _debugLabel.AddThemeFontSizeOverride("font_size", 14);
        _debugLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_debugLabel);
    }

    private VBoxContainer MakePanel(float aL, float aT, float aR, float aB,
                                     float oL, float oT, float oR, float oB)
    {
        var panel = new VBoxContainer
        {
            AnchorLeft = aL, AnchorTop = aT,
            AnchorRight = aR, AnchorBottom = aB,
            OffsetLeft = oL, OffsetTop = oT,
            OffsetRight = oR, OffsetBottom = oB,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        panel.AddThemeConstantOverride("separation", 2);
        return panel;
    }

    private Label MakeLabel(string text, int fontSize, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.5f));
        label.AddThemeConstantOverride("shadow_offset_x", 2);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        return label;
    }

    // ── Per-frame updates ───────────────────────────────────────

    public override void _Process(double delta)
    {
        // Speed display with subtle accent tint at high speeds
        if (_speedLabel != null && PlayerRef != null)
        {
            int kmh = (int)(Mathf.Abs(PlayerRef.MomentumSpeed) / 10f);
            _speedLabel.Text = kmh.ToString("N0");
            float t = Mathf.Clamp(kmh / 500f, 0f, 1f);
            _speedLabel.AddThemeColorOverride("font_color",
                UITheme.TextPrimary.Lerp(UITheme.Accent, t * 0.5f));
        }

        // Flip points fade-out
        if (_flipPointsTimer > 0)
        {
            _flipPointsTimer -= (float)delta;
            if (_flipPointsTimer < 0.5f && _flipPointsLabel != null)
            {
                float alpha = Mathf.Max(0, _flipPointsTimer / 0.5f);
                _flipPointsLabel.Modulate = new Color(1, 1, 1, alpha);
            }
            if (_flipPointsTimer <= 0 && _flipPointsLabel != null)
                _flipPointsLabel.Visible = false;
        }

        // Debug overlay
        if (_debugVisible && PlayerRef != null)
            UpdateDebugOverlay();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.F3)
        {
            _debugVisible = !_debugVisible;
            _debugLabel.Visible = _debugVisible;
        }
    }

    // ── Public API (called by GameManager / RunManager) ─────────

    public void UpdateDistance(float distance)
    {
        if (_distanceLabel != null)
            _distanceLabel.Text = $"{distance:N0}m";
    }

    public void UpdateScore(int score, float multiplier)
    {
        if (_scoreLabel != null)
            _scoreLabel.Text = score.ToString("N0");
    }

    public void UpdateVehicleIcon(bool isBike)
    {
        if (_vehicleIcon == null) return;
        var path = isBike ? "res://Assets/Art/UI/bike_icon.png" : "res://Assets/Art/UI/ski_icon.png";
        _vehicleIcon.Texture = GD.Load<Texture2D>(path);
    }

    public void ShowFlipPoints(int points, int flipCount)
    {
        if (_flipPointsLabel == null) return;
        string flipText = flipCount switch
        {
            1 => "FLIP!",
            2 => "DOUBLE FLIP!",
            _ => $"{flipCount}X FLIP!"
        };
        _flipPointsLabel.Text = $"+{points}\n{flipText}";
        _flipPointsLabel.Visible = true;
        _flipPointsLabel.Modulate = Colors.White;
        _flipPointsTimer = FlipPointsDisplayDuration;
    }

    // ── Debug overlay ───────────────────────────────────────────

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
            tuckInfo = p.DebugIsAirborne
                ? $"\nTuck: AERIAL DIVE (grav x{PhysicsConstants.TuckAerialGravityMultiplier:F1}, " +
                  $"dive +{PhysicsConstants.TuckAerialDiveAcceleration:F0})"
                : $"\nTuck: GROUNDED (launch x{PhysicsConstants.TuckLaunchThresholdMultiplier:F1}, " +
                  $"snap +{PhysicsConstants.TuckExtraSnapDistance:F0}px)";
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
}
