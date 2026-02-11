using Godot;

namespace PeakShift.Data;

[GlobalClass]
public partial class GearData : Resource
{
    [Export] public string Name { get; set; } = "Default Gear";
    [Export] public float SpeedBonus { get; set; } = 0.0f;
    [Export] public float GravityMod { get; set; } = 1.0f;
    [Export] public string Description { get; set; } = "";
}
