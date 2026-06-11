using UnityEngine;
using TMPro;
using System.Collections;

// UI 管理器（单例，随场景重建）。负责四个 Panel 的切换、通关面板按钮、关卡名与进关标题卡。
// 初始显示哪个由 GameManager.HasStarted 决定：没开始 → 主菜单；已开始（含重载）→ 游戏界面。
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject gamePanel;
    [SerializeField] private GameObject deathPanel;
    [SerializeField] private GameObject winPanel;

    [Header("Game Panel（关卡名 + 标题卡渐隐）")]
    [Tooltip("挂在 GamePanel 上的 CanvasGroup，用于标题卡渐隐（alpha 1→0）。")]
    [SerializeField] private CanvasGroup gamePanelGroup;
    [Tooltip("GamePanel 里显示关卡名的 TMP 文本。")]
    [SerializeField] private TMP_Text levelNameText;

    [Header("Win Panel 按钮（用于按可用性显隐）")]
    [Tooltip("通关面板的『上一关』按钮物体（第一关时隐藏）。")]
    [SerializeField] private GameObject winPrevButton;
    [Tooltip("通关面板的『下一关』按钮物体（最后一关时隐藏）。")]
    [SerializeField] private GameObject winNextButton;

    [Header("Level Intro（标题卡时长）")]
    [Tooltip("标题卡完整显示停留的秒数。")]
    [SerializeField] private float introHoldDuration = 1.5f;
    [Tooltip("标题卡渐隐的秒数。")]
    [SerializeField] private float introFadeDuration = 0.6f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        bool started = GameManager.Instance != null && GameManager.Instance.HasStarted;
        if (started) ShowGame();
        else ShowMainMenu();
    }

    // ===== 面板切换 =====

    public void ShowMainMenu() => Switch(mainMenuPanel);

    // 进入游戏界面：切到 GamePanel，并处理关卡名 + 标题卡
    public void ShowGame()
    {
        Switch(gamePanel);
        SetupLevelIntro();
    }

    public void ShowDeath() => Switch(deathPanel);

    // 通关面板：切到 WinPanel，并按可用性显隐 上一关/下一关
    public void ShowWin()
    {
        Switch(winPanel);
        var gm = GameManager.Instance;
        if (winPrevButton != null) winPrevButton.SetActive(gm != null && gm.HasPreviousLevel);
        if (winNextButton != null) winNextButton.SetActive(gm != null && gm.HasNextLevel);
    }

    // 只激活目标面板，其余隐藏
    private void Switch(GameObject active)
    {
        if (mainMenuPanel) mainMenuPanel.SetActive(active == mainMenuPanel);
        if (gamePanel) gamePanel.SetActive(active == gamePanel);
        if (deathPanel) deathPanel.SetActive(active == deathPanel);
        if (winPanel) winPanel.SetActive(active == winPanel);
    }

    // ===== 关卡名 + 进关标题卡 =====

    private void SetupLevelIntro()
    {
        var gm = GameManager.Instance;
        LevelData lv = gm != null ? gm.GetCurrentLevel() : null;

        if (levelNameText != null)
            levelNameText.text = lv != null ? lv.levelName : "";

        if (gamePanelGroup == null) return;

        // 试玩模式不走标题卡（保持原键盘流程，界面清爽）
        if (gm != null && gm.IsPlaytestMode) { gamePanelGroup.alpha = 0f; return; }

        if (gm != null && !gm.LevelIntroShown)
            StartCoroutine(LevelIntroRoutine());   // 本关首次进入 → 放标题卡
        else
            gamePanelGroup.alpha = 0f;             // 本关已放过（按 R 重玩）→ 不再显示
    }

    // 标题卡：完整显示停留 → 渐隐 → 标记本关已放过
    private IEnumerator LevelIntroRoutine()
    {
        gamePanelGroup.alpha = 1f;
        yield return new WaitForSeconds(introHoldDuration);

        float t = 0f;
        while (t < introFadeDuration)
        {
            t += Time.deltaTime;
            gamePanelGroup.alpha = Mathf.Lerp(1f, 0f, t / introFadeDuration);
            yield return null;
        }
        gamePanelGroup.alpha = 0f;
        GameManager.Instance?.MarkLevelIntroShown();
    }

    // ===== 按钮响应 =====

    // 主菜单"开始"
    public void OnStartButton()
    {
        GameManager.Instance.StartGame();
        ShowGame();
    }

    // 通关面板"下一关"
    public void OnNextButton() => GameManager.Instance.GoNextLevel();

    // 通关面板"上一关"
    public void OnPrevButton() => GameManager.Instance.GoPreviousLevel();

    // 通关 / 死亡面板"重试"
    public void OnRetryButton() => GameManager.Instance.RestartLevel();
}