using Godot;
using PeakShift.Core;
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
        _hud = GetNodeOrNull<HUDController>("../HUD");
        _mainMenu = GetNodeOrNull<MainMenuController>("../MainMenu");
        _gameOver = GetNodeOrNull<GameOverController>("../GameOver");
        _pauseMenu = GetNodeOrNull<PauseMenuController>("../PauseMenu");

        // Give TerrainManager a reference to the player
        if (_terrainManager != null && _player != null)
        {
            _terrainManager.PlayerNode = _player;
        }

        ConnectSignals();

        GD.Print("[GameManager] Initialized — state: Menu");
    }

    private void ConnectSignals()
    {
        // MainMenu → GameManager
        if (_mainMenu != null)
        {
            _mainMenu.PlayPressed += StartGame;
            GD.Print("[GameManager] Connected MainMenu.PlayPressed → StartGame");
        }
        else
        {
            GD.PrintErr("[GameManager] FAILED: MainMenu not found!");
        }

        // GameOver → GameManager
        if (_gameOver != null)
        {
            _gameOver.RetryPressed += StartGame;
            _gameOver.MenuPressed += ReturnToMenu;
            GD.Print("[GameManager] Connected GameOver signals");
        }
        else
        {
            GD.PrintErr("[GameManager] FAILED: GameOver not found!");
        }

        // PauseMenu → GameManager
        if (_pauseMenu != null)
        {
            _pauseMenu.ResumePressed += ResumeGame;
            _pauseMenu.QuitPressed += ReturnToMenu;
            GD.Print("[GameManager] Connected PauseMenu signals");
        }
        else
        {
            GD.PrintErr("[GameManager] FAILED: PauseMenu not found!");
        }

        // PlayerController → HUD (vehicle swap)
        if (_player != null && _hud != null)
        {
            _player.VehicleSwapped += (int newState) =>
            {
                bool isBike = newState == (int)PlayerController.PlayerState.Biking;
                _hud.UpdateVehicleIcon(isBike);
            };
            GD.Print("[GameManager] Connected PlayerController.VehicleSwapped → HUD");
        }

        // PlayerController crash → GameManager end game
        if (_player != null)
        {
            _player.PlayerCrashed += EndGame;
            GD.Print("[GameManager] Connected PlayerController.PlayerCrashed → EndGame");
        }
        else
        {
            GD.PrintErr("[GameManager] FAILED: PlayerController not found!");
        }

        // RunManager → HUD (score updates)
        if (_runManager != null && _hud != null)
        {
            _runManager.ScoreUpdated += (int newScore) =>
            {
                _hud.UpdateScore(newScore, _runManager.Multiplier);
            };
            GD.Print("[GameManager] Connected RunManager.ScoreUpdated → HUD");
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
            GD.Print("[GameManager] Connected TerrainManager.TerrainChanged → Player & RunManager");
        }

        // GameManager signals → UI visibility
        GameStarted += OnGameStarted;
        GameOver += OnGameOver;
        GD.Print("[GameManager] Connected GameManager signals");
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

        _audioManager?.PlayMusic();
        GD.Print("[GameManager] Game started - UI updated, music playing");
    }

    private void OnGameOver()
    {
        if (_gameOver != null && _runManager != null)
        {
            _gameOver.ShowScore(_runManager.Score);
        }
        if (_hud != null) _hud.Visible = false;
        _audioManager?.StopMusic();
        GD.Print("[GameManager] Game over - score shown, music stopped");
    }

    private void ReturnToMenu()
    {
        CurrentState = GameState.Menu;
        GetTree().Paused = false;
        EmitSignal(SignalName.StateChanged, (int)CurrentState);

        if (_mainMenu != null) _mainMenu.Visible = true;
        if (_hud != null) _hud.Visible = false;
        if (_gameOver != null) _gameOver.Visible = false;
        if (_pauseMenu != null) _pauseMenu.Visible = false;

        GD.Print("[GameManager] Returned to menu");
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
    }

    // ── Public API ───────────────────────────────────────────────

    public void StartGame()
    {
        if (CurrentState is not (GameState.Menu or GameState.GameOver))
        {
            GD.PrintErr($"[GameManager] StartGame() called but state is {CurrentState}, must be Menu or GameOver");
            return;
        }

        GD.Print("[GameManager] Starting game...");
        CurrentState = GameState.Playing;
        GetTree().Paused = false;
        EmitSignal(SignalName.GameStarted);
        EmitSignal(SignalName.StateChanged, (int)CurrentState);
        GD.Print("[GameManager] Game started - state changed to Playing");
    }

    public void PauseGame()
    {
        if (CurrentState != GameState.Playing)
            return;

        CurrentState = GameState.Paused;
        GetTree().Paused = true;
        if (_pauseMenu != null) _pauseMenu.Visible = true;
        EmitSignal(SignalName.StateChanged, (int)CurrentState);
        GD.Print("[GameManager] Game paused");
    }

    public void ResumeGame()
    {
        if (CurrentState != GameState.Paused)
            return;

        CurrentState = GameState.Playing;
        GetTree().Paused = false;
        if (_pauseMenu != null) _pauseMenu.Visible = false;
        EmitSignal(SignalName.StateChanged, (int)CurrentState);
        GD.Print("[GameManager] Game resumed");
    }

    public void EndGame()
    {
        if (CurrentState is not (GameState.Playing or GameState.Paused))
            return;

        CurrentState = GameState.GameOver;
        GetTree().Paused = false;
        EmitSignal(SignalName.GameOver);
        EmitSignal(SignalName.StateChanged, (int)CurrentState);
        GD.Print("[GameManager] Game over");
    }
}
