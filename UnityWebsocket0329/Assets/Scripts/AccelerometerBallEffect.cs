using UnityEngine;
// update: 2024-04-08 09:43
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
///
/// 直立與平放模式參數完全分離，各自獨立微調。
/// </summary>
public class AccelerometerBallEffect : MonoBehaviour
{
    [System.Serializable]
    private struct ModeSettings
    {
        [Tooltip("加速度 → 位移的縮放倍率，越大越敏感")]
        public float sensitivity;

        [Tooltip("平滑速度，越大越即時、越小越滑順")]
        [Range(1f, 30f)] public float smoothSpeed;

        [Tooltip("輸入濾波時間常數（秒），越小越即時、越大越平滑，建議 0.03 ~ 0.1")]
        [Range(0.01f, 0.5f)] public float inputFilterTime;

        [Tooltip("哪些軸要受加速度影響（1=開，0=關）")]
        public Vector3 movementAxesMask;

        [Tooltip("各軸方向翻轉（1=正常, -1=反轉）。若某軸方向相反，在 Inspector 改 -1 即可")]
        public Vector3 axisFlip;

        [Tooltip("各軸死區（m/s²）：低於此值的輸入歸零，消除靜止漂移。超過死區後連續輸出")]
        public Vector3 axisDeadzone;

        [Tooltip("各軸移動幅度縮放（X=左右, Y=上下, Z=前後），值越大移動範圍越大")]
        public Vector3 axisScale;

        [Tooltip("各軸允許的最大偏移距離（米），各軸獨立不互相壓縮")]
        public Vector3 maxOffsetPerAxis;
    }

    [Header("中心點")]
    [Tooltip("移動範圍的錨點，若不指定則以 Start 時的位置為中心")]
    [SerializeField] private Transform centerPoint;

    [Header("平放/直立切換")]
    [Tooltip("flatness 閾值（0~1），超過此值視為平放；建議 0.6~0.8")]
    [SerializeField] [Range(0f, 1f)] private float flatnessThreshold = 0.7f;
    [Tooltip("（唯讀）目前是否判定為平放模式")]
    [SerializeField] private bool phoneIsFlat = false;
    [Tooltip("模式切換時位置校正的淡出速度（越大越快，建議 2~5）")]
    [SerializeField] [Range(0.5f, 10f)] private float modeSwitchFadeSpeed = 3f;

    [Header("直立模式設定")]
    [SerializeField]
    private ModeSettings uprightSettings = new()
    {
        sensitivity      = 0.3f,
        smoothSpeed      = 10f,
        inputFilterTime  = 0.05f,
        movementAxesMask = new Vector3(1, 1, 0),
        axisFlip         = new Vector3(1f, 1f, -1f),
        axisDeadzone     = new Vector3(0.3f, 0.3f, 0.3f),
        axisScale        = new Vector3(1f, 1f, 1f),
        maxOffsetPerAxis = new Vector3(3f, 3f, 3f)
    };

    [Header("平放模式設定")]
    [SerializeField]
    private ModeSettings flatSettings = new()
    {
        sensitivity      = 0.08f,
        smoothSpeed      = 10f,
        inputFilterTime  = 0.05f,
        movementAxesMask = new Vector3(1, 0, 1),  // 平放：X=左右, Z=前後, Y=關閉
        axisFlip         = new Vector3(1f, 1f, -1f),
        axisDeadzone     = new Vector3(0.2f, 0.2f, 0.2f),
        axisScale        = new Vector3(1f, 1f, 1f),
        maxOffsetPerAxis = new Vector3(3f, 3f, 3f)
    };

    [Header("陀螺儀原始輸入（Debug）")]
    [Tooltip("裝置座標系重力向量 gDevice = Inverse(q)*(0,0,-g)；直立時 z≈0，平放時 z≈±9.81")]
    [SerializeField] private Vector3 debugGDevice         = Vector3.zero;
    [Tooltip("|gDevice.z|/g；超過 Flatness Threshold 判定為平放")]
    [SerializeField] private float   debugFlatnessRatio   = 0f;
    [Tooltip("四元數（Android GAME_ROTATION_VECTOR）")]
    [SerializeField] private float   debugQx = 0f;
    [SerializeField] private float   debugQy = 0f;
    [SerializeField] private float   debugQz = 0f;
    [SerializeField] private float   debugQw = 1f;
    [Tooltip("HandleAcceleration 收到的線性加速度（已去重力）；平放模式的位移來源")]
    [SerializeField] private Vector3 debugLinearAccInput  = Vector3.zero;
    [Tooltip("WebSocket 備用模式的 Euler 角（alpha/beta/gamma）；四元數模式下無效")]
    [SerializeField] private Vector3 debugEulerAngles     = Vector3.zero;

    [Header("調試")]
    [SerializeField] private Vector3 debugRawAcceleration      = Vector3.zero;
    [SerializeField] private Vector3 debugTargetOffset         = Vector3.zero;
    [SerializeField] private Vector3 debugCurrentOffset        = Vector3.zero;
    [SerializeField] private Vector3 debugModeSwitchCorrection = Vector3.zero;
    [SerializeField] private Vector3 debugActualPosition       = Vector3.zero;

    [Header("切換事件記錄")]
    [Tooltip("最後一次切換方向")]
    [SerializeField] private string debugLastSwitchDir       = "—";
    [Tooltip("距上次切換經過秒數（-1 = 尚未切換）")]
    [SerializeField] private float  debugTimeSinceLastSwitch = -1f;
    [Tooltip("切換瞬間注入的校正量大小（m）")]
    [SerializeField] private float  debugSwitchCorrectionMag = 0f;
    [Tooltip("淡出期間最大單幀位置跳動（m）；越小越平滑")]
    [SerializeField] private float  debugMaxFrameJump        = 0f;
    [Tooltip("剩餘校正量大小（趨近 0 代表淡出完成）")]
    [SerializeField] private float  debugFadeRemaining       = 0f;

    [Header("水平儀數值（濾波後）")]
    [Tooltip("X 軸加速度：左右傾斜（負=左, 正=右）")]
    [SerializeField] private float levelAxisX = 0f;
    [Tooltip("Y 軸加速度：前後傾斜（負=前, 正=後）")]
    [SerializeField] private float levelAxisY = 0f;
    [Tooltip("Roll 角（繞 Z 軸，左右傾斜角度）")]
    [SerializeField] private float rollDeg  = 0f;
    [Tooltip("Pitch 角（繞 X 軸，前後傾斜角度）")]
    [SerializeField] private float pitchDeg = 0f;

    private Vector3    centerLocalPosition;
    private Vector3    targetOffset        = Vector3.zero;
    private Vector3    currentOffset       = Vector3.zero;
    private Vector3    currentVelocity     = Vector3.zero;
    private Vector3    filteredAcceleration = Vector3.zero;
    private Vector3    rawAcceleration     = Vector3.zero;
    private bool       hasOrientationData  = false;
    private Quaternion currentOrientation  = Quaternion.identity;
    private bool       prevPhoneIsFlat          = false;
    private Vector3    modeSwitchCorrection      = Vector3.zero;
    private Vector3    prevFramePosition         = Vector3.zero;
    private float      switchTimer               = -1f;
    private bool       switchLoggedComplete      = false;

    private void Start()
    {
        centerLocalPosition = centerPoint != null
            ? centerPoint.localPosition
            : transform.localPosition;

        GyroscopeReceiver.OnGyroscopeDataReceived += HandleGyroscopeData;
        GyroscopeReceiver.OnAccelerationReceived  += HandleAcceleration;
    }

    private void OnDestroy()
    {
        GyroscopeReceiver.OnGyroscopeDataReceived -= HandleGyroscopeData;
        GyroscopeReceiver.OnAccelerationReceived  -= HandleAcceleration;
    }

    /// <summary>
    /// [UDP 模式] 四元數（qw != 0）：
    ///   計算裝置座標系重力向量，判斷手機是否平放。
    ///   直立模式：用重力方向映射到 Unity 座標系（原算法）。
    ///   平放模式：僅儲存四元數，等 HandleAcceleration 用線性加速度驅動。
    ///
    /// [WebSocket 模式] beta/gamma Euler 角：直接以角度公式輸出（傾斜備用）。
    /// </summary>
    private void HandleGyroscopeData(GyroscopeReceiver.GyroscopeData data)
    {
        const float g = 9.81f;

        if (data.qw != 0f)
        {
            var q = new Quaternion(data.qx, data.qy, data.qz, data.qw);
            currentOrientation = q;
            hasOrientationData = true;

            // gDevice.z ≈ ±9.81 → 平放（螢幕朝上或朝下均可）
            Vector3 gDevice = Quaternion.Inverse(q) * new Vector3(0f, 0f, -g);
            phoneIsFlat = Mathf.Abs(gDevice.z) / g >= flatnessThreshold;

            if (!phoneIsFlat)
            {
                // 直立模式：用重力方向作為位移輸入（原算法）
                rawAcceleration = new Vector3(gDevice.x, gDevice.z, -gDevice.y);
            }
            // 平放模式：rawAcceleration 由 HandleAcceleration 設定

            // --- 陀螺儀 Debug ---
            debugQx           = data.qx;
            debugQy           = data.qy;
            debugQz           = data.qz;
            debugQw           = data.qw;
            debugGDevice      = gDevice;
            debugFlatnessRatio = Mathf.Abs(gDevice.z) / g;
            debugEulerAngles  = Vector3.zero; // 四元數模式下 Euler 備用無效
        }
        else
        {
            // WebSocket / 無四元數備用：用傾斜角估算
            float betaRad  = data.beta  * Mathf.Deg2Rad;
            float gammaRad = data.gamma * Mathf.Deg2Rad;
            rawAcceleration = new Vector3(
                 Mathf.Sin(gammaRad) * g,
                -Mathf.Cos(betaRad) * Mathf.Cos(gammaRad) * g,
                 Mathf.Sin(betaRad) * Mathf.Cos(gammaRad) * g
            );

            // --- 陀螺儀 Debug（Euler 備用模式）---
            debugQx          = 0f; debugQy = 0f; debugQz = 0f; debugQw = 0f;
            debugGDevice     = Vector3.zero;
            debugFlatnessRatio = 0f;
            debugEulerAngles = new Vector3(data.alpha, data.beta, data.gamma);
        }
    }

    /// <summary>
    /// 接收 Android TYPE_LINEAR_ACCELERATION（已去重力）。
    /// 平放模式：以四元數將裝置系加速度旋轉到 Android 世界系，再映射至 Unity。
    ///   Android 世界系 → Unity：X→X, Y→Z, Z→Y
    /// 直立模式：rawAcceleration 已由 HandleGyroscopeData 設定，忽略此事件。
    /// </summary>
    private void HandleAcceleration(Vector3 acc)
    {
        debugLinearAccInput = acc; // 始終記錄原始線性加速度，方便對照兩種模式

        if (hasOrientationData && phoneIsFlat)
        {
            // q 為裝置系→世界系，直接旋轉即可
            Vector3 worldAcc = currentOrientation * acc;
            // Android 世界系(Z朝上) → Unity(Y朝上)
            rawAcceleration = new Vector3(worldAcc.x, worldAcc.z, worldAcc.y);
        }
        else if (!hasOrientationData)
        {
            rawAcceleration = acc;
        }
        // 直立模式：不覆蓋 HandleGyroscopeData 設定的 rawAcceleration
    }

    private void Update()
    {
        ModeSettings s = phoneIsFlat ? flatSettings : uprightSettings;

        // ── 切換偵測（必須在濾波更新前執行）──
        // 問題根源：切換時 filteredAcceleration 仍殘留舊模式值，
        //           被新模式的 axisScale/flip 放大後 proposedPosition 暴衝。
        // 對策：立即重設濾波值到新模式的合理起點，並跳到新穩態 currentOffset，
        //       讓 proposedPosition 瞬間穩定；視覺連續性由 modeSwitchCorrection 補償。
        bool modeJustSwitched = phoneIsFlat != prevPhoneIsFlat;
        if (modeJustSwitched)
        {
            // 平放靜止時線性加速度≈0；切回直立則用當前重力向量
            filteredAcceleration = phoneIsFlat ? Vector3.zero : rawAcceleration;
            // 立即跳到新模式穩態，消除 axisScale 差異造成的 proposedPosition 跳變
            currentOffset        = ComputeTargetOffset(filteredAcceleration, s);
            currentVelocity      = Vector3.zero;
            prevPhoneIsFlat      = phoneIsFlat;
        }

        float alpha = 1f - Mathf.Exp(-Time.deltaTime / s.inputFilterTime);
        filteredAcceleration = Vector3.Lerp(filteredAcceleration, rawAcceleration, alpha);

        // 套用方向翻轉
        Vector3 flipped = new Vector3(
            filteredAcceleration.x * s.axisFlip.x,
            filteredAcceleration.y * s.axisFlip.y,
            filteredAcceleration.z * s.axisFlip.z
        );

        // 逐軸死區
        Vector3 deadzoned = ApplyDeadzone(flipped, s.axisDeadzone);

        // 套用遮罩與靈敏度
        targetOffset = new Vector3(
            deadzoned.x * s.movementAxesMask.x,
            deadzoned.y * s.movementAxesMask.y,
            deadzoned.z * s.movementAxesMask.z
        ) * s.sensitivity;

        // 逐軸獨立 Clamp（不互相壓縮）
        targetOffset = new Vector3(
            Mathf.Clamp(targetOffset.x, -s.maxOffsetPerAxis.x, s.maxOffsetPerAxis.x),
            Mathf.Clamp(targetOffset.y, -s.maxOffsetPerAxis.y, s.maxOffsetPerAxis.y),
            Mathf.Clamp(targetOffset.z, -s.maxOffsetPerAxis.z, s.maxOffsetPerAxis.z)
        );

        float smoothTime = 1f / s.smoothSpeed;
        currentOffset = Vector3.SmoothDamp(currentOffset, targetOffset, ref currentVelocity, smoothTime);

        Vector3 proposedPosition = centerLocalPosition + new Vector3(
            currentOffset.x * s.axisScale.x,
            currentOffset.y * s.axisScale.y,
            currentOffset.z * s.axisScale.z
        );

        // ── 切換瞬間注入視覺校正（proposedPosition 已穩定後才算）──
        if (modeJustSwitched)
        {
            modeSwitchCorrection = transform.localPosition - proposedPosition;

            string dir = phoneIsFlat ? "直立 → 平放" : "平放 → 直立";
            debugLastSwitchDir       = dir;
            debugSwitchCorrectionMag = modeSwitchCorrection.magnitude;
            debugMaxFrameJump        = 0f;
            switchTimer              = 0f;
            switchLoggedComplete     = false;
            Debug.Log($"[模式切換] {dir}\n" +
                      $"  切換前位置 : {transform.localPosition:F3}\n" +
                      $"  新模式預測 : {proposedPosition:F3}\n" +
                      $"  注入校正量 : {modeSwitchCorrection:F3}  大小={modeSwitchCorrection.magnitude:F2}m");
        }

        modeSwitchCorrection    = Vector3.Lerp(modeSwitchCorrection, Vector3.zero, Time.deltaTime * modeSwitchFadeSpeed);
        transform.localPosition = proposedPosition + modeSwitchCorrection;

        // --- 淡出期間追蹤 ---
        if (switchTimer >= 0f)
        {
            switchTimer              += Time.deltaTime;
            float frameDelta          = Vector3.Distance(transform.localPosition, prevFramePosition);
            debugMaxFrameJump         = Mathf.Max(debugMaxFrameJump, frameDelta);
            debugTimeSinceLastSwitch  = switchTimer;
            debugFadeRemaining        = modeSwitchCorrection.magnitude;

            if (!switchLoggedComplete && modeSwitchCorrection.magnitude < 0.01f)
            {
                switchLoggedComplete = true;
                Debug.Log($"[切換淡出完成] {debugLastSwitchDir} | " +
                          $"耗時={switchTimer:F2}s | 最大單幀跳動={debugMaxFrameJump:F3}m");
            }
        }
        prevFramePosition = transform.localPosition;

        if (Input.GetKeyDown(KeyCode.Space))
            Recalibrate();

        debugRawAcceleration      = rawAcceleration;
        debugTargetOffset         = targetOffset;
        debugCurrentOffset        = currentOffset;
        debugModeSwitchCorrection = modeSwitchCorrection;
        debugActualPosition       = transform.localPosition;

        levelAxisX = filteredAcceleration.x;
        levelAxisY = filteredAcceleration.y;
        rollDeg    = Mathf.Atan2(filteredAcceleration.x, filteredAcceleration.z) * Mathf.Rad2Deg;
        pitchDeg   = Mathf.Atan2(filteredAcceleration.y, filteredAcceleration.z) * Mathf.Rad2Deg;
    }

    /// <summary>
    /// 將加速度向量經過 flip → deadzone → mask → sensitivity → clamp 完整管線，
    /// 回傳對應的 targetOffset。用於切換時立即算出新模式穩態。
    /// </summary>
    private static Vector3 ComputeTargetOffset(Vector3 acc, ModeSettings s)
    {
        Vector3 flipped = new Vector3(
            acc.x * s.axisFlip.x,
            acc.y * s.axisFlip.y,
            acc.z * s.axisFlip.z
        );
        Vector3 deadzoned = ApplyDeadzone(flipped, s.axisDeadzone);
        Vector3 masked = new Vector3(
            deadzoned.x * s.movementAxesMask.x,
            deadzoned.y * s.movementAxesMask.y,
            deadzoned.z * s.movementAxesMask.z
        ) * s.sensitivity;
        return new Vector3(
            Mathf.Clamp(masked.x, -s.maxOffsetPerAxis.x, s.maxOffsetPerAxis.x),
            Mathf.Clamp(masked.y, -s.maxOffsetPerAxis.y, s.maxOffsetPerAxis.y),
            Mathf.Clamp(masked.z, -s.maxOffsetPerAxis.z, s.maxOffsetPerAxis.z)
        );
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

        currentOffset        = Vector3.zero;
        currentVelocity      = Vector3.zero;
        targetOffset         = Vector3.zero;
        filteredAcceleration = Vector3.zero;
        modeSwitchCorrection     = Vector3.zero;
        switchTimer              = -1f;
        switchLoggedComplete     = false;
        debugMaxFrameJump        = 0f;
        debugFadeRemaining       = 0f;
        debugTimeSinceLastSwitch = -1f;
        Debug.Log($"[AccelerometerBallEffect] 已重新校準，中心點: {centerLocalPosition}");
    }
}