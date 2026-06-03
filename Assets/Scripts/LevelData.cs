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

    public EnemyConfig[] enemies;
    public bool requireAllVisited;

    [System.Serializable]
    public class EnemyConfig
    {
        public Vector2Int startPos;
        public int delay = 1;
    }
}