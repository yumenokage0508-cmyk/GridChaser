using UnityEngine;

[CreateAssetMenu(menuName = "Game/LevelData")]
public class LevelData : ScriptableObject
{
    public string levelName;

    [TextArea(2, 4)]
    public string description;          // 关卡说明，阶段 6.2 UI 读取

    [TextArea(5, 20)]
    public string layout;

    [Tooltip("通关路线，用 U/D/L/R 表示方向，如 RRRUULLL。阶段 4.4 提示系统使用。")]
    public string solutionMoves;        // 预存解法，阶段 4.4 提示系统读取

    public EnemyConfig[] enemies;       // 敌人系统当前停用，一笔画关卡此项为空
    public bool requireAllVisited;

    // ===== Dev / 策展元数据（仅编辑器/策展期使用，运行时游戏逻辑不读取）=====
    [Header("Dev / 策展（运行时不读取）")]
    [Tooltip("试玩后的人工评分，策展挑选正式关卡时排序用。")]
    public int curationScore;

    [Tooltip("生成器估计的难度：0 最易 ~ 1 最难（贪心试玩通关率的补）。")]
    public float difficulty;

    [Tooltip("可玩格子总数（需踩满的格数）。")]
    public int cells;

    [Tooltip("内部柱子数：被可玩区四面包围的墙，制造绕行结构。")]
    public int interiorPillars;

    [Tooltip("咽喉格数：可走邻居 ≤2 的格子数，越多越像隧道/独木桥。")]
    public int chokepoints;

    [System.Serializable]
    public class EnemyConfig
    {
        public Vector2Int startPos;
        public int delay = 1;
    }
}