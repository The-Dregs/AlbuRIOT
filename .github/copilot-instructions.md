# Copilot Instructions for AlbuRIOT

## Project Overview
AlbuRIOT is a multiplayer survival game inspired by Philippine mythology. Key features:
- Procedural map generation (Perlin noise, radial falloff)
- Behavior Tree AI for enemies (custom movesets, VFX/SFX integration)
- Power-stealing mechanic (ability gain)
- Multiplayer networking (co-op/competitive)
- Culturally inspired assets (visual/audio)

## Architecture & Major Components
- `Assets/Scripts/`: Main gameplay logic, organized by domain (Player, Enemies, Managers, UI, etc.)
- `Assets/Enemies/`: Enemy-specific logic, including AI behavior and attack patterns
- `TerrainGenerator.cs`: Dynamic terrain/biome/resource generation
- `EnemyController.cs`: Enemy AI, detection, movement, attack, health/damage
- `PlayerStats.cs`: Player health, stamina, equipment, stat modification
- `PrologueManager.cs`: Tutorial flow, scene transitions
- Power-stealing: Stat changes/ability acquisition in player/enemy scripts
- Multiplayer: Network logic in Managers/LobbyManager.cs and related scripts

## Developer Workflows
- **Player Setup**: See `README_PlayerSetup.md` for prefab/input configuration
- **Testing**: Validate power-stealing, procedural generation, AI adaptability, multiplayer stability (Unity Play Mode, custom tests)
- **Debugging**: Common issues (movement, camera, input, tutorial) in `README_PlayerSetup.md`
- **Customization**: Player/camera/tutorial settings via inspector fields

## Coding Conventions
- **Unity Assignment**: If code requires Unity assignment (e.g., prefabs, VFX), provide step-by-step Unity instructions
- **Structure**: Organize code/files for clarity and scalability
- **Multiplayer**: All gameplay logic must support multiplayer and be scalable
- **Iteration**: When fixing code, update automatically and keep iterating until resolved
- **Debugging**: Add debug messages for actions (e.g., attacks)
- **Comments**: Use all lowercase for comments
- **Naming**: Use standard Unity/C# conventions

## Gameplay/Design
- **Inventory**: Each player has an inventory system
- **Controls**: WASD movement
- **Camera**: 3rd person (see Player setup)
- **UI/UX**: Integrate with storyline.txt and alburiot.txt
- **Art/Audio**: Reference storyline.txt and alburiot.txt for integration

## Integration Points
- **External Assets**: Prefabs for trees, rocks, water, characters referenced in generator/setup scripts
- **Input System**: Unity's new Input System (`PlayerInput.inputactions`)
- **TextMeshPro**: For UI text (tutorials, damage numbers)

## Key Files & Directories
- `PrologueManager.cs`: Tutorial/scene flow
- `EnemyController.cs`: Enemy AI/combat
- `TerrainGenerator.cs`: Procedural map generation
- `PlayerStats.cs`: Player stat logic
- `README_PlayerSetup.md`: Player setup/troubleshooting
- `Assets/docs/CHAPTER 1 - 3.md`: Narrative, level design, enemy placements

## Patterns & Examples
- Stat modification: `PlayerStats.ApplyEquipment(ItemData item)`
- Procedural terrain: `TerrainGenerator.GenerateTerrain()`
- Enemy attack logic: `EnemyController.TryAttack()`
- Tutorial flow: `PrologueManager.OnTutorialComplete()`

## Additional Notes
- Refer to storyline.txt and alburiot.txt for narrative, UI, and art/audio conventions
- Maintain structured folders for prefabs/assets/scripts
- All code must be multiplayer-ready and scalable
- For new features, follow the conventions above and document any Unity assignment steps
