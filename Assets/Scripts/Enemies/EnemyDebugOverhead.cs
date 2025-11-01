using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public class EnemyDebugOverhead : MonoBehaviour
{
	[SerializeField] private BaseEnemyAI enemy;
	[SerializeField] private bool enable = true;
	[SerializeField] private float yOffset = 2.2f;
	[SerializeField] private Color color = Color.yellow;
	[SerializeField] private int fontSize = 3;

	private TextMeshPro tmp;

	private void Awake()
	{
		if (enemy == null) enemy = GetComponentInParent<BaseEnemyAI>();
	}

	private void Update()
	{
		if (!enable || enemy == null)
		{
			DestroyIfExists();
			return;
		}
		EnsureTMP();
		UpdateBillboard();
	}

	private void EnsureTMP()
	{
		if (tmp != null) return;
		var go = new GameObject("DebugOverhead");
		go.transform.SetParent(enemy.transform);
		go.transform.localPosition = new Vector3(0f, yOffset, 0f);
		tmp = go.AddComponent<TextMeshPro>();
		tmp.fontSize = fontSize;
		tmp.alignment = TextAlignmentOptions.Center;
		tmp.color = color;
		tmp.text = string.Empty;
		tmp.textWrappingMode = TextWrappingModes.NoWrap;
	}

	private void UpdateBillboard()
	{
		if (tmp == null) return;
		var cam = Camera.main;
		if (cam != null)
		{
			tmp.transform.rotation = Quaternion.LookRotation(tmp.transform.position - cam.transform.position);
		}
		float hp = enemy.HealthPercentage;
		var target = enemy.Target;
		float dist = target != null ? Vector3.Distance(enemy.transform.position, target.position) : -1f;
		string state = enemy.CurrentState.ToString();
		float basicCd = Mathf.Max(enemy.BasicCooldownRemaining, enemy.BasicCooldownTime);
		string header = target != null ? $"{state}  tgt:{dist:F1}  hp:{hp:P0}" : $"{state}  hp:{hp:P0}";

		// If this is an Amomongo, show ability cooldowns and buffs
		var amo = enemy as AmomongoAI;
		if (amo != null)
		{
			string cds = $"CD basic:{basicCd:F1}  slam:{amo.SlamCooldownRemaining:F1}  berserk:{amo.BerserkCooldownRemaining:F1}";
			string buffs = $"buffs Dmg:{(amo.IsBerserk ? "on" : "off")}({amo.BerserkTimeRemaining:F1})  Spd:{(amo.IsBerserk ? "on" : "off")}({amo.BerserkTimeRemaining:F1})  Sta:off(-)  Hp:off(-)";
			tmp.text = header + "\n" + cds + "\n" + buffs;
		}
		else
		{
			// If this is a Bungisngis, show ability cooldowns
			var bung = enemy as BungisngisAI;
			if (bung != null)
			{
				string cds = $"CD basic:{basicCd:F1}  laugh:{bung.LaughCooldownRemaining:F1}  pound:{bung.PoundCooldownRemaining:F1}";
				tmp.text = header + "\n" + cds;
			}
			else
			{
				tmp.text = header + $"\nCD basic:{basicCd:F1}";
			}
		}
	}

	private void OnDisable()
	{
		DestroyIfExists();
	}

	private void DestroyIfExists()
	{
		if (tmp != null)
		{
			Destroy(tmp.gameObject);
			tmp = null;
		}
	}
}


