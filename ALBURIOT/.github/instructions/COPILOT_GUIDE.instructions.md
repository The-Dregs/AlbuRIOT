---
applyTo: '**'
---
Copilot Instructions for AlbuRIOT
Project Overview
AlbuRIOT is a multiplayer survival game inspired by Philippine mythology. Key features include:

Procedural map generation using Perlin noise (TerrainGenerator.cs)
Behavior Tree AI for enemy mechanics (EnemyController.cs)
Power-stealing mechanic: Players gain temporary abilities from defeated mythological creatures
Multiplayer networking for cooperative and competitive modes
Culturally inspired assets (visuals, audio)
Architecture & Major Components
Assets/Scripts/: Main gameplay logic, organized by domain (Player, Enemies, Managers, UI, etc.)
TerrainGenerator.cs: Generates dynamic island terrain with biomes and resource placement using Perlin noise.
EnemyController.cs: Enemy AI with detection, movement, attack, and health/damage logic.
PlayerStats.cs: Player health, stamina, equipment, and stat modification.
PrologueManager.cs: Handles tutorial flow and scene transitions.
Power-Stealing Mechanic: Implemented via stat changes and ability acquisition in player/enemy scripts.
Procedural Generation: Terrain, resources, and enemy spawns are randomized for replayability.
Multiplayer: Network logic is present (see Managers/LobbyManager.cs and related scripts).
Developer Workflows
Player Setup: See README_PlayerSetup.md for step-by-step prefab and input configuration.
Testing: Functional requirements include validating power-stealing, procedural generation, AI adaptability, and multiplayer stability. Use Unity's Play Mode and custom test cases.
Debugging: Common issues (movement, camera, input, tutorial) are documented in README_PlayerSetup.md.
Customization: Player movement, camera, and tutorial settings are adjustable via inspector fields.
Project-Specific Conventions
Scene Management: Use PrologueManager for tutorial and transition logic.
Stat Modifiers: Equipment and power-stealing update player stats via ApplyEquipment/RemoveEquipment methods.
Enemy AI: Enemies use detection ranges and attack cooldowns; expand with custom behaviors as needed.
Procedural Content: All terrain and resource placement should use Perlin noise and radial falloff for island shape.
UI/UX: Tutorial and feedback are integrated with PrologueManager and UI prefabs.
Integration Points
External Assets: Prefabs for trees, rocks, water, and characters are referenced in generator and setup scripts.
Input System: Uses Unity's new Input System (PlayerInput.inputactions).
TextMeshPro: Used for UI text (tutorials, damage numbers).
Key Files & Directories
PrologueManager.cs: Tutorial and scene flow
EnemyController.cs: Enemy AI and combat
TerrainGenerator.cs: Procedural map generation
PlayerStats.cs: Player stat logic
README_PlayerSetup.md: Player setup and troubleshooting
Patterns & Examples
Stat modification: PlayerStats.ApplyEquipment(ItemData item)
Procedural terrain: TerrainGenerator.GenerateTerrain()
Enemy attack logic: EnemyController.TryAttack()
Tutorial flow: PrologueManager.OnTutorialComplete()

# Copilot Coding Guidelines for ALBURIOT

Use this file to specify your preferences and requirements for Copilot's code suggestions and edits. Fill out or update any section as needed!


## CODING
-when you write a code that there are stuff needed to assign in unity, tell me a step by step instruction on how to assign it in unity.
-always write code in a very structured way so that i can code better in unity.
-please when you're writing a code, please make sure that you are reading the whole script to avoid any errors please.
-when i tell you to code something, make sure that the code is able to handle multiplayer because i need this game to be a multiplayer game. also make sure that it is scalable.

-when i tell you to fix something, automatically update my code and no need to ask me for permission and keep iterating until the code is fixed.

## 1. Camera & Player Movement
- Preferred camera style: 3rd person
- the camera movement is good now

## 2. Code Style
- Indentation (tabs/spaces, size): normal coding, but for the actions like attacking and such, put a debug message for better coding experience
- Naming conventions (variables, classes, methods): normal like what you were doing
- Comment style: all lowercase
- File organization: very structured so that i can code better in unity

## 3. Unity/Project Structure
- Folder structure preferences: very structured so that i can code better in unity
- ScriptableObject usage:
- Prefab conventions: add a folder for a specific category of objects and prefabs

## 4. Gameplay/Design
- make sure that the game and codes your providing will be able to handle multiplayer because i need this game to be a multiplayer game
- has an inventory system per player
- Player controls: wasd 
- UI/UX notes: refer to the storyline.txt and alburiot.txt
- Art/audio integration: refer to the storyline.txt and alburiot.txt

## 5. Other Preferences
- Testing/validation:
- Performance considerations:
- Anything else: refer to the storyline.txt and alburiot.txt

---

