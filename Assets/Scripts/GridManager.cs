using UnityEngine;

public class GridManager : MonoBehaviour
{
    // Inspector 暴露字段
    [Header("Grid Settings")]
    [SerializeField] private int gridWidth = 5;
    [SerializeField] private int gridHeight = 5;
    [SerializeField] private float cellSize = 1f;

    [Header("Cell Colors")]
    [SerializeField] private Color colorNormal = new Color(0.165f, 0.165f, 0.165f);
    [SerializeField] private Color colorVisited = new Color(0.361f, 0.329f, 0.569f);
    [SerializeField] private Color colorGoal = new Color(0.961f, 0.784f, 0.259f);
    [SerializeField] private Color colorWall = new Color(0.1f, 0.1f, 0.1f);

    [Header("Level")]
    [SerializeField] private LevelData currentLevel;

    public LevelData CurrentLevel => currentLevel;

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
        // 清除旧格子
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

        CenterCamera();

    }

    public Vector2Int GetPlayerStartPos() => playerStartPos;

    private void BuildGrid()
    {
        cellStates = new CellState[gridWidth, gridHeight];
        cellRenderers = new SpriteRenderer[gridWidth, gridHeight];

        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                CreateCell(x, y);
            }
        }

        CenterCamera();
    }

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


    public void SetVisited(Vector2Int gridPos)
    {
        if (!IsInBounds(gridPos)) return;
        if (cellStates[gridPos.x, gridPos.y] != CellState.Normal) return;

        cellStates[gridPos.x, gridPos.y] = CellState.Visited;
        cellRenderers[gridPos.x, gridPos.y].color = colorVisited;
        visitedCount++;    // 新增

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

    // 恢复快照：写回每个格子的状态并重新着色，最后还原计数
    public void RestoreState(GridSnapshot snap)
    {
        for (int x = 0; x < gridWidth; x++)
            for (int y = 0; y < gridHeight; y++)
            {
                cellStates[x, y] = snap.states[x, y];
                RefreshCellColor(x, y);
            }
        visitedCount = snap.visitedCount;
    }

    // 按当前状态刷新单个格子的颜色（撤回时复原视觉）
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


    // 内部工具方法
    private bool IsInBounds(Vector2Int gridPos)
    {
        return gridPos.x >= 0 && gridPos.x < gridWidth &&
               gridPos.y >= 0 && gridPos.y < gridHeight;
    }


    private void CenterCamera()
    {
        if (Camera.main == null) return;
        Camera.main.transform.position = new Vector3(0f, 0f, -10f);
        Camera.main.orthographicSize = Mathf.Max(gridWidth, gridHeight) * cellSize * 0.65f;
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