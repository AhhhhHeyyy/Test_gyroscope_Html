using UnityEngine;
using System.Collections.Generic;
// update: 2026-05-12
/// <summary>
/// 重現「加速度儀校準 App」紅球效果（UDP 四元數模式）：
/// 手機往哪個方向推，物件就往那個方向移動；
/// 停止施力時會自動彈回；靜止時停在重力傾斜角所對應的位置。
///
/// 座標系映射（Android GAME_ROTATION_VECTOR → Unity）：
///   Android 世界系 Z 朝上；重力 = (0,0,-g)
///   gDevice = Inverse(q) * (0,0,-g) → 本體座標系重力
///   Unity X（直立） = gDevice.x     （左右傾斜，重力投影；持續量）
///   Unity Z（直立） = worldAcc.y    （前後位移，線性加速度；瞬時量）
///   Unity X（平放） = gDevice.x     （左右傾斜，重力投影；持續量）
///   Unity Z（平放） = gDevice.y     （前後傾斜，重力投影；持續量）
///
/// 直立與平放模式參數完全分離，各自獨立微調。
/// 自動校正嚮導可一鍵偵測 axisFlip、axisDeadzone、sensitivity 與軸交換（swapXZ）。
/// </summary>
public class AccelerometerBallEffect : MonoBehaviour
{
    private enum WizardPhase
    {
        Idle,
        UprightBaseline,     // 直立靜止 → tare + 噪聲死區
        UprightMaxGesture,   // 直立最大舒適幅度 → axisFlip + 自動算 axisScale
        UprightForward,      // 直立前後推 → axisFlip.z + Z axisScale
        FlatTransition,      // 等待 phoneIsFlat = true
        FlatBaseline,        // 平放靜止 → tare + 噪聲死區
        FlatMaxGesture,      // 平放最大舒適幅度 → axisFlip + axisScale
        FlatForward,         // 平放前後推 → axisFlip.z + Z axisScale
        Done
    }

    [System.Serializable]
    private struct ModeSettings
    {
        [Tooltip("各軸感應靈敏度（sensor m/s² → targetOffset 單位）。由嚮導自動計算：sensitivity.x = maxOffsetPerAxis.x / usableRange")]
        public Vector3 sensitivity;

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

        [Tooltip("輸出端最小有效位移（Unity 單位）：scaledOffset 低於此值歸零，消除微小抖動。由嚮導自動計算")]
        public Vector3 minOutputStep;

        [Tooltip("交換 X 與 Z 軸的輸入來源（嚮導偵測到手機旋轉 90° 時自動設定）")]
        public bool swapXZ;
    }

    [Header("中心點")]
    [Tooltip("移動範圍的錨點，若不指定則以 Start 時的位置為中心")]
    [SerializeField] private Transform centerPoint;

    [Header("自動校正")]
    [Tooltip("收到第一筆感測器資料後，等待幾秒再自動校正（讓濾波先穩定）；0 = 立即校正")]
    [SerializeField] [Range(0f, 5f)] private float autoCalibrationDelay = 1.5f;
    [Tooltip("（唯讀）是否已完成初始校正；未完成前物件鎖在原點")]
    [SerializeField] private bool hasCalibrated = false;

    [Header("平放/直立切換")]
    [Tooltip("flatness 閾值（0~1），超過此值視為平放；建議 0.6~0.8")]
    [SerializeField] [Range(0f, 1f)] private float flatnessThreshold = 0.7f;
    [Tooltip("（唯讀）目前是否判定為平放模式")]
    [SerializeField] private bool phoneIsFlat = false;
    [Tooltip("模式防抖時間（秒）：新狀態需穩定超過此值才真正切換，防止快速揮動造成頻繁切換；建議 0.2~0.4")]
    [SerializeField] [Range(0f, 1f)] private float modeSwitchDebounceTime = 0.3f;
    [Tooltip("模式切換時位置跳動補償的淡出時間（秒），預設 0.5 秒")]
    [SerializeField] [Range(0.1f, 2f)] private float modeSwitchTransitionDuration = 0.5f;

    [Header("直立模式設定")]
    [SerializeField]
    private ModeSettings uprightSettings = new()
    {
        sensitivity      = new Vector3(0.3f, 0.3f, 0.3f),
        smoothSpeed      = 10f,
        inputFilterTime  = 0.05f,
        movementAxesMask = new Vector3(1, 1, 1),
        axisFlip         = new Vector3(1f, 1f, -1f),
        axisDeadzone     = new Vector3(0.3f, 0.3f, 0.3f),
        axisScale        = new Vector3(1f, 1f, 1f),
        maxOffsetPerAxis = new Vector3(3f, 3f, 3f)
    };

    [Header("平放模式設定")]
    [Tooltip("平放 XZ 平推混合比例：0=純傾斜（球停在傾斜角度），1=純平推（停止推動後彈回），0.5=各半")]
    [SerializeField] [Range(0f, 1f)] private float flatLinearBlend = 0.4f;
    [SerializeField]
    private ModeSettings flatSettings = new()
    {
        sensitivity      = new Vector3(0.08f, 0.08f, 0.08f),
        smoothSpeed      = 7f,
        inputFilterTime  = 0.12f,
        movementAxesMask = new Vector3(1, 0, 1),
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
    [Header("調試")]
    [SerializeField] private Vector3 debugRawAcceleration        = Vector3.zero;
    [SerializeField] private Vector3 debugFilteredAcceleration   = Vector3.zero;
    [SerializeField] private Vector3 debugCalibratedAcceleration = Vector3.zero;
    [SerializeField] private Vector3 debugDebiasedAcceleration   = Vector3.zero;
    [SerializeField] private Vector3 debugTargetOffset           = Vector3.zero;
    [SerializeField] private Vector3 debugCurrentOffset          = Vector3.zero;
    [SerializeField] private float   debugTransitionProgress     = 1f;
    [SerializeField] private Vector3 debugActualPosition         = Vector3.zero;
    [Tooltip("scaledOffset 套用 minOutputStep 前（原始縮放後位移）")]
    [SerializeField] private Vector3 debugScaledBeforeFilter     = Vector3.zero;
    [Tooltip("scaledOffset 套用 minOutputStep 後（實際驅動位置的值）")]
    [SerializeField] private Vector3 debugScaledAfterFilter      = Vector3.zero;
    [Tooltip("目前模式的 minOutputStep（輸出端死區閾值）")]
    [SerializeField] private Vector3 debugMinOutputStep          = Vector3.zero;

    [Header("平放 XZ 管線（調試：來回移動時觀察）")]
    [Tooltip("Console 輸出間隔（秒）；0 = 關閉定時輸出")]
    [SerializeField] [Range(0f, 5f)] private float pipeLogInterval = 0.5f;
    [Tooltip("rawAcceleration.x/z — 輸入濾波前 (gDevice.x / gDevice.y)")]
    [SerializeField] private Vector2 dbPipe_raw        = Vector2.zero;
    [Tooltip("filteredAcceleration.x/z — 感測器 EMA 濾波後")]
    [SerializeField] private Vector2 dbPipe_filtered   = Vector2.zero;
    [Tooltip("calibratedAcceleration.x/z — Tare 參考點（重力補償會緩慢移動此值！）")]
    [SerializeField] private Vector2 dbPipe_tare       = Vector2.zero;
    [Tooltip("debiased x/z（swap 前）= filtered − tare")]
    [SerializeField] private Vector2 dbPipe_preSwap    = Vector2.zero;
    [Tooltip("debiased x/z（swap 後）= 實際驅動 Unity XZ 的訊號")]
    [SerializeField] private Vector2 dbPipe_postSwap   = Vector2.zero;
    [Tooltip("flip + 死區後 x/z（0 = 在死區內未驅動球）")]
    [SerializeField] private Vector2 dbPipe_afterDz    = Vector2.zero;
    [Tooltip("targetOffset x/z — 球的目標位置（EMA 前）")]
    [SerializeField] private Vector2 dbPipe_target     = Vector2.zero;
    [Tooltip("EMA alpha — 每幀追蹤速率（smoothSpeed=7 @ 60fps ≈ 0.10）")]
    [SerializeField] private float   dbPipe_emaAlpha   = 0f;
    [Tooltip("重力補償活躍度：1=Tare 正在向 filteredAcc 靠（會把球拉回中心）；0=已暫停")]
    [SerializeField] private float   dbPipe_idleRatio  = 0f;

    [Header("切換事件記錄")]
    [Tooltip("最後一次切換方向")]
    [SerializeField] private string debugLastSwitchDir       = "—";
    [Tooltip("距上次切換經過秒數（-1 = 尚未切換）")]
    [SerializeField] private float  debugTimeSinceLastSwitch = -1f;
    [Tooltip("切換瞬間起點與終點的距離（m）")]
    [SerializeField] private float  debugSwitchStartDist     = 0f;
    [Tooltip("過渡期間最大單幀位置跳動（m）；越小越平滑")]
    [SerializeField] private float  debugMaxFrameJump        = 0f;
    [Tooltip("過渡剩餘比例（1=剛切換，0=完成）")]
    [SerializeField] private float  debugTransitionRemaining = 0f;

    [Header("平放模式重力中心補償")]
    [Tooltip("啟用後：手機平放靜止時，自動緩慢修正 XZ 漂移（四元數時間累積誤差），讓球回中心")]
    [SerializeField] private bool enableFlatGravityCorrection = true;
    [Tooltip("XZ 漂移補償時間常數（秒）。越大越保守，越小越積極。建議 20~60；太小會吃掉慢速傾斜")]
    [SerializeField] [Range(5f, 120f)] private float flatGravityCorrectionTime = 30f;

    [Header("輸出平滑（抗抖動）")]
    [Tooltip("位置輸出的低通濾波時間常數（秒）。越大越平滑但反應越慢；0 = 關閉。建議 0.03 ~ 0.08")]
    [SerializeField] [Range(0f, 0.3f)] private float positionFilterTime = 0.05f;

    [Header("自動校正嚮導")]
    [Tooltip("每個收集階段的持續時間（秒）")]
    [SerializeField] [Range(1f, 5f)] private float wizardCollectDuration = 2.5f;
    [Tooltip("（唯讀）目前傾斜角度：X=左右傾斜、Z=前後傾斜（度）。校正時觀察舒適角度用")]
    [SerializeField] private Vector2 debugTiltAngleDeg = Vector2.zero;
    [Tooltip("是否在 Game 視窗顯示嚮導按鈕")]
    [SerializeField] private bool showWizardButton = true;
    [Tooltip("（唯讀）目前嚮導狀態文字")]
    [SerializeField] private string wizardStatusText = "等待啟動";
    [Tooltip("（唯讀）嚮導偵測到的直立模式 axisFlip 結果")]
    [SerializeField] private Vector3 wizardUprightFlip = Vector3.one;
    [Tooltip("（唯讀）嚮導偵測到的平放模式 axisFlip 結果")]
    [SerializeField] private Vector3 wizardFlatFlip = Vector3.one;
    [Tooltip("（唯讀）PushForward 偵測到的最小有意義動作幅度（Z 軸，已去基線，m/s²）")]
    [SerializeField] private float wizardMinPeakDisplay = 0f;
    [Tooltip("（唯讀）PushForward 偵測到的最大動作幅度（Z 軸，已去基線，m/s²）")]
    [SerializeField] private float wizardMaxPeakDisplay = 0f;

    [Header("校正按鈕（Game 視窗）")]
    [Tooltip("是否在 Game 視窗顯示校正按鈕")]
    [SerializeField] private bool showCalibrationButton = true;
    [Tooltip("按鈕左上角位置（像素，左上角為原點）")]
    [SerializeField] private Vector2 calibrationButtonPosition = new Vector2(10f, 200f);
    [Tooltip("按鈕尺寸（像素）")]
    [SerializeField] private Vector2 calibrationButtonSize = new Vector2(120f, 50f);

    [Header("偏移量 HUD（Game 視窗）")]
    [Tooltip("是否在 Game 視窗顯示偏移量指示器（移動原點 + 範圍邊界）")]
    [SerializeField] private bool showOffsetHUD = true;
    [Tooltip("HUD 左上角位置（像素）")]
    [SerializeField] private Vector2 hudPosition = new Vector2(10f, 10f);
    [Tooltip("HUD 方格邊長（像素）")]
    [SerializeField] [Range(60f, 200f)] private float hudSize = 100f;

    [Header("軸示意線（Scene / Game 視窗）")]
    [Tooltip("是否顯示 XYZ 軸示意線（紅=X，綠=Y，藍=Z）")]
    [SerializeField] private bool showAxisGizmos = true;
    [Tooltip("軸示意線長度（米）")]
    [SerializeField] [Range(0.1f, 5f)] private float axisGizmoLength = 1f;

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
    private Vector3    calibratedAcceleration = Vector3.zero; // Tare 基準：校正時記錄，下幀 debiased = filtered - calibrated ≈ 0
    private Vector3    rawAcceleration     = Vector3.zero;
    private bool       hasOrientationData  = false;
    private Quaternion currentOrientation  = Quaternion.identity;
    private bool       rawPhoneIsFlat               = false; // 感測器即時值，未經防抖
    private float      flatnessHoldTimer            = 0f;   // 新狀態持續計時
    private bool       prevPhoneIsFlat              = false;
    private float      modeSwitchTransitionProgress = 1f;
    private Vector3    modeSwitchTransitionOffset   = Vector3.zero; // 切換瞬間跳動量，淡出至 0
    private Vector3    prevFramePosition            = Vector3.zero;
    private float      switchTimer               = -1f;
    private bool       switchLoggedComplete      = false;
    private float      firstDataTime             = -1f; // 第一筆感測器資料到達的時間戳
    private Vector3    smoothedPosition          = Vector3.zero; // 輸出位置 EMA（抗抖動）
    private float      calibrationMsgTimer       = 0f;
    private Vector3    calibrationMsgPosition    = Vector3.zero;

    // 平放模式混合輸入暫存（傾斜 + 平推）
    private float _gravX = 0f, _gravZ = 0f; // gDevice 傾斜分量
    private float _linX  = 0f, _linZ  = 0f; // 水平線性加速度分量

    // 平放管線調試暫存（不序列化，僅在 Update 內部傳遞到 Inspector 欄位）
    private float   _dbIdleRatio     = 0f;
    private Vector3 _dbPreSwap       = Vector3.zero;
    private Vector3 _dbPostSwap      = Vector3.zero;
    private Vector3 _dbAfterDz       = Vector3.zero;
    private float   _dbEmaAlpha      = 0f;

    // 平放模式統計（供定時 Console 輸出 & OnDisable 總結用）
    private float   _pipeLogTimer    = 0f;
    private float   _statFlatTime    = 0f;
    private Vector2 _statTareEntry   = Vector2.zero;  // 進入平放時的 Tare 起點
    private bool    _statEntrySet    = false;
    private float   _statMaxIdle     = 0f;
    private Vector2 _statMaxTarget   = Vector2.zero;  // 歷史最大絕對 targetOffset
    private float   _statMaxJitter   = 0f;
    private float   _statJitterSum   = 0f;
    private int     _statJitterCount = 0;
    private Vector3 _statPrevPos     = Vector3.zero;

    // ── 嚮導私有狀態 ────────────────────────────────────────────
    private WizardPhase wizardPhase      = WizardPhase.Idle;
    private float       wizardTimer      = 0f;
    private int         wizardRetryCount = 0;
    private readonly List<Vector3> wizardSamples = new List<Vector3>();
    private Vector3 wizardUprightBaseline;
    private Vector3 wizardFlatBaseline;
    private bool    wizardPendingConfirm   = false;
    private bool    wizardReadyToCollect   = false; // 每個 phase 等使用者按「準備好了」才開始
    private float   wizardPeakZ            = 0f;   // PushForward phase：追蹤 Z 峰值方向
    private float   wizardMinPeakMagnitude = float.MaxValue; // PushForward：各次衝程最小有意義峰值
    private float   wizardUprightMaxSignal = 0f;  // MaxGesture phase：直立最大傾斜訊號
    private float   wizardFlatMaxSignal    = 0f;  // MaxGesture phase：平放最大傾斜訊號
    private float   wizardCurrentStrokeMax = 0f;             // 當前衝程最大絕對值
    private bool    wizardInStroke         = false;           // 是否正在一次有效衝程中
    private string  wizardLastStepResult   = "";
    // 暫存結果，確認後才寫入 settings
    private Vector3 pendingUprightFlip          = Vector3.one;
    private Vector3 pendingUprightDeadzone      = new Vector3(0.3f, 0.3f, 0.3f);
    private Vector3 pendingUprightSensitivity   = new Vector3(0.3f, 0.3f, 0.3f);
    private bool    pendingUprightSwapXZ        = false;
    private Vector3 pendingUprightMinOutputStep = Vector3.zero;
    private Vector3 pendingFlatFlip             = Vector3.one;
    private Vector3 pendingFlatDeadzone         = new Vector3(0.2f, 0.2f, 0.2f);
    private Vector3 pendingFlatSensitivity      = new Vector3(0.08f, 0.08f, 0.08f);
    private bool    pendingFlatSwapXZ           = false;
    private Vector3 pendingFlatMinOutputStep    = Vector3.zero;
    private bool    pendingHasFlatResults       = false;

    private void Start()
    {
        centerLocalPosition = centerPoint != null
            ? (transform.parent != null
                ? transform.parent.InverseTransformPoint(centerPoint.position)
                : centerPoint.position)
            : transform.localPosition;

        SensorEvents.OnGyroscopeDataReceived += HandleGyroscopeData;
        SensorEvents.OnAccelerationReceived  += HandleAcceleration;
    }

    private void OnDestroy()
    {
        SensorEvents.OnGyroscopeDataReceived -= HandleGyroscopeData;
        SensorEvents.OnAccelerationReceived  -= HandleAcceleration;
    }

    private void OnDisable()
    {
        if (_statFlatTime < 0.5f) return;
        float avgJitter  = _statJitterCount > 0 ? _statJitterSum / _statJitterCount : 0f;
        Vector2 tareDrift = dbPipe_tare - _statTareEntry;
        Debug.Log(
            $"[平放模式總結]\n" +
            $"  平放累計時間 : {_statFlatTime:F1}s\n" +
            $"  Tare 起點 XZ : ({_statTareEntry.x:+0.000;-0.000}, {_statTareEntry.y:+0.000;-0.000})\n" +
            $"  Tare 終點 XZ : ({dbPipe_tare.x:+0.000;-0.000}, {dbPipe_tare.y:+0.000;-0.000})  " +
            $"漂移量 = {tareDrift.magnitude:F4} m/s²\n" +
            $"  最大 targetOffset XZ : ({_statMaxTarget.x:F3}, {_statMaxTarget.y:F3})\n" +
            $"  最大 idleRatio : {_statMaxIdle:F3}  " +
            $"{(_statMaxIdle > 0.05f ? "⚠ 重力補償曾活躍 → Tare 被修改，可能是被拉回中心的原因" : "✓ 重力補償未明顯活躍")}\n" +
            $"  位置抖動 最大/平均 : {_statMaxJitter:F4}m / {avgJitter:F5}m");
    }

    private void OnGUI()
    {
        // ── 校正按鈕 ──
        if (showCalibrationButton)
        {
            if (GUI.Button(new Rect(calibrationButtonPosition.x, calibrationButtonPosition.y,
                                    calibrationButtonSize.x,   calibrationButtonSize.y), "校正"))
                Recalibrate();

            if (calibrationMsgTimer > 0f)
            {
                calibrationMsgTimer -= Time.deltaTime;
                var msgStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize  = 16,
                    fontStyle = FontStyle.Bold,
                    normal    = { textColor = Color.green }
                };
                string msg = $"校正成功\n位置: ({calibrationMsgPosition.x:F2}, {calibrationMsgPosition.y:F2}, {calibrationMsgPosition.z:F2})";
                GUI.Label(new Rect(calibrationButtonPosition.x,
                                   calibrationButtonPosition.y + calibrationButtonSize.y + 6f,
                                   260f, 50f), msg, msgStyle);
            }
        }

        // ── 嚮導按鈕 ──
        if (showWizardButton && wizardPhase == WizardPhase.Idle)
        {
            if (GUI.Button(new Rect(calibrationButtonPosition.x,
                                    calibrationButtonPosition.y + calibrationButtonSize.y + 64f,
                                    calibrationButtonSize.x, calibrationButtonSize.y), "嚮導校正"))
                StartWizard();
        }

        // ── 偏移量 HUD ──
        if (showOffsetHUD && hasCalibrated && wizardPhase == WizardPhase.Idle)
            DrawOffsetHUD();

        // ── 嚮導 Overlay ──
        if (wizardPhase == WizardPhase.Idle) return;

        float ox = Screen.width - 340f;
        float oy = 10f;
        float ow = 328f;
        // 高度依狀態動態計算
        bool isFwdPhase = wizardPhase == WizardPhase.UprightForward || wizardPhase == WizardPhase.FlatForward;
        float oh = wizardPendingConfirm ? 230f
                 : !wizardReadyToCollect ? 240f
                 : wizardPhase == WizardPhase.FlatTransition ? 120f
                 : isFwdPhase ? 210f
                 : 190f;
        GUI.Box(new Rect(ox, oy, ow, oh), "");

        var baseStyle  = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, normal  = { textColor = Color.white } };
        var boldStyle  = new GUIStyle(baseStyle)      { fontStyle = FontStyle.Bold, fontSize = 14 };
        var arrowStyle = new GUIStyle(baseStyle)      { fontStyle = FontStyle.Bold, fontSize = 36,
                                                        alignment = TextAnchor.MiddleCenter,
                                                        normal    = { textColor = Color.yellow } };
        var hintStyle  = new GUIStyle(baseStyle)      { fontSize = 12, normal = { textColor = new Color(0.7f, 1f, 0.7f) } };
        var retryStyle = new GUIStyle(baseStyle)      { fontSize = 12, normal = { textColor = new Color(1f, 0.8f, 0.3f) } };

        float ix = ox + 8f, iy = oy + 6f, iw = ow - 16f;

        // ── 標題列 ──
        int phaseNum = (int)wizardPhase;
        GUI.Label(new Rect(ix, iy, iw, 20f), $"自動校正嚮導  ({phaseNum} / 7)", boldStyle); iy += 24f;

        // ── 確認摘要畫面 ──
        if (wizardPendingConfirm)
        {
            GUI.Label(new Rect(ix, iy, iw, 70f), wizardStatusText, baseStyle); iy += 74f;
            if (wizardLastStepResult.Length > 0)
            { GUI.Label(new Rect(ix, iy, iw, 18f), "最後偵測：" + wizardLastStepResult, hintStyle); iy += 22f; }
            if (GUI.Button(new Rect(ix, iy, iw, 38f), "確認套用")) ApplyWizardResults();
            iy += 42f;
            if (GUI.Button(new Rect(ix, iy, iw, 30f), "取消"))
            { wizardPhase = WizardPhase.Idle; wizardPendingConfirm = false; wizardStatusText = "等待啟動"; }
            return;
        }

        // ── 等待平放 ──
        if (wizardPhase == WizardPhase.FlatTransition)
        {
            GUI.Label(new Rect(ix, iy, iw, 28f), "↓  手機平放（螢幕朝上）", arrowStyle); iy += 32f;
            GUI.Label(new Rect(ix, iy, iw, 34f),
                $"偵測中... {Mathf.Max(0f, 10f - wizardTimer):F0}s\n目前：{(phoneIsFlat ? "已平放 ✓" : "尚未平放")}", baseStyle);
            iy += 38f;
            if (GUI.Button(new Rect(ix, iy, iw, 28f), "跳過平放校正"))
            { pendingHasFlatResults = false; wizardPendingConfirm = true; wizardStatusText = "已跳過平放，請按「確認套用」"; }
            return;
        }

        // ── 說明畫面（等待使用者按「準備好了」）──
        if (!wizardReadyToCollect)
        {
            GUI.Label(new Rect(ix, iy, iw, 44f), GetPhaseArrow(), arrowStyle); iy += 48f;
            GUI.Label(new Rect(ix, iy, iw, 48f), GetPhaseDetail(), baseStyle); iy += 52f;
            if (wizardRetryCount > 0)
            { GUI.Label(new Rect(ix, iy, iw, 18f), wizardStatusText, retryStyle); iy += 22f; }
            else if (wizardLastStepResult.Length > 0)
            { GUI.Label(new Rect(ix, iy, iw, 18f), "✓ " + wizardLastStepResult, hintStyle); iy += 22f; }
            if (GUI.Button(new Rect(ix, iy, iw, 38f), "我準備好了，開始收集"))
            { wizardReadyToCollect = true; wizardTimer = 0f; wizardSamples.Clear(); }
            return;
        }

        // ── 收集中 ──
        float pct  = Mathf.Clamp01(wizardTimer / wizardCollectDuration);
        int   bars = Mathf.RoundToInt(pct * 10);
        GUI.Label(new Rect(ix, iy, iw, 20f),
            "收集中  [" + new string('█', bars) + new string('░', 10 - bars) + $"]  {wizardTimer:F1}s",
            boldStyle); iy += 24f;

        // 即時訊號條（Baseline 時顯示絕對值；方向 phase 顯示相對基準的差值）
        bool isBaseline = wizardPhase == WizardPhase.UprightBaseline || wizardPhase == WizardPhase.FlatBaseline;
        Vector3 liveRaw = isBaseline ? filteredAcceleration
            : filteredAcceleration - (wizardPhase == WizardPhase.FlatMaxGesture ||
                                      wizardPhase == WizardPhase.FlatForward
                                       ? wizardFlatBaseline : wizardUprightBaseline);
        DrawSignalBar(ix, ref iy, iw, "X 軸", liveRaw.x, baseStyle);
        DrawSignalBar(ix, ref iy, iw, "Z 軸", liveRaw.z, baseStyle);
        // PushForward phase 額外顯示最大 / 最小幅度
        bool showPeak = wizardPhase == WizardPhase.UprightForward || wizardPhase == WizardPhase.FlatForward;
        if (showPeak)
        {
            string minStr = wizardMinPeakDisplay > 0.01f ? $"{wizardMinPeakDisplay:F2} m/s²" : "—（繼續來回移動）";
            GUI.Label(new Rect(ix, iy, iw, 36f),
                $"最大幅：{wizardMaxPeakDisplay:F2} m/s²  {(wizardMaxPeakDisplay >= 0.3f ? "✓" : "（再大一點）")}\n最小幅：{minStr}",
                hintStyle);
        }
        else if (wizardLastStepResult.Length > 0)
            GUI.Label(new Rect(ix, iy, iw, 18f), "✓ " + wizardLastStepResult, hintStyle);
    }

    private string GetPhaseArrow() => wizardPhase switch
    {
        WizardPhase.UprightBaseline   => "[ 靜止不動 ]",
        WizardPhase.UprightMaxGesture => "→  向右傾斜（最大舒適）",
        WizardPhase.UprightForward    => "↑↓  前後移動",
        WizardPhase.FlatBaseline      => "[ 靜止不動 ]",
        WizardPhase.FlatMaxGesture    => "→  往右傾斜（最大舒適）",
        WizardPhase.FlatForward       => "↑↓  前後移動",
        _                             => ""
    };

    private string GetPhaseDetail() => wizardPhase switch
    {
        WizardPhase.UprightBaseline   => "手機豎直拿好\n保持靜止，不要動",
        WizardPhase.UprightMaxGesture => $"手機螢幕朝向你\n往右傾斜到你的【最大舒適角度】並保持\n最大位移由 Inspector 的 Max Offset Per Axis.x（目前={uprightSettings.maxOffsetPerAxis.x:F1}）決定",
        WizardPhase.UprightForward    => "握著手機，往前後來回移動幾次\n用你自然舒適的幅度",
        WizardPhase.FlatBaseline      => "手機平放（螢幕朝上）\n保持靜止，不要動",
        WizardPhase.FlatMaxGesture    => $"手機平放\n往右傾斜到你的【最大舒適角度】並保持\n最大位移由 Max Offset Per Axis.x（目前={flatSettings.maxOffsetPerAxis.x:F1}）決定",
        WizardPhase.FlatForward       => "手機平放\n往前後來回推動幾次，用舒適的自然幅度",
        _                             => ""
    };

    private void DrawSignalBar(float x, ref float y, float w, string label, float value, GUIStyle style)
    {
        const float maxVal = 8f;
        const int   barLen = 14;
        float norm   = Mathf.Clamp01(Mathf.Abs(value) / maxVal);
        int   filled = Mathf.RoundToInt(norm * barLen);
        string bar   = value >= 0
            ? "[" + new string('░', barLen - filled) + new string('█', filled) + "]"
            : "[" + new string('█', filled) + new string('░', barLen - filled) + "]";
        GUI.Label(new Rect(x, y, w, 18f), $"{label}: {bar}  {value:+0.0;-0.0} m/s²", style);
        y += 20f;
    }

    /// <summary>
    /// [UDP 模式] 以四元數計算裝置座標系重力向量，判斷手機是否平放。
    ///   直立模式：用重力方向映射到 Unity 座標系。
    ///   平放模式：僅儲存四元數，等 HandleAcceleration 用線性加速度驅動。
    /// </summary>
    private void HandleGyroscopeData(SensorEvents.GyroscopeData data)
    {
        const float g = 9.81f;

        if (data.qw != 0f)
        {
            // UDP 四元數模式
            var q = new Quaternion(data.qx, data.qy, data.qz, data.qw);
            currentOrientation = q;
            hasOrientationData = true;

            // gDevice.z ≈ ±9.81 → 平放（螢幕朝上或朝下均可）
            Vector3 gDevice = Quaternion.Inverse(q) * new Vector3(0f, 0f, -g);
            rawPhoneIsFlat = Mathf.Abs(gDevice.z) / g >= flatnessThreshold;

            if (!phoneIsFlat)
            {
                // 直立模式：X=左右傾斜（gDevice.x），Y=前後傾斜（gDevice.z）；持續量不彈回
                // Z 由 HandleAcceleration 的線性加速度負責（推力感）
                // rawPhoneIsFlat=true 表示手機正在往平放傾倒（防抖中），凍結 Y 避免球在過渡期飛出
                rawAcceleration.x = gDevice.x;
                rawAcceleration.y = rawPhoneIsFlat ? 0f : gDevice.z;
            }
            else
            {
                // 平放模式：傾斜分量（持續）+ 平推分量（彈回）混合
                _gravX = gDevice.x;
                _gravZ = gDevice.y;
                rawAcceleration.x = _gravX + _linX * flatLinearBlend;
                rawAcceleration.z = _gravZ + _linZ * flatLinearBlend;
            }

            debugQx            = data.qx;
            debugQy            = data.qy;
            debugQz            = data.qz;
            debugQw            = data.qw;
            debugGDevice       = gDevice;
            debugFlatnessRatio = Mathf.Abs(gDevice.z) / g;
            debugTiltAngleDeg  = new Vector2(
                Mathf.Asin(Mathf.Clamp(gDevice.x / g, -1f, 1f)) * Mathf.Rad2Deg,
                Mathf.Asin(Mathf.Clamp(gDevice.y / g, -1f, 1f)) * Mathf.Rad2Deg);
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
            debugQx = 0f; debugQy = 0f; debugQz = 0f; debugQw = 0f;
            debugGDevice = Vector3.zero;
            debugFlatnessRatio = 0f;
        }
    }

    /// <summary>
    /// 接收 Android TYPE_LINEAR_ACCELERATION（已去重力）。
    /// 直立模式：僅更新 Z 軸（前後推力）；X/Y 由 HandleGyroscopeData 的 gDevice 負責。
    ///   worldAcc.y（Android 水平前後）→ rawAcceleration.z（Unity Z）
    /// 平放模式：X/Z 由 HandleGyroscopeData 的 gDevice 負責，此處不覆寫。
    /// </summary>
    private void HandleAcceleration(Vector3 acc)
    {
        debugLinearAccInput = acc;

        if (hasOrientationData && !phoneIsFlat)
        {
            // 直立模式：X/Y 已由 HandleGyroscopeData 的 gDevice 負責；此處只更新 Z（前後推力）
            Vector3 worldAcc = currentOrientation * acc;
            rawAcceleration.z = worldAcc.y; // Android 世界 Y → Unity Z
        }
        else if (hasOrientationData && phoneIsFlat)
        {
            Vector3 worldAcc = currentOrientation * acc;
            // 水平線性加速度（平推）混入 XZ
            _linX = worldAcc.x;
            _linZ = worldAcc.y;
            rawAcceleration.x = _gravX + _linX * flatLinearBlend;
            rawAcceleration.z = _gravZ + _linZ * flatLinearBlend;
            // 垂直方向 → Unity Y
            rawAcceleration.y = worldAcc.z;
        }
        else if (!hasOrientationData)
        {
            rawAcceleration = acc;
        }
    }

    private void Update()
    {
        // ── 自動初始校正：第一筆資料到位 + 等待濾波穩定後執行一次 ──
        if (!hasCalibrated)
        {
            if (hasOrientationData)
            {
                if (firstDataTime < 0f)
                    firstDataTime = Time.time;
                else if (Time.time - firstDataTime >= autoCalibrationDelay)
                    Recalibrate();
            }
            // 嚮導在初始校正完成前也可運行，補跑濾波讓樣本有效
            if (wizardPhase != WizardPhase.Idle && wizardPhase != WizardPhase.Done)
            {
                ModeSettings ws = phoneIsFlat ? flatSettings : uprightSettings;
                float wa = 1f - Mathf.Exp(-Time.deltaTime / ws.inputFilterTime);
                filteredAcceleration = Vector3.Lerp(filteredAcceleration, rawAcceleration, wa);
                UpdateWizard();
            }
            transform.localPosition = centerLocalPosition;
            return;
        }

        // ── 模式防抖：rawPhoneIsFlat 穩定超過 modeSwitchDebounceTime 才更新 phoneIsFlat ──
        // 快速揮動時 rawPhoneIsFlat 頻繁翻轉，計時器被不斷重置，phoneIsFlat 不會切換
        if (rawPhoneIsFlat != phoneIsFlat)
        {
            flatnessHoldTimer += Time.deltaTime;
            if (flatnessHoldTimer >= modeSwitchDebounceTime)
            {
                phoneIsFlat       = rawPhoneIsFlat;
                flatnessHoldTimer = 0f;
            }
        }
        else
        {
            flatnessHoldTimer = 0f;
        }

        // phoneIsFlat 防抖更新完畢後才取 settings，確保本幀套用正確的模式參數
        ModeSettings s = phoneIsFlat ? flatSettings : uprightSettings;

        // ── 切換偵測（必須在濾波更新前執行）──
        // 問題根源：切換時 filteredAcceleration 仍殘留舊模式值，
        //           被新模式的 axisScale/flip 放大後 proposedPosition 暴衝。
        // 對策：立即重設濾波值到新模式的合理起點，並跳到新穩態 currentOffset，
        //       讓 proposedPosition 瞬間穩定；視覺連續性由跳動補償淡出處理。
        bool modeJustSwitched = phoneIsFlat != prevPhoneIsFlat;
        if (modeJustSwitched)
        {
            if (phoneIsFlat)
            {
                // 切入平放：以當前重力投影為 Tare 基準，讓球從中心出發不因切換姿勢跳位
                var flatBase = new Vector3(debugGDevice.x, 0f, debugGDevice.y);
                filteredAcceleration   = flatBase;
                calibratedAcceleration = flatBase;
                rawAcceleration.y      = 0f; // 清除直立模式殘留 Y 值，防止切換後 Y 軸漂移
            }
            else
            {
                // 切回直立：用當前重力向量為初始值
                filteredAcceleration   = rawAcceleration;
                calibratedAcceleration = filteredAcceleration;
            }
            currentOffset   = Vector3.zero;
            currentVelocity = Vector3.zero;
            prevPhoneIsFlat = phoneIsFlat;
        }

        float alpha = 1f - Mathf.Exp(-Time.deltaTime / s.inputFilterTime);
        filteredAcceleration = Vector3.Lerp(filteredAcceleration, rawAcceleration, alpha);

        // ── 嚮導校正進行中：凍結小球 ──
        if (wizardPhase != WizardPhase.Idle && wizardPhase != WizardPhase.Done)
        {
            UpdateWizard();
            debugRawAcceleration      = rawAcceleration;
            debugFilteredAcceleration = filteredAcceleration;
            debugActualPosition       = transform.localPosition;
            transform.localPosition   = centerLocalPosition;
            return;
        }
        // 嚮導完成後顯示 overlay 3 秒，倒計時結束自動隱藏
        if (wizardPhase == WizardPhase.Done)
        {
            wizardTimer += Time.deltaTime;
            if (wizardTimer >= 3f) { wizardTimer = 0f; wizardPhase = WizardPhase.Idle; }
        }

        // Tare 去偏：以校正瞬間的 filteredAcceleration 為基準，使當前姿勢 = (0,0,0)
        Vector3 debiased = filteredAcceleration - calibratedAcceleration;

        // ── 平放 XZ 重力漂移自動補償 ──────────────────────────────────
        // 根本原因：GAME_ROTATION_VECTOR 只用陀螺儀積分，長時間使用後四元數漂移，
        //           導致 gDevice.x/y 緩慢偏離 0，XZ 去偏值持續非零，球不回中心。
        // 對策：球靜止時（debiased.xz 在死區內），以 flatGravityCorrectionTime 為時間常數
        //       緩慢把 calibratedAcceleration.xz 往 filteredAcceleration.xz 靠，
        //       讓漂移自動被吸收；主動傾斜時（debiased 超出死區）暫停補償。
        if (phoneIsFlat && enableFlatGravityCorrection &&
            wizardPhase is WizardPhase.Idle or WizardPhase.Done)
        {
            float halfDz   = (s.axisDeadzone.x + s.axisDeadzone.z) * 0.5f;
            float idleRatio = 1f - Mathf.Clamp01(
                (Mathf.Abs(debiased.x) + Mathf.Abs(debiased.z)) / Mathf.Max(halfDz * 2f, 0.01f));
            _dbIdleRatio = idleRatio;
            float corrAlpha = (1f - Mathf.Exp(-Time.deltaTime / flatGravityCorrectionTime)) * idleRatio;
            calibratedAcceleration.x += (filteredAcceleration.x - calibratedAcceleration.x) * corrAlpha;
            calibratedAcceleration.z += (filteredAcceleration.z - calibratedAcceleration.z) * corrAlpha;
            // 重新計算去偏（本幀立即生效）
            debiased = filteredAcceleration - calibratedAcceleration;
        }
        else
        {
            _dbIdleRatio = 0f;
        }

        _dbPreSwap = debiased;
        // ── 軸交換（嚮導偵測到手機旋轉 90° 時啟用）──
        if (s.swapXZ)
            debiased = new Vector3(debiased.z, debiased.y, debiased.x);
        _dbPostSwap = debiased;

        // 套用方向翻轉
        Vector3 flipped = new Vector3(
            debiased.x * s.axisFlip.x,
            debiased.y * s.axisFlip.y,
            debiased.z * s.axisFlip.z
        );

        // 逐軸死區
        Vector3 deadzoned = ApplyDeadzone(flipped, s.axisDeadzone);
        _dbAfterDz = deadzoned;

        // 套用遮罩與各軸靈敏度
        targetOffset = new Vector3(
            deadzoned.x * s.movementAxesMask.x * s.sensitivity.x,
            deadzoned.y * s.movementAxesMask.y * s.sensitivity.y,
            deadzoned.z * s.movementAxesMask.z * s.sensitivity.z);

        // 逐軸獨立 Clamp（不互相壓縮）
        targetOffset = new Vector3(
            Mathf.Clamp(targetOffset.x, -s.maxOffsetPerAxis.x, s.maxOffsetPerAxis.x),
            Mathf.Clamp(targetOffset.y, -s.maxOffsetPerAxis.y, s.maxOffsetPerAxis.y),
            Mathf.Clamp(targetOffset.z, -s.maxOffsetPerAxis.z, s.maxOffsetPerAxis.z)
        );

        if (phoneIsFlat)
        {
            // 平放 X/Z 來自重力投影（位置信號），EMA 追蹤不累積速度，不過衝
            float flatAlpha = 1f - Mathf.Exp(-Time.deltaTime * s.smoothSpeed);
            _dbEmaAlpha     = flatAlpha;
            currentOffset   = Vector3.Lerp(currentOffset, targetOffset, flatAlpha);
            currentVelocity = Vector3.zero;
        }
        else
        {
            _dbEmaAlpha = 0f;
            float smoothTime = 1f / s.smoothSpeed;
            currentOffset = Vector3.SmoothDamp(currentOffset, targetOffset, ref currentVelocity, smoothTime);
        }

        Vector3 scaledOffset = new Vector3(
            currentOffset.x * s.axisScale.x,
            currentOffset.y * s.axisScale.y,
            currentOffset.z * s.axisScale.z
        );
        // 輸出端死區：位移小於 minOutputStep 歸零，消除靜止微抖
        debugScaledBeforeFilter = scaledOffset;
        if (Mathf.Abs(scaledOffset.x) < s.minOutputStep.x) scaledOffset.x = 0f;
        if (Mathf.Abs(scaledOffset.y) < s.minOutputStep.y) scaledOffset.y = 0f;
        if (Mathf.Abs(scaledOffset.z) < s.minOutputStep.z) scaledOffset.z = 0f;
        debugScaledAfterFilter = scaledOffset;
        debugMinOutputStep     = s.minOutputStep;
        Vector3 localScaledOffset = transform.parent != null
            ? transform.parent.InverseTransformDirection(scaledOffset)
            : scaledOffset;
        Vector3 proposedPosition = centerLocalPosition + localScaledOffset;

        // ── 切換瞬間：記錄跳動補償量，讓 proposedPosition 仍即時反應傾斜 ──
        // 只補償切換造成的位移跳動，不拖慢正常傾斜的反應速度
        if (modeJustSwitched)
        {
            // offset = 舊位置 - 新模式預測位置；之後隨時間淡出至 0
            modeSwitchTransitionOffset   = smoothedPosition - proposedPosition;
            modeSwitchTransitionProgress = 0f;

            string dir = phoneIsFlat ? "直立 → 平放" : "平放 → 直立";
            debugLastSwitchDir   = dir;
            debugSwitchStartDist = modeSwitchTransitionOffset.magnitude;
            debugMaxFrameJump    = 0f;
            switchTimer          = 0f;
            switchLoggedComplete = false;
            Debug.Log($"[模式切換] {dir}\n" +
                      $"  切換前位置 : {smoothedPosition:F3}\n" +
                      $"  新模式預測 : {proposedPosition:F3}\n" +
                      $"  補償跳動量 : {debugSwitchStartDist:F2}m  時長={modeSwitchTransitionDuration:F2}s");
        }

        // 補償淡出：SmoothStep 在 transitionDuration 秒內把跳動 offset 降到 0
        // proposedPosition 本身完全不受影響，傾斜反應依然即時
        Vector3 basePosition;
        if (modeSwitchTransitionProgress < 1f)
        {
            modeSwitchTransitionProgress = Mathf.Clamp01(
                modeSwitchTransitionProgress + Time.deltaTime / modeSwitchTransitionDuration);
            float fade   = 1f - Mathf.SmoothStep(0f, 1f, modeSwitchTransitionProgress);
            basePosition = proposedPosition + modeSwitchTransitionOffset * fade;
        }
        else
        {
            basePosition = proposedPosition;
        }

        // 輸出位置 EMA：在 basePosition 之後再做一次低通濾波，消除高頻抖動
        if (positionFilterTime > 0f)
        {
            float posAlpha   = 1f - Mathf.Exp(-Time.deltaTime / positionFilterTime);
            smoothedPosition = Vector3.Lerp(smoothedPosition, basePosition, posAlpha);
        }
        else
        {
            smoothedPosition = basePosition;
        }
        transform.localPosition = smoothedPosition;

        if (showAxisGizmos)
        {
            Vector3 p = transform.position;
            Debug.DrawLine(p, p + Vector3.right   * axisGizmoLength, Color.red);
            Debug.DrawLine(p, p + Vector3.up      * axisGizmoLength, Color.green);
            Debug.DrawLine(p, p + Vector3.forward * axisGizmoLength, Color.blue);
        }

        // --- 淡出期間追蹤 ---
        if (switchTimer >= 0f)
        {
            switchTimer              += Time.deltaTime;
            float frameDelta          = Vector3.Distance(transform.localPosition, prevFramePosition);
            debugMaxFrameJump         = Mathf.Max(debugMaxFrameJump, frameDelta);
            debugTimeSinceLastSwitch  = switchTimer;
            debugTransitionRemaining  = 1f - modeSwitchTransitionProgress;

            if (!switchLoggedComplete && modeSwitchTransitionProgress >= 1f)
            {
                switchLoggedComplete = true;
                Debug.Log($"[切換過渡完成] {debugLastSwitchDir} | " +
                          $"耗時={switchTimer:F2}s | 最大單幀跳動={debugMaxFrameJump:F3}m");
            }
        }
        prevFramePosition = transform.localPosition;

        // Editor：Space 鍵校正
        if (Input.GetKeyDown(KeyCode.Space))
            Recalibrate();

        // 實機：同時觸碰兩指（雙指點擊）校正
        if (Input.touchCount == 2 &&
            Input.GetTouch(0).phase == TouchPhase.Began &&
            Input.GetTouch(1).phase == TouchPhase.Began)
            Recalibrate();

        debugRawAcceleration        = rawAcceleration;
        debugFilteredAcceleration   = filteredAcceleration;
        debugCalibratedAcceleration = calibratedAcceleration;
        debugDebiasedAcceleration   = filteredAcceleration - calibratedAcceleration;
        debugTargetOffset           = targetOffset;
        debugCurrentOffset          = currentOffset;
        debugTransitionProgress     = modeSwitchTransitionProgress;
        debugActualPosition         = transform.localPosition;

        if (phoneIsFlat)
        {
            dbPipe_raw       = new Vector2(rawAcceleration.x,        rawAcceleration.z);
            dbPipe_filtered  = new Vector2(filteredAcceleration.x,   filteredAcceleration.z);
            dbPipe_tare      = new Vector2(calibratedAcceleration.x, calibratedAcceleration.z);
            dbPipe_preSwap   = new Vector2(_dbPreSwap.x,  _dbPreSwap.z);
            dbPipe_postSwap  = new Vector2(_dbPostSwap.x, _dbPostSwap.z);
            dbPipe_afterDz   = new Vector2(_dbAfterDz.x,  _dbAfterDz.z);
            dbPipe_target    = new Vector2(targetOffset.x, targetOffset.z);
            dbPipe_emaAlpha  = _dbEmaAlpha;
            dbPipe_idleRatio = _dbIdleRatio;
        }

        Vector3 db = filteredAcceleration - calibratedAcceleration;
        levelAxisX = db.x;
        levelAxisY = db.y;
        rollDeg    = Mathf.Atan2(db.x, db.z) * Mathf.Rad2Deg;
        pitchDeg   = Mathf.Atan2(db.y, db.z) * Mathf.Rad2Deg;

        // ── 平放模式統計 & 定時 Console 輸出 ──
        if (phoneIsFlat && hasCalibrated)
        {
            if (!_statEntrySet)
            {
                _statTareEntry = dbPipe_tare;
                _statEntrySet  = true;
            }
            _statFlatTime  += Time.deltaTime;
            _statMaxIdle    = Mathf.Max(_statMaxIdle, _dbIdleRatio);
            _statMaxTarget  = new Vector2(
                Mathf.Max(_statMaxTarget.x, Mathf.Abs(targetOffset.x)),
                Mathf.Max(_statMaxTarget.y, Mathf.Abs(targetOffset.z)));
            float jitter    = Vector3.Distance(transform.localPosition, _statPrevPos);
            _statMaxJitter  = Mathf.Max(_statMaxJitter, jitter);
            _statJitterSum += jitter;
            _statJitterCount++;

            if (pipeLogInterval > 0f)
            {
                _pipeLogTimer += Time.deltaTime;
                if (_pipeLogTimer >= pipeLogInterval)
                {
                    _pipeLogTimer = 0f;
                    Debug.Log(
                        $"[FlatPipe {_statFlatTime:F1}s]" +
                        $"  raw({dbPipe_raw.x:+0.00;-0.00},{dbPipe_raw.y:+0.00;-0.00})" +
                        $"  tare({dbPipe_tare.x:+0.00;-0.00},{dbPipe_tare.y:+0.00;-0.00})" +
                        $"  pre({dbPipe_preSwap.x:+0.00;-0.00},{dbPipe_preSwap.y:+0.00;-0.00})" +
                        $"  post({dbPipe_postSwap.x:+0.00;-0.00},{dbPipe_postSwap.y:+0.00;-0.00})" +
                        $"  dz({dbPipe_afterDz.x:+0.00;-0.00},{dbPipe_afterDz.y:+0.00;-0.00})" +
                        $"  tgt({dbPipe_target.x:+0.00;-0.00},{dbPipe_target.y:+0.00;-0.00})" +
                        $"  scaled_before({debugScaledBeforeFilter.x:+0.00;-0.00},{debugScaledBeforeFilter.z:+0.00;-0.00})" +
                        $"  scaled_after({debugScaledAfterFilter.x:+0.00;-0.00},{debugScaledAfterFilter.z:+0.00;-0.00})" +
                        $"  minStep({debugMinOutputStep.x:F2},{debugMinOutputStep.z:F2})" +
                        $"  idle={_dbIdleRatio:F2}");
                }
            }
        }
        else if (!phoneIsFlat)
        {
            _statEntrySet = false;
            _pipeLogTimer = 0f;
        }
        _statPrevPos = transform.localPosition;
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
            deadzoned.x * s.movementAxesMask.x * s.sensitivity.x,
            deadzoned.y * s.movementAxesMask.y * s.sensitivity.y,
            deadzoned.z * s.movementAxesMask.z * s.sensitivity.z);
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
        // Tare：以當前 filteredAcceleration 為新的「靜止基準」
        // 下一幀 debiased = filteredAcceleration - calibratedAcceleration ≈ 0
        calibratedAcceleration = filteredAcceleration;

        // 原點鎖定：以「校正當下的實際位置」為新中心
        // 之後物件只在 maxOffsetPerAxis 範圍內移動，不會再累加漂移
        centerLocalPosition = centerPoint != null
            ? (transform.parent != null
                ? transform.parent.InverseTransformPoint(centerPoint.position)
                : centerPoint.position)
            : transform.localPosition;

        hasCalibrated            = true;
        currentOffset            = Vector3.zero;
        currentVelocity          = Vector3.zero;
        targetOffset             = Vector3.zero;
        modeSwitchTransitionProgress = 1f;
        modeSwitchTransitionOffset   = Vector3.zero;
        smoothedPosition             = centerLocalPosition;
        switchTimer                  = -1f;
        switchLoggedComplete         = false;
        debugMaxFrameJump            = 0f;
        debugTransitionRemaining     = 0f;
        debugTimeSinceLastSwitch     = -1f;
        calibrationMsgPosition = centerLocalPosition;
        calibrationMsgTimer    = 3f;
        Debug.Log($"[AccelerometerBallEffect] 已校正 | Tare 基準: {calibratedAcceleration:F3} | 原點鎖定: {centerLocalPosition}");
    }

    // ══════════════════════════════════════════════════════════════
    //  自動校正嚮導
    // ══════════════════════════════════════════════════════════════

    public void StartWizard()
    {
        wizardPhase          = WizardPhase.UprightBaseline;
        wizardTimer          = 0f;
        wizardRetryCount     = 0;
        wizardPendingConfirm = false;
        wizardLastStepResult = "";
        wizardSamples.Clear();
        pendingHasFlatResults = false;
        // 以現有 settings 初始化暫存值（部分 phase 跳過時保留原值）
        pendingUprightFlip          = uprightSettings.axisFlip;
        pendingUprightDeadzone      = uprightSettings.axisDeadzone;
        pendingUprightSensitivity   = uprightSettings.sensitivity;
        pendingUprightSwapXZ        = false;
        pendingUprightMinOutputStep = Vector3.zero;
        pendingFlatFlip             = flatSettings.axisFlip;
        pendingFlatDeadzone         = flatSettings.axisDeadzone;
        pendingFlatSensitivity      = flatSettings.sensitivity;
        pendingFlatSwapXZ           = false;
        pendingFlatMinOutputStep    = Vector3.zero;
        // maxOffsetPerAxis 和 axisScale 由使用者自行設定，嚮導不修改
        wizardUprightFlip         = Vector3.one;
        wizardFlatFlip            = Vector3.one;
        wizardReadyToCollect      = false; // 第一步先看說明
        wizardPeakZ               = 0f;
        wizardMinPeakMagnitude    = float.MaxValue;
        wizardCurrentStrokeMax    = 0f;
        wizardInStroke            = false;
        wizardUprightMaxSignal    = 0f;
        wizardFlatMaxSignal       = 0f;
        wizardMinPeakDisplay      = 0f;
        wizardMaxPeakDisplay      = 0f;
        wizardStatusText          = GetPhaseDetail();
    }

    private void UpdateWizard()
    {
        if (wizardPendingConfirm) return;
        if (wizardPhase == WizardPhase.Idle || wizardPhase == WizardPhase.Done) return;

        // FlatTransition：自動等待平放偵測，不需要使用者按鍵
        if (wizardPhase == WizardPhase.FlatTransition)
        {
            wizardTimer += Time.deltaTime;
            if (phoneIsFlat)
            {
                AdvanceWizardPhase();
            }
            else if (wizardTimer >= 10f)
            {
                pendingHasFlatResults = false;
                wizardPendingConfirm  = true;
                wizardStatusText      = "平放逾時，僅套用直立模式校正\n請按「確認套用」";
            }
            return;
        }

        // 等待使用者按「準備好了」
        if (!wizardReadyToCollect) return;

        // 採樣
        wizardSamples.Add(filteredAcceleration);
        wizardTimer += Time.deltaTime;
        // PushForward phase：記錄 Z 軸峰值（符號 = 方向）
        bool isPushFwd = wizardPhase == WizardPhase.UprightForward || wizardPhase == WizardPhase.FlatForward;
        if (isPushFwd)
        {
            if (Mathf.Abs(filteredAcceleration.z) > Mathf.Abs(wizardPeakZ))
                wizardPeakZ = filteredAcceleration.z;

            bool isUprightFwd = (wizardPhase == WizardPhase.UprightForward);
            bool swapXZFwd    = isUprightFwd ? pendingUprightSwapXZ : pendingFlatSwapXZ;
            Vector3 blFwd     = isUprightFwd ? wizardUprightBaseline : wizardFlatBaseline;
            Vector3 dzFwd     = isUprightFwd ? pendingUprightDeadzone : pendingFlatDeadzone;
            float noiseThr    = swapXZFwd ? dzFwd.x : dzFwd.z;
            float trackSig    = Mathf.Abs(swapXZFwd
                ? filteredAcceleration.x - blFwd.x
                : filteredAcceleration.z - blFwd.z);

            // 偵測衝程（超過一半死區就算進入）
            if (trackSig > noiseThr * 0.5f)
            {
                if (!wizardInStroke) { wizardInStroke = true; wizardCurrentStrokeMax = 0f; }
                wizardCurrentStrokeMax = Mathf.Max(wizardCurrentStrokeMax, trackSig);
            }
            else if (wizardInStroke)
            {
                // 衝程結束：超過死區的才算有意義
                if (wizardCurrentStrokeMax > noiseThr)
                    wizardMinPeakMagnitude = Mathf.Min(wizardMinPeakMagnitude, wizardCurrentStrokeMax);
                wizardInStroke = false;
            }

            wizardMaxPeakDisplay = Mathf.Max(wizardMaxPeakDisplay, trackSig);
            wizardMinPeakDisplay = wizardMinPeakMagnitude < float.MaxValue * 0.5f ? wizardMinPeakMagnitude : 0f;
        }

        if (wizardTimer >= wizardCollectDuration)
            ProcessPhaseComplete();
    }

    private void AdvanceWizardPhase()
    {
        wizardSamples.Clear();
        wizardTimer      = 0f;
        wizardRetryCount = 0;
        wizardPhase      = (WizardPhase)((int)wizardPhase + 1);
        // FlatTransition 與 Done 不需要使用者按鍵；其餘 phase 顯示說明等待確認
        wizardReadyToCollect = (wizardPhase == WizardPhase.FlatTransition || wizardPhase == WizardPhase.Done);
        wizardPeakZ            = 0f;
        wizardMinPeakMagnitude = float.MaxValue;
        wizardCurrentStrokeMax = 0f;
        wizardInStroke         = false;
        wizardMaxPeakDisplay   = 0f;
        wizardMinPeakDisplay   = 0f;
        wizardStatusText       = GetPhaseDetail();
    }

    private void ProcessPhaseComplete()
    {
        ComputeMeanAndStd(wizardSamples, out Vector3 mean, out Vector3 std);
        wizardSamples.Clear();
        wizardTimer = 0f;

        switch (wizardPhase)
        {
            // ── Baseline：tare + deadzone ──────────────────────────
            case WizardPhase.UprightBaseline:
            case WizardPhase.FlatBaseline:
            {
                bool isUpright = (wizardPhase == WizardPhase.UprightBaseline);
                if ((std.x > 1f || std.z > 1f) && wizardRetryCount < 3)
                {
                    wizardRetryCount++;
                    wizardReadyToCollect = false;
                    wizardStatusText = $"手機未靜止，請重試 ({wizardRetryCount}/3)";
                    return;
                }
                Vector3 dz = new Vector3(
                    Mathf.Clamp(3f * std.x, 0.1f, 1f),
                    Mathf.Clamp(3f * std.y, 0.1f, 1f),
                    Mathf.Clamp(3f * std.z, 0.1f, 1f));
                if (isUpright) { wizardUprightBaseline  = mean; pendingUprightDeadzone = dz; }
                else           { wizardFlatBaseline     = mean; pendingFlatDeadzone    = dz; }
                wizardLastStepResult = $"死區偵測完成 ({dz.x:F2}, {dz.y:F2}, {dz.z:F2})";
                wizardRetryCount = 0;
                AdvanceWizardPhase();
                break;
            }

            // ── MaxGesture：axisFlip.x + swapXZ + 自動計算 axisScale / minOutputStep ──
            case WizardPhase.UprightMaxGesture:
            case WizardPhase.FlatMaxGesture:
            {
                bool isUpright = wizardPhase == WizardPhase.UprightMaxGesture;
                Vector3 baseline = isUpright ? wizardUprightBaseline : wizardFlatBaseline;
                Vector3 debiased = mean - baseline;
                float mx = Mathf.Abs(debiased.x), mz = Mathf.Abs(debiased.z);

                if (Mathf.Abs(mx - mz) < 0.3f && wizardRetryCount < 3)
                {
                    wizardRetryCount++;
                    wizardReadyToCollect = false;
                    wizardStatusText = $"動作不夠明顯，請加大幅度後重試 ({wizardRetryCount}/3)";
                    return;
                }
                bool swapXZ   = mz > mx;
                float domMean = swapXZ ? debiased.z : debiased.x;
                float flipX   = Mathf.Sign(domMean);
                float maxSig  = Mathf.Abs(domMean);

                if (isUpright)
                {
                    pendingUprightSwapXZ   = swapXZ;
                    pendingUprightFlip.x   = flipX;
                    wizardUprightMaxSignal = maxSig;
                    wizardUprightFlip      = new Vector3(flipX, pendingUprightFlip.y, pendingUprightFlip.z);
                }
                else
                {
                    pendingFlatSwapXZ   = swapXZ;
                    pendingFlatFlip.x   = flipX;
                    wizardFlatMaxSignal = maxSig;
                    wizardFlatFlip      = new Vector3(flipX, pendingFlatFlip.y, pendingFlatFlip.z);
                }
                // swapXZ 後 X slot 存的是原 Z 訊號（噪聲特性不同），交換死區避免跨軸汙染
                if (swapXZ)
                {
                    if (isUpright) pendingUprightDeadzone = new Vector3(pendingUprightDeadzone.z, pendingUprightDeadzone.y, pendingUprightDeadzone.x);
                    else           pendingFlatDeadzone    = new Vector3(pendingFlatDeadzone.z,    pendingFlatDeadzone.y,    pendingFlatDeadzone.x);
                }

                // ── 自動計算 sensitivity.x：使最大動作恰好到 maxOffsetPerAxis.x ──
                float dzX        = isUpright ? pendingUprightDeadzone.x : pendingFlatDeadzone.x;
                float usableX    = Mathf.Max(maxSig - dzX, 0.05f);
                float maxOff     = isUpright ? uprightSettings.maxOffsetPerAxis.x : flatSettings.maxOffsetPerAxis.x;
                float sensX      = maxOff / usableX;
                float minStepX   = (dzX / 3f) * sensX;

                if (isUpright)
                {
                    pendingUprightSensitivity.x    = sensX;
                    pendingUprightMinOutputStep.x  = minStepX;
                }
                else
                {
                    pendingFlatSensitivity.x    = sensX;
                    pendingFlatMinOutputStep.x  = minStepX;
                }

                wizardRetryCount     = 0;
                wizardLastStepResult = $"X{(flipX > 0 ? "正向" : "翻轉")}{(swapXZ ? " | 交換XZ" : "")} | 最大訊號={maxSig:F2} 死區={dzX:F2} → sensitivity.x={sensX:F3}";
                Debug.Log($"[嚮導 MaxGesture {(isUpright ? "直立" : "平放")}]\n" +
                          $"  最大訊號={maxSig:F3}  死區={dzX:F3}  可用範圍={usableX:F3}\n" +
                          $"  maxOffsetPerAxis.x={maxOff:F3}\n" +
                          $"  → sensitivity.x={sensX:F3}  minOutputStep.x={minStepX:F3}\n" +
                          $"  驗算：{usableX:F3} × {sensX:F3} = {usableX * sensX:F3}（應≈{maxOff:F1}）\n" +
                          $"  傾斜角度：{debugTiltAngleDeg.x:F1}°");
                AdvanceWizardPhase();
                break;
            }

            // ── Forward：axisFlip.z + 自動計算 Z axisScale / minOutputStep ──
            case WizardPhase.UprightForward:
            case WizardPhase.FlatForward:
            {
                bool isUpright   = wizardPhase == WizardPhase.UprightForward;
                Vector3 baseline = isUpright ? wizardUprightBaseline : wizardFlatBaseline;
                bool swapXZ      = isUpright ? pendingUprightSwapXZ  : pendingFlatSwapXZ;
                Vector3 dz       = isUpright ? pendingUprightDeadzone : pendingFlatDeadzone;
                // Z 軸用線性加速度（瞬時量），以峰值符號判斷方向，不用平均值
                // swapXZ 時 output_z = pre_x，所以 peak 應從 pre_x 讀
                float peakSignal = swapXZ ? (mean - baseline).x : wizardPeakZ;

                if (Mathf.Abs(peakSignal) < 0.3f && wizardRetryCount < 3)
                {
                    wizardRetryCount++;
                    wizardReadyToCollect   = false;
                    wizardPeakZ            = 0f;
                    wizardMinPeakMagnitude = float.MaxValue;
                    wizardCurrentStrokeMax = 0f;
                    wizardInStroke         = false;
                    wizardMaxPeakDisplay   = 0f;
                    wizardMinPeakDisplay   = 0f;
                    wizardStatusText = $"訊號太弱，請移動幅度大一點後重試 ({wizardRetryCount}/3)";
                    return;
                }
                float flipZ = (Mathf.Abs(peakSignal) < 0.05f) ? 1f : Mathf.Sign(peakSignal);

                // ── 收尾最後一次衝程（採樣結束時若仍在進行中）──
                float noiseThr = swapXZ ? dz.x : dz.z;
                if (wizardInStroke && wizardCurrentStrokeMax > noiseThr)
                    wizardMinPeakMagnitude = Mathf.Min(wizardMinPeakMagnitude, wizardCurrentStrokeMax);
                wizardInStroke = false;

                float maxDebiasedPeak = swapXZ
                    ? Mathf.Abs((mean - baseline).x)
                    : Mathf.Abs(wizardPeakZ - baseline.z);

                // ── 最小幅度 → 精修 deadzone.z ──
                // 把 deadzone.z 設為最小有意義動作的 40%，確保小動作能通過但靜止噪聲被過濾
                if (wizardMinPeakMagnitude < float.MaxValue * 0.5f && wizardMinPeakMagnitude > noiseThr * 0.5f)
                {
                    float noiseFloor = noiseThr / 3f;
                    float refinedDz  = Mathf.Clamp(wizardMinPeakMagnitude * 0.4f, noiseFloor, wizardMinPeakMagnitude * 0.7f);
                    if (isUpright) pendingUprightDeadzone.z = refinedDz;
                    else           pendingFlatDeadzone.z    = refinedDz;
                    noiseThr = refinedDz; // 後續計算使用精修後的死區
                }

                // ── 自動計算 sensitivity.z：使校正推動幅度恰好到 maxOffsetPerAxis.z ──
                float maxOffZ  = isUpright ? uprightSettings.maxOffsetPerAxis.z : flatSettings.maxOffsetPerAxis.z;
                float zRef     = Mathf.Max(maxDebiasedPeak, noiseThr * 2f);
                float usableZ  = Mathf.Max(zRef - noiseThr, 0.05f);
                float sensZ    = maxOffZ / usableZ;
                float minStepZ = (noiseThr / 3f) * sensZ;

                if (isUpright)
                {
                    pendingUprightSensitivity.z   = sensZ;
                    pendingUprightMinOutputStep.z = minStepZ;
                }
                else
                {
                    pendingFlatSensitivity.z   = sensZ;
                    pendingFlatMinOutputStep.z = minStepZ;
                }

                wizardMinPeakDisplay = wizardMinPeakMagnitude < float.MaxValue * 0.5f ? wizardMinPeakMagnitude : 0f;
                wizardMaxPeakDisplay = maxDebiasedPeak;

                Debug.Log($"[嚮導 Forward {(isUpright ? "直立" : "平放")}]\n" +
                          $"  最大幅={maxDebiasedPeak:F3}  死區={noiseThr:F3}  可用範圍={usableZ:F3}\n" +
                          $"  maxOffsetPerAxis.z={maxOffZ:F3}\n" +
                          $"  → sensitivity.z={sensZ:F3}  minOutputStep.z={minStepZ:F3}\n" +
                          $"  驗算：{usableZ:F3} × {sensZ:F3} = {usableZ * sensZ:F3}（應≈{maxOffZ:F1}）");
                if (isUpright)
                {
                    pendingUprightFlip.z = flipZ;
                    wizardUprightFlip    = pendingUprightFlip;
                    wizardRetryCount     = 0;
                    wizardLastStepResult = $"Z{(flipZ > 0 ? "正向" : "翻轉")} | 最大幅={maxDebiasedPeak:F2} → sensitivity.z={sensZ:F3} 最小步進={minStepZ:F2}";
                    AdvanceWizardPhase(); // → FlatTransition
                }
                else
                {
                    pendingFlatFlip.z     = flipZ;
                    wizardFlatFlip        = pendingFlatFlip;
                    pendingHasFlatResults = true;
                    wizardRetryCount      = 0;
                    wizardLastStepResult  = $"Z{(flipZ > 0 ? "正向" : "翻轉")} | 最大幅={maxDebiasedPeak:F2} → sensitivity.z={sensZ:F3} 最小步進={minStepZ:F2}";
                    wizardStatusText      = "嚮導完成！\n請確認後按「確認套用」";
                    wizardPendingConfirm  = true;
                }
                break;
            }
        }
    }

    private void ApplyWizardResults()
    {
        // 嚮導只寫入它負責計算的欄位；maxOffsetPerAxis 和 axisScale 保留使用者設定值
        uprightSettings.axisFlip      = pendingUprightFlip;
        uprightSettings.axisDeadzone  = pendingUprightDeadzone;
        uprightSettings.sensitivity   = pendingUprightSensitivity;
        uprightSettings.swapXZ        = pendingUprightSwapXZ;
        uprightSettings.minOutputStep = pendingUprightMinOutputStep;

        if (pendingHasFlatResults)
        {
            flatSettings.axisFlip      = pendingFlatFlip;
            flatSettings.axisDeadzone  = pendingFlatDeadzone;
            flatSettings.sensitivity   = pendingFlatSensitivity;
            flatSettings.swapXZ        = pendingFlatSwapXZ;
            flatSettings.minOutputStep = pendingFlatMinOutputStep;
        }

        Recalibrate(); // 以當前姿勢鎖定原點（使用者已靜止）
        wizardPendingConfirm = false;
        wizardPhase          = WizardPhase.Done;
        wizardTimer          = 0f;
        wizardStatusText     = "校正已套用！";
        Debug.Log($"[嚮導校正] 完成\n" +
                  $"  直立 flip={pendingUprightFlip} dz={pendingUprightDeadzone} sens=({pendingUprightSensitivity.x:F3},{pendingUprightSensitivity.y:F3},{pendingUprightSensitivity.z:F3}) swapXZ={pendingUprightSwapXZ} minStep={pendingUprightMinOutputStep}\n" +
                  $"  直立 maxOff={uprightSettings.maxOffsetPerAxis} axisScale={uprightSettings.axisScale} (使用者設定，未修改)\n" +
                  $"  平放 flip={pendingFlatFlip} dz={pendingFlatDeadzone} sens=({pendingFlatSensitivity.x:F3},{pendingFlatSensitivity.y:F3},{pendingFlatSensitivity.z:F3}) swapXZ={pendingFlatSwapXZ} minStep={pendingFlatMinOutputStep}\n" +
                  $"  平放 maxOff={flatSettings.maxOffsetPerAxis} axisScale={flatSettings.axisScale} (使用者設定，未修改) (有結果={pendingHasFlatResults})");
    }

    private void OnDrawGizmos()
    {
        if (!showAxisGizmos) return;
        Vector3 pos = transform.position;
        Gizmos.color = new Color(1f, 0.2f, 0.2f);
        Gizmos.DrawRay(pos, Vector3.right   * axisGizmoLength);
        Gizmos.color = new Color(0.2f, 1f, 0.2f);
        Gizmos.DrawRay(pos, Vector3.up      * axisGizmoLength);
        Gizmos.color = new Color(0.2f, 0.5f, 1f);
        Gizmos.DrawRay(pos, Vector3.forward * axisGizmoLength);
    }

    private void DrawOffsetHUD()
    {
        ModeSettings s   = phoneIsFlat ? flatSettings : uprightSettings;
        Vector3 maxOff   = s.maxOffsetPerAxis;
        float hx = hudPosition.x, hy = hudPosition.y;
        float sq = hudSize;
        float barW  = 18f;
        float totalW = sq + 4f + barW;
        float totalH = sq + 36f;

        var txtStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, normal = { textColor = Color.white } };
        var smlStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, normal = { textColor = new Color(0.75f, 1f, 0.75f) } };

        // 背景
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.Box(new Rect(hx - 4f, hy - 4f, totalW + 8f, totalH + 8f), GUIContent.none);
        GUI.color = Color.white;

        // 標題：模式 + 靈敏度
        GUI.Label(new Rect(hx, hy, totalW, 16f),
            $"[{(phoneIsFlat ? "平放" : "直立")}]  sens=({s.sensitivity.x:F2},{s.sensitivity.z:F2})", txtStyle);
        hy += 18f;

        // ── 2D 方格（X 橫軸, Z 縱軸）──
        GUI.color = new Color(0.12f, 0.12f, 0.12f);
        GUI.Box(new Rect(hx, hy, sq, sq), GUIContent.none);

        float cx = hx + sq * 0.5f, cy = hy + sq * 0.5f;

        // 中心十字線（灰）
        GUI.color = new Color(0.4f, 0.4f, 0.4f);
        GUI.Box(new Rect(hx,       cy - 1f, sq,  2f), GUIContent.none);
        GUI.Box(new Rect(cx - 1f,  hy,       2f, sq), GUIContent.none);

        // 邊界（移動極限：藍色）
        GUI.color = new Color(0.35f, 0.45f, 0.9f);
        GUI.Box(new Rect(hx,          hy,          sq,  2f), GUIContent.none);
        GUI.Box(new Rect(hx,          hy + sq - 2f, sq,  2f), GUIContent.none);
        GUI.Box(new Rect(hx,          hy,           2f, sq), GUIContent.none);
        GUI.Box(new Rect(hx + sq - 2f, hy,           2f, sq), GUIContent.none);

        // 當前位置圓點（紅）
        float normX = maxOff.x > 0.01f ? Mathf.Clamp(currentOffset.x / maxOff.x, -1f, 1f) : 0f;
        float normZ = maxOff.z > 0.01f ? Mathf.Clamp(currentOffset.z / maxOff.z, -1f, 1f) : 0f;
        float dotX = cx + normX * sq * 0.5f;
        float dotZ = cy + normZ * sq * 0.5f;
        GUI.color = new Color(1f, 0.15f, 0.15f);
        GUI.Box(new Rect(dotX - 6f, dotZ - 6f, 12f, 12f), GUIContent.none);
        GUI.color = Color.white;
        GUI.Box(new Rect(dotX - 2f, dotZ - 2f, 4f,  4f),  GUIContent.none);

        // ── Y 直條 ──
        float bx = hx + sq + 4f;
        GUI.color = new Color(0.12f, 0.12f, 0.12f);
        GUI.Box(new Rect(bx, hy, barW, sq), GUIContent.none);
        GUI.color = new Color(0.4f, 0.4f, 0.4f);
        GUI.Box(new Rect(bx, cy - 1f, barW, 2f), GUIContent.none);

        float normY = maxOff.y > 0.01f ? Mathf.Clamp(currentOffset.y / maxOff.y, -1f, 1f) : 0f;
        float bh    = Mathf.Max(Mathf.Abs(normY) * sq * 0.5f, 2f);
        GUI.color = new Color(1f, 0.85f, 0.2f);
        GUI.Box(normY >= 0f
            ? new Rect(bx + 2f, cy - bh, barW - 4f, bh)
            : new Rect(bx + 2f, cy,       barW - 4f, bh),
            GUIContent.none);
        GUI.color = Color.white;

        // ── 數值文字（視覺偏移 = currentOffset × axisScale）──
        hy += sq + 2f;
        float vx = currentOffset.x * s.axisScale.x;
        float vy = currentOffset.y * s.axisScale.y;
        float vz = currentOffset.z * s.axisScale.z;
        float emx = maxOff.x * s.axisScale.x;
        float emy = maxOff.y * s.axisScale.y;
        float emz = maxOff.z * s.axisScale.z;
        GUI.Label(new Rect(hx, hy, totalW, 16f),
            $"X={vx:+0.00;-0.00}  Z={vz:+0.00;-0.00}", smlStyle);
        hy += 15f;
        GUI.Label(new Rect(hx, hy, totalW, 16f),
            $"Y={vy:+0.00;-0.00}  ±({emx:F1},{emy:F1},{emz:F1})", smlStyle);
    }

    private static void ComputeMeanAndStd(List<Vector3> samples, out Vector3 mean, out Vector3 std)
    {
        if (samples.Count == 0) { mean = std = Vector3.zero; return; }
        mean = Vector3.zero;
        foreach (var v in samples) mean += v;
        mean /= samples.Count;
        Vector3 variance = Vector3.zero;
        foreach (var v in samples)
        {
            Vector3 d = v - mean;
            variance += new Vector3(d.x * d.x, d.y * d.y, d.z * d.z);
        }
        variance /= samples.Count;
        std = new Vector3(Mathf.Sqrt(variance.x), Mathf.Sqrt(variance.y), Mathf.Sqrt(variance.z));
    }
}