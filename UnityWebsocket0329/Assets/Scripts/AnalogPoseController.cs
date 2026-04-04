using UnityEngine;

/// <summary>
/// AnalogPoseController — 手機姿態「類比搖桿感」控制器
///
/// 架構說明：
///   父物件 (此腳本所在) → 由手機 Quaternion 旋轉
///   子物件 (childObject) → 掛在父物件下，位於 baseOffset 位置
///   傾角越大，子物件沿 baseOffset 方向延伸越遠 (幾何放大效果)
///   acceleration 只做 boost 加成，絕不積分、不決定方向
///
/// 使用方式：
///   1. 將此腳本掛到父物件
///   2. 將子物件指定到 childObject 欄位
///   3. 將 PoseControllerBridge 的目標改成此腳本
/// </summary>
public class AnalogPoseController : MonoBehaviour
{
    // ── Rotation Smoothing ────────────────────────────────────────────
    [Header("Rotation Smoothing")]
    [Tooltip("旋轉追蹤速度，越大越即時，越小越平滑")]
    [SerializeField] private float rotationSmoothSpeed = 20f;

    [Tooltip("true = 使用 localRotation，false = 使用 world rotation")]
    [SerializeField] private bool useLocalRotation = true;

    // ── Analog Input ──────────────────────────────────────────────────
    [Header("Analog Input")]
    [Tooltip("傾角 deadzone（度），低於此值視為中立")]
    [SerializeField] private float moveDeadZone = 4f;

    [Tooltip("最大有效傾角（度），超過此值 analog 輸出為 1")]
    [SerializeField] private float maxTiltAngle = 25f;

    [Tooltip("analog 輸出的全局縮放，影響子物件延伸距離的基礎強度")]
    [SerializeField] private float moveSpeed = 1f;

    [Tooltip("analog 輸入的平滑速度（MoveTowards），越大反應越快")]
    [SerializeField] private float inputSmoothSpeed = 12f;

    // ── Acceleration Boost ────────────────────────────────────────────
    [Header("Acceleration Boost")]
    [Tooltip("boost 對 finalSensitivity 的最大加成比例（0.5 = 最多 +50%）")]
    [SerializeField] private float boostFactor = 0.5f;

    [Tooltip("加速度 deadzone（m/s²），低於此值 boost = 0")]
    [SerializeField] private float accDeadZone = 1.3f;

    [Tooltip("加速度達到此值時 boost = 1（m/s²）")]
    [SerializeField] private float accMaxRange = 3f;

    [Tooltip("boost 上升速度（快，感受甩動瞬間反饋）")]
    [SerializeField] private float boostRiseSpeed = 25f;

    [Tooltip("boost 下降速度（慢，讓力道感受有餘韻）")]
    [SerializeField] private float boostFallSpeed = 10f;

    // ── Child Object ──────────────────────────────────────────────────
    [Header("Child Object")]
    [Tooltip("子物件 Transform，不指定則跳過位置控制")]
    [SerializeField] private Transform childObject;

    [Tooltip("子物件在父物件 local 空間的基礎偏移，方向決定延伸方向")]
    [SerializeField] private Vector3 baseOffset = new Vector3(0f, 0f, 2f);

    [Tooltip("傾角 magnitude 對 offset 距離的放大係數")]
    [SerializeField] private float offsetScaleFactor = 0.5f;

    // ── Debug ─────────────────────────────────────────────────────────
    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    // ── Private State ─────────────────────────────────────────────────
    // 由 OnPhoneData() 寫入，由 Update() 讀取
    private Quaternion _targetRotation  = Quaternion.identity;
    private Vector3    _rawAcceleration = Vector3.zero;
    private bool       _hasData         = false;

    // 平滑後的旋轉
    private Quaternion _smoothedRotation = Quaternion.identity;

    // 類比搖桿輸入（x = roll 左右，y = pitch 前後）
    private Vector2 _rawAnalogInput      = Vector2.zero;
    private Vector2 _smoothedAnalogInput = Vector2.zero;

    // 加速度 boost（非對稱平滑）
    private float _smoothBoost = 0f;

    // ── Public Read-only Properties ───────────────────────────────────
    /// <summary>平滑後的類比搖桿輸入（-1 到 1）</summary>
    public Vector2 AnalogInput      => _smoothedAnalogInput;

    /// <summary>當前 boost 值（0 到 1）</summary>
    public float   BoostValue       => _smoothBoost;

    /// <summary>最終靈敏度（含 boost 加成）</summary>
    public float   FinalSensitivity => moveSpeed * (1f + _smoothBoost * boostFactor);

    // ─────────────────────────────────────────────────────────────────
    // Public Interface
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 由 PoseControllerBridge 每幀呼叫，儲存最新手機資料。
    /// 不在此做任何計算——所有邏輯在 Update() 執行。
    /// </summary>
    public void OnPhoneData(Quaternion rotation, Vector3 gyro, Vector3 acceleration)
    {
        // 首次收到資料時，直接初始化平滑旋轉，避免第一幀從 identity 跳變
        if (!_hasData)
        {
            _smoothedRotation = rotation;
        }

        _targetRotation  = rotation;
        _rawAcceleration = acceleration;
        _hasData         = true;
        // gyro 保留參數相容性，目前不使用
    }

    // ─────────────────────────────────────────────────────────────────
    // Update
    // ─────────────────────────────────────────────────────────────────

    private void Update()
    {
        // Step 1：尚未收到任何手機資料，跳過
        if (!_hasData) return;

        // Step 2：平滑旋轉，追向目標（Slerp 不會黏住也不會過衝）
        _smoothedRotation = Quaternion.Slerp(
            _smoothedRotation,
            _targetRotation,
            rotationSmoothSpeed * Time.deltaTime
        );

        if (useLocalRotation)
            transform.localRotation = _smoothedRotation;
        else
            transform.rotation = _smoothedRotation;

        // Step 3：從平滑旋轉提取 pitch（X轉，前後）與 roll（Z轉，左右）
        //         eulerAngles 範圍 [0,360]，需正規化到 [-180,180]
        Vector3 euler = _smoothedRotation.eulerAngles;
        float pitch   = NormalizeAngle(euler.x); // 前後傾
        float roll    = NormalizeAngle(euler.z); // 左右傾

        // Step 4：Deadzone + 重新映射 + SmoothStep 曲線
        _rawAnalogInput = new Vector2(
            ApplyDeadzone(roll,  moveDeadZone, maxTiltAngle), // x = 左右
            ApplyDeadzone(pitch, moveDeadZone, maxTiltAngle)  // y = 前後
        );

        // Step 5：MoveTowards 平滑 analog 輸入（固定速率，手感穩定）
        _smoothedAnalogInput = Vector2.MoveTowards(
            _smoothedAnalogInput,
            _rawAnalogInput,
            inputSmoothSpeed * Time.deltaTime
        );

        // Step 6：計算加速度對應的 rawBoost（只用 magnitude，不用方向）
        float accMag  = _rawAcceleration.magnitude;
        float rawBoost;
        if (accMag < accDeadZone)
        {
            rawBoost = 0f;
        }
        else
        {
            float boostRange = accMaxRange - accDeadZone;
            if (boostRange <= 0f)
                rawBoost = 1f; // 防 divide-by-zero
            else
                rawBoost = Mathf.Clamp01((accMag - accDeadZone) / boostRange);
        }

        // Step 7：非對稱平滑 boost（上升快→甩動立即感，下降慢→餘韻感）
        float boostSpeed = (rawBoost > _smoothBoost) ? boostRiseSpeed : boostFallSpeed;
        _smoothBoost     = Mathf.MoveTowards(_smoothBoost, rawBoost, boostSpeed * Time.deltaTime);

        // Step 8：最終靈敏度 = 基礎速度 × (1 + boost 加成)
        float finalSensitivity = moveSpeed * (1f + _smoothBoost * boostFactor);

        // Step 9 & 10：子物件沿 baseOffset 方向縮放距離
        //   - 方向完全由父物件旋轉決定（baseOffset 是 local 方向）
        //   - analogInput magnitude 決定距離倍率
        //   - 不把 analogInput 拆成 X/Y 平面偏移！
        if (childObject != null)
        {
            float analogMag = _smoothedAnalogInput.magnitude;
            // offsetScale：中立時 = 1，傾角越大延伸越遠
            float offsetScale = 1f + analogMag * offsetScaleFactor * finalSensitivity;
            childObject.localPosition = baseOffset * offsetScale;
        }

        // Step 11：Debug 輸出（預設關閉）
        if (showDebugLogs)
        {
            Debug.Log(
                $"[APC] analog={_smoothedAnalogInput:F2} | " +
                $"boost={_smoothBoost:F2} | " +
                $"sens={finalSensitivity:F2} | " +
                $"accMag={accMag:F2}"
            );
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Helper Methods
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 將 Unity eulerAngles [0, 360] 正規化到 [-180, 180]
    /// </summary>
    private float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f) angle -= 360f;
        return angle;
    }

    /// <summary>
    /// Deadzone + 重映射 + SmoothStep 曲線，輸出 -1 到 1。
    ///
    /// 概念：
    ///   abs(angle) < deadzone           → 0（中立區）
    ///   deadzone ≤ abs(angle) ≤ maxAngle → 0~1（SmoothStep 曲線）
    ///   abs(angle) > maxAngle           → 1（飽和）
    ///   保留正負號代表方向。
    /// </summary>
    private float ApplyDeadzone(float angle, float deadzone, float maxAngle)
    {
        float range = maxAngle - deadzone;
        if (range <= 0f) return 0f; // 防設定錯誤造成 divide-by-zero

        float absAngle = Mathf.Abs(angle);
        if (absAngle < deadzone) return 0f; // 在 deadzone 內，輸出零

        // 超出 deadzone 後從 0 重新映射，讓手感從 0 起步而不是突然跳
        float normalized = Mathf.Clamp01((absAngle - deadzone) / range);

        // SmoothStep：S 型曲線，小角度更細膩，大角度更有力
        normalized = Mathf.SmoothStep(0f, 1f, normalized);

        return Mathf.Sign(angle) * normalized;
    }
}
