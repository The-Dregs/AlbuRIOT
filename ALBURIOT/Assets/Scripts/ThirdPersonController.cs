using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class ThirdPersonController : MonoBehaviour
{
	[Header("Movement Settings")]
	public float moveSpeed = 6f;
	public float rotationSpeed = 10f;
	public float jumpHeight = 2f;
	public float gravity = -15f;

	[Header("References")]
	public Transform cameraPivot; // assign the CameraRig or Pivot to orient movement

	private CharacterController controller;
	private Vector3 verticalVelocity;

	void Awake()
	{
		controller = GetComponent<CharacterController>();
	}

	void Start()
	{
		Cursor.lockState = CursorLockMode.Locked;
		Cursor.visible = false;
	}

	void Update()
	{
		HandleMovement();
	}

	void HandleMovement()
	{
		// Read input
		float h = Input.GetAxisRaw("Horizontal");
		float v = Input.GetAxisRaw("Vertical");

		// Movement relative to camera yaw, unless in free look (right mouse held)
		Vector3 camForward = Vector3.forward;
		Vector3 camRight = Vector3.right;
		bool rightMouseHeld = Input.GetMouseButton(1);
		if (!rightMouseHeld && cameraPivot != null)
		{
			Vector3 forward = cameraPivot.forward;
			forward.y = 0f;
			forward.Normalize();
			Vector3 right = cameraPivot.right;
			right.y = 0f;
			right.Normalize();
			camForward = forward;
			camRight = right;
		}
		else
		{
			// Use player's own forward/right
			camForward = transform.forward;
			camRight = transform.right;
		}

		Vector3 move = camForward * v + camRight * h;
		if (move.sqrMagnitude > 1f) move.Normalize();

		// Rotate to face movement direction
		if (move.sqrMagnitude > 0.0001f)
		{
			Quaternion targetRot = Quaternion.LookRotation(move, Vector3.up);
			transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
		}

		// Horizontal move
		Vector3 horizontal = move * moveSpeed;

		// Grounding and jumping
		bool isGrounded = controller.isGrounded;
		if (isGrounded && verticalVelocity.y < 0f)
		{
			verticalVelocity.y = -2f; // small downward force keeps controller grounded
		}

		if (Input.GetKeyDown(KeyCode.Space) && isGrounded)
		{
			verticalVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
		}

		// Apply gravity
		verticalVelocity.y += gravity * Time.deltaTime;

		// Move
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


