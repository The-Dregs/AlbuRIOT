using UnityEngine;

public class ThirdPersonCameraOrbit : MonoBehaviour
{
	private float defaultPitch;
	public float pitchLerpSpeed = 5f;
	public float yawLerpSpeed = 10f;
	private bool wasRightMouseHeld = false;
	private bool isLerpingToPlayer = false;
	[Header("Orbit Settings")]
	public Transform target;           // usually the Player root
	public Transform cameraTransform;  // the actual Camera
	public float mouseSensitivity = 200f; // degrees per second for delta
	public float minPitch = -30f;
	public float maxPitch = 60f;

	[Header("Zoom Settings")]
	public float minDistance = 2f;
	public float maxDistance = 6f;
	public float zoomSpeed = 3f;

	[Header("Collision Settings")]
	public LayerMask collisionMask = ~0; // collide with everything by default
	public float collisionRadius = 0.2f;
	public float collisionBuffer = 0.1f;

	private float yaw;   // around Y
	private float pitch; // around X
	private float targetDistance;

	void Start()
	{
		if (cameraTransform == null)
		{
			Camera cam = GetComponentInChildren<Camera>();
			if (cam != null) cameraTransform = cam.transform;
		}
		Vector3 localPos = cameraTransform.localPosition;
		targetDistance = -localPos.z; // expecting camera behind pivot (negative z)
		defaultPitch = 10f; // Set your preferred default pitch here (e.g., 10 degrees down)
		pitch = defaultPitch;
	}

	void LateUpdate()
	{
		if (target == null || cameraTransform == null) return;

	bool rightMouseHeld = Input.GetMouseButton(1);
		float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
		float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

		if (rightMouseHeld)
		{
			yaw += mouseX;
			pitch = Mathf.Clamp(pitch - mouseY, minPitch, maxPitch);
			isLerpingToPlayer = false;
		}
		else
		{
			// On right mouse release, start lerping
			if (wasRightMouseHeld)
			{
				isLerpingToPlayer = true;
			}
			// Rotate player (target) with mouse X
			target.Rotate(Vector3.up, mouseX * 2f); // 2f is a turn speed multiplier, adjust as needed
			if (isLerpingToPlayer)
			{
				// Smoothly lerp pitch and yaw to player-aligned values
				float targetYaw = target.eulerAngles.y;
				float targetPitch = defaultPitch;
				yaw = Mathf.LerpAngle(yaw, targetYaw, yawLerpSpeed * Time.deltaTime);
				pitch = Mathf.Lerp(pitch, targetPitch, pitchLerpSpeed * Time.deltaTime);
				// Stop lerping if close enough
				if (Mathf.Abs(Mathf.DeltaAngle(yaw, targetYaw)) < 0.1f && Mathf.Abs(pitch - targetPitch) < 0.1f)
				{
					yaw = targetYaw;
					pitch = targetPitch;
					isLerpingToPlayer = false;
				}
			}
			else
			{
				// Snap to player
				yaw = target.eulerAngles.y;
				pitch = defaultPitch;
			}
		}
		wasRightMouseHeld = rightMouseHeld;

		transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

		// Zoom input
		float scroll = Input.GetAxis("Mouse ScrollWheel");
		if (Mathf.Abs(scroll) > 0.0001f)
		{
			targetDistance = Mathf.Clamp(targetDistance - scroll * (maxDistance - minDistance) * zoomSpeed, minDistance, maxDistance);
		}

		// Desired camera position in world
		Vector3 desiredLocal = new Vector3(0f, 0f, -targetDistance);
		Vector3 desiredWorld = transform.TransformPoint(desiredLocal);

		// Collision: spherecast from pivot to desired pos
		Vector3 pivotWorld = transform.position;
		Vector3 dir = desiredWorld - pivotWorld;
		float distance = dir.magnitude;
		Vector3 finalPos = desiredWorld;
		if (distance > 0.001f)
		{
			Ray ray = new Ray(pivotWorld, dir.normalized);
			if (Physics.SphereCast(ray, collisionRadius, out RaycastHit hit, distance, collisionMask, QueryTriggerInteraction.Ignore))
			{
				finalPos = hit.point - dir.normalized * collisionBuffer;
			}
		}

		cameraTransform.position = finalPos;

		// Keep the pivot at the target position
		transform.position = target.position + Vector3.up * 1.6f; // head height
	}
}


