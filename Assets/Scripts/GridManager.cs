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

    // 格子状态枚举
    public enum CellState { Normal, Visited, Gone, Goal }

    // 内部数据
    private CellState[,] cellStates;
    private SpriteRenderer[,] cellRenderers;

    // 单例
    public static GridManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // 初始化
    private void Start()
    {
        BuildGrid();
    }

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
        return cellStates[gridPos.x, gridPos.y] != CellState.Gone;
    }

    public void SetVisited(Vector2Int gridPos)
    {
        if (!IsInBounds(gridPos)) return;
        if (cellStates[gridPos.x, gridPos.y] != CellState.Normal) return;

        cellStates[gridPos.x, gridPos.y] = CellState.Visited;
        cellRenderers[gridPos.x, gridPos.y].color = colorVisited;
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