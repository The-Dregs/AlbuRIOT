using UnityEngine;

[RequireComponent(typeof(Animator))]
public class PlayerAnimationController : MonoBehaviour
{
    [Header("Animation Settings")]
    public float animationBlendSpeed = 8f;
    public float animationThreshold = 0.1f;
    
    // Animation parameter names - match your Animator Controller
    private const string IS_MOVING = "isMoving";
    private const string MOVE_SPEED = "MoveSpeed";
    private const string IS_GROUNDED = "isGrounded";
    private const string JUMP_TRIGGER = "Jump";
    private const string FALL_TRIGGER = "Fall";
    
    // Components
    private Animator animator;
    private SimplePlayerMovement movementScript;
    private CharacterController characterController;
    
    // Animation state tracking
    private bool wasGrounded = true;
    private bool wasMoving = false;
    
    void Start()
    {
        // Get components
        animator = GetComponent<Animator>();
        movementScript = GetComponent<SimplePlayerMovement>();
        characterController = GetComponent<CharacterController>();
        
        if (animator == null)
        {
            Debug.LogError("No Animator component found on player!");
        }
        else
        {
            Debug.Log($"Animator found! Controller: {animator.runtimeAnimatorController}");
        }
        
        Debug.Log("PlayerAnimationController initialized!");
    }
    
    void Update()
    {
        if (animator == null) return;
        
        UpdateMovementAnimations();
        UpdateJumpAnimations();
    }
    
    void UpdateMovementAnimations()
    {
        // Get movement input from the movement script
        Vector2 moveInput = Vector2.zero;
        if (movementScript != null)
        {
            // Use the public property from SimplePlayerMovement
            moveInput = movementScript.MoveInput;
        }
        
        // Calculate movement speed
        float moveSpeed = moveInput.magnitude;
        
        // Determine if moving - use a lower threshold for better detection
        bool isMoving = moveSpeed > 0.01f; // Lower threshold for better detection
        
        // Update animator parameters
        animator.SetBool(IS_MOVING, isMoving);
        animator.SetFloat(MOVE_SPEED, moveSpeed);
        
        // Debug movement - only log when state changes
        if (isMoving != wasMoving)
        {
            Debug.Log($"Movement state changed: {isMoving}, Speed: {moveSpeed}, Input: {moveInput}");
        }
        
        wasMoving = isMoving;
    }
    
    void UpdateJumpAnimations()
    {
        if (movementScript == null) return;
        
        // Get grounded state from movement script
        bool isGrounded = movementScript.IsGrounded;
        
        // Update grounded parameter
        animator.SetBool(IS_GROUNDED, isGrounded);
        
        // Handle jump trigger
        if (movementScript != null)
        {
            // Check if jump was pressed (we'll need to make this accessible)
            bool jumpPressed = false;
            // For now, we'll detect jump by checking if we were grounded and now we're not
            if (wasGrounded && !isGrounded)
            {
                jumpPressed = true;
            }
            
            if (jumpPressed)
            {
                animator.SetTrigger(JUMP_TRIGGER);
                Debug.Log("Jump animation triggered!");
            }
        }
        
        // Handle fall trigger - only trigger when actually falling, not when grounded
        if (!isGrounded && wasGrounded && characterController.velocity.y < -1f)
        {
            animator.SetTrigger(FALL_TRIGGER);
            Debug.Log("Fall animation triggered!");
        }
        
        // Reset fall trigger when grounded
        if (isGrounded && !wasGrounded)
        {
            // Clear the fall trigger by setting it to false
            animator.ResetTrigger(FALL_TRIGGER);
            Debug.Log("Fall animation reset - grounded!");
        }
        
        wasGrounded = isGrounded;
    }
    
    // Public method to trigger jump animation (called from movement script)
    public void TriggerJumpAnimation()
    {
        if (animator != null)
        {
            animator.SetTrigger(JUMP_TRIGGER);
            Debug.Log("Jump animation triggered externally!");
        }
    }
    
    // Public method to trigger fall animation
    public void TriggerFallAnimation()
    {
        if (animator != null)
        {
            animator.SetTrigger(FALL_TRIGGER);
            Debug.Log("Fall animation triggered externally!");
        }
    }
    
    // Public method to reset fall animation
    public void ResetFallAnimation()
    {
        if (animator != null)
        {
            animator.ResetTrigger(FALL_TRIGGER);
            Debug.Log("Fall animation reset externally!");
        }
    }
    
    // Test method to manually trigger animations
    public void TestAnimations()
    {
        if (animator != null)
        {
            Debug.Log("Testing animations...");
            Debug.Log($"IsMoving: {animator.GetBool(IS_MOVING)}");
            Debug.Log($"MoveSpeed: {animator.GetFloat(MOVE_SPEED)}");
            Debug.Log($"IsGrounded: {animator.GetBool(IS_GROUNDED)}");
            
            // Test setting movement
            animator.SetBool(IS_MOVING, true);
            Debug.Log("Set IsMoving to true");
        }
    }
}
