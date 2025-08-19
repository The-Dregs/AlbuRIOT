using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float jumpHeight = 2f;
    public float gravity = -9.81f;
    public float rotationSpeed = 100f;
    
    [Header("Camera Settings")]
    public Transform cameraTransform;
    public float mouseSensitivity = 2f;
    public float maxLookAngle = 80f;
    
    [Header("Tutorial Settings")]
    public string[] tutorialMessages;
    public GameObject tutorialUI;
    public TextMeshProUGUI tutorialText;
    public int currentTutorialStep = 0;
    
    [Header("Ground Check")]
    public LayerMask groundMask = 1;
    public float groundDistance = 0.4f;
    
    // Private variables
    private CharacterController controller;
    private Vector3 velocity;
    private bool isGrounded;
    private Vector2 moveInput;
    private Vector2 lookInput;
    private bool jumpPressed;
    private bool spacePressed;
    
    // Tutorial variables
    private bool tutorialActive = true;
    private float tutorialTimer = 0f;
    private const float TUTORIAL_STEP_DELAY = 3f;
    
    void Start()
    {
        controller = GetComponent<CharacterController>();
        
        // Lock cursor for camera control
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        
        // Set up camera if not assigned
        if (cameraTransform == null)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                cameraTransform = mainCamera.transform;
            }
        }
        
        // Initialize tutorial
        if (tutorialMessages != null && tutorialMessages.Length > 0)
        {
            ShowTutorialMessage(0);
        }
    }
    
    void Update()
    {
        HandleMovement();
        HandleCamera();
        HandleTutorial();
    }
    
    void HandleMovement()
    {
        // Ground check
        isGrounded = Physics.CheckSphere(transform.position, groundDistance, groundMask);
        
        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }
        
        // Movement
        Vector3 move = transform.right * moveInput.x + transform.forward * moveInput.y;
        controller.Move(move * moveSpeed * Time.deltaTime);
        
        // Jump
        if (jumpPressed && isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        
        // Apply gravity
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }
    
    void HandleCamera()
    {
        if (cameraTransform == null) return;
        
        // Mouse look
        float mouseX = lookInput.x * mouseSensitivity * Time.deltaTime;
        float mouseY = lookInput.y * mouseSensitivity * Time.deltaTime;
        
        // Rotate player horizontally
        transform.Rotate(Vector3.up * mouseX);
        
        // Rotate camera vertically
        float currentRotationX = cameraTransform.localEulerAngles.x;
        float newRotationX = currentRotationX - mouseY;
        
        // Clamp vertical rotation
        if (newRotationX > 180f)
            newRotationX -= 360f;
        
        newRotationX = Mathf.Clamp(newRotationX, -maxLookAngle, maxLookAngle);
        cameraTransform.localEulerAngles = new Vector3(newRotationX, 0f, 0f);
    }
    
    void HandleTutorial()
    {
        if (!tutorialActive || tutorialMessages == null || tutorialMessages.Length == 0)
            return;
            
        tutorialTimer += Time.deltaTime;
        
        // Check for space key press to advance tutorial
        if (spacePressed && tutorialTimer > 1f)
        {
            tutorialTimer = 0f;
            AdvanceTutorial();
        }
        
        // Auto-advance tutorial based on player actions
        if (tutorialTimer > TUTORIAL_STEP_DELAY)
        {
            // Check if player has performed the required action for current step
            bool shouldAdvance = false;
            
            switch (currentTutorialStep)
            {
                case 0: // Welcome message
                    shouldAdvance = true;
                    break;
                case 1: // Movement tutorial
                    shouldAdvance = moveInput.magnitude > 0.1f;
                    break;
                case 2: // Camera tutorial
                    shouldAdvance = lookInput.magnitude > 0.1f;
                    break;
                case 3: // Jump tutorial
                    shouldAdvance = jumpPressed && isGrounded;
                    break;
                case 4: // Final message
                    shouldAdvance = true;
                    break;
            }
            
            if (shouldAdvance)
            {
                AdvanceTutorial();
            }
        }
    }
    
    void AdvanceTutorial()
    {
        currentTutorialStep++;
        
        if (currentTutorialStep >= tutorialMessages.Length)
        {
            CompleteTutorial();
            return;
        }
        
        ShowTutorialMessage(currentTutorialStep);
    }
    
    void ShowTutorialMessage(int step)
    {
        if (tutorialText != null && step < tutorialMessages.Length)
        {
            tutorialText.text = tutorialMessages[step];
        }
        
        if (tutorialUI != null)
        {
            tutorialUI.SetActive(true);
        }
    }
    
    void CompleteTutorial()
    {
        tutorialActive = false;
        
        if (tutorialUI != null)
        {
            tutorialUI.SetActive(false);
        }
        
        // Notify PrologueManager
        PrologueManager prologueManager = FindObjectOfType<PrologueManager>();
        if (prologueManager != null)
        {
            prologueManager.OnTutorialComplete();
        }
    }
    
    // Input System callbacks
    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
        Debug.Log($"Move input received: {moveInput}");
    }
    
    public void OnLook(InputValue value)
    {
        lookInput = value.Get<Vector2>();
        Debug.Log($"Look input received: {lookInput}");
    }
    
    public void OnJump(InputValue value)
    {
        jumpPressed = value.isPressed;
    }
    
    public void OnSpace(InputValue value)
    {
        spacePressed = value.isPressed;
    }
    
    // Public methods for external access
    public void SetTutorialMessages(string[] messages)
    {
        tutorialMessages = messages;
    }
    
    public void SetTutorialUI(GameObject ui)
    {
        tutorialUI = ui;
    }
    
    public void SetTutorialText(TextMeshProUGUI text)
    {
        tutorialText = text;
    }
}
