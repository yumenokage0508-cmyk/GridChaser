using UnityEngine;

// UI 管理器（单例，随场景重建，不做 DontDestroyOnLoad）。
// 持有四个 Panel，任意时刻只显示其中一个。初始显示哪个由 GameManager.HasStarted 决定：
// 没开始过 → 主菜单；已开始（含关卡重载后）→ 游戏界面。
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject gamePanel;
    [SerializeField] private GameObject deathPanel;
    [SerializeField] private GameObject winPanel;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        // GameManager 跨场景持有"是否已开始"：已开始（含每次关卡重载）直接进游戏界面
        bool started = GameManager.Instance != null && GameManager.Instance.HasStarted;
        if (started) ShowGame();
        else ShowMainMenu();
    }

    public void ShowMainMenu() => Switch(mainMenuPanel);
    public void ShowGame() => Switch(gamePanel);
    public void ShowDeath() => Switch(deathPanel);
    public void ShowWin() => Switch(winPanel);

    // 只激活目标面板，其余全部隐藏
    private void Switch(GameObject active)
    {
        if (mainMenuPanel) mainMenuPanel.SetActive(active == mainMenuPanel);
        if (gamePanel) gamePanel.SetActive(active == gamePanel);
        if (deathPanel) deathPanel.SetActive(active == deathPanel);
        if (winPanel) winPanel.SetActive(active == winPanel);
    }

    // 主菜单"开始"按钮调用：标记已开始 + 切到游戏界面
    public void OnStartButton()
    {
        GameManager.Instance.StartGame();
        ShowGame();
    }
}