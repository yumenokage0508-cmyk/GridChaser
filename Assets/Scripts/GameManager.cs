using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Level Sequence")]
    [SerializeField] private LevelData[] levels;
    private int currentLevelIndex = 0;

    private bool isGameOver = false;
    public bool IsGameOver => isGameOver;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public LevelData GetCurrentLevel()
    {
        if (levels == null || levels.Length == 0)
        {
            Debug.LogError("GameManager: levels 数组为空，请点击 Inspector 里的「自动填充关卡列表」按钮！");
            return null;
        }
        return levels[currentLevelIndex];
    }

    public void ResetGameState()
    {
        isGameOver = false;
    }

    public void TriggerDeath()
    {
        if (isGameOver) return;
        isGameOver = true;
        Debug.Log("DEAD");
        StartCoroutine(ReloadAfterDelay(0.5f));
    }

    public void TriggerWin()
    {
        if (isGameOver) return;
        isGameOver = true;

        if (currentLevelIndex < levels.Length - 1)
        {
            currentLevelIndex++;
            Debug.Log($"WIN → 加载第 {currentLevelIndex + 1} 关");
            StartCoroutine(ReloadAfterDelay(0.5f));
        }
        else
        {
            Debug.Log("通关全部关卡！");
        }
    }

    private IEnumerator ReloadAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}