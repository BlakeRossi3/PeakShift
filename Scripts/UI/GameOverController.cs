using Godot;

namespace PeakShift.UI;

public partial class GameOverController : Control
{
    [Signal] public delegate void RetryPressedEventHandler();
    [Signal] public delegate void MenuPressedEventHandler();

    private Label _scoreLabel;
    private Label _distanceLabel;
    private Button _retryButton;
    private Button _menuButton;
    private Button _quitButton;

    public override void _Ready()
    {
        _scoreLabel = GetNodeOrNull<Label>("%FinalScoreLabel");
        _distanceLabel = GetNodeOrNull<Label>("%FinalDistanceLabel");
        _retryButton = GetNodeOrNull<Button>("%RetryButton");
        _menuButton = GetNodeOrNull<Button>("%MenuButton");
        _quitButton = GetNodeOrNull<Button>("%QuitButton");

        if (_retryButton != null)
        {
            _retryButton.Pressed += OnRetryPressed;
            UITheme.StyleButton(_retryButton);
        }
        if (_menuButton != null)
        {
            _menuButton.Pressed += OnMenuPressed;
            UITheme.StyleButton(_menuButton, primary: false);
        }
        if (_quitButton != null)
        {
            _quitButton.Pressed += OnQuitPressed;
            UITheme.StyleButton(_quitButton, primary: false);
        }
    }

    public void ShowResults(int score, float distance)
    {
        if (_scoreLabel != null)
            _scoreLabel.Text = score.ToString("N0");
        if (_distanceLabel != null)
            _distanceLabel.Text = $"{distance:N0}m";
        Visible = true;
    }

    public void ShowScore(int score) => ShowResults(score, 0);

    private void OnRetryPressed() => EmitSignal(SignalName.RetryPressed);
    private void OnMenuPressed() => EmitSignal(SignalName.MenuPressed);
    private void OnQuitPressed() => GetTree().Quit();
}
