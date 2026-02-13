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
    private Button _quitButton;

    public override void _Ready()
    {
        _scoreLabel = GetNodeOrNull<Label>("%FinalScoreLabel");
        _retryButton = GetNodeOrNull<Button>("%RetryButton");
        _menuButton = GetNodeOrNull<Button>("%MenuButton");
        _quitButton = GetNodeOrNull<Button>("%QuitButton");

        if (_retryButton != null) _retryButton.Pressed += OnRetryPressed;
        if (_menuButton != null) _menuButton.Pressed += OnMenuPressed;
        if (_quitButton != null) _quitButton.Pressed += OnQuitPressed;
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

    private void OnQuitPressed()
    {
        GD.Print("[GameOver] Quit pressed");
        GetTree().Quit();
    }
}
