using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    // 试玩目标在 SessionState 里的键（编辑器试玩工具 LevelPlaytest 与此处共用同一字符串）
    public const string PlaytestSessionKey = "GridChaser.PlaytestLevelPath";

    [Header("Level Sequence")]
    [SerializeField] private LevelData[] levels;
    private int currentLevelIndex = 0;

    private LevelData playtestOverride;             // 非 null = 处于试玩模式
    public bool IsPlaytestMode => playtestOverride != null;

    private bool isGameOver = false;
    public bool IsGameOver => isGameOver;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

#if UNITY_EDITOR
        // 编辑器试玩：若设置了试玩目标，则加载它，绕过正常关卡序列
        string p = UnityEditor.SessionState.GetString(PlaytestSessionKey, "");
        if (!string.IsNullOrEmpty(p))
            playtestOverride = UnityEditor.AssetDatabase.LoadAssetAtPath<LevelData>(p);
#endif
    }

    public LevelData GetCurrentLevel()
    {
        if (playtestOverride != null) return playtestOverride;   // 试玩模式优先

        if (levels == null || levels.Length == 0)
        {
            Debug.LogError("GameManager: levels 数组为空。正常游玩需先在策展窗口『应用到游戏』填入关卡。");
            return null;
        }
        return levels[currentLevelIndex];
    }

    public void ResetGameState()
    {
        isGameOver = false;
    }

    // 注：敌人停用、卡死判负已移除，当前游戏内不会触发死亡。
    // 此方法仅供停用中的 EnemyManager 调用以保持编译，将来恢复敌人时复用。
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

        // 试玩模式：通关不进下一关，重玩本关方便反复试手感
        if (IsPlaytestMode)
        {
            Debug.Log("试玩通关 → 重玩本关");
            StartCoroutine(ReloadAfterDelay(0.5f));
            return;
        }

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

    // 重新开始当前关：立即重载场景（试玩模式下也会重玩当前试玩关）
    public void RestartLevel()
    {
        StopAllCoroutines();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private IEnumerator ReloadAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}