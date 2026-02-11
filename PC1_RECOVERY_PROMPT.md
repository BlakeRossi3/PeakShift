# PeakShift — PC1 Recovery Prompt

Paste this into a new Claude Code chat on the PC1 machine (or this machine if continuing here).

---

## PROMPT START

You are resuming work on **PeakShift**, a Godot 4.6 C# 2D endless runner. The previous PC1 agent ran out of tokens. Your job: audit what's done, identify gaps, fix issues, and get the project to a buildable/runnable state.

**Repo:** `c:\code\peakshift\PeakShift` (adjust path if different)
**Branch:** `main` has all PC1 + PC2 work merged.

### WHAT PC1 CREATED (on main)
**Scripts (11 files):**
- `Scripts/Core/SignalBus.cs` — Signal name string constants (SHARED CONTRACT) ✅
- `Scripts/Core/TerrainType.cs` — Enum: Snow, Dirt, Ice, Slush ✅
- `Scripts/Core/GameManager.cs` — Game state machine: Menu/Playing/Paused/GameOver ✅
- `Scripts/Core/RunManager.cs` — Distance, score, multiplier tracking ✅
- `Scripts/Player/PlayerController.cs` — CharacterBody2D, auto-forward, vehicle swap, jump, tuck, collision ✅
- `Scripts/Vehicles/VehicleBase.cs` — Abstract: GetSpeedModifier(), GetGravityMultiplier() ✅
- `Scripts/Vehicles/BikeController.cs` — Dirt=1.5x, Snow=0.6x, gravity=1.3 ✅
- `Scripts/Vehicles/SkiController.cs` — Snow=1.5x, Dirt=0.6x, gravity=0.7, tucking ✅
- `Scripts/Terrain/TerrainManager.cs` — Chunk spawning/recycling, terrain change signals ✅
- `Scripts/Terrain/TerrainChunk.cs` — Resource: Type, Difficulty, Length, ObstacleSpawnPoints ✅
- `Scripts/Audio/AudioManager.cs` — SFX dict, music tension stub ✅

**Scenes (10 files):**
- `Scenes/Core/Main.tscn` — Root scene wiring GameManager, RunManager, AudioManager, Player, TerrainManager ✅
- `Scenes/Core/GameManager.tscn`, `RunManager.tscn`, `AudioManager.tscn` — Standalone scenes ✅
- `Scenes/Player/Player.tscn` — CharacterBody2D + Sprite2D + CollisionShape2D ✅
- `Scenes/Player/Bike.tscn`, `Skis.tscn` — Vehicle nodes with sprites ✅
- `Scenes/Player/PlayerCamera.tscn` — Camera2D with smoothing ✅
- `Scenes/Terrain/TerrainChunk.tscn` — StaticBody2D + ColorRect + collision ✅
- `Scenes/Terrain/TerrainManager.tscn` — Node2D with script ✅

### WHAT PC2 CREATED (on main)
**Scripts (14 files):**
- `Scripts/Data/BiomeData.cs` — 5 biome presets (Alpine Meadow, Pine Forest, Frozen Lake, Rocky Ridge, Summit Storm)
- `Scripts/Data/RiderData.cs`, `GearData.cs`, `CurrencyData.cs` — Data stubs
- `Scripts/Core/BiomeManager.cs` — Distance-based biome transitions
- `Scripts/Hazards/HazardBase.cs` — Abstract Area2D: Idle→Warning→Active→Cleanup state machine
- `Scripts/Hazards/Avalanche.cs`, `FallingTree.cs`, `Rockslide.cs`, `Whiteout.cs`
- `Scripts/UI/HUDController.cs`, `MainMenuController.cs`, `GameOverController.cs`, `PauseMenuController.cs`

**Scenes (9 files):**
- `Scenes/Core/BiomeManager.tscn`, 4 hazard scenes, 4 UI scenes

**Art (11 PNG files):**
- `Assets/Art/Hazards/` — 4 placeholder PNGs
- `Assets/Art/UI/` — swap_button, bike_icon, ski_icon, heart_icon
- `Assets/Art/Particles/` — snow, dirt, spark (8x8 dots)

### KNOWN ISSUES TO FIX

**1. Missing art assets referenced by PC1 scenes:**
- `Assets/Art/Characters/player_placeholder.png` — referenced by `Main.tscn` and `Player.tscn` (DOES NOT EXIST)
- `Assets/Art/Vehicles/bike_placeholder.png` — referenced by `Bike.tscn` (DOES NOT EXIST)
- `Assets/Art/Vehicles/skis_placeholder.png` — referenced by `Skis.tscn` (DOES NOT EXIST)
→ Create these placeholder images (simple colored shapes, any method).

**2. Namespace conflict — PC2 has a duplicate TerrainType enum:**
- `Scripts/Data/BiomeData.cs` declares `enum TerrainType` in `PeakShift.Data` namespace
- PC1's canonical version is in `Scripts/Core/TerrainType.cs` under `PeakShift` namespace
→ Remove the duplicate from BiomeData.cs and add `using PeakShift;` or adjust the using.

**3. Namespace mismatch between PC1 and PC2:**
- PC1 uses `namespace PeakShift;` (flat)
- PC2 uses `namespace PeakShift.Data;`, `PeakShift.Core;`, `PeakShift.Hazards;`, `PeakShift.UI;`
→ Decide: either flatten PC2 to `PeakShift` or keep sub-namespaces and add `using` statements. Sub-namespaces are fine, just ensure cross-references compile.

**4. Main.tscn is missing PC2 nodes:**
- No BiomeManager, HUD, MainMenu, GameOver, or PauseMenu in Main.tscn
→ Wire these into the scene tree (or instantiate via code).

**5. Signal wiring is not connected:**
- PC1 scripts emit signals but nothing subscribes to them yet
- PC2 UI controllers reference signals by comment only, no actual connections
→ Wire up: GameManager ↔ MainMenu (PlayPressed→StartGame), GameManager ↔ GameOver (RetryPressed→StartGame, MenuPressed), GameManager ↔ PauseMenu, RunManager → HUD (ScoreUpdated), PlayerController → HUD (VehicleSwapped), TerrainManager → PlayerController (TerrainChanged), BiomeManager ← RunManager distance updates

**6. Player.tscn missing vehicle child nodes:**
- PlayerController expects BikeNode and SkiNode exports but Player.tscn doesn't include Bike/Skis as children
→ Add Bike and Skis as child nodes of Player, or instance their scenes.

**7. TerrainManager.GetPlayerX() returns 0 (stub):**
→ Needs a reference to the Player node to track actual position.

**8. RunManager.UpdateDistance() is never called:**
→ Wire into _PhysicsProcess or have PlayerController call it.

**9. .done_scripts and .done_scenes marker files on main:**
→ Delete these, they're build artifacts.

**10. Cleanup: delete `GameManager.cs` and `GameManager.cs.uid` at project root:**
→ These are the original empty stubs, superseded by `Scripts/Core/GameManager.cs`.

### PRIORITY ORDER
1. Fix missing assets (create placeholder PNGs) — unblocks editor loading
2. Remove duplicate TerrainType enum from BiomeData.cs
3. Delete root-level GameManager.cs stub and .done_* files
4. Wire Player scene with vehicle children
5. Update Main.tscn to include BiomeManager + UI scenes
6. Connect signals between systems
7. Fix TerrainManager player reference
8. Wire RunManager.UpdateDistance into game loop
9. Test build: `dotnet build` in project root
10. Test run in Godot editor

## PROMPT END
