using Godot;

namespace PeakShift;

/// <summary>
/// Camera that dynamically zooms out when the player is airborne
/// so the terrain below stays visible, then smoothly returns to
/// default zoom on landing.
///
/// Also applies a speed-based look-ahead offset so the player
/// can see what's coming.
///
/// Runs in _PhysicsProcess to stay synchronised with the player's
/// CharacterBody2D movement and avoid high-speed visual lag.
/// </summary>
public partial class PlayerCamera : Camera2D
{
	/// <summary>Zoom level when grounded (matches the scene default).</summary>
	[Export]
	public float DefaultZoom { get; set; } = 0.5f;

	/// <summary>Never zoom out further than this.</summary>
	[Export]
	public float MinZoom { get; set; } = 0.12f;

	/// <summary>Extra pixels of padding below the terrain surface.</summary>
	[Export]
	public float BottomMargin { get; set; } = 120f;

	/// <summary>How fast the zoom transitions (per second lerp weight).</summary>
	[Export]
	public float ZoomSpeed { get; set; } = 2.5f;

	/// <summary>
	/// Look-ahead pixels at maximum speed.
	/// Camera leads the player so terrain ahead is visible.
	/// </summary>
	[Export]
	public float MaxLookAhead { get; set; } = 250f;

	/// <summary>Speed (px/s) at which look-ahead reaches maximum.</summary>
	[Export]
	public float LookAheadSpeedRef { get; set; } = 1200f;

	/// <summary>Smoothing speed for the look-ahead offset.</summary>
	[Export]
	public float LookAheadSmoothing { get; set; } = 5f;

	/// <summary>How quickly the camera follows the player position (higher = tighter).</summary>
	[Export]
	public float FollowSpeed { get; set; } = 18f;

	private PlayerController _player;
	private TerrainManager _terrain;
	private float _currentZoom;
	private float _currentLookAhead;

	public override void _Ready()
	{
		_currentZoom = DefaultZoom;
		_player = GetParentOrNull<PlayerController>();
		_terrain = GetNodeOrNull<TerrainManager>("../../TerrainManager");

		// Disable Godot's built-in position smoothing — we handle it manually
		// in _PhysicsProcess so it stays in sync with the player's physics.
		PositionSmoothingEnabled = false;

		// Detach from parent transform so we can position ourselves each frame
		TopLevel = true;

		// Start at player position
		if (_player != null)
			GlobalPosition = _player.GlobalPosition;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_player == null) return;

		float dt = (float)delta;

		// ── Follow player position ─────────────────────────────────
		Vector2 targetPos = _player.GlobalPosition;
		GlobalPosition = GlobalPosition.Lerp(targetPos, 1f - Mathf.Exp(-FollowSpeed * dt));

		// ── Dynamic zoom ───────────────────────────────────────────
		float targetZoom = DefaultZoom;

		if (_terrain != null)
		{
			float playerY = _player.GlobalPosition.Y;
			float playerX = _player.GlobalPosition.X;

			// Check terrain height directly below and slightly ahead
			float terrainBelow = _terrain.GetTerrainHeight(playerX);
			float terrainAhead = _terrain.GetTerrainHeight(playerX + 300f);
			float terrainY = Mathf.Max(terrainBelow, terrainAhead);

			float gap = terrainY - playerY + BottomMargin;

			if (gap > 0f)
			{
				float viewportH = GetViewportRect().Size.Y;
				float neededZoom = viewportH / (2f * gap);
				targetZoom = Mathf.Min(DefaultZoom, neededZoom);
				targetZoom = Mathf.Max(MinZoom, targetZoom);
			}
		}

		_currentZoom = Mathf.Lerp(_currentZoom, targetZoom, 1f - Mathf.Exp(-ZoomSpeed * dt));
		Zoom = new Vector2(_currentZoom, _currentZoom);

		// ── Speed-based look-ahead ─────────────────────────────────
		float speed = _player.MomentumSpeed;
		float targetLookAhead = Mathf.Min(speed / LookAheadSpeedRef, 1f) * MaxLookAhead;
		_currentLookAhead = Mathf.Lerp(_currentLookAhead, targetLookAhead,
			1f - Mathf.Exp(-LookAheadSmoothing * dt));
		Offset = new Vector2(_currentLookAhead, 0f);
	}
}
