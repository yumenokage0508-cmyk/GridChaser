using UnityEngine;

public class GameInitializer : MonoBehaviour
{
    [SerializeField] private LevelData levelData;

    private void Start()
    {
        GameManager.Instance.ResetGameState();   // 新增：重置本关结束状态

        GridManager.Instance.LoadLevel(levelData);
        PlayerController.Instance.Initialize(GridManager.Instance.GetPlayerStartPos());
        EnemyManager.Instance.Initialize(levelData);
    }
}