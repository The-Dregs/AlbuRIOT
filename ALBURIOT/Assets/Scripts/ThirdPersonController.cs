using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class ThirdPersonController : MonoBehaviour
{
	public float moveSpeed = 6f;
	public float rotationSpeed = 12f;
	public float jumpHeight = 2f;
	public float gravity = -15f;
	public Transform cameraPivot; // assign the CameraRig (the camera orbit root)

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

		// horizontal move
		Vector3 horizontal = move * moveSpeed;

		// grounding and jumping
		bool isGrounded = controller.isGrounded;
		if (isGrounded && verticalVelocity.y < 0f)
		{
			verticalVelocity.y = -2f;
		}
		if (Input.GetKeyDown(KeyCode.Space) && isGrounded)
		{
			verticalVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
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




