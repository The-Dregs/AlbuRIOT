using UnityEngine;
using UnityEngine.UI;

public class EnemyHealthBar : MonoBehaviour
{
	public EnemyController enemyController;
	public Slider healthSlider;
	public Canvas healthCanvas;
	public float visibleDuration = 2f;
	private float visibleTimer = 0f;

	void Start()
	{
		if (enemyController == null)
			enemyController = GetComponent<EnemyController>();
		if (healthSlider == null)
			healthSlider = GetComponentInChildren<Slider>();
		if (healthCanvas == null)
			healthCanvas = GetComponentInChildren<Canvas>();

		if (healthSlider != null && enemyController != null)
		{
			healthSlider.maxValue = enemyController.stats.maxHealth;
			healthSlider.value = enemyController.GetCurrentHealth();
		}
		if (healthCanvas != null)
			healthCanvas.enabled = false;
	}

	void Update()
	{
		if (healthSlider != null && enemyController != null)
		{
			healthSlider.value = enemyController.GetCurrentHealth();
		}
		if (healthCanvas != null && healthCanvas.enabled)
		{
			visibleTimer -= Time.deltaTime;
			if (visibleTimer <= 0f)
			{
				healthCanvas.enabled = false;
			}
		}
	}

	// Call this method when the enemy takes damage
	public void ShowHealthBar()
	{
		if (healthCanvas != null)
		{
			healthCanvas.enabled = true;
			visibleTimer = visibleDuration;
		}
	}
}
