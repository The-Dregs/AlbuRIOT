
using UnityEngine;

public class ThirdPersonCameraOrbit : MonoBehaviour
{
	public Transform target; // player
	public Transform cameraTransform; // camera
	public float mouseSensitivity = 200f;
	public float minPitch = -30f;
	public float maxPitch = 60f;
	public float followDistance = 3.5f;
	public float followHeight = 1.0f;

	[Header("Collision Settings")]
	public LayerMask collisionMask = ~0; // collide with everything by default
	public float collisionRadius = 0.2f;
	public float collisionBuffer = 0.1f;
	public float followSmooth = 10f;
	public float returnSmooth = 7f;

	private float yaw;
	private float pitch;
	private bool isFreeLook = false;
	public bool cameraControlActive = true; // set by controller

	public void SetCameraControlActive(bool value)
	{
		cameraControlActive = value;
	}

	void Start()
	{
		if (cameraTransform == null)
		{
			Camera cam = GetComponentInChildren<Camera>();
			if (cam != null) cameraTransform = cam.transform;
		}
		Vector3 angles = transform.eulerAngles;
		yaw = angles.y;
		pitch = 12f; // look slightly down at the player, but not too high
	}

	void LateUpdate()
	{
		if (target == null || cameraTransform == null) return;
		if (!cameraControlActive) return;

		bool rightMouseHeld = Input.GetMouseButton(1);
		float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
		float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

		if (rightMouseHeld)
		{
			isFreeLook = true;
			yaw += mouseX;
			pitch = Mathf.Clamp(pitch - mouseY, minPitch, maxPitch);
		}
		else
		{
			// camera always follows mouse movement and rotates player
			yaw += mouseX;
			pitch = Mathf.Clamp(pitch - mouseY, minPitch, maxPitch);
			isFreeLook = false;
		}

		// Set camera rig rotation
		transform.position = target.position + Vector3.up * followHeight;
		transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

		// Set camera position with collision
		Vector3 camOffset = transform.rotation * new Vector3(0, 0, -followDistance);
		Vector3 desiredCamPos = transform.position + camOffset;
		Vector3 pivotPos = transform.position;
		Vector3 dir = (desiredCamPos - pivotPos).normalized;
		float distance = followDistance;
		Ray ray = new Ray(pivotPos, dir);
		if (Physics.SphereCast(ray, collisionRadius, out RaycastHit hit, followDistance, collisionMask, QueryTriggerInteraction.Ignore))
		{
			distance = hit.distance - collisionBuffer;
			if (distance < 0.1f) distance = 0.1f;
		}
		cameraTransform.position = pivotPos + dir * distance;
		cameraTransform.LookAt(transform.position + Vector3.up * 0.5f);
	}
}


