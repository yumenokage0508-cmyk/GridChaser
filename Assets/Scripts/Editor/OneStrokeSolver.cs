#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

// 一笔画关卡求解/评估工具（编辑器期用）。
// 输入：空地集合(含起点终点)。规则与游戏一致——从 S 出发，每个空地走且仅走一次，G 最后到达。
// 提供：可解校验(带剪枝 DFS 找哈密顿路径，产出解法)、难度估计(Warnsdorff 贪心试玩)。
public static class OneStrokeSolver
{
    public const int NodeLimit = 2_000_000;   // DFS 节点上限，超过判“未确定”，防极端图卡死

    // 编辑器坐标 y 向下；映射到游戏方向：上移(y-1)=U，下移(y+1)=D，左=L，右=R
    private static readonly Vector2Int[] Dirs =
        { new Vector2Int(0, -1), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(1, 0) };
    private static readonly char[] DirCh = { 'U', 'D', 'L', 'R' };

    public enum Status { Ok, NoStart, NoGoal, Disconnected, NoSolution, Timeout }

    public class Result
    {
        public Status status;
        public string solutionMoves = "";   // 可解时的一条解法
        public string Message => status switch
        {
            Status.Ok => "✓ 可解",
            Status.NoStart => "✗ 缺少起点",
            Status.NoGoal => "✗ 缺少终点",
            Status.Disconnected => "✗ 空地不连通（有孤立区域）",
            Status.NoSolution => "✗ 无一笔画解",
            Status.Timeout => "? 太复杂，未能在限定步数内判定",
            _ => ""
        };
        public bool Solvable => status == Status.Ok;
    }

    // 主入口：校验 cells(空地集合) 在给定起终点下是否可解
    public static Result Solve(HashSet<Vector2Int> cells, Vector2Int? start, Vector2Int? goal)
    {
        var r = new Result();
        if (!start.HasValue) { r.status = Status.NoStart; return r; }
        if (!goal.HasValue) { r.status = Status.NoGoal; return r; }
        if (!Connected(cells)) { r.status = Status.Disconnected; return r; }

        var path = new List<Vector2Int>(cells.Count) { start.Value };
        var visited = new HashSet<Vector2Int> { start.Value };
        int nodes = 0;
        bool timeout = false;

        bool found = Dfs(cells, goal.Value, start.Value, visited, path, ref nodes, ref timeout);

        if (timeout) { r.status = Status.Timeout; return r; }
        if (!found) { r.status = Status.NoSolution; return r; }
        r.status = Status.Ok;
        r.solutionMoves = PathToMoves(path);
        return r;
    }

    // 带剪枝的 DFS：Warnsdorff 启发式（优先走“出口最少”的邻居）+ 终点必须最后到
    private static bool Dfs(HashSet<Vector2Int> cells, Vector2Int goal, Vector2Int pos,
                            HashSet<Vector2Int> visited, List<Vector2Int> path, ref int nodes, ref bool timeout)
    {
        if (visited.Count == cells.Count) return pos == goal;

        if (++nodes > NodeLimit) { timeout = true; return false; }

        // 收集候选邻居：未访问、在空地内；终点只允许在最后一步进入
        var cand = new List<Vector2Int>(4);
        foreach (var d in Dirs)
        {
            var nb = pos + d;
            if (!cells.Contains(nb) || visited.Contains(nb)) continue;
            if (nb == goal && visited.Count != cells.Count - 1) continue;   // 终点不能提前到
            cand.Add(nb);
        }
        if (cand.Count == 0) return false;

        // Warnsdorff：按“该邻居自身剩余出口数”升序，先走死路风险大的
        cand.Sort((a, b) => OnDeg(cells, visited, a).CompareTo(OnDeg(cells, visited, b)));

        foreach (var nb in cand)
        {
            visited.Add(nb); path.Add(nb);
            if (Dfs(cells, goal, nb, visited, path, ref nodes, ref timeout)) return true;
            if (timeout) return false;
            visited.Remove(nb); path.RemoveAt(path.Count - 1);
        }
        return false;
    }

    // 某格在“当前已访问”下还有几个可走出口
    private static int OnDeg(HashSet<Vector2Int> cells, HashSet<Vector2Int> visited, Vector2Int c)
    {
        int n = 0;
        foreach (var d in Dirs)
        {
            var nb = c + d;
            if (cells.Contains(nb) && !visited.Contains(nb)) n++;
        }
        return n;
    }

    // 空地是否全连通（洪水填充）
    private static bool Connected(HashSet<Vector2Int> cells)
    {
        if (cells.Count == 0) return false;
        var seen = new HashSet<Vector2Int>();
        var stack = new Stack<Vector2Int>();
        foreach (var c in cells) { stack.Push(c); break; }
        while (stack.Count > 0)
        {
            var c = stack.Pop();
            if (!seen.Add(c)) continue;
            foreach (var d in Dirs)
            {
                var nb = c + d;
                if (cells.Contains(nb) && !seen.Contains(nb)) stack.Push(nb);
            }
        }
        return seen.Count == cells.Count;
    }

    private static string PathToMoves(List<Vector2Int> path)
    {
        var sb = new System.Text.StringBuilder(path.Count - 1);
        for (int i = 1; i < path.Count; i++)
        {
            var d = path[i] - path[i - 1];
            for (int k = 0; k < Dirs.Length; k++)
                if (Dirs[k] == d) { sb.Append(DirCh[k]); break; }
        }
        return sb.ToString();
    }

    // ---- 难度估计：Warnsdorff 贪心试玩 rollouts 次，难度 = 1 - 通关率 ----
    public static float EstimateDifficulty(HashSet<Vector2Int> cells, Vector2Int start, Vector2Int goal, int rollouts = 150)
    {
        if (cells.Count <= 1) return 0f;
        var rng = new System.Random(12345);   // 固定种子，结果稳定可复现
        int wins = 0;
        for (int i = 0; i < rollouts; i++)
            if (GreedyRollout(cells, start, goal, rng)) wins++;
        return 1f - (float)wins / rollouts;
    }

    private static bool GreedyRollout(HashSet<Vector2Int> cells, Vector2Int start, Vector2Int goal, System.Random rng)
    {
        var visited = new HashSet<Vector2Int> { start };
        var pos = start;
        int total = cells.Count;
        while (visited.Count < total)
        {
            var cand = new List<Vector2Int>(4);
            foreach (var d in Dirs)
            {
                var nb = pos + d;
                if (!cells.Contains(nb) || visited.Contains(nb)) continue;
                if (nb == goal && visited.Count != total - 1) continue;
                cand.Add(nb);
            }
            if (cand.Count == 0) return false;

            int min = int.MaxValue;
            foreach (var c in cand) min = Mathf.Min(min, OnDeg(cells, visited, c));
            var best = cand.FindAll(c => OnDeg(cells, visited, c) == min);
            pos = best[rng.Next(best.Count)];
            visited.Add(pos);
        }
        return pos == goal;
    }
}
#endif