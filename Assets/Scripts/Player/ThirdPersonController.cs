using Photon.Pun;
using UnityEngine;

[
RequireComponent(typeof(CharacterController))]
public class ThirdPersonController : MonoBehaviourPun
{
	public bool CanAttack => controller != null && controller.isGrounded;
	public float moveSpeed = 6f;
	public float runSpeed = 11f;
	public float rotationSpeed = 12f;
	public float jumpHeight = 2f;
	public float gravity = -15f;
	public Transform cameraPivot; // assign the CameraRig (the camera orbit root)

	// roll / slide settings
	[Header("roll / slide")]
	public KeyCode rollKey = KeyCode.LeftControl;
	public KeyCode alternateRollKey = KeyCode.C;
	public float rollSpeed = 14f;
	public float rollDuration = 0.45f;
	public float rollCooldown = 0.6f;
	public bool allowRightCtrlAlso = true; // convenience input
	public int rollStaminaCost = 15;

	[Header("jump settings")]
	public int jumpStaminaCost = 10;

	[Header("running stamina usage")]
	[Tooltip("Stamina drained per second while running (Left Shift + forward). Set small value, e.g., 2-5.")]
	public float runningStaminaDrainPerSecond = 3f;
	private float runningStaminaDrainAccumulator = 0f;

	private CharacterController controller;
	private Vector3 verticalVelocity;
	private Animator animator;
	private bool isJumping = false;
	private bool isCrouched = false; // placeholder, add crouch logic if needed
	private bool attackPressed = false;
	private bool isRunning = false; // centralized running state
	public bool IsRunning => isRunning;
	public bool IsRolling => isRolling;
	private PlayerCombat combat;

	// roll state
	private bool isRolling = false;
	private float rollTimer = 0f;
	private float rollCooldownTimer = 0f;
	private Vector3 rollDirection = Vector3.zero;

	private bool canMove = true;
	private bool canControl = true;

	// when true, prevent the character from rotating to face movement
	// REMOVE rotationLocked and SetRotationLocked; always allow facing movement direction

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
		playerStats = GetComponent<PlayerStats>();
		combat = GetComponent<PlayerCombat>();

		// Only enable camera/audio for local player
		if (photonView != null && !photonView.IsMine)
		{
			Camera myCam = GetComponentInChildren<Camera>();
			AudioListener myListener = GetComponentInChildren<AudioListener>();
			if (myCam != null) myCam.enabled = false;
			if (myListener != null) myListener.enabled = false;
		}
		else
		{
			Cursor.lockState = CursorLockMode.Locked;
			Cursor.visible = false;
		}
	}

	void Update()
{
	if (Photon.Pun.PhotonNetwork.InRoom && photonView != null && !photonView.IsMine)
		return; // Only allow local player to control

		// always tick roll timers and stamina regen block regardless of movement gating
		TickRollAndStaminaRegenBlock();

	// block movement input if PauseMenu is open
	bool blockMovementInput = LocalUIManager.Instance != null && LocalUIManager.Instance.IsOwner("PauseMenu");
	if (canControl)
	{
		if (canMove)
		{
			HandleMovement();
		}
		else
		{
			// zero input-driven horizontal velocity to prevent sliding/animation sticking
			var vel = controller != null ? controller.velocity : Vector3.zero;
			vel = new Vector3(0f, vel.y, 0f);
			verticalVelocity = new Vector3(0f, verticalVelocity.y, 0f);
			// cancel rolling state if any
			isRolling = false;
			rollTimer = 0f;
		}
	}
	UpdateAnimator();
}
	void UpdateAnimator()
	{
		if (animator == null) return;

		// Speed: use horizontal movement magnitude (ignore vertical). If input is blocked, force 0.
		float speed = canMove ? new Vector3(controller.velocity.x, 0f, controller.velocity.z).magnitude : 0f;
		animator.SetFloat("Speed", speed);

	    // IsWalking: moving (forward, backward, or sideways) but not running
		// use centralized running state to keep animator consistent with movement/stamina
		bool isWalking = speed > 0.1f && !isRunning && canMove;
		animator.SetBool("IsWalking", isWalking);

		// IsRunning: reflects actual run state (considering stamina, rolling, etc.)
		// override to false during attack so animator can transition to attack even while player keeps moving fast
		bool animatorRunning = canMove && isRunning;
		if (combat != null && combat.IsAttacking)
		{
			animatorRunning = false;
		}
		animator.SetBool("IsRunning", animatorRunning);

		// Clear stance bools when player starts moving or jumping (allows exit from stance to idle)
		if ((isWalking || animatorRunning || isJumping) && canMove)
		{
			if (AnimatorHasParameter(animator, "IsUnarmedStance"))
				animator.SetBool("IsUnarmedStance", false);
			if (AnimatorHasParameter(animator, "IsArmedStance"))
				animator.SetBool("IsArmedStance", false);
		}

		// IsJumping: set true when jumping, false when grounded
		animator.SetBool("IsJumping", isJumping);

		// IsCrouched: placeholder, set to false (add crouch logic if needed)
		animator.SetBool("IsCrouched", isCrouched);

		// optional: sync rolling flag if animator has it
		if (AnimatorHasParameter(animator, "IsRolling"))
		{
			animator.SetBool("IsRolling", isRolling);
		}
	}

	void HandleMovement()
	{
		float h = Input.GetAxisRaw("Horizontal");
		float v = Input.GetAxisRaw("Vertical");


		// movement is always relative to camera, not player facing
		Vector3 move = Vector3.zero;
		var cameraOrbit = cameraPivot.GetComponent<ThirdPersonCameraOrbit>();
		if (cameraOrbit != null)
		{
			Vector3 camForward = cameraPivot.forward;
			camForward.y = 0f;
			camForward.Normalize();
			Vector3 camRight = cameraPivot.right;
			camRight.y = 0f;
			camRight.Normalize();
			move = camForward * v + camRight * h;
		}
		if (move.sqrMagnitude > 1f) move.Normalize();

		// Check if player is attacking - prevent movement/actions during attacks
		bool isAttacking = combat != null && combat.IsAttacking;

		// try start roll if ctrl or 'c' is pressed and we have a move direction (but not while attacking or exhausted)
		if (!isRolling && !isAttacking && controller.isGrounded)
		{
			bool exhausted = playerStats != null && playerStats.IsExhausted;
			bool rollPressed = Input.GetKeyDown(rollKey) || (allowRightCtrlAlso && Input.GetKeyDown(KeyCode.RightControl)) || Input.GetKeyDown(alternateRollKey);
			bool rooted = playerStats != null && playerStats.IsRooted;
			bool silenced = playerStats != null && playerStats.IsSilenced;
			bool stunned = playerStats != null && playerStats.IsStunned;
			if (rollPressed && !rooted && !stunned && !silenced && !exhausted && move.sqrMagnitude > 0.001f && rollCooldownTimer <= 0f)
			{
				// stamina check
				bool hasStats = playerStats != null;
				int finalCost = rollStaminaCost;
				if (hasStats)
				{
					finalCost = Mathf.Max(1, rollStaminaCost + playerStats.staminaCostModifier);
				}
				if (!hasStats || playerStats.UseStamina(finalCost))
				{
					isRolling = true;
					rollTimer = rollDuration;
					rollDirection = move.normalized;
					// face roll direction instantly (only if rotation not locked)
					if (rollDirection.sqrMagnitude > 0.001f)
					{
						Quaternion lookRot = Quaternion.LookRotation(rollDirection, Vector3.up);
						transform.rotation = lookRot;
					}
					// animator trigger if available
					if (animator != null && AnimatorHasParameter(animator, "Roll"))
					{
						animator.SetTrigger("Roll");
					}
					Debug.Log($"player roll start, stamina cost: {finalCost}");
				}
				else
				{
					Debug.Log("not enough stamina to roll!");
				}
			}
		}

		// face movement direction during walk/run (not while rolling or attacking)
		if (!isRolling && !isAttacking && move.sqrMagnitude > 0.0001f)
		{
			Quaternion targetRot = Quaternion.LookRotation(move.normalized, Vector3.up);
			transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
		}

		Vector3 horizontal = Vector3.zero;
		
		// Stop all horizontal movement while attacking
		if (isAttacking)
		{
			horizontal = Vector3.zero;
		}
		else if (isRolling)
		{
			// while rolling, override horizontal movement
			float currentRollSpeed = rollSpeed;
			if (playerStats != null) currentRollSpeed += Mathf.Max(0f, playerStats.speedModifier);
			horizontal = rollDirection * currentRollSpeed;
		}
		else
		{
			// running: if run state is true, use runSpeed for movement
			float currentSpeed = moveSpeed;
			if (playerStats != null)
			{
				currentSpeed += playerStats.speedModifier;
				// apply slow percentage to both walk and run
				currentSpeed *= (1f - Mathf.Clamp01(playerStats.slowPercent));
			}
			// Block running when exhausted - force walking speed only
			bool exhausted = playerStats != null && playerStats.IsExhausted;
			if (isRunning && !exhausted)
			{
				currentSpeed = runSpeed;
				if (playerStats != null)
				{
					currentSpeed += playerStats.speedModifier;
					currentSpeed *= (1f - Mathf.Clamp01(playerStats.slowPercent));
				}
			}
			horizontal = move * currentSpeed;
		}

		// grounding and jumping
		bool isGrounded = controller.isGrounded;
		if (isGrounded && verticalVelocity.y < 0f)
		{
			verticalVelocity.y = -2f;
		}
		bool canJump = !isRolling && !isAttacking && Input.GetKeyDown(KeyCode.Space) && isGrounded;
		if (playerStats != null && (playerStats.IsRooted || playerStats.IsStunned || playerStats.IsExhausted)) canJump = false;
		if (canJump)
		{
			// stamina check for jumping
			int finalJumpCost = jumpStaminaCost;
			if (playerStats != null)
				finalJumpCost = Mathf.Max(1, jumpStaminaCost + playerStats.staminaCostModifier);
			bool canSpend = playerStats == null || playerStats.UseStamina(finalJumpCost);
			if (canSpend)
			{
				verticalVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
				isJumping = true;
				Debug.Log($"player jump, stamina cost: {finalJumpCost}");
			}
			else
			{
				Debug.Log("not enough stamina to jump!");
			}
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

		// do not override Esc here; handled by PauseMenuController to avoid camera drifting
	}

	// utility: check animator has parameter to avoid warnings
	private bool AnimatorHasParameter(Animator anim, string paramName)
	{
		if (anim == null) return false;
		foreach (var p in anim.parameters)
		{
			if (p.name == paramName) return true;
		}
		return false;
	}

	// tick timers and stamina regen block every frame, independent of movement gating
	private void TickRollAndStaminaRegenBlock()
	{
		// roll timer and cooldown
		if (isRolling)
		{
			rollTimer -= Time.deltaTime;
			if (rollTimer <= 0f)
			{
				isRolling = false;
				rollCooldownTimer = rollCooldown;
				Debug.Log("player roll end");
			}
		}
		if (rollCooldownTimer > 0f)
		{
			rollCooldownTimer -= Time.deltaTime;
		}

		// stamina regen blocking: block when running (any direction with shift) or when rolling
		if (playerStats != null)
		{
			bool moving = (Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.5f || Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.5f);
			bool exhausted = playerStats.IsExhausted;
			// Block running when exhausted - player can only walk
			bool running = (canControl && canMove) && !exhausted && Input.GetKey(KeyCode.LeftShift) && moving && !isRolling && (playerStats == null || playerStats.currentStamina > 0);
			isRunning = running;
			bool blockRegen = isRunning || isRolling;
			playerStats.SetStaminaRegenBlocked(blockRegen);
		}
	}

	// apply running stamina drain after Update to avoid racing with attack spending
	void LateUpdate()
	{
		if (Photon.Pun.PhotonNetwork.InRoom && photonView != null && !photonView.IsMine)
			return;
		if (playerStats == null) return;
		if (isRunning && runningStaminaDrainPerSecond > 0f)
		{
			float rate = Mathf.Max(0f, runningStaminaDrainPerSecond + playerStats.staminaCostModifier);
			runningStaminaDrainAccumulator += rate * Time.deltaTime;
			if (runningStaminaDrainAccumulator >= 1f)
			{
				int drainInt = Mathf.FloorToInt(runningStaminaDrainAccumulator);
				int available = Mathf.Max(0, playerStats.currentStamina);
				int toDrain = Mathf.Min(drainInt, available);
				if (toDrain > 0)
				{
					playerStats.UseStamina(toDrain);
					runningStaminaDrainAccumulator -= toDrain;
				}
			}
		}
	}
}




