using System.Collections.Generic;
using Godot;

namespace PeakShift;

/// <summary>
/// Autoload singleton that manages all game audio: SFX playback, music
/// tension control, and named sound effect constants.
/// </summary>
public partial class AudioManager : Node
{
    // ── SFX name constants ───────────────────────────────────────

    /// <summary>Sound effect: vehicle swap.</summary>
    public const string SfxSwap = "Swap";

    /// <summary>Sound effect: player jump.</summary>
    public const string SfxJump = "Jump";

    /// <summary>Sound effect: player crash.</summary>
    public const string SfxCrash = "Crash";

    /// <summary>Sound effect: snow terrain ambience.</summary>
    public const string SfxTerrainSnow = "TerrainSnow";

    /// <summary>Sound effect: dirt terrain ambience.</summary>
    public const string SfxTerrainDirt = "TerrainDirt";

    /// <summary>Sound effect: speed boost.</summary>
    public const string SfxBoost = "Boost";

    // ── State ────────────────────────────────────────────────────

    /// <summary>Dictionary of named AudioStreamPlayer nodes for SFX.</summary>
    private readonly Dictionary<string, AudioStreamPlayer> _sfxPlayers = new();

    /// <summary>Current music tension level (0.0 = calm, 1.0 = intense).</summary>
    public float MusicTension { get; private set; }

    // ── Lifecycle ────────────────────────────────────────────────

    public override void _Ready()
    {
        // Discover any AudioStreamPlayer children and register them by name
        foreach (var child in GetChildren())
        {
            if (child is AudioStreamPlayer player)
            {
                _sfxPlayers[player.Name] = player;
            }
        }

        GD.Print($"[AudioManager] Initialized with {_sfxPlayers.Count} SFX players");
    }

    // ── Public API ───────────────────────────────────────────────

    /// <summary>
    /// Play a sound effect by name. If no AudioStreamPlayer is found,
    /// prints to console as a stub.
    /// </summary>
    /// <param name="name">The SFX name constant (e.g. <see cref="SfxSwap"/>).</param>
    public void PlaySfx(string name)
    {
        if (_sfxPlayers.TryGetValue(name, out var player))
        {
            player.Play();
        }
        else
        {
            GD.Print($"[AudioManager] PlaySFX stub: {name}");
        }
    }

    /// <summary>
    /// Stop a sound effect by name.
    /// </summary>
    /// <param name="name">The SFX name constant.</param>
    public void StopSfx(string name)
    {
        if (_sfxPlayers.TryGetValue(name, out var player))
        {
            player.Stop();
        }
        else
        {
            GD.Print($"[AudioManager] StopSFX stub: {name}");
        }
    }

    /// <summary>
    /// Set the music tension level. 0.0 = calm, 1.0 = intense.
    /// Stub — will eventually crossfade or blend music layers.
    /// </summary>
    /// <param name="tension">Tension value clamped between 0 and 1.</param>
    public void SetMusicTension(float tension)
    {
        MusicTension = Mathf.Clamp(tension, 0f, 1f);
        GD.Print($"[AudioManager] Music tension set to {MusicTension:F2}");
    }

    /// <summary>Start playing background music (stub).</summary>
    public void PlayMusic()
    {
        GD.Print("[AudioManager] PlayMusic stub");
    }

    /// <summary>Stop background music (stub).</summary>
    public void StopMusic()
    {
        GD.Print("[AudioManager] StopMusic stub");
    }
}
