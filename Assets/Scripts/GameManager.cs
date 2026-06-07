using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    // 试玩队列在 SessionState 里的键（编辑器试玩工具 LevelPlaytest 与此处共用）
    // 值是用 '\n' 连接的一串关卡资产路径；单关试玩就是只有一条的队列。
    public const string PlaytestPathsKey = "GridChaser.PlaytestPaths";

    [Header("Level Sequence")]
    [SerializeField] private LevelData[] levels;
    private int currentLevelIndex = 0;

    private LevelData[] playtestLevels;             // 非空 = 处于试玩模式（单关或队列）
    private int playtestIndex;
    public bool IsPlaytestMode => playtestLevels != null && playtestLevels.Length > 0;

    private bool isGameOver = false;
    public bool IsGameOver => isGameOver;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

#if UNITY_EDITOR
        // 编辑器试玩：若设置了试玩队列，则加载它，绕过正常关卡序列
        string joined = UnityEditor.SessionState.GetString(PlaytestPathsKey, "");
        if (!string.IsNullOrEmpty(joined))
        {
            var list = new System.Collections.Generic.List<LevelData>();
            foreach (string p in joined.Split('\n'))
            {
                if (string.IsNullOrEmpty(p)) continue;
                LevelData ld = UnityEditor.AssetDatabase.LoadAssetAtPath<LevelData>(p);
                if (ld != null) list.Add(ld);
            }
            if (list.Count > 0) { playtestLevels = list.ToArray(); playtestIndex = 0; }
        }
#endif
    }

    public LevelData GetCurrentLevel()
    {
        if (IsPlaytestMode) return playtestLevels[playtestIndex];   // 试玩模式优先

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

        // 试玩模式：通关不进下一关，重玩本关（换关用 N/L 手动切）
        if (IsPlaytestMode)
        {
            Debug.Log("试玩通关 → 重玩本关（N 下一关 / L 上一关）");
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

    // 序列试玩：切到队列下一关（N 键；到末尾绕回开头）
    public void AdvancePlaytest()
    {
        if (!IsPlaytestMode) return;
        playtestIndex = (playtestIndex + 1) % playtestLevels.Length;
        LoadPlaytestCurrent();
    }

    // 序列试玩：切到队列上一关（L 键；到开头绕回末尾）
    public void RetreatPlaytest()
    {
        if (!IsPlaytestMode) return;
        playtestIndex = (playtestIndex - 1 + playtestLevels.Length) % playtestLevels.Length;
        LoadPlaytestCurrent();
    }

    private void LoadPlaytestCurrent()
    {
        Debug.Log($"试玩队列 → 第 {playtestIndex + 1}/{playtestLevels.Length} 关：{playtestLevels[playtestIndex].name}");
        StopAllCoroutines();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // 重新开始当前关：立即重载场景（试玩模式下重玩当前试玩关）
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