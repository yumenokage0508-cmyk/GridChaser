using UnityEngine;
using UnityEditor;
using System.IO;

// 关卡 JSON 导入器（编辑器工具）。
// 菜单 Tools/GridChaser/导入候选关卡 → 弹窗选 level_generator 产出的 JSON →
// 把其中每一关变成 LevelData 资产，写入 Assets/Levels/_Candidates/。
// 同名资产覆盖刷新（幂等导入）；策展元数据一并写入，curationScore 归零等待打分。
public static class LevelJsonImporter
{
    private const string CandidateDir = "Assets/Levels/_Candidates";

    [MenuItem("Tools/GridChaser/导入候选关卡 (JSON)")]
    public static void ImportFromJson()
    {
        // 1. 弹窗选文件
        string path = EditorUtility.OpenFilePanel("选择生成器输出的 JSON", Application.dataPath, "json");
        if (string.IsNullOrEmpty(path)) return;   // 用户取消

        // 2. 读取文本
        string jsonText;
        try { jsonText = File.ReadAllText(path); }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog("导入失败", "无法读取文件：\n" + e.Message, "确定");
            return;
        }

        // 3. 解析（JsonUtility 会忽略 job/generatedAt 等未声明字段，只取 levels）
        JsonPayload payload = JsonUtility.FromJson<JsonPayload>(jsonText);
        if (payload == null || payload.levels == null || payload.levels.Length == 0)
        {
            EditorUtility.DisplayDialog("导入失败",
                "没在 JSON 里找到 levels 数据。确认选的是生成器输出的 generated_*.json 吗？", "确定");
            return;
        }

        // 4. 确保候选目录存在
        EnsureFolder(CandidateDir);

        // 5. 逐关创建或覆盖
        int created = 0, updated = 0;
        foreach (JsonLevel jl in payload.levels)
        {
            string assetPath = $"{CandidateDir}/{Sanitize(jl.name)}.asset";
            LevelData data = AssetDatabase.LoadAssetAtPath<LevelData>(assetPath);
            bool isNew = data == null;
            if (isNew) data = ScriptableObject.CreateInstance<LevelData>();

            // 运行时字段
            data.levelName        = jl.name;
            data.layout           = jl.layout;
            data.solutionMoves    = jl.solutionMoves;
            data.requireAllVisited = jl.requireAllVisited;
            data.enemies          = new LevelData.EnemyConfig[0];   // 一笔画 only，无敌人

            // 策展元数据（curationScore 归零，等试玩后打分）
            data.curationScore    = 0;
            data.difficulty       = jl.difficulty;
            data.cells            = jl.cells;
            data.interiorPillars  = jl.interiorPillars;
            data.chokepoints      = jl.chokepoints;

            if (isNew) { AssetDatabase.CreateAsset(data, assetPath); created++; }
            else       { EditorUtility.SetDirty(data); updated++; }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("导入完成",
            $"来源：{Path.GetFileName(path)}\n新建 {created} 关，覆盖 {updated} 关。\n位置：{CandidateDir}", "好的");
        Debug.Log($"[LevelImporter] 新建 {created}，覆盖 {updated}，来源 {path}");
    }

    // 递归确保多级文件夹存在（AssetDatabase 一次只能建一层）
    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace("\\", "/");
        string leaf = Path.GetFileName(folder);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    // 把名字里的非法文件名字符替换掉，避免建资产失败
    private static string Sanitize(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    // ---- JSON 映射类：字段名必须与 JSON 的 key 完全一致 ----
    // 未声明的 key（job、generatedAt）会被 JsonUtility 自动忽略。
    [System.Serializable]
    private class JsonLevel
    {
        public string name;
        public string layout;
        public string solutionMoves;
        public bool   requireAllVisited;
        public float  difficulty;
        public int    cells;
        public int    interiorPillars;
        public int    chokepoints;
    }

    [System.Serializable]
    private class JsonPayload
    {
        public JsonLevel[] levels;
    }
}
