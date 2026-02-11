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
            GD.Print("[MainMenu] PlayButton found and connected successfully");
        }
        else
        {
            GD.PrintErr("[MainMenu] FAILED: PlayButton not found! Unique name '%PlayButton' may not exist");
        }
    }

    private void OnPlayPressed()
    {
        GD.Print("[MainMenu] ===== PLAY BUTTON CLICKED =====");
        GD.Print("[MainMenu] About to emit PlayPressed signal...");
        EmitSignal(SignalName.PlayPressed);
        GD.Print("[MainMenu] PlayPressed signal emitted successfully");
    }
}
