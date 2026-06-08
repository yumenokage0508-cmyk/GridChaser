#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

// 关卡管理窗口（菜单 Tools/GridChaser/关卡管理）。
// 两个标签页：
//   候选库  —— 全部关卡，多选/打分/收藏/重命名/试玩/删除，可排序(升降序)/筛选。
//   正式视图 —— 只看已收藏关卡；可打分/试玩；「最终顺序」按钮激活后用 ▲▼ 手排顺序，
//             即是写入游戏的实际顺序；【应用到游戏】把它写进 LevelManifest 清单资产。
public class CurationWindow : EditorWindow
{
    private const string PoolDir = "Assets/Levels";
    private const string ManifestPath = "Assets/Levels/LevelManifest.asset";
    private const string SelKey = "GridChaser.CurationSelected";   // 选中状态持久化(跨域重载)

    private enum Tab { 候选库, 正式视图 }
    private enum SortMode { 星级, 难度, 名称 }
    private enum FilterMode { 全部, 仅已收藏, 仅未评分 }

    private readonly List<LevelData> all = new List<LevelData>();
    private readonly HashSet<LevelData> selected = new HashSet<LevelData>();
    private Tab tab = Tab.候选库;
    private SortMode sort = SortMode.星级;
    private FilterMode filter = FilterMode.全部;
    private bool sortAscending = false;     // 默认降序
    private bool finalOrderMode = false;    // 正式视图：最终顺序编辑模式
    private Vector2 scroll;
    private bool dirty;

    // 重命名状态
    private LevelData renaming;
    private string renameBuffer;
    private bool focusPending;

    // 撤销/重做（只覆盖 打分/收藏/最终顺序；仅窗口内有效，不跨重启）
    private readonly Stack<CState> undoStack = new Stack<CState>();
    private readonly Stack<CState> redoStack = new Stack<CState>();
    private class CState { public Dictionary<LevelData, (int s, bool f, int o)> map; }

    private HashSet<string> playtestSet = new HashSet<string>();   // 当前正在试玩的关卡路径
    private int lastClickedIndex = -1;                             // Shift 连选锚点（显示列表内）

    [MenuItem("Tools/GridChaser/关卡管理")]
    public static void Open() => GetWindow<CurationWindow>("关卡管理");

    private void OnEnable() => Refresh();
    private void OnFocus() => Refresh();

    private void Refresh()
    {
        all.Clear();
        foreach (string guid in AssetDatabase.FindAssets("t:LevelData", new[] { PoolDir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            LevelData d = AssetDatabase.LoadAssetAtPath<LevelData>(p);
            if (d != null) all.Add(d);
        }
        LoadSelection();
    }

    private void OnGUI()
    {
        HandleUndoShortcuts();
        RefreshPlaytestSet();
        DrawTabs();
        if (tab == Tab.正式视图) DrawFinalBar();
        DrawToolbar();
        DrawBatchBar();
        EditorGUILayout.Space(2);
        DrawHeader();
        DrawList();

        if (dirty) { AssetDatabase.SaveAssets(); dirty = false; }
    }

    private void DrawTabs()
    {
        Tab newTab = (Tab)GUILayout.Toolbar((int)tab, new[] { "候选库", "正式视图" });
        if (newTab != tab) { tab = newTab; if (tab != Tab.正式视图) finalOrderMode = false; }
    }

    private void DrawFinalBar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        bool newMode = GUILayout.Toggle(finalOrderMode, "最终顺序", "Button", GUILayout.Width(80));
        if (newMode != finalOrderMode) { finalOrderMode = newMode; if (newMode) EnterFinalOrderMode(); }
        GUILayout.Label(finalOrderMode
            ? "用 ▲▼ 调整顺序（这就是写入游戏的实际顺序）"
            : "激活后可手排最终顺序");
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("应用到游戏", GUILayout.Width(90))) ApplyToGame();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawToolbar()
    {
        bool lockSort = (tab == Tab.正式视图 && finalOrderMode);
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUILayout.LabelField($"共 {all.Count} 关 / 已选 {selected.Count}",
            EditorStyles.boldLabel, GUILayout.Width(170));
        GUILayout.FlexibleSpace();
        using (new EditorGUI.DisabledScope(lockSort))
        {
            EditorGUILayout.LabelField("排序", GUILayout.Width(30));
            sort = (SortMode)EditorGUILayout.EnumPopup(sort, EditorStyles.toolbarPopup, GUILayout.Width(60));
            if (GUILayout.Button(sortAscending ? "▲ 升序" : "▼ 降序", EditorStyles.toolbarButton, GUILayout.Width(58)))
                sortAscending = !sortAscending;
            EditorGUILayout.LabelField("筛选", GUILayout.Width(30));
            filter = (FilterMode)EditorGUILayout.EnumPopup(filter, EditorStyles.toolbarPopup, GUILayout.Width(80));
        }
        if (GUILayout.Button("新建关卡", EditorStyles.toolbarButton, GUILayout.Width(64)))
            EditorApplication.delayCall += () => LevelEditorWindow.OpenBlank();
        using (new EditorGUI.DisabledScope(undoStack.Count == 0))
            if (GUILayout.Button("撤销", EditorStyles.toolbarButton, GUILayout.Width(45))) Undo();
        using (new EditorGUI.DisabledScope(redoStack.Count == 0))
            if (GUILayout.Button("重做", EditorStyles.toolbarButton, GUILayout.Width(45))) Redo();
        if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(45))) Refresh();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawBatchBar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        EditorGUILayout.LabelField("批量(对选中)", GUILayout.Width(82));
        using (new EditorGUI.DisabledScope(selected.Count == 0))
        {
            if (GUILayout.Button("+★", GUILayout.Width(40))) BatchAddStar(+1);
            if (GUILayout.Button("-★", GUILayout.Width(40))) BatchAddStar(-1);
            if (GUILayout.Button("收藏", GUILayout.Width(42))) BatchFavorite(true);
            if (GUILayout.Button("取消收藏", GUILayout.Width(64))) BatchFavorite(false);
            if (GUILayout.Button("▶ 序列试玩", GUILayout.Width(82))) RequestSequencePlaytest();
            if (GUILayout.Button("删除", GUILayout.Width(42))) BatchDelete();
        }
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("全选(当前)", GUILayout.Width(80)))
        { foreach (LevelData l in CurrentList()) selected.Add(l); SaveSelection(); }
        if (GUILayout.Button("清空选择", GUILayout.Width(70)))
        { selected.Clear(); SaveSelection(); }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawHeader()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("", GUILayout.Width(20));
        if (tab == Tab.正式视图 && finalOrderMode)
            EditorGUILayout.LabelField("序", GUILayout.Width(70));
        EditorGUILayout.LabelField("名称(单击改名)", GUILayout.Width(150));
        EditorGUILayout.LabelField("来源", GUILayout.Width(70));
        EditorGUILayout.LabelField("难度", GUILayout.Width(52));
        EditorGUILayout.LabelField("格数", GUILayout.Width(44));
        EditorGUILayout.LabelField("咽喉", GUILayout.Width(44));
        EditorGUILayout.LabelField("评分", GUILayout.Width(92));
        EditorGUILayout.LabelField("收藏", GUILayout.Width(54));
        EditorGUILayout.LabelField("操作", GUILayout.Width(54));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawList()
    {
        List<LevelData> list = CurrentList();
        bool reorder = (tab == Tab.正式视图 && finalOrderMode);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        bool exit = false;
        for (int i = 0; i < list.Count; i++)
        {
            if (DrawRow(list[i], reorder, list, i)) { exit = true; break; }
        }
        EditorGUILayout.EndScrollView();
        if (exit) { Repaint(); GUIUtility.ExitGUI(); }
    }

    // 返回 true 表示发生结构性改动（删除/重命名/移动），调用方应结束本帧 GUI
    private bool DrawRow(LevelData l, bool reorder, List<LevelData> list, int index)
    {
        bool structural = false;
        bool wantConfirmRename = false;

        Color oldBg = GUI.backgroundColor;
        string assetPath = AssetDatabase.GetAssetPath(l);
        if (playtestSet.Contains(assetPath)) GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);  // 正在试玩：泛绿
        else if (!l.solvable) GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);                  // 不可解：泛红
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

        bool sel = selected.Contains(l);
        bool newSel = EditorGUILayout.Toggle(sel, GUILayout.Width(20));
        if (newSel != sel)
        {
            if (Event.current.shift && lastClickedIndex >= 0 && lastClickedIndex < list.Count)
            {
                int lo = Mathf.Min(lastClickedIndex, index), hi = Mathf.Max(lastClickedIndex, index);
                for (int k = lo; k <= hi; k++) selected.Add(list[k]);   // 连选：锚点到本行全选
            }
            else { if (newSel) selected.Add(l); else selected.Remove(l); }
            lastClickedIndex = index;
            SaveSelection();
        }

        // 最终顺序模式：序号 + ▲▼
        if (reorder)
        {
            EditorGUILayout.LabelField((index + 1).ToString(), GUILayout.Width(24));
            using (new EditorGUI.DisabledScope(index == 0))
                if (GUILayout.Button("▲", GUILayout.Width(22))) { Move(list, index, -1); structural = true; }
            using (new EditorGUI.DisabledScope(index == list.Count - 1))
                if (GUILayout.Button("▼", GUILayout.Width(22))) { Move(list, index, +1); structural = true; }
        }

        // 名称：单击变输入框，回车确认 / Esc 取消
        if (renaming == l)
        {
            GUI.SetNextControlName("renameField");
            renameBuffer = EditorGUILayout.TextField(renameBuffer, GUILayout.Width(150));
            if (focusPending) { EditorGUI.FocusTextInControl("renameField"); focusPending = false; }
            Event e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            { wantConfirmRename = true; e.Use(); }
            else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            { renaming = null; e.Use(); }
        }
        else
        {
            if (GUILayout.Button(l.name, EditorStyles.label, GUILayout.Width(150)))
            { renaming = l; renameBuffer = l.name; focusPending = true; }
        }

        EditorGUILayout.LabelField(string.IsNullOrEmpty(l.source) ? "-" : l.source, GUILayout.Width(70));
        EditorGUILayout.LabelField(l.solvable ? l.difficulty.ToString("0.00") : "不可解", GUILayout.Width(52));
        EditorGUILayout.LabelField(l.cells.ToString(), GUILayout.Width(44));
        EditorGUILayout.LabelField(l.chokepoints.ToString(), GUILayout.Width(44));

        DrawStars(l);

        bool fav = EditorGUILayout.ToggleLeft(" ", l.isFavorite, GUILayout.Width(54));
        if (fav != l.isFavorite) { PushUndo(); l.isFavorite = fav; EditorUtility.SetDirty(l); dirty = true; }

        // 编辑：用手搓编辑器载入这一关
        if (GUILayout.Button("✎", GUILayout.Width(24)))
        {
            LevelData target = l;
            EditorApplication.delayCall += () => LevelEditorWindow.OpenWith(target);
        }

        // 试玩：点已选中行=对全部选中序列试玩；点未选中行=单关试玩
        if (GUILayout.Button("▶", GUILayout.Width(24)))
        {
            if (selected.Contains(l))
            {
                List<LevelData> ordered = CurrentList().Where(x => selected.Contains(x)).ToList();
                EditorApplication.delayCall += () => LevelPlaytest.StartSequence(ordered);
            }
            else
            {
                EditorApplication.delayCall += () => LevelPlaytest.StartPlaytest(l);
            }
        }

        if (GUILayout.Button("×", GUILayout.Width(24)))
        {
            if (EditorUtility.DisplayDialog("删除关卡", $"确定删除 {l.name} 吗？此操作不可撤销。", "删除", "取消"))
            {
                selected.Remove(l);
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(l));
                SaveSelection();
                Refresh();
                structural = true;
            }
        }

        EditorGUILayout.EndHorizontal();
        GUI.backgroundColor = oldBg;

        if (wantConfirmRename) structural = ConfirmRename(l) || structural;
        return structural;
    }

    // 五颗星：点第 N 颗→设为 N；点当前最高那颗→归零。
    // 点已选中行的星→所有选中关卡都设成该分；点未选中行→只改这一关。
    private void DrawStars(LevelData l)
    {
        EditorGUILayout.BeginHorizontal(GUILayout.Width(92));
        for (int i = 1; i <= 5; i++)
        {
            bool on = i <= l.curationScore;
            if (GUILayout.Button(on ? "★" : "☆", EditorStyles.label, GUILayout.Width(16)))
            {
                int target = (i == l.curationScore) ? 0 : i;
                PushUndo();
                if (selected.Contains(l))
                    foreach (LevelData s in selected) SetScore(s, target);
                else
                    SetScore(l, target);
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    // ---- 当前标签页 + 筛选 + 排序后的显示列表 ----
    private List<LevelData> CurrentList()
    {
        IEnumerable<LevelData> q = all;
        if (tab == Tab.正式视图) q = q.Where(l => l.isFavorite);   // 正式视图只看已收藏

        // 最终顺序模式：固定按 finalOrder 排，忽略 排序/筛选
        if (tab == Tab.正式视图 && finalOrderMode)
            return q.OrderBy(l => l.finalOrder).ThenBy(l => l.name).ToList();

        if (filter == FilterMode.仅已收藏) q = q.Where(l => l.isFavorite);
        else if (filter == FilterMode.仅未评分) q = q.Where(l => l.curationScore == 0);

        bool asc = sortAscending;
        switch (sort)
        {
            case SortMode.星级:
                q = asc ? q.OrderBy(l => l.curationScore).ThenBy(l => l.name)
                        : q.OrderByDescending(l => l.curationScore).ThenBy(l => l.name); break;
            case SortMode.难度:
                q = asc ? q.OrderBy(l => l.difficulty).ThenBy(l => l.name)
                        : q.OrderByDescending(l => l.difficulty).ThenBy(l => l.name); break;
            case SortMode.名称:
                q = asc ? q.OrderBy(l => l.name) : q.OrderByDescending(l => l.name); break;
        }
        return q.ToList();
    }

    // ---- 编辑操作 ----
    private void SetScore(LevelData l, int s)
    {
        l.curationScore = Mathf.Clamp(s, 0, 5);
        EditorUtility.SetDirty(l);
        dirty = true;
    }

    private void BatchAddStar(int delta)
    {
        PushUndo();
        foreach (LevelData l in selected) SetScore(l, l.curationScore + delta);
    }

    private void BatchFavorite(bool v)
    {
        PushUndo();
        foreach (LevelData l in selected) { l.isFavorite = v; EditorUtility.SetDirty(l); }
        dirty = true;
    }

    private void BatchDelete()
    {
        if (selected.Count == 0) return;
        if (!EditorUtility.DisplayDialog("批量删除",
            $"确定删除选中的 {selected.Count} 关吗？此操作不可撤销。", "删除", "取消")) return;

        foreach (LevelData l in selected.ToList())
        {
            string p = AssetDatabase.GetAssetPath(l);
            if (!string.IsNullOrEmpty(p)) AssetDatabase.DeleteAsset(p);
        }
        selected.Clear();
        SaveSelection();
        Refresh();
        GUIUtility.ExitGUI();
    }

    private bool ConfirmRename(LevelData l)
    {
        string newName = (renameBuffer ?? "").Trim();
        renaming = null;
        if (string.IsNullOrEmpty(newName) || newName == l.name) return false;

        string err = AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(l), newName);
        if (!string.IsNullOrEmpty(err)) { EditorUtility.DisplayDialog("重命名失败", err, "确定"); return false; }

        l.levelName = newName;
        EditorUtility.SetDirty(l);
        dirty = true;
        SaveSelection();
        Refresh();
        return true;
    }

    private void RequestSequencePlaytest()
    {
        List<LevelData> ordered = CurrentList().Where(l => selected.Contains(l)).ToList();
        if (ordered.Count == 0) return;
        EditorApplication.delayCall += () => LevelPlaytest.StartSequence(ordered);
    }

    // ---- 最终顺序 ----
    // 激活时把已收藏关卡的 finalOrder 规整成连续 0,1,2…，方便用 ▲▼ 交换。
    private void EnterFinalOrderMode()
    {
        PushUndo();
        var favs = all.Where(l => l.isFavorite).OrderBy(l => l.finalOrder).ThenBy(l => l.name).ToList();
        for (int i = 0; i < favs.Count; i++) { favs[i].finalOrder = i; EditorUtility.SetDirty(favs[i]); }
        dirty = true;
    }

    private void Move(List<LevelData> list, int i, int delta)
    {
        int j = i + delta;
        if (j < 0 || j >= list.Count) return;
        PushUndo();
        int tmp = list[i].finalOrder; list[i].finalOrder = list[j].finalOrder; list[j].finalOrder = tmp;
        EditorUtility.SetDirty(list[i]); EditorUtility.SetDirty(list[j]);
        dirty = true;
    }

    // ---- 应用到游戏：写进 LevelManifest 清单资产 ----
    private void ApplyToGame()
    {
        var favs = all.Where(l => l.isFavorite).OrderBy(l => l.finalOrder).ThenBy(l => l.name).ToList();
        if (favs.Count == 0)
        {
            EditorUtility.DisplayDialog("应用到游戏", "当前没有已收藏的关卡，收藏列表为空。", "确定");
            return;
        }
        if (!EditorUtility.DisplayDialog("应用到游戏",
            $"将把 {favs.Count} 关按最终顺序写入游戏关卡清单，覆盖现有内容，确定？", "写入", "取消")) return;

        LevelManifest manifest = FindOrCreateManifest();
        manifest.orderedLevels = favs.ToArray();
        EditorUtility.SetDirty(manifest);
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("完成",
            $"已写入 {favs.Count} 关到 {AssetDatabase.GetAssetPath(manifest)}。", "好的");
    }

    private LevelManifest FindOrCreateManifest()
    {
        string[] guids = AssetDatabase.FindAssets("t:LevelManifest");
        if (guids.Length > 0)
            return AssetDatabase.LoadAssetAtPath<LevelManifest>(AssetDatabase.GUIDToAssetPath(guids[0]));

        if (!AssetDatabase.IsValidFolder(PoolDir)) AssetDatabase.CreateFolder("Assets", "Levels");
        LevelManifest m = ScriptableObject.CreateInstance<LevelManifest>();
        AssetDatabase.CreateAsset(m, ManifestPath);
        AssetDatabase.SaveAssets();
        return m;
    }

    // ---- 选中状态持久化（跨进出 Play Mode）----
    // ---------- 撤销/重做 ----------
    private CState Snapshot()
    {
        var st = new CState { map = new Dictionary<LevelData, (int, bool, int)>() };
        foreach (LevelData l in all) if (l != null) st.map[l] = (l.curationScore, l.isFavorite, l.finalOrder);
        return st;
    }

    // 在每个会改 打分/收藏/最终顺序 的操作发生【前】调用
    private void PushUndo() { undoStack.Push(Snapshot()); redoStack.Clear(); }

    private void Restore(CState st)
    {
        foreach (var kv in st.map)
        {
            if (kv.Key == null) continue;
            kv.Key.curationScore = kv.Value.s;
            kv.Key.isFavorite = kv.Value.f;
            kv.Key.finalOrder = kv.Value.o;
            EditorUtility.SetDirty(kv.Key);
        }
        dirty = true;
    }

    private void Undo()
    {
        if (undoStack.Count == 0) return;
        redoStack.Push(Snapshot());
        Restore(undoStack.Pop());
        Repaint();
    }

    private void Redo()
    {
        if (redoStack.Count == 0) return;
        undoStack.Push(Snapshot());
        Restore(redoStack.Pop());
        Repaint();
    }

    private void HandleUndoShortcuts()
    {
        Event e = Event.current;
        if (e.type != EventType.KeyDown || !(e.control || e.command)) return;
        if (e.keyCode == KeyCode.Z) { if (e.shift) Redo(); else Undo(); e.Use(); }
        else if (e.keyCode == KeyCode.Y) { Redo(); e.Use(); }
    }

    // 读取 GameManager 写入的试玩路径队列，得到"正在试玩"的关卡集合
    private void RefreshPlaytestSet()
    {
        playtestSet.Clear();
        string raw = SessionState.GetString("GridChaser.PlaytestPaths", "");
        if (string.IsNullOrEmpty(raw)) return;
        foreach (string p in raw.Split('\n'))
            if (!string.IsNullOrEmpty(p)) playtestSet.Add(p);
    }

    private void SaveSelection()
    {
        SessionState.SetString(SelKey,
            string.Join("\n", selected.Select(l => AssetDatabase.GetAssetPath(l))));
    }

    private void LoadSelection()
    {
        selected.Clear();
        string s = SessionState.GetString(SelKey, "");
        if (string.IsNullOrEmpty(s)) return;
        var set = new HashSet<string>(s.Split('\n'));
        foreach (LevelData l in all)
            if (set.Contains(AssetDatabase.GetAssetPath(l))) selected.Add(l);
    }
}
#endif