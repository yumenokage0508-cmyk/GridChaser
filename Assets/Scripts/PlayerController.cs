using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class PlayerController : MonoBehaviour
{
    [Header("Animation")]
    [Tooltip("一步滑动到目标格的时长（秒）。越小越脆快，越大越柔。")]
    [SerializeField] private float moveDuration = 0.08f;

    private Vector2Int gridPos;
    private SpriteRenderer sr;
    private Vector2Int prevGridPos;
    public Vector2Int PrevGridPos => prevGridPos;

    private bool isMoving = false;   // 移动动画进行中：锁输入，避免连按错位

    public static PlayerController Instance { get; private set; }

    // 撤回用的玩家状态快照
    public class PlayerSnapshot
    {
        public Vector2Int gridPos;
        public Vector2Int prevGridPos;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void Initialize(Vector2Int startPos)
    {
        if (sr == null)
        {
            sr = gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = CreateSquareSprite();
            sr.sortingOrder = 2;
            transform.localScale = Vector3.one * GridManager.Instance.CellSize * 0.85f;
        }
        sr.color = GridManager.Instance.PlayerColor;   // 玩家颜色来自配色主题

        isMoving = false;
        gridPos = startPos;
        prevGridPos = startPos;
        transform.position = GridManager.Instance.GridToWorld(startPos);
    }

    // 运行中实时换肤：由 GridManager.ApplyThemeNow 调用
    public void ApplyColor(Color c)
    {
        if (sr != null) sr.color = c;
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        // 移动动画期间锁所有输入（含 Z/R/N/L），动画结束后自动解锁
        if (isMoving) return;

        // 游戏结束（通关）后锁输入，避免重载前误操作
        if (GameManager.Instance.IsGameOver) return;

        // 序列试玩：按 N 切到队列下一关（仅试玩模式生效，正常游玩无反应）
        if (keyboard.nKey.wasPressedThisFrame && GameManager.Instance.IsPlaytestMode)
        {
            GameManager.Instance.AdvancePlaytest();
            return;
        }

        // 序列试玩：按 L 切到队列上一关（仅试玩模式生效）
        if (keyboard.lKey.wasPressedThisFrame && GameManager.Instance.IsPlaytestMode)
        {
            GameManager.Instance.RetreatPlaytest();
            return;
        }

        // 重新开始：按 R 重载当前关
        if (keyboard.rKey.wasPressedThisFrame)
        {
            GameManager.Instance.RestartLevel();
            return;
        }

        // 撤回：按 Z 撤销上一步移动
        if (keyboard.zKey.wasPressedThisFrame)
        {
            UndoManager.Instance.Undo();
            return;
        }

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

        // 终点必须最后进入；同时统一了"不能走 Visited"的判断
        if (!GridManager.Instance.CanPlayerEnter(nextPos, gridPos)) return;

        // 合法移动已确认，在改动任何状态之前先拍快照（供撤回）
        UndoManager.Instance.RecordTurn();

        // —— 逻辑即时更新（与原来一致，不依赖动画）——
        Vector2Int prevPos = gridPos;
        prevGridPos = prevPos;
        gridPos = nextPos;

        // —— 视觉滑动：仅 transform 走协程，逻辑已经到位 ——
        StartCoroutine(AnimateMove(transform.position, GridManager.Instance.GridToWorld(gridPos)));

        GridManager.Instance.SetVisited(prevPos);

        // 死亡检查①——敌人停用时 Instance 为 null，整段自动跳过（当前游戏无死亡）
        if (EnemyManager.Instance != null)
        {
            foreach (var ePos in EnemyManager.Instance.GetEnemyPositions())
                if (gridPos == ePos) { GameManager.Instance.TriggerDeath(); return; }
        }

        EnemyManager.Instance?.OnPlayerMoved(dir);
        if (GameManager.Instance.IsGameOver) return;   // 敌人移动可能已触发死亡

        // 通关判定（即时；通关后 GameManager 会延迟 0.5s 重载，滑动动画在此期间播完）
        if (GridManager.Instance.GetState(gridPos) == GridManager.CellState.Goal)
        {
            bool allDone = !GridManager.Instance.CurrentLevel.requireAllVisited
                           || GridManager.Instance.AllVisited();
            if (allDone) GameManager.Instance.TriggerWin();
            return;
        }

        // 卡死判负已移除：走进死路不再判负，玩家用 Z 撤回或 R 重开
    }

    // 视觉滑动协程：把方块从 from 线性插值到 to，期间锁输入
    private IEnumerator AnimateMove(Vector3 from, Vector3 to)
    {
        isMoving = true;
        float t = 0f;
        while (t < moveDuration)
        {
            t += Time.deltaTime;
            transform.position = Vector3.Lerp(from, to, t / moveDuration);
            yield return null;
        }
        transform.position = to;
        isMoving = false;
    }

    // 拍快照：保存玩家当前与上一步位置
    public PlayerSnapshot CaptureState()
    {
        return new PlayerSnapshot { gridPos = gridPos, prevGridPos = prevGridPos };
    }

    // 恢复快照：写回位置并把方块摆回去（撤回不可能在动画中触发，这里 isMoving 复位仅作防御）
    public void RestoreState(PlayerSnapshot snap)
    {
        StopAllCoroutines();
        isMoving = false;
        gridPos = snap.gridPos;
        prevGridPos = snap.prevGridPos;
        transform.position = GridManager.Instance.GridToWorld(gridPos);
    }


    // 工具方法
    public Vector2Int GridPos => gridPos;

    public void SetStartPos(Vector2Int pos)
    {
        gridPos = pos;
        prevGridPos = pos;
        transform.position = GridManager.Instance.GridToWorld(gridPos);
    }

    private Sprite CreateSquareSprite()
    {
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
    }
}