using UnityEngine;

/// <summary>
/// 重現「加速度儀校準 App」紅球效果：
/// 手機往哪個方向推，物件就往那個方向移動；
/// 停止施力時會自動彈回；靜止時停在重力傾斜角所對應的位置。
///
/// 座標系映射（Android GAME_ROTATION_VECTOR → Unity）：
///   Android 世界系 Z 朝上；重力 = (0,0,-g)
///   gDevice = Inverse(q) * (0,0,-g) → 本體座標系重力
///   Unity X = gDevice.x  （左右傾斜）
///   Unity Y = gDevice.z  （平放前後傾斜 / 平放時 = -9.81）
///   Unity Z = -gDevice.y （直立前後傾斜）
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
    [SerializeField] private Vector3 movementAxesMask = new Vector3(1, 1, 0);

    [Tooltip("各軸方向翻轉（1=正常, -1=反轉）。若某軸方向相反，在 Inspector 改 -1 即可")]
    [SerializeField] private Vector3 axisFlip = new Vector3(1f, 1f, -1f);

    [Tooltip("各軸死區（m/s²）：低於此值的輸入歸零，消除靜止漂移。超過死區後連續輸出")]
    [SerializeField] private Vector3 axisDeadzone = new Vector3(0.3f, 0.3f, 0.3f);

    [Tooltip("各軸移動幅度縮放（X=左右, Y=上下, Z=前後），值越大移動範圍越大")]
    [SerializeField] private Vector3 axisScale = Vector3.one;

    [Header("邊界限制")]
    [Tooltip("各軸允許的最大偏移距離（米），各軸獨立不互相壓縮")]
    [SerializeField] private Vector3 maxOffsetPerAxis = new Vector3(3f, 3f, 3f);

    [Header("調試")]
    [SerializeField] private Vector3 debugRawAcceleration = Vector3.zero;
    [SerializeField] private Vector3 debugTargetOffset = Vector3.zero;
    [SerializeField] private Vector3 debugCurrentOffset = Vector3.zero;

    [Header("水平儀數值（濾波後）")]
    [Tooltip("X 軸加速度：左右傾斜（負=左, 正=右）")]
    [SerializeField] private float levelAxisX = 0f;
    [Tooltip("Y 軸加速度：前後傾斜（負=前, 正=後）")]
    [SerializeField] private float levelAxisY = 0f;
    [Tooltip("Roll 角（繞 Z 軸，左右傾斜角度）")]
    [SerializeField] private float rollDeg = 0f;
    [Tooltip("Pitch 角（繞 X 軸，前後傾斜角度）")]
    [SerializeField] private float pitchDeg = 0f;

    private Vector3 centerLocalPosition;
    private Vector3 targetOffset = Vector3.zero;
    private Vector3 currentOffset = Vector3.zero;
    private Vector3 currentVelocity = Vector3.zero;
    private Vector3 filteredAcceleration = Vector3.zero;
    private Vector3 rawAcceleration = Vector3.zero;

    private void Start()
    {
        centerLocalPosition = centerPoint != null
            ? centerPoint.localPosition
            : transform.localPosition;

        GyroscopeReceiver.OnGyroscopeDataReceived += HandleGyroscopeData;
        GyroscopeReceiver.OnAccelerationReceived += HandleAcceleration;
    }

    private void OnDestroy()
    {
        GyroscopeReceiver.OnGyroscopeDataReceived -= HandleGyroscopeData;
        GyroscopeReceiver.OnAccelerationReceived -= HandleAcceleration;
    }

    private bool hasOrientationData = false;

    /// <summary>
    /// 從方向角資料重建重力向量，並統一映射到 Unity 座標系。
    ///
    /// [UDP 模式] data 含四元數（qw != 0）→ 用四元數旋轉重力向量
    ///   gravity_world = (0, 0, -g)（世界 Z 朝上，重力朝 -Z）
    ///   gDevice = Inverse(q) * (0, 0, -g)
    ///   Unity(x, y, z) = (gDevice.x, gDevice.z, -gDevice.y)
    ///
    /// [WebSocket 模式] data 含 beta/gamma Euler 角 → 直接以角度公式輸出 Unity 座標系
    ///   Unity X =  sin(gamma) * g
    ///   Unity Y = -cos(beta) * cos(gamma) * g  （平放 = -9.81）
    ///   Unity Z =  sin(beta)  * cos(gamma) * g
    /// </summary>
    private void HandleGyroscopeData(GyroscopeReceiver.GyroscopeData data)
    {
        const float g = 9.81f;

        if (data.qw != 0f)
        {
            var q = new Quaternion(data.qx, data.qy, data.qz, data.qw);
            Vector3 gDevice = Quaternion.Inverse(q) * new Vector3(0f, 0f, -g);
            rawAcceleration = new Vector3(gDevice.x, gDevice.z, -gDevice.y);
        }
        else
        {
            float betaRad  = data.beta  * Mathf.Deg2Rad;
            float gammaRad = data.gamma * Mathf.Deg2Rad;
            rawAcceleration = new Vector3(
                 Mathf.Sin(gammaRad) * g,
                -Mathf.Cos(betaRad) * Mathf.Cos(gammaRad) * g,
                 Mathf.Sin(betaRad) * Mathf.Cos(gammaRad) * g
            );
        }
        hasOrientationData = true;
    }

    private void HandleAcceleration(Vector3 acc)
    {
        if (!hasOrientationData)
            rawAcceleration = acc;
    }

    private void Update()
    {
        float alpha = 1f - Mathf.Exp(-Time.deltaTime / inputFilterTime);
        filteredAcceleration = Vector3.Lerp(filteredAcceleration, rawAcceleration, alpha);

        // 套用方向翻轉
        Vector3 flipped = new Vector3(
            filteredAcceleration.x * axisFlip.x,
            filteredAcceleration.y * axisFlip.y,
            filteredAcceleration.z * axisFlip.z
        );

        // 逐軸死區：低於死區歸零，超過死區連續輸出（不跳躍）
        Vector3 deadzoned = ApplyDeadzone(flipped, axisDeadzone);

        // 套用遮罩與靈敏度
        targetOffset = new Vector3(
            deadzoned.x * movementAxesMask.x,
            deadzoned.y * movementAxesMask.y,
            deadzoned.z * movementAxesMask.z
        ) * sensitivity;

        // 逐軸獨立 Clamp（不互相壓縮）
        targetOffset = new Vector3(
            Mathf.Clamp(targetOffset.x, -maxOffsetPerAxis.x, maxOffsetPerAxis.x),
            Mathf.Clamp(targetOffset.y, -maxOffsetPerAxis.y, maxOffsetPerAxis.y),
            Mathf.Clamp(targetOffset.z, -maxOffsetPerAxis.z, maxOffsetPerAxis.z)
        );

        float smoothTime = 1f / smoothSpeed;
        currentOffset = Vector3.SmoothDamp(currentOffset, targetOffset, ref currentVelocity, smoothTime);

        transform.localPosition = centerLocalPosition + new Vector3(
            currentOffset.x * axisScale.x,
            currentOffset.y * axisScale.y,
            currentOffset.z * axisScale.z
        );

        if (Input.GetKeyDown(KeyCode.Space))
            Recalibrate();

        debugRawAcceleration = rawAcceleration;
        debugTargetOffset = targetOffset;
        debugCurrentOffset = currentOffset;

        levelAxisX = filteredAcceleration.x;
        levelAxisY = filteredAcceleration.y;
        rollDeg  = Mathf.Atan2(filteredAcceleration.x, filteredAcceleration.z) * Mathf.Rad2Deg;
        pitchDeg = Mathf.Atan2(filteredAcceleration.y, filteredAcceleration.z) * Mathf.Rad2Deg;
    }

    private static Vector3 ApplyDeadzone(Vector3 v, Vector3 dz)
    {
        return new Vector3(
            Mathf.Abs(v.x) < dz.x ? 0f : Mathf.Sign(v.x) * (Mathf.Abs(v.x) - dz.x),
            Mathf.Abs(v.y) < dz.y ? 0f : Mathf.Sign(v.y) * (Mathf.Abs(v.y) - dz.y),
            Mathf.Abs(v.z) < dz.z ? 0f : Mathf.Sign(v.z) * (Mathf.Abs(v.z) - dz.z)
        );
    }

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
