using UnityEngine;

// 相机自动取景：根据传入的关卡尺寸，把整张关卡完整、居中地框进正交相机视野。
// 同时按屏幕宽高比(aspect)计算，保证横向/纵向都不会被裁掉；四周留可配置的留白。
// 挂到 Main Camera 上，GameInitializer 在关卡加载后调用 Fit()。
[RequireComponent(typeof(Camera))]
public class CameraFitter : MonoBehaviour
{
    [Header("Fit Settings")]
    [Tooltip("关卡四周留白，单位=世界单位(格)。越大关卡看起来越小。")]
    [SerializeField] private float padding = 1f;

    private Camera cam;

    private void Awake() => cam = GetComponent<Camera>();

    // 把以 center 为中心、宽 contentWidth、高 contentHeight 的矩形完整框入视野。
    // orthographicSize = 视野"半高"。半宽 = 半高 × aspect。
    // 要同时装下宽和高，分别算两边所需的 size，取较大值。
    public void Fit(Vector2 center, float contentWidth, float contentHeight)
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (!cam.orthographic) return;

        float halfH = contentHeight * 0.5f + padding;
        float halfW = contentWidth * 0.5f + padding;

        float sizeForHeight = halfH;
        float sizeForWidth = halfW / cam.aspect;

        cam.orthographicSize = Mathf.Max(sizeForHeight, sizeForWidth);

        // 居中：只对齐 x/y，保留原本的 z（2D 常用 -10）
        Vector3 p = cam.transform.position;
        cam.transform.position = new Vector3(center.x, center.y, p.z);
    }
}