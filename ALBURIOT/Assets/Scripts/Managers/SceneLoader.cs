using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;

// simple singleton scene loader that shows a loading panel and optional progress
public class SceneLoader : MonoBehaviour
{
    public static SceneLoader Instance { get; private set; }

    [Header("UI")]
    public GameObject loadingPanel;
    public Slider progressBar;
    public TextMeshProUGUI progressText;

    [Header("behavior")]
    // keep alive across scenes
    public bool persist = true;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (persist) DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
        if (loadingPanel != null) loadingPanel.SetActive(false);
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    // Public API: call to load a scene. If Photon is connected and we're online, the loader will use PhotonNetwork.LoadLevel to keep clients synced.
    public void LoadScene(string sceneName, bool forceLocal = false)
    {
        if (loadingPanel != null) loadingPanel.SetActive(true);
        if (!forceLocal && PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.OfflineMode)
        {
            StartCoroutine(CoNetworkLoad(sceneName));
        }
        else
        {
            StartCoroutine(CoLocalLoad(sceneName));
        }
    }

    IEnumerator CoNetworkLoad(string sceneName)
    {
        // call PhotonNetwork.LoadLevel (this will trigger scene loading on all clients)
        PhotonNetwork.LoadLevel(sceneName);
        // wait until scene is active
        while (SceneManager.GetActiveScene().name != sceneName)
        {
            yield return null;
        }
        // ensure UI is hidden (OnSceneLoaded will also run)
        if (loadingPanel != null) loadingPanel.SetActive(false);
    }

    IEnumerator CoLocalLoad(string sceneName)
    {
        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        op.allowSceneActivation = true;
        while (!op.isDone)
        {
            float progress = Mathf.Clamp01(op.progress / 0.9f);
            if (progressBar != null) progressBar.value = progress;
            if (progressText != null) progressText.text = $"Loading... {Mathf.RoundToInt(progress * 100f)}%";
            yield return null;
        }
        if (loadingPanel != null) loadingPanel.SetActive(false);
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (loadingPanel != null) loadingPanel.SetActive(false);
        if (progressBar != null) progressBar.value = 1f;
        if (progressText != null) progressText.text = "";
    }
}
