using UnityEngine;
using System.Collections;

public class GridManager : MonoBehaviour
{
    // Inspector 暴露字段
    [Header("Grid Settings")]
    [SerializeField] private int gridWidth = 5;
    [SerializeField] private int gridHeight = 5;
    [SerializeField] private float cellSize = 1f;

    [Header("Theme")]
    [Tooltip("配色主题资产。换肤/对比时拖入不同的 ColorTheme；运行中改完可右键组件→Apply Theme Now 实时生效。")]
    [SerializeField] private ColorTheme theme;

    [Header("Animation")]
    [Tooltip("踩过格子变色的渐变时长（秒）。")]
    [SerializeField] private float visitFadeDuration = 0.15f;

    [Header("Level")]
    [SerializeField] private LevelData currentLevel;

    public LevelData CurrentLevel => currentLevel;

    // 实际使用的颜色（从 theme 缓存而来；theme 为空时用下面这组默认值兜底）
    private Color colorNormal = new Color(0.165f, 0.165f, 0.165f);
    private Color colorVisited = new Color(0.361f, 0.329f, 0.569f);
    private Color colorGoal = new Color(0.961f, 0.784f, 0.259f);
    private Color colorWall = new Color(0.290f, 0.290f, 0.322f);
    private Color colorBackground = new Color(0.102f, 0.102f, 0.102f);
    private Color colorPlayer = new Color(0.494f, 0.812f, 1f);

    // 供 PlayerController 读取（玩家颜色也由主题统一管理）
    public Color PlayerColor => colorPlayer;

    private Vector2Int playerStartPos;
    private int totalFillableCells = 0;   // 关卡内可填格子总数（分母）
    private int visitedCount = 0;         // 已离开的格子数（不含当前站立格）

    // 格子状态枚举
    public enum CellState { Normal, Visited, Gone, Goal, Wall }

    // 撤回用的格子状态快照
    public class GridSnapshot
    {
        public CellState[,] states;
        public int visitedCount;
    }

    // 内部数据
    private CellState[,] cellStates;
    private SpriteRenderer[,] cellRenderers;
    private Coroutine[,] colorTweens;     // 每格正在跑的变色协程，便于撤回时打断

    // 单例
    public static GridManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }


    // 初始化

    public void LoadLevel(LevelData level)
    {
        CacheTheme();          // 先把主题颜色缓存进各 colorXxx 字段
        StopAllColorTweens();  // 停掉旧关卡残留的变色协程
        foreach (Transform child in transform)
            Destroy(child.gameObject);

        currentLevel = level;

        string[] rows = level.layout.Split('\n');
        for (int i = 0; i < rows.Length; i++)
            rows[i] = rows[i].Trim();

        gridWidth = rows[0].Length;
        gridHeight = rows.Length;

        cellStates = new CellState[gridWidth, gridHeight];
        cellRenderers = new SpriteRenderer[gridWidth, gridHeight];
        colorTweens = new Coroutine[gridWidth, gridHeight];

        for (int y = 0; y < gridHeight; y++)
        {
            string row = rows[gridHeight - 1 - y];
            for (int x = 0; x < gridWidth; x++)
            {
                char c = row[x];
                CreateCell(x, y);

                if (c == 'S') playerStartPos = new Vector2Int(x, y);
                else if (c == 'G') SetGoal(new Vector2Int(x, y));
                else if (c == 'X')
                {
                    cellStates[x, y] = CellState.Wall;
                    cellRenderers[x, y].color = colorWall;
                }
            }
        }

        CountFillableCells();
        ApplyBackground();
        // 取景由 CameraFitter 负责，GameInitializer 在此之后调用
    }

    public Vector2Int GetPlayerStartPos() => playerStartPos;

    private void CreateCell(int x, int y)
    {
        Vector3 worldPos = GridToWorld(new Vector2Int(x, y));

        GameObject cellObj = new GameObject($"Cell_{x}_{y}");
        cellObj.transform.position = worldPos;
        cellObj.transform.localScale = Vector3.one * cellSize * 1.0f;
        cellObj.transform.SetParent(transform);

        SpriteRenderer sr = cellObj.AddComponent<SpriteRenderer>();
        sr.sprite = CreateWhiteSquareSprite();
        sr.color = colorNormal;
        sr.sortingOrder = 0;

        cellStates[x, y] = CellState.Normal;
        cellRenderers[x, y] = sr;
    }

    // 公开 API（供其他脚本调用）
    public bool CanEnter(Vector2Int gridPos)
    {
        if (!IsInBounds(gridPos)) return false;
        CellState s = cellStates[gridPos.x, gridPos.y];
        return s == CellState.Normal || s == CellState.Goal;
    }

    // 敌人专用：只挡墙和越界，允许进入玩家走过的 Visited 格
    public bool CanEnemyEnter(Vector2Int gridPos)
    {
        if (!IsInBounds(gridPos)) return false;
        return cellStates[gridPos.x, gridPos.y] != CellState.Wall;
    }

    // 玩家可进入判断：不能走 Visited/Gone/Wall；终点必须等到可通关才允许进入
    public bool CanPlayerEnter(Vector2Int pos, Vector2Int currentPos)
    {
        if (!IsInBounds(pos)) return false;
        CellState s = cellStates[pos.x, pos.y];
        if (s == CellState.Goal)
            return !currentLevel.requireAllVisited || AllVisitedExceptCurrent(currentPos);
        return s == CellState.Normal;
    }

    // 除玩家当前格和终点外，其余可填格是否都已踩过
    public bool AllVisitedExceptCurrent(Vector2Int currentPos)
    {
        for (int x = 0; x < gridWidth; x++)
            for (int y = 0; y < gridHeight; y++)
            {
                if (x == currentPos.x && y == currentPos.y) continue;
                if (cellStates[x, y] == CellState.Normal) return false;
            }
        return true;
    }


    // 踩过格子：逻辑即时置为 Visited 并计数；颜色走渐变协程（视觉追赶）
    public void SetVisited(Vector2Int gridPos)
    {
        if (!IsInBounds(gridPos)) return;
        if (cellStates[gridPos.x, gridPos.y] != CellState.Normal) return;

        cellStates[gridPos.x, gridPos.y] = CellState.Visited;
        visitedCount++;
        StartColorTween(gridPos.x, gridPos.y, colorVisited);
    }

    public CellState GetState(Vector2Int gridPos)
    {
        if (!IsInBounds(gridPos)) return CellState.Gone;
        return cellStates[gridPos.x, gridPos.y];
    }

    public void SetGoal(Vector2Int gridPos)
    {
        if (!IsInBounds(gridPos)) return;
        cellStates[gridPos.x, gridPos.y] = CellState.Goal;
        cellRenderers[gridPos.x, gridPos.y].color = colorGoal;
    }

    // 坐标转换
    public Vector3 GridToWorld(Vector2Int gridPos)
    {
        float offsetX = (gridWidth - 1) * cellSize * 0.5f;
        float offsetY = (gridHeight - 1) * cellSize * 0.5f;

        return new Vector3(
            gridPos.x * cellSize - offsetX,
            gridPos.y * cellSize - offsetY,
            0f
        );
    }

    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        float offsetX = (gridWidth - 1) * cellSize * 0.5f;
        float offsetY = (gridHeight - 1) * cellSize * 0.5f;

        int x = Mathf.RoundToInt((worldPos.x + offsetX) / cellSize);
        int y = Mathf.RoundToInt((worldPos.y + offsetY) / cellSize);
        return new Vector2Int(x, y);
    }

    public bool AllVisited()
    {
        for (int x = 0; x < gridWidth; x++)
            for (int y = 0; y < gridHeight; y++)
            {
                CellState s = cellStates[x, y];
                if (s == CellState.Normal) return false;
            }
        return true;
    }

    public bool HasAnyExit(Vector2Int currentPos)
    {
        return CanPlayerEnter(currentPos + Vector2Int.up, currentPos)
            || CanPlayerEnter(currentPos + Vector2Int.down, currentPos)
            || CanPlayerEnter(currentPos + Vector2Int.left, currentPos)
            || CanPlayerEnter(currentPos + Vector2Int.right, currentPos);
    }


    // 统计可填格子总数，LoadLevel 时调用一次
    private void CountFillableCells()
    {
        totalFillableCells = 0;
        visitedCount = 0;
        for (int x = 0; x < gridWidth; x++)
            for (int y = 0; y < gridHeight; y++)
            {
                CellState s = cellStates[x, y];
                // Normal 和 Goal 都算可填格（Start 格是 Normal，终点格是 Goal）
                if (s == CellState.Normal || s == CellState.Goal)
                    totalFillableCells++;
            }
    }

    // 阶段 6.3 填格计数 UI 读取
    public int TotalFillableCells => totalFillableCells;

    // 已访问数 + 玩家当前站立格（当前格永远是 Normal/Goal，不是 Visited）
    public int FilledCellCount => visitedCount + 1;


    // ===== 撤回支持 =====

    // 拍快照：深拷贝格子状态数组 + 已访问计数
    public GridSnapshot CaptureState()
    {
        return new GridSnapshot
        {
            states = (CellState[,])cellStates.Clone(),
            visitedCount = visitedCount
        };
    }

    // 恢复快照：先停掉所有变色协程（防止半截渐变把颜色覆盖回去），再写回状态并瞬时着色
    public void RestoreState(GridSnapshot snap)
    {
        StopAllColorTweens();
        for (int x = 0; x < gridWidth; x++)
            for (int y = 0; y < gridHeight; y++)
            {
                cellStates[x, y] = snap.states[x, y];
                RefreshCellColor(x, y);
            }
        visitedCount = snap.visitedCount;
    }

    // 按当前状态刷新单个格子的颜色（撤回/换肤时瞬时复原视觉）
    private void RefreshCellColor(int x, int y)
    {
        Color c;
        switch (cellStates[x, y])
        {
            case CellState.Visited: c = colorVisited; break;
            case CellState.Goal: c = colorGoal; break;
            case CellState.Wall: c = colorWall; break;
            default: c = colorNormal; break;
        }
        cellRenderers[x, y].color = c;
    }


    // ===== 配色主题 =====

    // 把主题资产里的颜色复制到各 colorXxx 字段；theme 为空则保留默认值
    private void CacheTheme()
    {
        if (theme == null) return;
        colorNormal = theme.normal;
        colorVisited = theme.visited;
        colorGoal = theme.goal;
        colorWall = theme.wall;
        colorBackground = theme.background;
        colorPlayer = theme.player;
    }

    // 设置相机背景色
    private void ApplyBackground()
    {
        if (Camera.main != null) Camera.main.backgroundColor = colorBackground;
    }

    // 运行中实时换肤：重新缓存主题 → 重置背景/全部格子/玩家颜色，无需重启。
    // 右键 GridManager 组件标题 → Apply Theme Now 调用。
    [ContextMenu("Apply Theme Now")]
    public void ApplyThemeNow()
    {
        CacheTheme();
        ApplyBackground();

        StopAllColorTweens();
        if (cellRenderers != null)
            for (int x = 0; x < gridWidth; x++)
                for (int y = 0; y < gridHeight; y++)
                    RefreshCellColor(x, y);

        if (PlayerController.Instance != null)
            PlayerController.Instance.ApplyColor(colorPlayer);
    }


    // ===== 格子变色渐变 =====

    // 启动一格的变色渐变；若该格已有渐变在跑，先停掉避免叠加
    private void StartColorTween(int x, int y, Color target)
    {
        if (colorTweens[x, y] != null) StopCoroutine(colorTweens[x, y]);
        colorTweens[x, y] = StartCoroutine(ColorTween(x, y, target));
    }

    private IEnumerator ColorTween(int x, int y, Color target)
    {
        SpriteRenderer sr = cellRenderers[x, y];
        Color from = sr.color;
        float t = 0f;
        while (t < visitFadeDuration)
        {
            t += Time.deltaTime;
            sr.color = Color.Lerp(from, target, t / visitFadeDuration);
            yield return null;
        }
        sr.color = target;
        colorTweens[x, y] = null;
    }

    // 停掉所有正在跑的变色协程（按数组实际尺寸遍历，兼容关卡换尺寸）
    private void StopAllColorTweens()
    {
        if (colorTweens == null) return;
        for (int x = 0; x < colorTweens.GetLength(0); x++)
            for (int y = 0; y < colorTweens.GetLength(1); y++)
                if (colorTweens[x, y] != null)
                {
                    StopCoroutine(colorTweens[x, y]);
                    colorTweens[x, y] = null;
                }
    }


    // 内部工具方法
    private bool IsInBounds(Vector2Int gridPos)
    {
        return gridPos.x >= 0 && gridPos.x < gridWidth &&
               gridPos.y >= 0 && gridPos.y < gridHeight;
    }

    private Sprite CreateWhiteSquareSprite()
    {
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(
            tex,
            new Rect(0, 0, 1, 1),
            new Vector2(0.5f, 0.5f),
            1f
        );
    }

    // 只读属性（外部访问网格尺寸用）
    public int GridWidth => gridWidth;
    public int GridHeight => gridHeight;
    public float CellSize => cellSize;
}