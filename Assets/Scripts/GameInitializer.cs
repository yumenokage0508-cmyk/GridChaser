using UnityEngine;

public class GameInitializer : MonoBehaviour
{
    [SerializeField] private LevelData levelData;

    private void Start()
    {
        // 显式按顺序初始化，依赖关系一目了然
        GridManager.Instance.LoadLevel(levelData);
        PlayerController.Instance.Initialize(GridManager.Instance.GetPlayerStartPos());
        EnemyManager.Instance.Initialize(levelData);
    }
}