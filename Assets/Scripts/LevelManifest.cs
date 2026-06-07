using UnityEngine;

// 正式关卡清单（数据资产）。
// 由「关卡管理」窗口的【应用到游戏】写入：已收藏、按最终顺序排好的关卡引用。
// GameManager 运行时读取它作为关卡序列。工具写、运行时读，彼此解耦、不碰场景。
[CreateAssetMenu(menuName = "Game/LevelManifest")]
public class LevelManifest : ScriptableObject
{
    [Tooltip("游戏正式关卡，按最终游玩顺序排列。请用『关卡管理』窗口的【应用到游戏】写入，不要手改。")]
    public LevelData[] orderedLevels;
}