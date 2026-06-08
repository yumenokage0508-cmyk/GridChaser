using UnityEngine;

// 音效管理器（单例）。持有 5 个游戏音效，用同一个 AudioSource 的 PlayOneShot 播放。
// 设计要点：clip 未指定时静默跳过、不报错——便于先放代码、后续拿到音再增量接入。
[RequireComponent(typeof(AudioSource))]
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Clips")]
    [SerializeField] private AudioClip moveClip;        // 玩家移动
    [SerializeField] private AudioClip visitClip;       // 格子变色（可选，默认未接线）
    [SerializeField] private AudioClip deathClip;       // 死亡（敌人系统恢复后才会触发）
    [SerializeField] private AudioClip winClip;         // 通关
    [SerializeField] private AudioClip enemyMoveClip;   // 敌人移动（敌人系统恢复后用）

    [Header("Volume")]
    [Range(0f, 1f)][SerializeField] private float masterVolume = 1f;

    private AudioSource source;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
    }

    public void PlayMove() => Play(moveClip);
    public void PlayVisit() => Play(visitClip);
    public void PlayDeath() => Play(deathClip);
    public void PlayWin() => Play(winClip);
    public void PlayEnemyMove() => Play(enemyMoveClip);

    private void Play(AudioClip clip)
    {
        if (clip == null) return;   // 未指定就静默，不报错
        source.PlayOneShot(clip, masterVolume);
    }
}