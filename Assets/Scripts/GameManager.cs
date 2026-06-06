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

    // 重新开始当前关：立即重载场景。
    // GameManager 持久化（DontDestroyOnLoad），currentLevelIndex 不变，故重载即重玩本关；
    // 重载时 GameInitializer 会重置 isGameOver、重建格子、清空撤回历史。
    public void RestartLevel()
    {
        StopAllCoroutines();   // 清掉可能在排队的重载/胜利协程，避免重开后被打断
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private IEnumerator ReloadAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}