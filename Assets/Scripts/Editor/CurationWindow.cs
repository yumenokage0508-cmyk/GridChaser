#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

// 关卡管理窗口（菜单 Tools/GridChaser/关卡管理）。
// 本步只做 Tab 1「候选库」：列出 Assets/Levels/ 全部关卡，可多选、打分、收藏、重命名、试玩、删除，
// 顶部可排序(升/降序切换)/筛选。Tab 2「正式视图」+ 应用到游戏 在 4b-3 加入。
public class CurationWindow : EditorWindow
{
    private const string PoolDir = "Assets/Levels";
    private const string SelKey = "GridChaser.CurationSelected";   // 选中状态持久化(跨域重载)

    private enum SortMode { 星级, 难度, 名称 }
    private enum FilterMode { 全部, 仅已收藏, 仅未评分 }

    private readonly List<LevelData> all = new List<LevelData>();
    private readonly HashSet<LevelData> selected = new HashSet<LevelData>();
    private SortMode sort = SortMode.星级;
    private FilterMode filter = FilterMode.全部;
    private bool sortAscending = false;     // 默认降序(高分/高难在前)
    private Vector2 scroll;
    private bool dirty;                     // 本帧有改动 → 帧末统一存盘

    // 重命名状态
    private LevelData renaming;
    private string renameBuffer;
    private bool focusPending;

    [MenuItem("Tools/GridChaser/关卡管理")]
    public static void Open() => GetWindow<CurationWindow>("关卡管理");

    private void OnEnable() => Refresh();
    private void OnFocus() => Refresh();   // 切回窗口时刷新，反映外部改动/删除

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
        DrawToolbar();
        DrawBatchBar();
        EditorGUILayout.Space(2);
        DrawHeader();
        DrawList();

        if (dirty) { AssetDatabase.SaveAssets(); dirty = false; }
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUILayout.LabelField($"候选库  共 {all.Count} 关 / 已选 {selected.Count}",
            EditorStyles.boldLabel, GUILayout.Width(220));
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField("排序", GUILayout.Width(30));
        sort = (SortMode)EditorGUILayout.EnumPopup(sort, EditorStyles.toolbarPopup, GUILayout.Width(60));
        if (GUILayout.Button(sortAscending ? "▲ 升序" : "▼ 降序", EditorStyles.toolbarButton, GUILayout.Width(58)))
            sortAscending = !sortAscending;
        EditorGUILayout.LabelField("筛选", GUILayout.Width(30));
        filter = (FilterMode)EditorGUILayout.EnumPopup(filter, EditorStyles.toolbarPopup, GUILayout.Width(80));
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
        if (GUILayout.Button("全选(当前筛选)", GUILayout.Width(108)))
        { foreach (LevelData l in Displayed()) selected.Add(l); SaveSelection(); }
        if (GUILayout.Button("清空选择", GUILayout.Width(70)))
        { selected.Clear(); SaveSelection(); }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawHeader()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("", GUILayout.Width(20));
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
        scroll = EditorGUILayout.BeginScrollView(scroll);
        bool exit = false;
        foreach (LevelData l in Displayed())
        {
            if (DrawRow(l)) { exit = true; break; }   // 行内发生结构性改动(删除/改名)，结束本帧
        }
        EditorGUILayout.EndScrollView();
        if (exit) { Repaint(); GUIUtility.ExitGUI(); }
    }

    // 返回 true 表示发生结构性改动（删除/重命名），调用方应结束本帧 GUI
    private bool DrawRow(LevelData l)
    {
        bool structural = false;
        bool wantConfirmRename = false;

        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

        bool sel = selected.Contains(l);
        bool newSel = EditorGUILayout.Toggle(sel, GUILayout.Width(20));
        if (newSel != sel) { if (newSel) selected.Add(l); else selected.Remove(l); SaveSelection(); }

        // 名称列：单击变输入框，回车确认 / Esc 取消
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
        EditorGUILayout.LabelField(l.difficulty.ToString("0.00"), GUILayout.Width(52));
        EditorGUILayout.LabelField(l.cells.ToString(), GUILayout.Width(44));
        EditorGUILayout.LabelField(l.chokepoints.ToString(), GUILayout.Width(44));

        DrawStars(l);

        bool fav = EditorGUILayout.ToggleLeft(" ", l.isFavorite, GUILayout.Width(54));
        if (fav != l.isFavorite) { l.isFavorite = fav; EditorUtility.SetDirty(l); dirty = true; }

        // 试玩：点已选中行=对全部选中序列试玩；点未选中行=单关试玩
        if (GUILayout.Button("▶", GUILayout.Width(24)))
        {
            if (selected.Contains(l))
            {
                List<LevelData> ordered = Displayed().Where(x => selected.Contains(x)).ToList();
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

        if (wantConfirmRename) structural = ConfirmRename(l) || structural;
        return structural;
    }

    // 五颗星：点第 N 颗→设为 N；点当前最高那颗→归零。
    // 若点的是已选中行的星，则把所有选中关卡都设成该分（批量）；否则只改这一关。
    private void DrawStars(LevelData l)
    {
        EditorGUILayout.BeginHorizontal(GUILayout.Width(92));
        for (int i = 1; i <= 5; i++)
        {
            bool on = i <= l.curationScore;
            if (GUILayout.Button(on ? "★" : "☆", EditorStyles.label, GUILayout.Width(16)))
            {
                int target = (i == l.curationScore) ? 0 : i;
                if (selected.Contains(l))
                    foreach (LevelData s in selected) SetScore(s, target);
                else
                    SetScore(l, target);
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    // ---- 筛选 + 排序后的显示列表 ----
    private IEnumerable<LevelData> Displayed()
    {
        IEnumerable<LevelData> q = all;
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
        foreach (LevelData l in selected) SetScore(l, l.curationScore + delta);
    }

    private void BatchFavorite(bool v)
    {
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
        SaveSelection();   // 路径变了，重存选中
        Refresh();
        return true;
    }

    private void RequestSequencePlaytest()
    {
        List<LevelData> ordered = Displayed().Where(l => selected.Contains(l)).ToList();
        if (ordered.Count == 0) return;
        EditorApplication.delayCall += () => LevelPlaytest.StartSequence(ordered);
    }

    // ---- 选中状态持久化（按资产路径存进 SessionState，跨进出 Play Mode 存活）----
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