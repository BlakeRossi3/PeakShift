using Godot;

namespace PeakShift.UI;

public partial class PauseMenuController : Control
{
    [Signal] public delegate void ResumePressedEventHandler();
    [Signal] public delegate void QuitPressedEventHandler();

    private Button _resumeButton;
    private Button _quitButton;

    public override void _Ready()
    {
        _resumeButton = GetNodeOrNull<Button>("%ResumeButton");
        _quitButton = GetNodeOrNull<Button>("%QuitButton");

        if (_resumeButton != null) _resumeButton.Pressed += OnResumePressed;
        if (_quitButton != null) _quitButton.Pressed += OnQuitPressed;

        Visible = false;
    }

    private void OnResumePressed()
    {
        GD.Print("[PauseMenu] Resumed");
        EmitSignal(SignalName.ResumePressed);
    }

    private void OnQuitPressed()
    {
        GD.Print("[PauseMenu] Quit pressed");
        EmitSignal(SignalName.QuitPressed);
    }
}
