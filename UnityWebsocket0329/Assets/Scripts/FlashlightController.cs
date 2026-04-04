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

    [SerializeField] float xRange = 3f;   // X 軸左右最大距離
    [SerializeField] float yMin = 0.5f;   // Y 軸最下
    [SerializeField] float yMax = 3f;     // Y 軸最高
    [SerializeField] float fixedZ = 5f;   // 固定 Z 值（距離）
    [SerializeField] float smoothSpeed = 10f; // 平滑速度（越大越順）

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

        MoveObjectByUV();
    }

    void MoveObjectByUV()
    {
        Vector2 uv = tracker.spotUV;  // 已經經過濾波，最穩定的 UV 來源

        // 翻轉 Y（因為 WebCamTexture 上下顛倒）
        uv.y = 1f - uv.y;

        // 直接把 UV(0~1) 映射到空間 X/Y
        float x = Mathf.Lerp(-xRange, xRange, uv.x);
        float y = Mathf.Lerp(yMin, yMax, uv.y);

        Vector3 target = new Vector3(x, y, fixedZ);

        // 低成本、高順暢的平滑
        float t = 1f - Mathf.Exp(-Time.deltaTime * smoothSpeed);
        targetObject.position = Vector3.Lerp(targetObject.position, target, t);
    }
}
