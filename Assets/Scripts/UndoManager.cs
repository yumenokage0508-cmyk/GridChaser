using UnityEngine;
using System.Collections.Generic;

// 撤回系统：每次合法移动前对全局状态拍快照，按 Z 弹出最近一次快照恢复。
// 当前为「活着时撤回」版本：死亡后场景重载、历史清空，暂不支持撤销死亡。
public class UndoManager : MonoBehaviour
{
    public static UndoManager Instance { get; private set; }

    // 一个回合的完整快照：把玩家和格子各自的快照打包在一起
    private class TurnRecord
    {
        public PlayerController.PlayerSnapshot player;
        public GridManager.GridSnapshot grid;
    }

    private readonly Stack<TurnRecord> history = new Stack<TurnRecord>();

    // 当前可撤回的步数，供 UI / 调试查询（现在没用到，先留着）
    public int Count => history.Count;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // 不加 DontDestroyOnLoad：让它随场景重载而重建，历史自然清空，避免跨关撤回
    }

    // 移动前调用：把当前玩家 + 格子状态压栈
    public void RecordTurn()
    {
        history.Push(new TurnRecord
        {
            player = PlayerController.Instance.CaptureState(),
            grid = GridManager.Instance.CaptureState()
        });
    }

    // 撤回一步：先恢复格子（含重新着色），再恢复玩家位置
    public void Undo()
    {
        if (history.Count == 0) return;

        TurnRecord r = history.Pop();
        GridManager.Instance.RestoreState(r.grid);
        PlayerController.Instance.RestoreState(r.player);
    }

    // 每关开始时清空历史，避免跨关撤回
    public void Clear() => history.Clear();
}