using UnityEngine;

[CreateAssetMenu(menuName = "Game/LevelData")]
public class LevelData : ScriptableObject
{
    public string levelName;

    [TextArea]
    public string[] layout;
    // 每行一个字符串，从下到上排列
    // S = 玩家起点
    // G = 终点
    // X = 墙
    // . = 普通格子

    public EnemyConfig[] enemies;
    public bool requireAllVisited;

    [System.Serializable]
    public class EnemyConfig
    {
        public Vector2Int startPos;
        public int delay = 1;
    }
}