using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerSetup : MonoBehaviour
{
    [Header("Character Setup")]
    public GameObject characterPrefab;
    public Transform spawnPoint;
    
    [Header("Camera Setup")]
    public Camera playerCamera;
    public Vector3 cameraOffset = new Vector3(0, 1.6f, 0);
    
    [Header("Input Setup")]
    public PlayerInput playerInput;
    
    private GameObject playerInstance;
    private PlayerMovement playerMovement;
    
    void Start()
    {
        SetupPlayer();
    }
    
    public void SetupPlayer()
    {
        // Find spawn point if not assigned
        if (spawnPoint == null)
        {
            GameObject spawnPointObj = GameObject.Find("PlayerSpawnPoint");
            if (spawnPointObj != null)
            {
                spawnPoint = spawnPointObj.transform;
            }
            else
            {
                // Create a default spawn point
                GameObject defaultSpawn = new GameObject("PlayerSpawnPoint");
                defaultSpawn.transform.position = Vector3.up * 2f;
                spawnPoint = defaultSpawn.transform;
            }
        }
        
        // Instantiate character if prefab is assigned
        if (characterPrefab != null)
        {
            playerInstance = Instantiate(characterPrefab, spawnPoint.position, spawnPoint.rotation);
        }
        else
        {
            // Create a simple player if no prefab is assigned
            CreateSimplePlayer();
        }
        
        // Set up camera
        SetupCamera();
        
        // Set up input
        SetupInput();
        
        // Set up movement component
        SetupMovement();
    }
    
    void CreateSimplePlayer()
    {
        // Create a simple player object
        playerInstance = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        playerInstance.name = "Player";
        playerInstance.transform.position = spawnPoint.position;
        
        // Add character controller
        CharacterController controller = playerInstance.GetComponent<CharacterController>();
        if (controller == null)
        {
            controller = playerInstance.AddComponent<CharacterController>();
        }
        
        // Remove the primitive collider since we're using CharacterController
        Collider primitiveCollider = playerInstance.GetComponent<Collider>();
        if (primitiveCollider != null)
        {
            DestroyImmediate(primitiveCollider);
        }
    }
    
    void SetupCamera()
    {
        // Find or create camera
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
            if (playerCamera == null)
            {
                GameObject cameraObj = new GameObject("PlayerCamera");
                playerCamera = cameraObj.AddComponent<Camera>();
                cameraObj.AddComponent<AudioListener>();
            }
        }
        
        // Position camera relative to player
        if (playerInstance != null)
        {
            playerCamera.transform.SetParent(playerInstance.transform);
            playerCamera.transform.localPosition = cameraOffset;
            playerCamera.transform.localRotation = Quaternion.identity;
        }
    }
    
    void SetupInput()
    {
        // Add PlayerInput component if not present
        if (playerInput == null)
        {
            playerInput = playerInstance.GetComponent<PlayerInput>();
            if (playerInput == null)
            {
                playerInput = playerInstance.AddComponent<PlayerInput>();
            }
        }
        
        // Set up input actions
        if (playerInput.actions == null)
        {
            // Try to find the PlayerInput asset
            var inputActions = Resources.Load<InputActionAsset>("PlayerInput");
            if (inputActions == null)
            {
                // Try to load from the Scripts folder
                inputActions = Resources.Load<InputActionAsset>("Assets/Scripts/PlayerInput");
            }
            
            if (inputActions == null)
            {
                // Try to find it in the project
                inputActions = UnityEngine.Object.FindObjectOfType<InputActionAsset>();
            }
            
            if (inputActions == null)
            {
                Debug.LogError("Could not find PlayerInput.inputactions asset. Please assign it manually in the PlayerInput component.");
            }
            else
            {
                playerInput.actions = inputActions;
            }
        }
        
        // Set behavior
        playerInput.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
        
        // Enable the Player action map
        if (playerInput.actions != null)
        {
            playerInput.actions.Enable();
        }
    }
    
    void SetupMovement()
    {
        // Add PlayerMovement component
        playerMovement = playerInstance.GetComponent<PlayerMovement>();
        if (playerMovement == null)
        {
            playerMovement = playerInstance.AddComponent<PlayerMovement>();
        }
        
        // Set camera reference
        if (playerCamera != null)
        {
            playerMovement.cameraTransform = playerCamera.transform;
        }
    }
    
    // Public method to get the player instance
    public GameObject GetPlayerInstance()
    {
        return playerInstance;
    }
    
    // Public method to get the player movement component
    public PlayerMovement GetPlayerMovement()
    {
        return playerMovement;
    }
    
    // Public method to set tutorial UI
    public void SetTutorialUI(GameObject tutorialUI, TMPro.TextMeshProUGUI tutorialText)
    {
        if (playerMovement != null)
        {
            playerMovement.SetTutorialUI(tutorialUI);
            playerMovement.SetTutorialText(tutorialText);
        }
    }
}
