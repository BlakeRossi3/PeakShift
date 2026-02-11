using Godot;

namespace PeakShift;

/// <summary>
/// Singleton node that manages the overall game state machine.
/// States: Menu, Playing, Paused, GameOver.
/// </summary>
public partial class GameManager : Node
{
    /// <summary>Possible game states.</summary>
    public enum GameState
    {
        /// <summary>Main menu / title screen.</summary>
        Menu,

        /// <summary>Active gameplay.</summary>
        Playing,

        /// <summary>Game is paused.</summary>
        Paused,

        /// <summary>Run has ended.</summary>
        GameOver
    }

    // ── Signals ──────────────────────────────────────────────────

    /// <summary>Emitted when a new game run starts.</summary>
    [Signal]
    public delegate void GameStartedEventHandler();

    /// <summary>Emitted when the game ends.</summary>
    [Signal]
    public delegate void GameOverEventHandler();

    /// <summary>Emitted when the game state changes.</summary>
    [Signal]
    public delegate void StateChangedEventHandler(int newState);

    // ── Properties ───────────────────────────────────────────────

    /// <summary>The current state of the game.</summary>
    public GameState CurrentState { get; private set; } = GameState.Menu;

    // ── Lifecycle ────────────────────────────────────────────────

    public override void _Ready()
    {
        CurrentState = GameState.Menu;
        GD.Print("[GameManager] Initialized — state: Menu");
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

    /// <summary>Transition from Menu or GameOver to Playing and begin a new run.</summary>
    public void StartGame()
    {
        if (CurrentState is not (GameState.Menu or GameState.GameOver))
            return;

        CurrentState = GameState.Playing;
        GetTree().Paused = false;
        EmitSignal(SignalName.GameStarted);
        EmitSignal(SignalName.StateChanged, (int)CurrentState);
        GD.Print("[GameManager] Game started");
    }

    /// <summary>Pause the active run.</summary>
    public void PauseGame()
    {
        if (CurrentState != GameState.Playing)
            return;

        CurrentState = GameState.Paused;
        GetTree().Paused = true;
        EmitSignal(SignalName.StateChanged, (int)CurrentState);
        GD.Print("[GameManager] Game paused");
    }

    /// <summary>Resume a paused run.</summary>
    public void ResumeGame()
    {
        if (CurrentState != GameState.Paused)
            return;

        CurrentState = GameState.Playing;
        GetTree().Paused = false;
        EmitSignal(SignalName.StateChanged, (int)CurrentState);
        GD.Print("[GameManager] Game resumed");
    }

    /// <summary>End the current run and transition to GameOver.</summary>
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
