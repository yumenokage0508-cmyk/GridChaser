#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

// 可视化手搓关卡编辑器（4c-1：画布 + 笔刷 + 撤回/重做 + 保存）。
// 无限画布，概念上初始全是墙，开发者挖出空地、放起点/终点。
// 操作：左键画(点/拖)、右键框选、中键拖平移、滚轮缩放、Ctrl+Z 撤回 / Ctrl+Y 重做。
// 保存：算出空地最小包围盒，裁成紧凑 .X 图，生成 source="manual" 的 LevelData 进 Assets/Levels/。
// 注：可解校验 + 难度估计在 4c-2 加入；本步保存的 solutionMoves 为空、难度为 0。
public class LevelEditorWindow : EditorWindow
{
    private const string PoolDir = "Assets/Levels";

    private enum CellKind { Empty, Start, Goal }
    private enum Brush { 挖空, 填墙, 起点, 终点 }

    // ---- 画布状态 ----
    private Dictionary<Vector2Int, CellKind> cells = new Dictionary<Vector2Int, CellKind>();
    private Vector2Int? start, goal;

    private Brush brush = Brush.挖空;
    private float cellPx = 24f;        // 缩放：每格像素
    private Vector2 pan = new Vector2(200, 200);   // 画布平移（canvas 局部像素）

    private string levelName = "manual_关卡";
    private LevelData editingAsset;    // 4c-2 才会从已有关卡载入；本步恒为 null（新建）

    // ---- 交互临时状态 ----
    private bool painting;
    private bool boxing;
    private Vector2Int boxStart, boxEnd;

    // 导入"从已有关卡载入"用的对象选择器
    private int pickerId = -1;
    private bool pendingPick;

    // ---- 校验缓存（避免每帧重算）----
    private OneStrokeSolver.Result lastResult;
    private float lastDifficulty;
    private bool validateDirty = true;   // 画布变动后置脏，下次状态栏按需重算

    // ---- 撤回/重做 ----
    private class Snap { public Dictionary<Vector2Int, CellKind> cells; public Vector2Int? start, goal; }
    private readonly Stack<Snap> undo = new Stack<Snap>();
    private readonly Stack<Snap> redo = new Stack<Snap>();

    // ---- 颜色 ----
    private static readonly Color cBg = new Color(0.12f, 0.12f, 0.13f);
    private static readonly Color cLine = new Color(1, 1, 1, 0.06f);
    private static readonly Color cEmpty = new Color(0.35f, 0.35f, 0.40f);
    private static readonly Color cStart = new Color(0.49f, 0.81f, 1f);
    private static readonly Color cGoal = new Color(0.96f, 0.78f, 0.26f);
    private static readonly Color cBox = new Color(0.49f, 0.81f, 1f, 0.25f);

    [MenuItem("Tools/GridChaser/关卡编辑器(手搓)")]
    public static void Open()
    {
        var w = GetWindow<LevelEditorWindow>("关卡编辑器");
        var sel = Selection.activeObject as LevelData;   // Project 里选中关卡则直接载入
        if (sel != null) w.LoadFromLevelData(sel);
    }

    // 供关卡管理窗口【✎编辑】调用
    public static void OpenWith(LevelData d)
    {
        var w = GetWindow<LevelEditorWindow>("关卡编辑器");
        w.LoadFromLevelData(d);
        w.Focus();
    }

    // 载入已有关卡资产（可覆盖保存它）
    public void LoadFromLevelData(LevelData d)
    {
        if (d == null) return;
        ParseLayout(d.layout);
        editingAsset = d;
        levelName = d.name;
    }

    // 载入粘贴的文本（视为全新关卡，只能另存为）
    public void LoadFromLayoutText(string text)
    {
        ParseLayout(text);
        editingAsset = null;
    }

    // 反解析：把 X.GS 字符串变回画布字典（保存的逆操作）。行号即 y，与保存/游戏读法一致。
    private void ParseLayout(string layout)
    {
        cells.Clear(); start = null; goal = null;
        if (!string.IsNullOrEmpty(layout))
        {
            string[] rows = layout.Replace("\r", "").Split('\n');
            for (int y = 0; y < rows.Length; y++)
            {
                string row = rows[y];
                for (int x = 0; x < row.Length; x++)
                {
                    char c = row[x];
                    var p = new Vector2Int(x, y);
                    if (c == 'S') { cells[p] = CellKind.Start; start = p; }
                    else if (c == 'G') { cells[p] = CellKind.Goal; goal = p; }
                    else if (c == '.') cells[p] = CellKind.Empty;
                    // 'X'、空格、其它字符 = 墙，跳过
                }
            }
        }
        undo.Clear(); redo.Clear();
        validateDirty = true;
        pan = new Vector2(40, 40);
        Repaint();
    }

    private void OnGUI()
    {
        const float topH = 46, botH = 22;
        Rect canvas = new Rect(0, topH, position.width, position.height - topH - botH);

        HandleImportPicker();
        DrawToolbar(new Rect(0, 0, position.width, topH));
        HandleCanvasEvents(canvas);
        DrawCanvas(canvas);
        DrawStatusBar(new Rect(0, position.height - botH, position.width, botH));
    }

    // ---------- 工具栏 ----------
    private void DrawToolbar(Rect area)
    {
        GUILayout.BeginArea(area, EditorStyles.toolbar);
        EditorGUILayout.BeginHorizontal();

        // 笔刷
        EditorGUILayout.LabelField("笔刷", GUILayout.Width(30));
        brush = (Brush)GUILayout.Toolbar((int)brush, new[] { "挖空", "填墙", "起点", "终点" }, GUILayout.Width(200));

        GUILayout.Space(8);
        if (GUILayout.Button("导入", EditorStyles.toolbarButton, GUILayout.Width(45)))
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("从已有关卡载入…"), false, () => pendingPick = true);
            menu.AddItem(new GUIContent("粘贴 X.GS 文本载入…"), false, () => PasteLayoutWindow.Open(this));
            menu.ShowAsContext();
        }
        if (GUILayout.Button("▶ 试玩", EditorStyles.toolbarButton, GUILayout.Width(50)))
        {
            if (editingAsset != null)
            {
                LevelData target = editingAsset;
                EditorApplication.delayCall += () => LevelPlaytest.StartPlaytest(target);
            }
            else EditorUtility.DisplayDialog("先保存", "新关卡需要先『另存为』成资产后才能试玩。", "确定");
        }
        GUILayout.Space(8);
        if (GUILayout.Button("撤回", EditorStyles.toolbarButton, GUILayout.Width(45))) Undo();
        if (GUILayout.Button("重做", EditorStyles.toolbarButton, GUILayout.Width(45))) Redo();
        if (GUILayout.Button("清空", EditorStyles.toolbarButton, GUILayout.Width(45))) ResetCanvas();

        GUILayout.FlexibleSpace();

        EditorGUILayout.LabelField("名称", GUILayout.Width(30));
        levelName = EditorGUILayout.TextField(levelName, GUILayout.Width(160));
        using (new EditorGUI.DisabledScope(editingAsset == null))   // 全新关卡：保存置灰，只能另存为
            if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(45))) Save(false);
        if (GUILayout.Button("另存为", EditorStyles.toolbarButton, GUILayout.Width(55))) Save(true);

        EditorGUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    private void DrawStatusBar(Rect area)
    {
        EnsureValidated();
        string verdict = lastResult != null ? lastResult.Message : "";
        if (lastResult != null && lastResult.Solvable) verdict += $"（难度 {lastDifficulty:0.00}）";

        GUILayout.BeginArea(area, EditorStyles.helpBox);
        GUILayout.Label(
            $"空地 {cells.Count}   {verdict}   缩放 {cellPx:0}px    " +
            "（左键画 / 右键框选 / Alt 临时切挖空填墙 / 中键平移 / 滚轮缩放 / Ctrl+Z 撤回 / Ctrl+Y 重做）",
            EditorStyles.miniLabel);
        GUILayout.EndArea();
    }

    // ---------- 导入：从已有关卡载入（对象选择器）----------
    private void HandleImportPicker()
    {
        if (pendingPick && Event.current.type == EventType.Layout)
        {
            pendingPick = false;
            pickerId = GUIUtility.GetControlID(FocusType.Passive);
            EditorGUIUtility.ShowObjectPicker<LevelData>(null, false, "", pickerId);
        }
        if (pickerId != -1 && Event.current.commandName == "ObjectSelectorClosed"
            && EditorGUIUtility.GetObjectPickerControlID() == pickerId)
        {
            var picked = EditorGUIUtility.GetObjectPickerObject() as LevelData;
            pickerId = -1;
            if (picked != null) LoadFromLevelData(picked);
        }
    }

    // ---------- 校验 ----------
    // 把当前空地集合(含 S/G)交给求解器，缓存结果；只在画布变动后重算
    private void EnsureValidated()
    {
        if (!validateDirty) return;
        validateDirty = false;

        var set = new HashSet<Vector2Int>(cells.Keys);
        lastResult = OneStrokeSolver.Solve(set, start, goal);
        lastDifficulty = lastResult.Solvable
            ? OneStrokeSolver.EstimateDifficulty(set, start.Value, goal.Value)
            : 0f;
    }

    // ---------- 坐标换算 ----------
    private Vector2 GridToLocal(int x, int y) => pan + new Vector2(x * cellPx, y * cellPx);
    private Vector2Int LocalToGrid(Vector2 local) =>
        new Vector2Int(Mathf.FloorToInt((local.x - pan.x) / cellPx), Mathf.FloorToInt((local.y - pan.y) / cellPx));

    // ---------- 事件 ----------
    private void HandleCanvasEvents(Rect canvas)
    {
        Event e = Event.current;
        Vector2 local = e.mousePosition - canvas.position;
        bool inside = canvas.Contains(e.mousePosition);

        // 键盘：撤回/重做
        if (e.type == EventType.KeyDown && e.control)
        {
            if (e.keyCode == KeyCode.Z) { if (e.shift) Redo(); else Undo(); e.Use(); return; }
            if (e.keyCode == KeyCode.Y) { Redo(); e.Use(); return; }
        }

        if (!inside && e.type != EventType.MouseDrag && e.type != EventType.MouseUp) return;

        switch (e.type)
        {
            case EventType.ScrollWheel:
                if (inside)
                {
                    float old = cellPx;
                    cellPx = Mathf.Clamp(cellPx - e.delta.y * 2f, 8f, 48f);
                    Vector2 grid = (local - pan) / old;     // 以鼠标为锚点缩放
                    pan = local - grid * cellPx;
                    e.Use(); Repaint();
                }
                break;

            case EventType.MouseDown:
                if (!inside) break;
                if (e.button == 2) { e.Use(); }                       // 中键：平移开始
                else if (e.button == 0) { PushUndo(); painting = true; PaintCell(LocalToGrid(local)); e.Use(); Repaint(); }
                else if (e.button == 1) { boxing = true; boxStart = boxEnd = LocalToGrid(local); e.Use(); Repaint(); }
                break;

            case EventType.MouseDrag:
                if (e.button == 2) { pan += e.delta; e.Use(); Repaint(); }
                else if (e.button == 0 && painting) { PaintCell(LocalToGrid(local)); e.Use(); Repaint(); }
                else if (e.button == 1 && boxing) { boxEnd = LocalToGrid(local); e.Use(); Repaint(); }
                break;

            case EventType.MouseUp:
                if (e.button == 0 && painting) { painting = false; e.Use(); }
                else if (e.button == 1 && boxing) { ApplyBox(); boxing = false; e.Use(); Repaint(); }
                break;
        }
    }

    // ---------- 绘制 ----------
    private void DrawCanvas(Rect canvas)
    {
        EditorGUI.DrawRect(canvas, cBg);
        GUI.BeginClip(canvas);

        // 可见网格范围
        int x0 = Mathf.FloorToInt(-pan.x / cellPx), x1 = Mathf.FloorToInt((canvas.width - pan.x) / cellPx);
        int y0 = Mathf.FloorToInt(-pan.y / cellPx), y1 = Mathf.FloorToInt((canvas.height - pan.y) / cellPx);

        // 网格线（缩放够大时才画）
        if (cellPx >= 12)
        {
            for (int x = x0; x <= x1 + 1; x++)
                EditorGUI.DrawRect(new Rect(GridToLocal(x, 0).x, 0, 1, canvas.height), cLine);
            for (int y = y0; y <= y1 + 1; y++)
                EditorGUI.DrawRect(new Rect(0, GridToLocal(0, y).y, canvas.width, 1), cLine);
        }

        // 已画的格子
        foreach (var kv in cells)
        {
            Vector2Int p = kv.Key;
            if (p.x < x0 || p.x > x1 || p.y < y0 || p.y > y1) continue;
            Vector2 lp = GridToLocal(p.x, p.y);
            Rect r = new Rect(lp.x + 1, lp.y + 1, cellPx - 2, cellPx - 2);
            Color c = kv.Value == CellKind.Start ? cStart : kv.Value == CellKind.Goal ? cGoal : cEmpty;
            EditorGUI.DrawRect(r, c);
            if (cellPx >= 16 && kv.Value != CellKind.Empty)
                GUI.Label(r, kv.Value == CellKind.Start ? "S" : "G", CenteredLabel());
        }

        // 框选预览
        if (boxing)
        {
            RectInt b = BoxRect();
            Vector2 a = GridToLocal(b.xMin, b.yMin);
            EditorGUI.DrawRect(new Rect(a.x, a.y, b.width * cellPx, b.height * cellPx), cBox);
        }

        GUI.EndClip();
    }

    private GUIStyle CenteredLabel()
    {
        var s = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
        s.normal.textColor = Color.black;
        return s;
    }

    // ---------- 绘制操作 ----------
    // Alt 按住时临时对调 挖空/填墙（对 起点/终点 不生效）
    private Brush EffectiveBrush()
    {
        if (!Event.current.alt) return brush;
        if (brush == Brush.挖空) return Brush.填墙;
        if (brush == Brush.填墙) return Brush.挖空;
        return brush;
    }

    private void PaintCell(Vector2Int p)
    {
        validateDirty = true;
        switch (EffectiveBrush())
        {
            case Brush.填墙:
                cells.Remove(p);
                if (start == p) start = null;
                if (goal == p) goal = null;
                break;
            case Brush.挖空:
                cells[p] = CellKind.Empty;
                if (start == p) start = null;
                if (goal == p) goal = null;
                break;
            case Brush.起点:
                if (start.HasValue && start.Value != p) cells[start.Value] = CellKind.Empty;
                if (goal == p) goal = null;
                cells[p] = CellKind.Start; start = p;
                break;
            case Brush.终点:
                if (goal.HasValue && goal.Value != p) cells[goal.Value] = CellKind.Empty;
                if (start == p) start = null;
                cells[p] = CellKind.Goal; goal = p;
                break;
        }
    }

    private RectInt BoxRect()
    {
        int minX = Mathf.Min(boxStart.x, boxEnd.x), maxX = Mathf.Max(boxStart.x, boxEnd.x);
        int minY = Mathf.Min(boxStart.y, boxEnd.y), maxY = Mathf.Max(boxStart.y, boxEnd.y);
        return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private void ApplyBox()
    {
        PushUndo();
        // 起点/终点是唯一的，框选时只落在结束格；挖空/填墙才整片刷
        if (EffectiveBrush() == Brush.起点 || EffectiveBrush() == Brush.终点) { PaintCell(boxEnd); return; }
        RectInt b = BoxRect();
        for (int x = b.xMin; x < b.xMax; x++)
            for (int y = b.yMin; y < b.yMax; y++)
                PaintCell(new Vector2Int(x, y));
    }

    private void ResetCanvas()
    {
        if (!EditorUtility.DisplayDialog("清空画布", "确定清空当前所有内容吗？此操作不可撤销。", "清空", "取消")) return;
        cells.Clear(); start = null; goal = null;
        undo.Clear(); redo.Clear();
        validateDirty = true;
        Repaint();
    }

    // ---------- 撤回/重做 ----------
    private Snap MakeSnap() => new Snap
    {
        cells = new Dictionary<Vector2Int, CellKind>(cells),
        start = start,
        goal = goal
    };

    private void Apply(Snap s) { cells = new Dictionary<Vector2Int, CellKind>(s.cells); start = s.start; goal = s.goal; validateDirty = true; }

    private void PushUndo() { undo.Push(MakeSnap()); redo.Clear(); }

    private void Undo()
    {
        if (undo.Count == 0) return;
        redo.Push(MakeSnap());
        Apply(undo.Pop());
        Repaint();
    }

    private void Redo()
    {
        if (redo.Count == 0) return;
        undo.Push(MakeSnap());
        Apply(redo.Pop());
        Repaint();
    }

    // ---------- 保存 ----------
    private void Save(bool saveAs)
    {
        if (cells.Count == 0) { EditorUtility.DisplayDialog("保存失败", "画布是空的，没有任何空地。", "确定"); return; }

        if ((!start.HasValue || !goal.HasValue) &&
            !EditorUtility.DisplayDialog("缺少起点/终点",
                "当前缺少起点或终点，保存的关卡暂时无法正常游玩。仍要保存为半成品吗？", "仍保存", "取消"))
            return;

        string layout = BuildLayout();

        if (!AssetDatabase.IsValidFolder(PoolDir)) AssetDatabase.CreateFolder("Assets", "Levels");
        string safe = Sanitize(string.IsNullOrEmpty(levelName) ? "manual_关卡" : levelName);

        LevelData d;
        string path;

        if (!saveAs && editingAsset != null)
        {
            // 保存：覆盖正在编辑的原关卡（4c-2 的编辑入口才会走到这里）
            d = editingAsset;
            path = AssetDatabase.GetAssetPath(d);
        }
        else
        {
            // 另存为（以及全新关卡）：按名称建资产；若重名，问 覆盖 / 创建副本 / 取消
            string wanted = $"{PoolDir}/{safe}.asset";
            LevelData existing = AssetDatabase.LoadAssetAtPath<LevelData>(wanted);
            if (existing != null)
            {
                int choice = EditorUtility.DisplayDialogComplex("名称已存在",
                    $"已存在名为「{safe}」的关卡，要怎么处理？", "覆盖", "取消", "创建副本");
                if (choice == 1) return;                                   // 取消
                if (choice == 0) { d = existing; path = wanted; }          // 覆盖
                else                                                       // 创建副本
                {
                    path = AssetDatabase.GenerateUniqueAssetPath(wanted);
                    d = ScriptableObject.CreateInstance<LevelData>();
                    AssetDatabase.CreateAsset(d, path);
                }
            }
            else
            {
                path = wanted;
                d = ScriptableObject.CreateInstance<LevelData>();
                AssetDatabase.CreateAsset(d, path);
            }
        }

        EnsureValidated();   // 确保校验结果是最新的

        d.levelName = System.IO.Path.GetFileNameWithoutExtension(path);
        d.layout = layout;
        d.solutionMoves = lastResult != null ? lastResult.solutionMoves : "";
        d.requireAllVisited = true;
        d.enemies = new LevelData.EnemyConfig[0];
        d.source = "manual";
        d.solvable = lastResult != null && lastResult.Solvable;
        d.difficulty = lastDifficulty;
        d.cells = cells.Count;
        d.interiorPillars = 0;       // 手搓暂不统计这两项结构指标
        d.chokepoints = 0;
        d.curationScore = 0;
        d.isFavorite = false;
        d.finalOrder = 0;

        EditorUtility.SetDirty(d);
        AssetDatabase.SaveAssets();
        EditorGUIUtility.PingObject(d);
        EditorUtility.DisplayDialog("已保存", $"关卡已保存到：\n{path}\n（已进入关卡管理窗口的候选库）", "好的");
    }

    // 把空地（含 S/G）裁成紧凑 .X 矩形；y 小的在前 = 顶部行在前，符合 GridManager 读法
    private string BuildLayout()
    {
        int minX = cells.Keys.Min(p => p.x), maxX = cells.Keys.Max(p => p.x);
        int minY = cells.Keys.Min(p => p.y), maxY = cells.Keys.Max(p => p.y);

        var rows = new List<string>();
        for (int y = minY; y <= maxY; y++)
        {
            var sb = new System.Text.StringBuilder();
            for (int x = minX; x <= maxX; x++)
            {
                Vector2Int p = new Vector2Int(x, y);
                if (cells.TryGetValue(p, out CellKind k))
                    sb.Append(k == CellKind.Start ? 'S' : k == CellKind.Goal ? 'G' : '.');
                else
                    sb.Append('X');
            }
            rows.Add(sb.ToString());
        }
        return string.Join("\n", rows);
    }

    private static string Sanitize(string name)
    {
        foreach (char c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }
}

// 粘贴文本载入的小弹窗
public class PasteLayoutWindow : EditorWindow
{
    private LevelEditorWindow owner;
    private string text = "";
    private Vector2 scroll;

    public static void Open(LevelEditorWindow owner)
    {
        var w = GetWindow<PasteLayoutWindow>(true, "粘贴关卡文本", true);
        w.owner = owner;
        w.minSize = new Vector2(340, 280);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("粘贴 X . G S 地图（X=墙  .=空地  S=起点  G=终点）", EditorStyles.boldLabel);
        EditorGUILayout.Space(2);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        text = EditorGUILayout.TextArea(text, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("载入到编辑器"))
        {
            if (owner != null) { owner.LoadFromLayoutText(text); owner.Focus(); Close(); }
        }
        if (GUILayout.Button("取消")) Close();
        EditorGUILayout.EndHorizontal();
    }
}
#endif