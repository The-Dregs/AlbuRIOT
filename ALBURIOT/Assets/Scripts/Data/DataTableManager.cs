using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class DataTableManager : MonoBehaviour
{
    [Header("Data Tables")]
    public EnemyDataTable enemyDataTable;
    public ItemDataTable itemDataTable;
    public QuestDataTable questDataTable;
    public MovesetDataTable movesetDataTable;
    public ShrineDataTable shrineDataTable;
    public DebuffDataTable debuffDataTable;
    public PowerStealDataTable powerStealDataTable;
    
    // Singleton pattern
    public static DataTableManager Instance { get; private set; }
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }
    
    // Enemy Data Methods
    public EnemyData GetEnemyData(string enemyName)
    {
        return enemyDataTable?.GetEnemyData(enemyName);
    }
    
    public EnemyData[] GetAllEnemyData()
    {
        return enemyDataTable?.GetAllEnemyData();
    }
    
    // Item Data Methods
    public ItemData GetItemData(string itemName)
    {
        return itemDataTable?.GetItemData(itemName);
    }
    
    public ItemData[] GetItemsByType(ItemType itemType)
    {
        return itemDataTable?.GetItemsByType(itemType);
    }
    
    // Quest Data Methods
    public QuestData GetQuestData(int questId)
    {
        return questDataTable?.GetQuestData(questId);
    }
    
    public QuestData[] GetQuestsByType(QuestType questType)
    {
        return questDataTable?.GetQuestsByType(questType);
    }
    
    // Moveset Data Methods
    public MovesetData GetMovesetData(string movesetName)
    {
        return movesetDataTable?.GetMovesetData(movesetName);
    }
    
    public MovesetData[] GetAllMovesetData()
    {
        return movesetDataTable?.GetAllMovesetData();
    }
    
    // Shrine Data Methods
    public ShrineData GetShrineData(string shrineId)
    {
        return shrineDataTable?.GetShrineData(shrineId);
    }
    
    public ShrineData[] GetAllShrineData()
    {
        return shrineDataTable?.GetAllShrineData();
    }
    
    // Debuff Data Methods
    public DebuffData GetDebuffData(string debuffName)
    {
        return debuffDataTable?.GetDebuffData(debuffName);
    }
    
    public DebuffData[] GetDebuffsByType(DebuffType debuffType)
    {
        return debuffDataTable?.GetDebuffsByType(debuffType);
    }
    
    // Power Steal Data Methods
    public PowerStealData GetPowerStealData(string enemyName)
    {
        return powerStealDataTable?.GetPowerStealData(enemyName);
    }
    
    public PowerStealData[] GetAllPowerStealData()
    {
        return powerStealDataTable?.GetAllPowerStealData();
    }
}

[System.Serializable]
public class EnemyDataTable
{
    public EnemyData[] enemies;
    
    public EnemyData GetEnemyData(string enemyName)
    {
        if (enemies == null) return null;
        
        return enemies.FirstOrDefault(e => e.enemyName == enemyName);
    }
    
    public EnemyData[] GetAllEnemyData()
    {
        return enemies;
    }
    
    public EnemyData[] GetEnemiesByType(EnemyType enemyType)
    {
        if (enemies == null) return new EnemyData[0];
        
        return enemies.Where(e => e.enemyType == enemyType).ToArray();
    }
}

[System.Serializable]
public class ItemDataTable
{
    public ItemData[] items;
    
    public ItemData GetItemData(string itemName)
    {
        if (items == null) return null;
        
        return items.FirstOrDefault(i => i.itemName == itemName);
    }
    
    public ItemData[] GetAllItemData()
    {
        return items;
    }
    
    public ItemData[] GetItemsByType(ItemType itemType)
    {
        if (items == null) return new ItemData[0];
        
        return items.Where(i => i.itemType == itemType).ToArray();
    }
}

[System.Serializable]
public class QuestDataTable
{
    public QuestData[] quests;
    
    public QuestData GetQuestData(int questId)
    {
        if (quests == null) return null;
        
        return quests.FirstOrDefault(q => q.questID == questId);
    }
    
    public QuestData[] GetAllQuestData()
    {
        return quests;
    }
    
    public QuestData[] GetQuestsByType(QuestType questType)
    {
        if (quests == null) return new QuestData[0];
        
        return quests.Where(q => q.questType == questType).ToArray();
    }
}

[System.Serializable]
public class MovesetDataTable
{
    public MovesetData[] movesets;
    
    public MovesetData GetMovesetData(string movesetName)
    {
        if (movesets == null) return null;
        
        return movesets.FirstOrDefault(m => m.movesetName == movesetName);
    }
    
    public MovesetData[] GetAllMovesetData()
    {
        return movesets;
    }
}

[System.Serializable]
public class ShrineDataTable
{
    public ShrineData[] shrines;
    
    public ShrineData GetShrineData(string shrineId)
    {
        if (shrines == null) return null;
        
        return shrines.FirstOrDefault(s => s.shrineId == shrineId);
    }
    
    public ShrineData[] GetAllShrineData()
    {
        return shrines;
    }
}

[System.Serializable]
public class DebuffDataTable
{
    public DebuffData[] debuffs;
    
    public DebuffData GetDebuffData(string debuffName)
    {
        if (debuffs == null) return null;
        
        return debuffs.FirstOrDefault(d => d.debuffName == debuffName);
    }
    
    public DebuffData[] GetAllDebuffData()
    {
        return debuffs;
    }
    
    public DebuffData[] GetDebuffsByType(DebuffType debuffType)
    {
        if (debuffs == null) return new DebuffData[0];
        
        return debuffs.Where(d => d.debuffType == debuffType).ToArray();
    }
}

[System.Serializable]
public class PowerStealDataTable
{
    public PowerStealData[] powerSteals;
    
    public PowerStealData GetPowerStealData(string enemyName)
    {
        if (powerSteals == null) return null;
        
        return powerSteals.FirstOrDefault(p => p.enemyName == enemyName);
    }
    
    public PowerStealData[] GetAllPowerStealData()
    {
        return powerSteals;
    }
}

[System.Serializable]
public class EnemyData
{
    [Header("Enemy Information")]
    public string enemyName;
    [TextArea] public string description;
    public EnemyType enemyType;
    public Sprite icon;
    
    [Header("Stats")]
    public int maxHealth = 100;
    public int baseDamage = 25;
    public float moveSpeed = 3f;
    public float attackRange = 2f;
    public float attackCooldown = 1f;
    
    [Header("Power Stealing")]
    public bool canBeStolen = true;
    public string powerName = "";
    public float powerDuration = 30f;
    
    [Header("Moveset")]
    public MovesetData movesetData;
    
    [Header("VFX")]
    public GameObject[] moveVFX;
    public GameObject deathVFX;
    
    [Header("Audio")]
    public AudioClip[] moveSounds;
    public AudioClip deathSound;
    
    [Header("Quest Integration")]
    public bool isQuestTarget = false;
    public string questId = "";
    
    [Header("Loot")]
    public ItemData[] lootTable;
    public int[] lootChances;
}

public enum EnemyType
{
    Aswang,
    Manananggal,
    Tikbalang,
    Kapre,
    Tiyanak,
    Berberoka,
    Busaw,
    Diwata,
    Pugot,
    Sigbin,
    Wakwak,
    Amomongo,
    Bungisngis,
    Minokawa,
    Sarimanok,
    ShadowTouchedDiwata,
    Custom
}

public enum MoveTargetType
{
    Self,
    Enemy,
    Ally,
    Ground,
    Air
}

