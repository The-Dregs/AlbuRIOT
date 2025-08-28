using UnityEngine;
using UnityEngine.UI;

public class EnemyHealthBar : MonoBehaviour
{
    public EnemyStats enemyStats;
    public GameObject healthBarCanvas;
    public Slider healthSlider;
    public float visibleDuration = 2f;

    private float timer = 0f;

    void Start()
    {
        if (healthBarCanvas != null)
            healthBarCanvas.SetActive(false);
        if (enemyStats == null)
            enemyStats = GetComponentInParent<EnemyStats>();
    }

    void Update()
    {
        if (healthBarCanvas.activeSelf)
        {
            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                Debug.Log($"Hiding health bar for {enemyStats?.gameObject.name}");
                healthBarCanvas.SetActive(false);
            }
        }
        if (enemyStats != null && healthSlider != null)
        {
            healthSlider.value = (float)enemyStats.currentHealth / enemyStats.maxHealth;
        }
    }

    public void ShowBar()
    {
        if (healthBarCanvas != null)
        {
            Debug.Log($"Showing health bar for {enemyStats?.gameObject.name}");
            healthBarCanvas.SetActive(true);
            timer = visibleDuration;
        }
    }
}
