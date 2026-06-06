using UnityEngine;

public class GameInitializer : MonoBehaviour
{
    private void Start()
    {
        LevelData currentLevel = GameManager.Instance.GetCurrentLevel();
        if (currentLevel == null) return;

        GameManager.Instance.ResetGameState();

        GridManager.Instance.LoadLevel(currentLevel);
        PlayerController.Instance.Initialize(GridManager.Instance.GetPlayerStartPos());

        // 敌人系统已停用（一笔画 only）：不再调用 EnemyManager.Initialize
        // 将来恢复敌人玩法时，把场景里的 EnemyManager 重新勾选启用，并在此处加回初始化即可

        UndoManager.Instance.Clear();   // 新关卡开始，清空撤回历史
    }
}