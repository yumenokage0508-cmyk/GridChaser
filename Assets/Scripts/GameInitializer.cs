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
        EnemyManager.Instance.Initialize(currentLevel);
    }
}