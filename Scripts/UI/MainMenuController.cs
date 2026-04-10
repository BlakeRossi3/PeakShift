using Godot;

namespace PeakShift.UI;

public partial class MainMenuController : Control
{
	[Signal] public delegate void PlayPressedEventHandler();

	private Button _playButton;

	public override void _Ready()
	{
		_playButton = GetNodeOrNull<Button>("%PlayButton");
		if (_playButton != null)
		{
			_playButton.Pressed += OnPlayPressed;
			UITheme.StyleButton(_playButton);
		}
	}

	private void OnPlayPressed()
	{
		EmitSignal(SignalName.PlayPressed);
	}
}
