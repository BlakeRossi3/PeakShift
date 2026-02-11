using Godot;
using System.Collections.Generic;

namespace PeakShift.Hazards;

public enum HazardState { Idle, Warning, Active, Cleanup }

public abstract partial class HazardBase : Area2D
{
    // Uses SignalBus.HazardWarning ("hazard_warning"), HazardActive ("hazard_active"), HazardCleared ("hazard_cleared")
    [Signal] public delegate void HazardWarningEventHandler(string hazardName);
    [Signal] public delegate void HazardActiveEventHandler(string hazardName);
    [Signal] public delegate void HazardClearedEventHandler(string hazardName);

    [Export] public float WarningDuration { get; set; } = 2.0f;
    [Export] public float ActiveDuration { get; set; } = 3.0f;

    public HazardState CurrentState { get; private set; } = HazardState.Idle;
    public List<string> BiomeCompatibility { get; set; } = new();

    private float _stateTimer = 0f;

    public override void _Process(double delta)
    {
        if (CurrentState == HazardState.Idle) return;

        _stateTimer -= (float)delta;

        if (_stateTimer <= 0f)
        {
            AdvanceState();
        }
    }

    public void Trigger()
    {
        if (CurrentState != HazardState.Idle) return;
        CurrentState = HazardState.Warning;
        _stateTimer = WarningDuration;
        EmitSignal(SignalName.HazardWarning, GetType().Name);
        OnWarning();
    }

    private void AdvanceState()
    {
        switch (CurrentState)
        {
            case HazardState.Warning:
                CurrentState = HazardState.Active;
                _stateTimer = ActiveDuration;
                EmitSignal(SignalName.HazardActive, GetType().Name);
                OnActivate();
                break;
            case HazardState.Active:
                CurrentState = HazardState.Cleanup;
                _stateTimer = 1.0f;
                OnCleanup();
                break;
            case HazardState.Cleanup:
                CurrentState = HazardState.Idle;
                EmitSignal(SignalName.HazardCleared, GetType().Name);
                QueueFree();
                break;
        }
    }

    protected virtual void OnWarning() { }
    protected virtual void OnActivate() { }
    protected virtual void OnCleanup() { }

    public bool IsCompatibleWithBiome(string biomeName)
    {
        return BiomeCompatibility.Count == 0 || BiomeCompatibility.Contains(biomeName);
    }
}
