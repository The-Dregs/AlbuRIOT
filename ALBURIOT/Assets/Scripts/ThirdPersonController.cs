using UnityEngine;

[
RequireComponent(typeof(CharacterController))]
public class ThirdPersonController : MonoBehaviour
{
	public bool CanAttack => controller != null && controller.isGrounded;
	public float moveSpeed = 6f;
	public float runSpeed = 11f;
	public float rotationSpeed = 12f;
	public float jumpHeight = 2f;
	public float gravity = -15f;
	public Transform cameraPivot; // assign the CameraRig (the camera orbit root)

	private CharacterController controller;
	private Vector3 verticalVelocity;
	private Animator animator;
	private bool isJumping = false;
	private bool isCrouched = false; // placeholder, add crouch logic if needed
	private bool attackPressed = false;

	private bool canMove = true;
	private bool canControl = true;

	public void SetCanMove(bool value)
	{
		canMove = value;
	}

	public void SetCanControl(bool value)
	{
		canControl = value;
		canMove = value;
	}

	void Awake()
	{
		controller = GetComponent<CharacterController>();
		animator = GetComponent<Animator>();
	}


	private PlayerStats playerStats;

	void Start()
	{
		Cursor.lockState = CursorLockMode.Locked;
		Cursor.visible = false;
		playerStats = GetComponent<PlayerStats>();
	}

	void Update()
	{
		if (canControl)
		{
			if (canMove)
			{
				HandleMovement();
			}
		}
		UpdateAnimator();
	}
	void UpdateAnimator()
	{
		if (animator == null) return;

		// Speed: use horizontal movement magnitude (ignore vertical)
		float speed = new Vector3(controller.velocity.x, 0f, controller.velocity.z).magnitude;
		animator.SetFloat("Speed", speed);

	    // IsWalking: moving (forward, backward, or sideways) but not running
		// Consider walking if moving at any speed and not running (regardless of direction)
		bool isRunning = Input.GetKey(KeyCode.LeftShift) && Input.GetAxisRaw("Vertical") > 0.5f && speed > 0.1f;
		bool isWalking = speed > 0.1f && !isRunning;
		animator.SetBool("IsWalking", isWalking);

		// IsRunning: running (Shift + W)
		isRunning = Input.GetKey(KeyCode.LeftShift) && Input.GetAxisRaw("Vertical") > 0.5f && speed > 0.1f;
		animator.SetBool("IsRunning", isRunning);

		// IsJumping: set true when jumping, false when grounded
		animator.SetBool("IsJumping", isJumping);

		// IsCrouched: placeholder, set to false (add crouch logic if needed)
		animator.SetBool("IsCrouched", isCrouched);
	}

	void HandleMovement()
	{
		float h = Input.GetAxisRaw("Horizontal");
		float v = Input.GetAxisRaw("Vertical");


		// movement direction depends on freelook state
		Vector3 move = Vector3.zero;
		var cameraOrbit = cameraPivot.GetComponent<ThirdPersonCameraOrbit>();
		if (cameraOrbit != null)
		{
			if (Input.GetMouseButton(1)) // freelook: move relative to player
			{
				Vector3 playerForward = transform.forward;
				playerForward.y = 0f;
				playerForward.Normalize();
				Vector3 playerRight = transform.right;
				playerRight.y = 0f;
				playerRight.Normalize();
				move = playerForward * v + playerRight * h;
			}
			else // normal: move relative to camera
			{
				Vector3 camForward = cameraPivot.forward;
				camForward.y = 0f;
				camForward.Normalize();
				Vector3 camRight = cameraPivot.right;
				camRight.y = 0f;
				camRight.Normalize();
				move = camForward * v + camRight * h;
			}
		}
		if (move.sqrMagnitude > 1f) move.Normalize();

		// rotate player to match camera yaw, unless in free look
		if (cameraOrbit != null && !Input.GetMouseButton(1)) // not in free look
		{
			Quaternion targetRot = Quaternion.Euler(0f, cameraOrbit.transform.eulerAngles.y, 0f);
			transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
		}

		// running: if Shift and W are pressed, use runSpeed for forward movement
		float currentSpeed = moveSpeed;
		if (playerStats != null)
			currentSpeed += playerStats.speedModifier;
		if (Input.GetKey(KeyCode.LeftShift) && v > 0.5f)
		{
			currentSpeed = runSpeed;
			if (playerStats != null)
				currentSpeed += playerStats.speedModifier;
		}
		Vector3 horizontal = move * currentSpeed;

		// grounding and jumping
		bool isGrounded = controller.isGrounded;
		if (isGrounded && verticalVelocity.y < 0f)
		{
			verticalVelocity.y = -2f;
		}
		if (Input.GetKeyDown(KeyCode.Space) && isGrounded)
		{
			verticalVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
			isJumping = true;
		}
		if (isGrounded && verticalVelocity.y <= 0f)
		{
			isJumping = false;
		}
		// apply gravity
		verticalVelocity.y += gravity * Time.deltaTime;

		// move
		Vector3 finalMove = horizontal + verticalVelocity;
		controller.Move(finalMove * Time.deltaTime);

		// Escape releases cursor
		if (Input.GetKeyDown(KeyCode.Escape))
		{
			Cursor.lockState = CursorLockMode.None;
			Cursor.visible = true;
		}
	}
}




