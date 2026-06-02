using UnityEngine;
using System.Collections.Generic;

public class EnemyManager : MonoBehaviour
{
    [System.Serializable]
    public class EnemyData
    {
        public Vector2Int startPos;
        public int delay = 1;
        [HideInInspector] public Vector2Int gridPos;
        [HideInInspector] public GameObject go;
    }

    [Header("Enemy Settings")]
    [SerializeField] private EnemyData[] enemies;
    [SerializeField] private Color enemyColor = new Color(1f, 0.486f, 0.486f);

    private Queue<Vector2Int> historyBuffer = new Queue<Vector2Int>();
    private int maxDelay;

    public static EnemyManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (enemies == null || enemies.Length == 0) return;

        // 计算需要保留的最大历史步数
        maxDelay = 0;
        foreach (var e in enemies)
            if (e.delay > maxDelay) maxDelay = e.delay;

        // 生成每个敌人的视觉方块
        foreach (var e in enemies)
        {
            e.gridPos = e.startPos;

            GameObject go = new GameObject("Enemy");
            go.transform.SetParent(transform);

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = CreateSquareSprite();
            sr.color = enemyColor;
            sr.sortingOrder = 2;
            go.transform.localScale = Vector3.one * GridManager.Instance.CellSize * 0.85f;
            go.transform.position = GridManager.Instance.GridToWorld(e.gridPos);

            e.go = go;
        }
    }

    // PlayerController 每次移动后调用此方法
    public void OnPlayerMoved(Vector2Int direction)
    {
        historyBuffer.Enqueue(direction);
        if (historyBuffer.Count <= maxDelay) return;

        Vector2Int dirToUse = historyBuffer.Dequeue();

        foreach (var e in enemies)
        {
            Vector2Int prevPos = e.gridPos;
            Vector2Int nextPos = e.gridPos + dirToUse;

            Vector2Int playerPos = PlayerController.Instance.GridPos;
            Vector2Int playerPrevPos = PlayerController.Instance.PrevGridPos;

            // 对穿检测：在视觉移动之前判断，直接死亡不播放移动
            if (nextPos == playerPrevPos && prevPos == playerPos)
            {
                GameManager.Instance.TriggerDeath();
                return;
            }

            // 能走就走
            if (GridManager.Instance.CanEnter(nextPos))
            {
                e.gridPos = nextPos;
                e.go.transform.position = GridManager.Instance.GridToWorld(e.gridPos);
            }

            // 同格重叠检测
            CheckDeath(e, prevPos);
        }
    }

    private void CheckDeath(EnemyData e, Vector2Int enemyPrevPos)
    {
        Vector2Int playerPos = PlayerController.Instance.GridPos;

        if (e.gridPos == playerPos)
        {
            GameManager.Instance.TriggerDeath();
        }
    }

    private Sprite CreateSquareSprite()
    {
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
    }
}