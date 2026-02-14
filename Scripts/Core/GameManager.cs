using Godot;
using PeakShift.Core;
using PeakShift.Hazards;
using PeakShift.UI;

namespace PeakShift;

/// <summary>
/// Singleton node that manages the overall game state machine.
/// States: Menu, Playing, Paused, GameOver.
/// Wires signals between all game systems on _Ready.
/// </summary>
public partial class GameManager : Node
{
	/// <summary>Possible game states.</summary>
	public enum GameState
	{
		Menu,
		Playing,
		Paused,
		GameOver
	}

	// ── Signals ──────────────────────────────────────────────────

	[Signal]
	public delegate void GameStartedEventHandler();

	[Signal]
	public delegate void GameOverEventHandler();

	[Signal]
	public delegate void StateChangedEventHandler(int newState);

	// ── Properties ───────────────────────────────────────────────

	public GameState CurrentState { get; private set; } = GameState.Menu;

	// ── Cached references ────────────────────────────────────────

	private RunManager _runManager;
	private PlayerController _player;
	private TerrainManager _terrainManager;
	private BiomeManager _biomeManager;
	private AudioManager _audioManager;
	private HUDController _hud;
	private MainMenuController _mainMenu;
	private GameOverController _gameOver;
	private PauseMenuController _pauseMenu;
	private AvalancheWall _avalancheWall;

	// ── Lifecycle ────────────────────────────────────────────────

	public override void _Ready()
	{
		CurrentState = GameState.Menu;

		// Resolve sibling nodes
		_runManager = GetNodeOrNull<RunManager>("../RunManager");
		_player = GetNodeOrNull<PlayerController>("../Player");
		_terrainManager = GetNodeOrNull<TerrainManager>("../TerrainManager");
		_biomeManager = GetNodeOrNull<BiomeManager>("../BiomeManager");
		_audioManager = GetNodeOrNull<AudioManager>("../AudioManager");
		_hud = GetNodeOrNull<HUDController>("../UILayer/HUD");
		_mainMenu = GetNodeOrNull<MainMenuController>("../UILayer/MainMenu");
		_gameOver = GetNodeOrNull<GameOverController>("../UILayer/GameOver");
		_pauseMenu = GetNodeOrNull<PauseMenuController>("../UILayer/PauseMenu");
		_avalancheWall = GetNodeOrNull<AvalancheWall>("../AvalancheWall");

		// Give TerrainManager a reference to the player
		if (_terrainManager != null && _player != null)
		{
			_terrainManager.PlayerNode = _player;
		}

		// Give HUD a reference to the player for the debug overlay
		if (_hud != null && _player != null)
		{
			_hud.PlayerRef = _player;
		}

		// Give HUD a reference to the terrain manager for module debug info
		if (_hud != null && _terrainManager != null)
		{
			_hud.TerrainRef = _terrainManager;
		}

		// Give AvalancheWall references to player and terrain
		if (_avalancheWall != null)
		{
			if (_player != null) _avalancheWall.PlayerRef = _player;
			if (_terrainManager != null) _avalancheWall.TerrainRef = _terrainManager;
		}

		ConnectSignals();
	}

	private void ConnectSignals()
	{
		// MainMenu → GameManager
		if (_mainMenu != null)
			_mainMenu.PlayPressed += StartGame;

		// GameOver → GameManager
		if (_gameOver != null)
		{
			_gameOver.RetryPressed += ReturnToMenu;
			_gameOver.MenuPressed += ReturnToMenu;
		}

		// PauseMenu → GameManager
		if (_pauseMenu != null)
		{
			_pauseMenu.ResumePressed += ResumeGame;
			_pauseMenu.QuitPressed += ReturnToMenu;
		}

		// PlayerController → HUD (vehicle swap)
		if (_player != null && _hud != null)
		{
			_player.VehicleSwapped += (int newState) =>
			{
				bool isBike = newState == (int)PlayerController.VehicleType.Bike;
				_hud.UpdateVehicleIcon(isBike);
			};

			// PlayerController → HUD (flip points)
			_player.FlipPointsScored += (int points, int flipCount) =>
			{
				_hud.ShowFlipPoints(points, flipCount);
			};
		}

		// PlayerController crash → GameManager end game
		if (_player != null)
			_player.PlayerCrashed += EndGame;

		// AvalancheWall → GameManager end game
		if (_avalancheWall != null)
			_avalancheWall.AvalancheCaughtPlayer += EndGame;

		// RunManager → HUD (score updates)
		if (_runManager != null && _hud != null)
		{
			_runManager.ScoreUpdated += (int newScore) =>
			{
				_hud.UpdateScore(newScore, _runManager.Multiplier);
			};
		}

		// TerrainManager → PlayerController (terrain changes)
		if (_terrainManager != null && _player != null)
		{
			_terrainManager.TerrainChanged += (int newTerrainType) =>
			{
				_player.CurrentTerrain = (TerrainType)newTerrainType;
				if (_runManager != null)
					_runManager.CurrentTerrain = (TerrainType)newTerrainType;
			};
		}

		// GameManager signals → UI visibility
		GameStarted += OnGameStarted;
		GameOver += OnGameOver;
	}

	private void OnGameStarted()
	{
		_player?.ResetForNewRun();
		_terrainManager?.Reset();
		_runManager?.ResetRun();
		_biomeManager?.Reset();

		if (_mainMenu != null) _mainMenu.Visible = false;
		if (_hud != null) _hud.Visible = true;
		if (_gameOver != null) _gameOver.Visible = false;
		if (_pauseMenu != null) _pauseMenu.Visible = false;

		// _avalancheWall?.Activate();
		_audioManager?.PlayMusic();
	}

	private void OnGameOver()
	{
		if (_gameOver != null && _runManager != null)
		{
			_gameOver.ShowScore(_runManager.Score);
		}
		if (_hud != null) _hud.Visible = false;
		_avalancheWall?.Deactivate();
		_audioManager?.StopMusic();
	}

	private void ReturnToMenu()
	{
		CurrentState = GameState.Menu;
		GetTree().Paused = false;
		EmitSignal(SignalName.StateChanged, (int)CurrentState);

		// Reset everything back to the top of the mountain
		_player?.ResetForNewRun();
		_terrainManager?.Reset();
		_runManager?.ResetRun();
		_biomeManager?.Reset();
		_avalancheWall?.Deactivate();

		if (_mainMenu != null) _mainMenu.Visible = true;
		if (_hud != null) _hud.Visible = false;
		if (_gameOver != null) _gameOver.Visible = false;
		if (_pauseMenu != null) _pauseMenu.Visible = false;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel"))
		{
			switch (CurrentState)
			{
				case GameState.Playing:
					PauseGame();
					break;
				case GameState.Paused:
					ResumeGame();
					break;
			}
		}

		// Quick restart with R key when game over
		if (@event.IsActionPressed("restart") && CurrentState == GameState.GameOver)
		{
			StartGame();
		}
	}

	// ── Public API ───────────────────────────────────────────────

	public void StartGame()
	{
		if (CurrentState is not (GameState.Menu or GameState.GameOver))
			return;

		CurrentState = GameState.Playing;
		GetTree().Paused = false;
		EmitSignal(SignalName.GameStarted);
		EmitSignal(SignalName.StateChanged, (int)CurrentState);
	}

	public void PauseGame()
	{
		if (CurrentState != GameState.Playing)
			return;

		CurrentState = GameState.Paused;
		GetTree().Paused = true;
		if (_pauseMenu != null) _pauseMenu.Visible = true;
		EmitSignal(SignalName.StateChanged, (int)CurrentState);
	}

	public void ResumeGame()
	{
		if (CurrentState != GameState.Paused)
			return;

		CurrentState = GameState.Playing;
		GetTree().Paused = false;
		if (_pauseMenu != null) _pauseMenu.Visible = false;
		EmitSignal(SignalName.StateChanged, (int)CurrentState);
	}

	public void EndGame()
	{
		if (CurrentState is not (GameState.Playing or GameState.Paused))
			return;

		CurrentState = GameState.GameOver;
		GetTree().Paused = false;
		EmitSignal(SignalName.GameOver);
		EmitSignal(SignalName.StateChanged, (int)CurrentState);
	}
}
