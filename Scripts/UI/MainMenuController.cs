using Godot;

namespace PeakShift.UI;

public partial class MainMenuController : Control
{
    // Uses SignalBus.GameStarted ("game_started")
    [Signal] public delegate void PlayPressedEventHandler();

    private Button _playButton;

    public override void _Ready()
    {
        _playButton = GetNodeOrNull<Button>("%PlayButton");
        if (_playButton != null)
        {
            _playButton.Pressed += OnPlayPressed;
        }
    }

    private void OnPlayPressed()
    {
        GD.Print("[MainMenu] Play pressed");
        EmitSignal(SignalName.PlayPressed);
    }
}
