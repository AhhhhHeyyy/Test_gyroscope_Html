using UnityEngine;

/// <summary>
/// 重現「加速度儀校準 App」紅球效果：
/// 手機往哪個方向推，物件就往那個方向移動；
/// 停止施力時會自動彈回；靜止時停在重力傾斜角所對應的位置。
///
/// 原理：直接把原始加速度值對應到位置偏移。
///   acc = 重力分量（傾斜）+ 線性加速度（手動施力）
/// 完全不需要另外模擬彈力，物理行為是加速度儀本身的特性。
/// </summary>
public class AccelerometerBallEffect : MonoBehaviour
{
    [Header("中心點")]
    [Tooltip("移動範圍的錨點，若不指定則以 Start 時的位置為中心")]
    [SerializeField] private Transform centerPoint;

    [Header("移動設定")]
    [Tooltip("加速度 → 位移的縮放倍率，越大越敏感")]
    [SerializeField] private float sensitivity = 0.3f;

    [Tooltip("平滑速度，越大越即時、越小越滑順")]
    [SerializeField] [Range(1f, 30f)] private float smoothSpeed = 10f;

    [Tooltip("輸入濾波時間常數（秒），越小越即時、越大越平滑，建議 0.03 ~ 0.1")]
    [SerializeField] [Range(0.01f, 0.5f)] private float inputFilterTime = 0.05f;

    [Tooltip("哪些軸要受加速度影響（1=開，0=關）")]
    [SerializeField] private Vector3 movementAxesMask = new Vector3(1, 1, 0); // 預設 XY 平面

    [Tooltip("各軸移動幅度縮放（X=左右, Y=上下, Z=前後），值越大移動範圍越大")]
    [SerializeField] private Vector3 axisScale = Vector3.one;

    [Header("邊界限制")]
    [Tooltip("允許的最大偏移距離（米），始終從中心點起算")]
    [SerializeField] private float maxOffset = 3f;

    [Header("調試")]
    [SerializeField] private Vector3 debugRawAcceleration = Vector3.zero;
    [SerializeField] private Vector3 debugTargetOffset = Vector3.zero;
    [SerializeField] private Vector3 debugCurrentOffset = Vector3.zero;

    // 固定中心點的 local 座標（不會漂移）
    private Vector3 centerLocalPosition;

    // 當前目標偏移（由加速度決定）
    private Vector3 targetOffset = Vector3.zero;

    // 目前實際的平滑偏移（SmoothDamp 使用）
    private Vector3 currentOffset = Vector3.zero;
    private Vector3 currentVelocity = Vector3.zero;

    // 低通濾波後的加速度
    private Vector3 filteredAcceleration = Vector3.zero;

    // 最新的加速度（由 GyroscopeReceiver 事件更新）
    private Vector3 rawAcceleration = Vector3.zero;

    private void Start()
    {
        // 以 centerPoint 的位置為中心；若未指定則以自身初始位置
        centerLocalPosition = centerPoint != null
            ? centerPoint.localPosition
            : transform.localPosition;

        GyroscopeReceiver.OnAccelerationReceived += HandleAcceleration;
    }

    private void OnDestroy()
    {
        GyroscopeReceiver.OnAccelerationReceived -= HandleAcceleration;
    }

    /// <summary>
    /// 收到加速度事件（已由 GyroscopeReceiver 轉換為 Unity 座標系：X=左右, Y=上下, Z=前後）
    /// </summary>
    private void HandleAcceleration(Vector3 acc)
    {
        rawAcceleration = acc;
    }

    private void Update()
    {
        // 幀率無關的低通濾波：消除感測器高頻雜訊，不受幀率影響
        float alpha = 1f - Mathf.Exp(-Time.deltaTime / inputFilterTime);
        filteredAcceleration = Vector3.Lerp(filteredAcceleration, rawAcceleration, alpha);

        // 把濾波後的加速度對應到目標偏移，並限制在 maxOffset 半徑內
        targetOffset = new Vector3(
            filteredAcceleration.x * movementAxesMask.x,
            filteredAcceleration.y * movementAxesMask.y,
            filteredAcceleration.z * movementAxesMask.z
        ) * sensitivity;

        targetOffset = Vector3.ClampMagnitude(targetOffset, maxOffset);

        // SmoothDamp：速度追蹤，移動曲線比 Lerp 更自然
        float smoothTime = 1f / smoothSpeed;
        currentOffset = Vector3.SmoothDamp(currentOffset, targetOffset, ref currentVelocity, smoothTime);

        // 最終位置 = 固定中心點 + 偏移（永遠不會超出 maxOffset），各軸乘上縮放
        transform.localPosition = centerLocalPosition + new Vector3(
            currentOffset.x * axisScale.x,
            currentOffset.y * axisScale.y,
            currentOffset.z * axisScale.z
        );

        // 按空白鍵重新以 centerPoint 校準（或更新 centerPoint 後手動刷新）
        if (Input.GetKeyDown(KeyCode.Space))
            Recalibrate();

        // 調試顯示
        debugRawAcceleration = rawAcceleration;
        debugTargetOffset = targetOffset;
        debugCurrentOffset = currentOffset;
    }

    /// <summary>
    /// 重新校準：以 centerPoint（或自身當前位置）作為新的中心點
    /// </summary>
    public void Recalibrate()
    {
        centerLocalPosition = centerPoint != null
            ? centerPoint.localPosition
            : transform.localPosition - currentOffset;

        currentOffset = Vector3.zero;
        currentVelocity = Vector3.zero;
        targetOffset = Vector3.zero;
        filteredAcceleration = Vector3.zero;
        Debug.Log($"[AccelerometerBallEffect] 已重新校準，中心點: {centerLocalPosition}");
    }
}
