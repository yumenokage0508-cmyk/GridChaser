using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [Header("Player Settings")]
    [SerializeField] private Color playerColor = new Color(0.494f, 0.812f, 1f);

    private Vector2Int gridPos;
    private SpriteRenderer sr;
    private Vector2Int prevGridPos;
    public Vector2Int PrevGridPos => prevGridPos;

    public static PlayerController Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        // 创建玩家的视觉方块
        sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = CreateSquareSprite();
        sr.color = playerColor;
        sr.sortingOrder = 2;
        transform.localScale = Vector3.one * GridManager.Instance.CellSize * 0.85f;

        // 出生在格子中心
        gridPos = new Vector2Int(0, 0);
        transform.position = GridManager.Instance.GridToWorld(gridPos);
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        Vector2Int dir = Vector2Int.zero;

        if (keyboard.wKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame) dir = Vector2Int.up;
        if (keyboard.sKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame) dir = Vector2Int.down;
        if (keyboard.aKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame) dir = Vector2Int.left;
        if (keyboard.dKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame) dir = Vector2Int.right;

        if (dir != Vector2Int.zero) TryMove(dir);
    }

    private void TryMove(Vector2Int dir)
    {
        Vector2Int nextPos = gridPos + dir;

        if (!GridManager.Instance.CanEnter(nextPos)) return;

        Vector2Int prevPos = gridPos;
        prevGridPos = prevPos;
        gridPos = nextPos;
        transform.position = GridManager.Instance.GridToWorld(gridPos);

        GridManager.Instance.SetVisited(prevPos);

        EnemyManager.Instance?.OnPlayerMoved(dir);
    }

    // 工具方法
    public Vector2Int GridPos => gridPos;

    private Sprite CreateSquareSprite()
    {
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
    }
}