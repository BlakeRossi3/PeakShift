using Godot;

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

        GD.Print("[HUD] Initialized - found labels and buttons");
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

    private void OnSwapPressed()
    {
        if (_swapCooldown > 0) return;
        _swapCooldown = SwapCooldownTime;
        // Uses SignalBus.VehicleSwapped ("vehicle_swapped") — would emit via SignalBus
        GD.Print("[HUD] Swap button pressed");
    }
}
