using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    private bool isGameOver = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void TriggerDeath()
    {
        if (isGameOver) return;   // 本关已结束，忽略后续触发
        isGameOver = true;
        Debug.Log("DEAD");
        StartCoroutine(ReloadAfterDelay(0.5f));
    }

    public void TriggerWin()
    {
        if (isGameOver) return;
        isGameOver = true;
        Debug.Log("WIN");
    }

    // 每次进入/重载关卡时由 GameInitializer 调用，重置状态
    public void ResetGameState()
    {
        isGameOver = false;
    }

    private IEnumerator ReloadAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}