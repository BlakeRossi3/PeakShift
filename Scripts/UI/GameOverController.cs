using Godot;

namespace PeakShift.UI;

public partial class GameOverController : Control
{
    // Uses SignalBus.GameOver ("game_over")
    [Signal] public delegate void RetryPressedEventHandler();
    [Signal] public delegate void MenuPressedEventHandler();

    private Label _scoreLabel;
    private Button _retryButton;
    private Button _menuButton;

    public override void _Ready()
    {
        _scoreLabel = GetNodeOrNull<Label>("%FinalScoreLabel");
        _retryButton = GetNodeOrNull<Button>("%RetryButton");
        _menuButton = GetNodeOrNull<Button>("%MenuButton");

        if (_retryButton != null) _retryButton.Pressed += OnRetryPressed;
        if (_menuButton != null) _menuButton.Pressed += OnMenuPressed;
    }

    public void ShowScore(int score)
    {
        if (_scoreLabel != null)
            _scoreLabel.Text = $"Score: {score}";
        Visible = true;
    }

    private void OnRetryPressed()
    {
        GD.Print("[GameOver] Retry pressed");
        EmitSignal(SignalName.RetryPressed);
    }

    private void OnMenuPressed()
    {
        GD.Print("[GameOver] Menu pressed");
        EmitSignal(SignalName.MenuPressed);
    }
}
