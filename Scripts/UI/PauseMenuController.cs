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

        if (_resumeButton != null)
        {
            _resumeButton.Pressed += OnResumePressed;
            UITheme.StyleButton(_resumeButton);
        }
        if (_quitButton != null)
        {
            _quitButton.Pressed += OnQuitPressed;
            UITheme.StyleButton(_quitButton, primary: false);
        }

        Visible = false;
    }

    private void OnResumePressed() => EmitSignal(SignalName.ResumePressed);
    private void OnQuitPressed() => EmitSignal(SignalName.QuitPressed);
}
