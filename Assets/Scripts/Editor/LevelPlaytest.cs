#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

// 关卡试玩工具（编辑器）。
// 把一关或一串关卡设为「试玩队列」（存进 SessionState，能跨域重载存活），打开 Game 场景进 Play。
// GameManager 启动时读取队列：通关重玩本关，按 N 切下一关；停止播放时自动清除队列。
[InitializeOnLoad]
public static class LevelPlaytest
{
    private const string GameScenePath = "Assets/Scenes/Game.unity";
    private static bool pendingRestart;   // 是否"切换试玩目标"导致的停止（停下后需自动重进）

    static LevelPlaytest()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    // 单关试玩 = 长度为 1 的队列
    public static void StartPlaytest(LevelData level)
    {
        if (level == null) return;
        StartSequence(new List<LevelData> { level });
    }

    // 序列试玩：按给定顺序排队
    public static void StartSequence(IList<LevelData> levels)
    {
        if (levels == null || levels.Count == 0) return;

        var paths = new List<string>();
        foreach (LevelData lv in levels)
        {
            if (lv == null) continue;
            string p = AssetDatabase.GetAssetPath(lv);
            if (!string.IsNullOrEmpty(p)) paths.Add(p);
        }
        if (paths.Count == 0)
        {
            EditorUtility.DisplayDialog("试玩失败", "选中的关卡都不是已保存的资产。", "确定");
            return;
        }

        SessionState.SetString(GameManager.PlaytestPathsKey, string.Join("\n", paths));

        if (EditorApplication.isPlaying)
        {
            pendingRestart = true;
            EditorApplication.isPlaying = false;
        }
        else
        {
            EnterPlay();
        }
    }

    // 测试入口：试玩当前在 Project 里选中的 LevelData
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
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            EditorSceneManager.OpenScene(GameScenePath);
        }
        EditorApplication.isPlaying = true;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode) return;

        if (pendingRestart)
        {
            pendingRestart = false;
            EnterPlay();
        }
        else
        {
            SessionState.EraseString(GameManager.PlaytestPathsKey);   // 正常停止：清除队列
        }
    }
}
#endif