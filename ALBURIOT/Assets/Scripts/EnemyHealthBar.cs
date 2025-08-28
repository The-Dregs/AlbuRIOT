using UnityEngine;
using UnityEngine.UI;

public class EnemyHealthBar : MonoBehaviour
{
	public EnemyStats enemyStats;
	public Slider healthSlider;
	public Canvas healthCanvas;
	public float visibleDuration = 2f;
	private float visibleTimer = 0f;

	void Start()
	{
		if (enemyStats == null)
			enemyStats = GetComponent<EnemyStats>();
		if (healthSlider == null)
			healthSlider = GetComponentInChildren<Slider>();
		if (healthCanvas == null)
			healthCanvas = GetComponentInChildren<Canvas>();

		if (healthSlider != null && enemyStats != null)
		{
			healthSlider.maxValue = enemyStats.maxHealth;
			healthSlider.value = enemyStats.currentHealth;
		}
		if (healthCanvas != null)
			healthCanvas.enabled = false;

	// No event subscription needed; EnemyStats will call ShowHealthBar() directly
	}

	void Update()
	{
		if (healthSlider != null && enemyStats != null)
		{
			healthSlider.value = enemyStats.currentHealth;
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
