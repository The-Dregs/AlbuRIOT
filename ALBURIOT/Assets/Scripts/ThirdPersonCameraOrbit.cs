using UnityEngine;

public class ThirdPersonCameraOrbit : MonoBehaviour
{
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
		}
		else
		{
			// Only update yaw if the angle difference is small (prevents sudden flips)
			float targetYaw = target.eulerAngles.y;
			float angleDiff = Mathf.DeltaAngle(yaw, targetYaw);
			if (Mathf.Abs(angleDiff) < 90f) // Only snap if not going backwards
			{
				yaw = targetYaw;
			}
			// Rotate player (target) with mouse X
			target.Rotate(Vector3.up, mouseX * 2f); // 2f is a turn speed multiplier, adjust as needed
		}

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


