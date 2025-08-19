# Player Movement Setup for Prologue Scene

This guide explains how to set up the player character with movement controls in your prologue scene.

## What's Been Added

1. **PlayerMovement.cs** - Main movement script with:
   - WASD movement controls
   - Mouse look camera control
   - Jump functionality
   - Tutorial integration
   - Character controller integration

2. **PlayerSetup.cs** - Helper script for easy player setup

3. **Updated PlayerInput.inputactions** - Input system with:
   - WASD movement
   - Mouse look
   - Jump (Space)
   - Tutorial advance (Space)

## How to Set Up Your Player Character

### Option 1: Using the HumanMale_Character_Free Prefab

1. **Open your PROLOGUE scene**
2. **Add the PlayerSetup script to an empty GameObject** in your scene
3. **Assign the HumanMale_Character_Free prefab** to the `characterPrefab` field in the PlayerSetup component
4. **Create a spawn point** by creating an empty GameObject named "PlayerSpawnPoint" where you want the player to start
5. **Run the scene** - the PlayerSetup script will automatically set up everything

### Option 2: Manual Setup

1. **Drag the HumanMale_Character_Free prefab** into your PROLOGUE scene
2. **Add a CharacterController component** to the character if it doesn't have one
3. **Add the PlayerMovement script** to the character
4. **Set up the camera**:
   - Create a Camera as a child of the character
   - Position it at the head level (around Y = 1.6)
   - Assign it to the `cameraTransform` field in PlayerMovement
5. **Add a PlayerInput component** to the character
6. **Assign the PlayerInput.inputactions** asset to the PlayerInput component

## Controls

- **WASD** - Move around
- **Mouse** - Look around
- **Space** - Jump
- **Space** - Advance tutorial (when tutorial is active)

## Tutorial Integration

The PlayerMovement script automatically integrates with the existing PrologueManager:

- Tutorial messages are displayed automatically
- Tutorial advances when you perform the required actions
- Tutorial completion triggers the scene transition

## Troubleshooting

### Character doesn't move
- Make sure the CharacterController component is attached
- Check that the PlayerInput component has the correct input actions assigned
- Verify the PlayerMovement script is attached

### Camera doesn't follow
- Ensure the camera is a child of the player character
- Check that the `cameraTransform` field is assigned in PlayerMovement

### Input not working
- Make sure the PlayerInput.inputactions asset is properly assigned
- Check that the input actions are enabled in the PlayerInput component

### Tutorial not showing
- Verify the tutorial UI elements are assigned in the PrologueManager
- Check that the tutorial messages array is populated

## Customization

You can adjust the movement settings in the PlayerMovement component:
- `moveSpeed` - How fast the character moves
- `jumpHeight` - How high the character jumps
- `mouseSensitivity` - How fast the camera turns
- `maxLookAngle` - Maximum up/down look angle

## Notes

- The cursor is automatically locked for camera control
- The character uses Unity's CharacterController for physics
- Ground detection is automatic
- The tutorial system is integrated with the existing PrologueManager


