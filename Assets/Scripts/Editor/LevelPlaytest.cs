#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// 关卡试玩工具（编辑器）。
// 把指定关卡设为「试玩目标」（存进 SessionState，能跨域重载存活），然后打开 Game 场景进 Play。
// GameManager 启动时若发现试玩目标，就加载它而非正常关卡序列；停止播放时自动清除目标。
// 4b-2 的策展窗口里每个【▶ 试玩】按钮都会调用这里的 StartPlaytest。
[InitializeOnLoad]
public static class LevelPlaytest
{
    private const string GameScenePath = "Assets/Scenes/Game.unity";
    private static bool pendingRestart;   // 是否"切换试玩关"导致的停止（停下后需自动重进）

    // [InitializeOnLoad] + 静态构造：编辑器加载时注册播放状态回调
    static LevelPlaytest()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    // 供菜单 / 策展窗口调用：试玩某一关
    public static void StartPlaytest(LevelData level)
    {
        if (level == null) return;

        string path = AssetDatabase.GetAssetPath(level);
        if (string.IsNullOrEmpty(path))
        {
            EditorUtility.DisplayDialog("试玩失败", "这个关卡还不是已保存的资产，无法试玩。", "确定");
            return;
        }

        // 记下试玩目标（GameManager 启动时会读取它）
        SessionState.SetString(GameManager.PlaytestSessionKey, path);

        if (EditorApplication.isPlaying)
        {
            // 正在播放别的关：先停，停下后自动重进（见 OnPlayModeChanged）
            pendingRestart = true;
            EditorApplication.isPlaying = false;
        }
        else
        {
            EnterPlay();
        }
    }

    // 测试入口（4b-1 阶段用）：试玩当前在 Project 里选中的 LevelData
    [MenuItem("Tools/GridChaser/试玩选中的关卡")]
    public static void PlaytestSelected()
    {
        LevelData lv = Selection.activeObject as LevelData;
        if (lv == null)
        {
            EditorUtility.DisplayDialog("没选中关卡",
                "请先在 Project 窗口里点选一个 LevelData 资产，再用此菜单。", "确定");
            return;
        }
        StartPlaytest(lv);
    }

    private static void EnterPlay()
    {
        var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (active.path != GameScenePath)
        {
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();   // 切场景前先问要不要存当前改动
            EditorSceneManager.OpenScene(GameScenePath);
        }
        EditorApplication.isPlaying = true;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode) return;

        if (pendingRestart)
        {
            // 是「切换试玩关」触发的停止：保留目标，重新进入播放
            pendingRestart = false;
            EnterPlay();
        }
        else
        {
            // 正常停止播放：清除试玩目标，以免下次正常 Play 被劫持
            SessionState.EraseString(GameManager.PlaytestSessionKey);
        }
    }
}
#endif