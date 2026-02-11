using Godot;
using PeakShift.Core;
using PeakShift.UI;

namespace PeakShift;

/// <summary>
/// Tracks all run data: distance travelled, score, multiplier, and current terrain.
/// Drives distance/score updates each physics frame when the game is playing.
/// </summary>
public partial class RunManager : Node
{
	// ── Signals ──────────────────────────────────────────────────

	[Signal]
	public delegate void ScoreUpdatedEventHandler(int newScore);

	// ── Exports ──────────────────────────────────────────────────

	[Export]
	public float BaseSpeed { get; set; } = 300f;

	/// <summary>Points awarded per 100 pixels of distance.</summary>
	[Export]
	public int PointsPer100px { get; set; } = 10;

	// ── Properties ───────────────────────────────────────────────

	public float Distance { get; private set; }
	public int Score { get; private set; }
	public float Multiplier { get; set; } = 1.0f;
	public TerrainType CurrentTerrain { get; set; } = TerrainType.Snow;

	// ── Cached references ────────────────────────────────────────

	private GameManager _gameManager;
	private BiomeManager _biomeManager;
	private HUDController _hud;
	private float _distanceSinceLastScore;

	// ── Lifecycle ────────────────────────────────────────────────

	public override void _Ready()
	{
		_gameManager = GetNodeOrNull<GameManager>("../GameManager");
		_biomeManager = GetNodeOrNull<BiomeManager>("../BiomeManager");
		_hud = GetNodeOrNull<HUDController>("../HUD");
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_gameManager == null || _gameManager.CurrentState != GameManager.GameState.Playing)
			return;

		float dt = (float)delta;
		UpdateDistance(dt);

		// Award score based on distance
		_distanceSinceLastScore += dt * BaseSpeed;
		if (_distanceSinceLastScore >= 100f)
		{
			int chunks = (int)(_distanceSinceLastScore / 100f);
			AddScore(chunks * PointsPer100px);
			_distanceSinceLastScore -= chunks * 100f;
		}

		// Update HUD distance display
		_hud?.UpdateDistance(Distance);

		// Drive biome transitions
		_biomeManager?.UpdateDistance(Distance);
	}

	// ── Public API ───────────────────────────────────────────────

	public void ResetRun()
	{
		Distance = 0f;
		Score = 0;
		Multiplier = 1.0f;
		CurrentTerrain = TerrainType.Snow;
		_distanceSinceLastScore = 0f;
		EmitSignal(SignalName.ScoreUpdated, Score);
		GD.Print("[RunManager] Run reset");
	}

	public void AddScore(int points)
	{
		int gained = Mathf.RoundToInt(points * Multiplier);
		Score += gained;
		EmitSignal(SignalName.ScoreUpdated, Score);
	}

	public void UpdateDistance(float delta)
	{
		Distance += delta * BaseSpeed;
	}
}
