using UnityEngine;
using UnityEngine.InputSystem;

public class InputDebugger : MonoBehaviour
{
    [Header("Debug Settings")]
    public bool showInputDebug = true;
    public bool showPlayerInputInfo = true;
    
    private PlayerInput playerInput;
    private PlayerMovement playerMovement;
    
    void Start()
    {
        // Find the player components
        playerInput = FindObjectOfType<PlayerInput>();
        playerMovement = FindObjectOfType<PlayerMovement>();
        
        if (showPlayerInputInfo)
        {
            DebugPlayerInputInfo();
        }
    }
    
    void Update()
    {
        if (showInputDebug)
        {
            // Check for direct keyboard input
            Vector2 keyboardInput = new Vector2(
                Input.GetAxis("Horizontal"),
                Input.GetAxis("Vertical")
            );
            
            if (keyboardInput.magnitude > 0.1f)
            {
                Debug.Log($"Direct keyboard input: {keyboardInput}");
            }
            
            // Check for mouse input
            Vector2 mouseInput = new Vector2(
                Input.GetAxis("Mouse X"),
                Input.GetAxis("Mouse Y")
            );
            
            if (mouseInput.magnitude > 0.1f)
            {
                Debug.Log($"Direct mouse input: {mouseInput}");
            }
        }
    }
    
    void DebugPlayerInputInfo()
    {
        if (playerInput == null)
        {
            Debug.LogError("No PlayerInput component found in scene!");
            return;
        }
        
        Debug.Log($"PlayerInput found: {playerInput.name}");
        
        if (playerInput.actions == null)
        {
            Debug.LogError("PlayerInput has no actions assigned!");
        }
        else
        {
            Debug.Log($"PlayerInput actions assigned: {playerInput.actions.name}");
            
            // Check if the Player action map is enabled
            var playerActionMap = playerInput.actions.FindActionMap("Player");
            if (playerActionMap != null)
            {
                Debug.Log($"Player action map enabled: {playerActionMap.enabled}");
            }
            else
            {
                Debug.LogError("Player action map not found!");
            }
        }
        
        if (playerMovement == null)
        {
            Debug.LogError("No PlayerMovement component found in scene!");
        }
        else
        {
            Debug.Log($"PlayerMovement found: {playerMovement.name}");
            
            // Check if character controller is attached
            CharacterController controller = playerMovement.GetComponent<CharacterController>();
            if (controller == null)
            {
                Debug.LogError("No CharacterController found on PlayerMovement!");
            }
            else
            {
                Debug.Log($"CharacterController found and enabled: {controller.enabled}");
            }
        }
    }
    
    // Public method to manually test input
    [ContextMenu("Test Input System")]
    public void TestInputSystem()
    {
        DebugPlayerInputInfo();
        
        if (playerInput != null && playerInput.actions != null)
        {
            Debug.Log("Attempting to enable input actions...");
            playerInput.actions.Enable();
            
            var playerActionMap = playerInput.actions.FindActionMap("Player");
            if (playerActionMap != null)
            {
                playerActionMap.Enable();
                Debug.Log("Player action map enabled successfully");
            }
        }
    }
}


