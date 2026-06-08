using UnityEngine;

public class GameInitializer : MonoBehaviour
{
    [Header("References")]
    [Tooltip("主相机上的 CameraFitter，用于把关卡完整框入视野。")]
    [SerializeField] private CameraFitter cameraFitter;

    private void Start()
    {
        LevelData currentLevel = GameManager.Instance.GetCurrentLevel();
        if (currentLevel == null) return;

        GameManager.Instance.ResetGameState();

        GridManager.Instance.LoadLevel(currentLevel);

        // 关卡尺寸确定后，把整张关卡居中、完整地框进相机
        // 网格中心在世界原点(0,0)——GridToWorld 按 (size-1)*cellSize*0.5 对称偏移
        if (cameraFitter != null)
        {
            float w = GridManager.Instance.GridWidth * GridManager.Instance.CellSize;
            float h = GridManager.Instance.GridHeight * GridManager.Instance.CellSize;
            cameraFitter.Fit(Vector2.zero, w, h);
        }

        PlayerController.Instance.Initialize(GridManager.Instance.GetPlayerStartPos());

        // 敌人系统已停用（一笔画 only）：不再调用 EnemyManager.Initialize
        // 将来恢复敌人玩法时，把场景里的 EnemyManager 重新勾选启用，并在此处加回初始化即可

        UndoManager.Instance.Clear();   // 新关卡开始，清空撤回历史
    }
}