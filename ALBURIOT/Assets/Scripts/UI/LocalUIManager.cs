using UnityEngine;

public class LocalUIManager : MonoBehaviour
{
    public static LocalUIManager Instance { get; private set; }

    public string CurrentOwner { get; private set; } = null;
    public bool IsAnyOpen => !string.IsNullOrEmpty(CurrentOwner);

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public static LocalUIManager Ensure()
    {
        if (Instance == null)
        {
            var go = new GameObject("LocalUIManager");
            Instance = go.AddComponent<LocalUIManager>();
        }
        return Instance;
    }

    public bool TryOpen(string owner)
    {
        Ensure();
        if (IsAnyOpen) return false;
        CurrentOwner = owner;
        return true;
    }

    public void Close(string owner)
    {
        if (!IsAnyOpen) return;
        if (CurrentOwner != owner) return;
        CurrentOwner = null;
    }

    public void ForceClose()
    {
        CurrentOwner = null;
    }

    public bool IsOwner(string owner)
    {
        return IsAnyOpen && CurrentOwner == owner;
    }
}
