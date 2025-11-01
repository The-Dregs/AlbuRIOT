using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class EnemyHealthBar : MonoBehaviour
{
	[SerializeField] private BaseEnemyAI enemy;
	[SerializeField] private Transform healthBarRoot; // optional; defaults to child named "HealthBar"
	[SerializeField] private Slider slider; // optional; defaults to first Slider under root
	[SerializeField] private float hideDelaySeconds = 3f;

	private float hideTimer;

	private void Awake()
	{
		if (enemy == null) enemy = GetComponentInParent<BaseEnemyAI>();
		if (healthBarRoot == null)
		{
			var t = transform.Find("HealthBar");
			if (t == null && transform.parent != null)
				t = transform.parent.Find("HealthBar");
			healthBarRoot = t;
		}
		if (slider == null && healthBarRoot != null)
			slider = healthBarRoot.GetComponentInChildren<Slider>(true);
	}

	private void OnEnable()
	{
		if (enemy != null)
		{
			enemy.OnEnemyTookDamage += HandleDamaged;
			enemy.OnEnemyDied += HandleDied;
		}
        Refresh(false);
        if (healthBarRoot != null) healthBarRoot.gameObject.SetActive(false);
	}

	private void OnDisable()
	{
		if (enemy != null)
		{
			enemy.OnEnemyTookDamage -= HandleDamaged;
			enemy.OnEnemyDied -= HandleDied;
		}
	}

	private void Update()
	{
		if (healthBarRoot == null || slider == null || enemy == null) return;
		if (hideTimer > 0f)
		{
			hideTimer -= Time.deltaTime;
			if (hideTimer <= 0f && !enemy.IsDead)
			{
				healthBarRoot.gameObject.SetActive(false);
			}
		}

		// face main camera
		var cam = Camera.main;
		if (cam != null)
		{
			var fwd = cam.transform.rotation * Vector3.forward;
			var up = cam.transform.rotation * Vector3.up;
			healthBarRoot.rotation = Quaternion.LookRotation(fwd, up);
		}
	}

	private void HandleDamaged(BaseEnemyAI _, int __)
	{
		Refresh(true);
	}

	private void HandleDied(BaseEnemyAI _)
	{
		Refresh(true);
	}

	private void Refresh(bool show)
	{
		if (enemy == null || slider == null || healthBarRoot == null) return;
		float frac = enemy.MaxHealth > 0 ? Mathf.Clamp01(enemy.HealthPercentage) : 0f;
		slider.minValue = 0f;
		slider.maxValue = 1f;
		slider.value = frac;
		if (show)
		{
			healthBarRoot.gameObject.SetActive(true);
			hideTimer = hideDelaySeconds;
		}
	}
}


