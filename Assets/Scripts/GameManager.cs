using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    // 试玩队列在 SessionState 里的键（编辑器试玩工具 LevelPlaytest 与此处共用）
    public const string PlaytestPathsKey = "GridChaser.PlaytestPaths";

    [Header("Level Source")]
    [Tooltip("正式关卡清单资产。由『关卡管理』窗口的【应用到游戏】写入；这里拖入引用一次即可。")]
    [SerializeField] private LevelManifest manifest;

    private int currentLevelIndex = 0;

    private LevelData[] playtestLevels;             // 非空 = 处于试玩模式（单关或队列）
    private int playtestIndex;
    public bool IsPlaytestMode => playtestLevels != null && playtestLevels.Length > 0;

    private bool isGameOver = false;
    public bool IsGameOver => isGameOver;

    // 是否已点过主菜单"开始"。跨场景持有，关卡重载后保持 true，使重载后直接进游戏界面。
    private bool hasStarted = false;
    public bool HasStarted => hasStarted;

    // 本关是否已经展示过"进关标题卡"。跨场景持有：同一关按 R 重载不再重复展示，
    // 切到别的关时重置（GoNextLevel/GoPreviousLevel 里置 false）。
    private bool levelIntroShown = false;
    public bool LevelIntroShown => levelIntroShown;
    public void MarkLevelIntroShown() => levelIntroShown = true;

    // 正式关卡序列（从清单读）
    private LevelData[] Levels => manifest != null ? manifest.orderedLevels : null;

    // 供 UI 判断按钮可用性
    public bool HasNextLevel
    {
        get { var lv = Levels; return lv != null && currentLevelIndex < lv.Length - 1; }
    }
    public bool HasPreviousLevel => currentLevelIndex > 0;

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

        // 试玩模式跳过主菜单：直接算作已开始，进 Play 即可操作关卡
        if (IsPlaytestMode) hasStarted = true;
    }

    // 主菜单"开始"按钮触发：标记游戏已开始（UIManager 负责切界面）
    public void StartGame()
    {
        hasStarted = true;
    }

    public LevelData GetCurrentLevel()
    {
        if (IsPlaytestMode) return playtestLevels[playtestIndex];   // 试玩模式优先

        LevelData[] lv = Levels;
        if (lv == null || lv.Length == 0)
        {
            Debug.LogError("GameManager: 关卡清单为空。请在『关卡管理』窗口收藏关卡并点【应用到游戏】，" +
                           "并把 LevelManifest 拖到 GameManager 的 Manifest 字段。");
            return null;
        }
        return lv[currentLevelIndex];
    }

    public void ResetGameState()
    {
        isGameOver = false;
    }

    // 注：敌人停用、卡死判负已移除，当前游戏内不会触发死亡。
    // 此方法仅供停用中的 EnemyManager 调用以保持编译，将来恢复敌人时复用。
    // 现在改为：标记结束 + 播音效 + 通知 UI 显示死亡面板（不自动重载，由玩家点"重试"）。
    public void TriggerDeath()
    {
        if (isGameOver) return;
        isGameOver = true;
        AudioManager.Instance?.PlayDeath();
        Debug.Log("DEAD");

        // 试玩模式：保持老逻辑（延迟重载本关，不弹 UI）
        if (IsPlaytestMode) { StartCoroutine(ReloadAfterDelay(0.5f)); return; }

        UIManager.Instance?.ShowDeath();
    }

    // 通关：只标记结束 + 播音效 + 决定后续。不再自动推进关卡。
    public void TriggerWin()
    {
        if (isGameOver) return;
        isGameOver = true;
        AudioManager.Instance?.PlayWin();

        // 试玩模式：保持老逻辑（重玩本关，换关用 N/L），不弹 UI
        if (IsPlaytestMode)
        {
            Debug.Log("试玩通关 → 重玩本关（N 下一关 / L 上一关）");
            StartCoroutine(ReloadAfterDelay(0.5f));
            return;
        }

        // 正式模式：弹通关面板，等玩家点 上一关/重试/下一关
        UIManager.Instance?.ShowWin();
    }

    // ===== 通关面板按钮调用的关卡推进（正式模式）=====

    // 下一关：关号 +1，重置标题卡标记，重载场景
    public void GoNextLevel()
    {
        if (!HasNextLevel) { Debug.Log("已是最后一关。"); return; }
        currentLevelIndex++;
        levelIntroShown = false;     // 新关要重新放标题卡
        StopAllCoroutines();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // 上一关：关号 -1，重置标题卡标记，重载场景
    public void GoPreviousLevel()
    {
        if (!HasPreviousLevel) { Debug.Log("已是第一关。"); return; }
        currentLevelIndex--;
        levelIntroShown = false;
        StopAllCoroutines();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
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

    // 重新开始当前关：立即重载场景（试玩模式下重玩当前试玩关）。
    // 注意：不重置 levelIntroShown —— 重玩本关不再放标题卡。
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