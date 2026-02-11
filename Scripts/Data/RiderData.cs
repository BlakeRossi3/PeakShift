using Godot;

namespace PeakShift.Data;

[GlobalClass]
public partial class RiderData : Resource
{
    [Export] public string Name { get; set; } = "Default Rider";
    [Export] public string SpritePath { get; set; } = "";
    [Export] public int UnlockCost { get; set; } = 0;
}
