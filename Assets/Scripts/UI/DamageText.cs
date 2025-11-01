using UnityEngine;
using TMPro;

public class DamageText : MonoBehaviour
{
    public float floatSpeed = 1f;
    public float fadeDuration = 1f;
    private TextMeshPro textMesh;
    private Color originalColor;
    private float timer = 0f;

    public void ShowDamage(int damage)
    {
        if (textMesh == null) textMesh = GetComponent<TextMeshPro>();
        textMesh.text = damage.ToString();
        originalColor = textMesh.color;
        timer = 0f;
    }

    void Awake()
    {
        textMesh = GetComponent<TextMeshPro>();
        originalColor = textMesh.color;
    }

    void Update()
    {
        transform.position += Vector3.up * floatSpeed * Time.deltaTime;
        // Make the text always face the camera
        if (Camera.main != null)
        {
            transform.LookAt(Camera.main.transform);
            transform.Rotate(0, 180f, 0); // Flip to face camera
        }
        timer += Time.deltaTime;
        float alpha = Mathf.Lerp(originalColor.a, 0, timer / fadeDuration);
        textMesh.color = new Color(originalColor.r, originalColor.g, originalColor.b, alpha);
        if (timer >= fadeDuration)
        {
            Destroy(gameObject);
        }
    }
}
