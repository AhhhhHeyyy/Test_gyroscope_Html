using UnityEngine;
using System.Collections.Generic;

public partial class AccelerometerBallEffect
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

    [Header("自動校正嚮導")]
    [Tooltip("每個收集階段的持續時間（秒）")]
    [SerializeField] [Range(1f, 5f)] private float wizardCollectDuration = 2.5f;
    [HideInInspector] [Tooltip("（唯讀）目前傾斜角度：X=左右傾斜、Z=前後傾斜（度）。校正時觀察舒適角度用")]
    [SerializeField] private Vector2 debugTiltAngleDeg = Vector2.zero;
    [Tooltip("是否在 Game 視窗顯示嚮導按鈕")]
    [SerializeField] private bool showWizardButton = true;
    [HideInInspector] [Tooltip("（唯讀）目前嚮導狀態文字")]
    [SerializeField] private string wizardStatusText = "等待啟動";
    [HideInInspector] [Tooltip("（唯讀）嚮導偵測到的直立模式 axisFlip 結果")]
    [SerializeField] private Vector3 wizardUprightFlip = Vector3.one;
    [HideInInspector] [Tooltip("（唯讀）嚮導偵測到的平放模式 axisFlip 結果")]
    [SerializeField] private Vector3 wizardFlatFlip = Vector3.one;
    [HideInInspector] [Tooltip("（唯讀）PushForward 偵測到的最小有意義動作幅度（Z 軸，已去基線，m/s²）")]
    [SerializeField] private float wizardMinPeakDisplay = 0f;
    [HideInInspector] [Tooltip("（唯讀）PushForward 偵測到的最大動作幅度（Z 軸，已去基線，m/s²）")]
    [SerializeField] private float wizardMaxPeakDisplay = 0f;

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
    private bool    phaseCompletePending   = false; // 延遲一幀執行 ProcessPhaseComplete，讓進度條先顯示 100%
    // 暫存結果，確認後才寫入 settings
    private Vector3 pendingUprightFlip          = Vector3.one;
    private Vector3 pendingUprightDeadzone      = new Vector3(0.3f, 0.3f, 0.3f);
    private Vector3 pendingUprightSensitivity   = new Vector3(0.3f, 0.3f, 0.3f);
    private bool    pendingUprightSwapXZ        = false;
    private Vector3 pendingUprightMinOutputStep = Vector3.zero;
    private Vector3 pendingFlatFlip             = Vector3.one;
    private Vector3 pendingFlatDeadzone         = new Vector3(0.2f, 0.2f, 0.2f);
    private Vector3 pendingFlatSensitivity      = new Vector3(0.3f, 0.3f, 0.3f);
    private bool    pendingFlatSwapXZ           = false;
    private Vector3 pendingFlatMinOutputStep    = Vector3.zero;
    private bool    pendingHasFlatResults       = false;

    // ══════════════════════════════════════════════════════════════
    //  自動校正嚮導
    // ══════════════════════════════════════════════════════════════

    public void StartWizard()
    {
        wizardPhase          = WizardPhase.UprightBaseline;
        wizardTimer          = 0f;
        wizardRetryCount     = 0;
        wizardPendingConfirm = false;
        phaseCompletePending = false;
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

        if (!phaseCompletePending && wizardTimer >= wizardCollectDuration)
        {
            wizardTimer = wizardCollectDuration; // 鎖在 100%，讓本幀 OnGUI 顯示滿格
            phaseCompletePending = true;
        }
        else if (phaseCompletePending)
        {
            phaseCompletePending = false;
            ProcessPhaseComplete();
        }
    }

    private void AdvanceWizardPhase()
    {
        wizardSamples.Clear();
        wizardTimer          = 0f;
        wizardRetryCount     = 0;
        phaseCompletePending = false;
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
                    Mathf.Clamp(3f * std.y, 0.1f, 0.5f), // Y (gDevice.z) 噪聲不應超過 0.5
                    Mathf.Clamp(3f * std.z, 0.1f, 0.5f)); // Z (線性加速基線) 噪聲不應超過 0.5
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

                // 角度驗證：傾斜角應在 15°~45° 之間（建議 ~30°）
                float domSig   = Mathf.Max(mx, mz);
                float tiltDeg  = Mathf.Asin(Mathf.Clamp(domSig / 9.81f, -1f, 1f)) * Mathf.Rad2Deg;
                if ((tiltDeg < 15f || tiltDeg > 45f) && wizardRetryCount < 3)
                {
                    wizardRetryCount++;
                    wizardReadyToCollect = false;
                    string angleHint = tiltDeg < 15f
                        ? $"傾斜角太小（{tiltDeg:F1}°），請傾斜到 15°～45° 再試"
                        : $"傾斜角太大（{tiltDeg:F1}°），請輕輕傾斜到 15°～45° 再試";
                    wizardStatusText = $"{angleHint} ({wizardRetryCount}/3)";
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
                float sensY_mg = isUpright ? pendingUprightSensitivity.y : pendingFlatSensitivity.y;
                Debug.Log($"[嚮導 MaxGesture {(isUpright ? "直立" : "平放")}]\n" +
                          $"  最大訊號={maxSig:F3}  死區={dzX:F3}  可用範圍={usableX:F3}\n" +
                          $"  maxOffsetPerAxis.x={maxOff:F3}\n" +
                          $"  → sensitivity.x={sensX:F3}  minOutputStep.x={minStepX:F3}\n" +
                          $"  sensitivity.y={sensY_mg:F3}（保留設定值，嚮導未計算）\n" +
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
                    float refinedDz  = Mathf.Clamp(wizardMinPeakMagnitude * 0.3f, noiseFloor, wizardMinPeakMagnitude * 0.5f);
                    refinedDz = Mathf.Min(refinedDz, 0.7f); // 上限 0.7m/s²，避免死區過大導致 Z 軸移動不明顯
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
        // 套用前快照，供後續比對 log 使用
        Vector3 oldUSens   = uprightSettings.sensitivity;
        Vector3 oldUDz     = uprightSettings.axisDeadzone;
        Vector3 oldUFlip   = uprightSettings.axisFlip;
        Vector3 oldUStep   = uprightSettings.minOutputStep;
        bool    oldUSwap   = uprightSettings.swapXZ;
        Vector3 oldFSens   = flatSettings.sensitivity;
        Vector3 oldFDz     = flatSettings.axisDeadzone;
        Vector3 oldFFlip   = flatSettings.axisFlip;
        Vector3 oldFStep   = flatSettings.minOutputStep;
        bool    oldFSwap   = flatSettings.swapXZ;

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

        // 用 Baseline 採樣均值當 Tare，而非嚮導結束時的當前姿勢。
        // 根本原因：Forward 步驟結束後手機角度偏離自然靜止角，以當下 filteredAcceleration
        // 為 Tare 會造成後續「靜置 → debiased 持續非零 → 球永遠偏移」。
        // Baseline 階段是唯一要求使用者靜止採樣的時機，均值才代表真正的靜止參考角。
        filteredAcceleration = phoneIsFlat ? wizardFlatBaseline : wizardUprightBaseline;
        Recalibrate(); // filteredAcceleration 已替換為 Baseline 均值，Tare 記錄正確角度
        // 同時寫入另一個模式的 savedTare，切換時兩邊都有正確參考
        if (phoneIsFlat) savedTareUpright = wizardUprightBaseline;
        else             savedTareFlat    = pendingHasFlatResults ? wizardFlatBaseline : savedTareFlat;
        wizardPendingConfirm = false;
        wizardPhase          = WizardPhase.Done;
        wizardTimer          = 0f;
        wizardStatusText     = "校正已套用！";
        Debug.Log($"[嚮導校正] 完成\n" +
                  $"  直立 flip={pendingUprightFlip} dz={pendingUprightDeadzone} sens=({pendingUprightSensitivity.x:F3},{pendingUprightSensitivity.y:F3},{pendingUprightSensitivity.z:F3}) swapXZ={pendingUprightSwapXZ} minStep={pendingUprightMinOutputStep}\n" +
                  $"  直立 maxOff={uprightSettings.maxOffsetPerAxis} axisScale={uprightSettings.axisScale} (使用者設定，未修改)\n" +
                  $"  平放 flip={pendingFlatFlip} dz={pendingFlatDeadzone} sens=({pendingFlatSensitivity.x:F3},{pendingFlatSensitivity.y:F3},{pendingFlatSensitivity.z:F3}) swapXZ={pendingFlatSwapXZ} minStep={pendingFlatMinOutputStep}\n" +
                  $"  平放 maxOff={flatSettings.maxOffsetPerAxis} axisScale={flatSettings.axisScale} (使用者設定，未修改) (有結果={pendingHasFlatResults})\n" +
                  $"── 套用前後比對 ──\n" +
                  $"  [直立] sens  : ({oldUSens.x:F3},{oldUSens.y:F3},{oldUSens.z:F3}) → ({pendingUprightSensitivity.x:F3},{pendingUprightSensitivity.y:F3},{pendingUprightSensitivity.z:F3})\n" +
                  $"  [直立] dz    : ({oldUDz.x:F3},{oldUDz.y:F3},{oldUDz.z:F3}) → ({pendingUprightDeadzone.x:F3},{pendingUprightDeadzone.y:F3},{pendingUprightDeadzone.z:F3})\n" +
                  $"  [直立] flip  : ({oldUFlip.x:F1},{oldUFlip.y:F1},{oldUFlip.z:F1}) → ({pendingUprightFlip.x:F1},{pendingUprightFlip.y:F1},{pendingUprightFlip.z:F1})\n" +
                  $"  [直立] step  : ({oldUStep.x:F3},{oldUStep.y:F3},{oldUStep.z:F3}) → ({pendingUprightMinOutputStep.x:F3},{pendingUprightMinOutputStep.y:F3},{pendingUprightMinOutputStep.z:F3})\n" +
                  $"  [直立] swapXZ: {oldUSwap} → {pendingUprightSwapXZ}\n" +
                  (pendingHasFlatResults
                      ? $"  [平放] sens  : ({oldFSens.x:F3},{oldFSens.y:F3},{oldFSens.z:F3}) → ({pendingFlatSensitivity.x:F3},{pendingFlatSensitivity.y:F3},{pendingFlatSensitivity.z:F3})\n" +
                        $"  [平放] dz    : ({oldFDz.x:F3},{oldFDz.y:F3},{oldFDz.z:F3}) → ({pendingFlatDeadzone.x:F3},{pendingFlatDeadzone.y:F3},{pendingFlatDeadzone.z:F3})\n" +
                        $"  [平放] flip  : ({oldFFlip.x:F1},{oldFFlip.y:F1},{oldFFlip.z:F1}) → ({pendingFlatFlip.x:F1},{pendingFlatFlip.y:F1},{pendingFlatFlip.z:F1})\n" +
                        $"  [平放] step  : ({oldFStep.x:F3},{oldFStep.y:F3},{oldFStep.z:F3}) → ({pendingFlatMinOutputStep.x:F3},{pendingFlatMinOutputStep.y:F3},{pendingFlatMinOutputStep.z:F3})\n" +
                        $"  [平放] swapXZ: {oldFSwap} → {pendingFlatSwapXZ}"
                      : "  [平放] 跳過（無結果）"));
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
