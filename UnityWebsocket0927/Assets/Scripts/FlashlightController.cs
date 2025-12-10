using UnityEngine;

/// <summary>
/// 最終精簡版：純 UV 控制 X（左右） + Y（上下）
/// 不使用 Raycast、不使用 ViewportToWorldPoint、不受相機角度與距離影響
/// 保證極度穩定、不抖動、不亂跳、不突然停止
/// </summary>
public class FlashlightController : MonoBehaviour
{
    [Header("追踪器引用")]
    public LightSpotTracker tracker;

    [Header("控制設定")]
    [Tooltip("要移動的目標物件")]
    public Transform targetObject;

    [Tooltip("X 軸世界座標範圍")]
    public float worldXMin = -5f;
    public float worldXMax = 5f;

    [Tooltip("Y 軸世界座標範圍")]
    public float worldYMin = 0f;
    public float worldYMax = 4f;

    [Tooltip("Z 軸固定位置")]
    public float fixedZ = -3f;

    [Tooltip("平滑速度（越大越快）")]
    public float smoothSpeed = 10f;

    void Start()
    {
        if (targetObject == null)
            targetObject = transform;

        if (tracker == null)
            tracker = FindFirstObjectByType<LightSpotTracker>();
    }

    void Update()
    {
        if (tracker == null || !tracker.isTracking) return;

        // 取得 UV
        Vector2 uv = tracker.spotUV;

        // WebCamTexture Y 軸是倒的 → 翻轉
        uv.y = 1f - uv.y;

        // 映射到世界座標
        float x = Mathf.Lerp(worldXMin, worldXMax, uv.x);
        float y = Mathf.Lerp(worldYMin, worldYMax, uv.y);
        float z = fixedZ;

        Vector3 targetPos = new Vector3(x, y, z);

        // 平滑移動
        targetObject.position = Vector3.Lerp(
            targetObject.position,
            targetPos,
            Time.deltaTime * smoothSpeed
        );
    }
}
