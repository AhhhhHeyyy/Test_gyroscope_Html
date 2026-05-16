using UnityEngine;

public partial class AccelerometerBallEffect
{
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

    [Header("軸縮放即時調整 HUD（Game 視窗）")]
    [Tooltip("是否在 Game 視窗顯示 axisScale 即時調整面板（嚮導校正後用此微調移動距離）")]
    [SerializeField] private bool showScaleTuner = true;
    [Tooltip("調整面板左上角位置（像素）；預設置於偏移 HUD 右側，避免與校正按鈕重疊")]
    [SerializeField] private Vector2 scaleTunerPosition = new Vector2(140f, 10f);
    [Tooltip("每次按 + / − 的調整幅度；可在面板底部切換")]
    [SerializeField] [Range(0.01f, 1f)] private float scaleTunerStep = 0.1f;

    // 面板手動鎖定的模式（不跟隨手機方向，讓使用者自由選擇要調哪個模式）
    private bool _scaleTunerForFlat = false;

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

        // ── 軸縮放即時調整面板 ──
        if (showScaleTuner && hasCalibrated && wizardPhase == WizardPhase.Idle)
            DrawScaleTuner();

        // ── 嚮導 Overlay ──
        if (wizardPhase == WizardPhase.Idle) return;

        float ox = Screen.width - 340f;
        float oy = 10f;
        float ow = 328f;
        // 高度依狀態動態計算
        bool isFwdPhase        = wizardPhase == WizardPhase.UprightForward || wizardPhase == WizardPhase.FlatForward;
        bool isMaxGesturePhase = wizardPhase == WizardPhase.UprightMaxGesture || wizardPhase == WizardPhase.FlatMaxGesture
                                || wizardPhase == WizardPhase.UprightPitchGesture;
        bool showZCompare = wizardPendingConfirm && pendingHasFlatResults && wizardUprightForwardPeak > 0f;
        float oh = wizardPendingConfirm ? (showZCompare ? 320f : 230f)
                 : !wizardReadyToCollect ? 240f
                 : wizardPhase == WizardPhase.FlatTransition ? 120f
                 : isFwdPhase ? 270f
                 : isMaxGesturePhase ? 230f
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
        GUI.Label(new Rect(ix, iy, iw, 20f), $"自動校正嚮導  ({phaseNum} / 8)", boldStyle); iy += 24f;

        // ── 確認摘要畫面 ──
        if (wizardPendingConfirm)
        {
            GUI.Label(new Rect(ix, iy, iw, 70f), wizardStatusText, baseStyle); iy += 74f;
            if (wizardLastStepResult.Length > 0)
            { GUI.Label(new Rect(ix, iy, iw, 18f), "最後偵測：" + wizardLastStepResult, hintStyle); iy += 22f; }

            // ── Z 軸推力對比（兩模式都完成 Forward 校正時顯示）──
            if (showZCompare)
            {
                iy += 4f;
                float sensRatio = pendingFlatSensitivity.z / Mathf.Max(pendingUprightSensitivity.z, 0.001f);
                bool  balanced  = sensRatio >= 0.6f && sensRatio <= 1.7f;
                var cmpStyle  = new GUIStyle(baseStyle) { fontSize = 12 };
                var okStyle   = new GUIStyle(baseStyle) { fontSize = 11, normal = { textColor = new Color(0.4f, 0.9f, 0.4f) } };
                var warnStyle = new GUIStyle(baseStyle) { fontSize = 11, wordWrap = true, normal = { textColor = new Color(1f, 0.75f, 0.2f) } };

                GUI.Label(new Rect(ix, iy, iw, 16f), "── Z 軸推力對比 ──", cmpStyle); iy += 18f;
                GUI.Label(new Rect(ix, iy, iw, 16f),
                    $"直立  峰值 {wizardUprightForwardPeak:F2} m/s²　sens.z = {pendingUprightSensitivity.z:F3}", cmpStyle); iy += 18f;
                GUI.Label(new Rect(ix, iy, iw, 16f),
                    $"平放  峰值 {wizardFlatForwardPeak:F2} m/s²　sens.z = {pendingFlatSensitivity.z:F3}", cmpStyle); iy += 18f;
                if (balanced)
                {
                    GUI.Label(new Rect(ix, iy, iw, 16f), "✓ 兩模式推力相近，靈敏度平衡", okStyle); iy += 20f;
                }
                else
                {
                    string side = sensRatio > 1.7f ? "平放推力偏小（平放 Z 較靈敏）" : "直立推力偏小（直立 Z 較靈敏）";
                    GUI.Label(new Rect(ix, iy, iw, 30f),
                        $"⚠ {side}\n建議以相近力道重新校正以求一致手感", warnStyle); iy += 34f;
                }
                iy += 2f;
            }

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
        // PitchGesture 量 Y 軸（俯仰），其餘顯示 Z 軸
        if (wizardPhase == WizardPhase.UprightPitchGesture)
            DrawSignalBar(ix, ref iy, iw, "Y 軸", liveRaw.y, baseStyle);
        else
            DrawSignalBar(ix, ref iy, iw, "Z 軸", liveRaw.z, baseStyle);
        // MaxGesture / PitchGesture：傾斜角視覺化；Forward：推動強度視覺化
        bool showPeak = wizardPhase == WizardPhase.UprightForward || wizardPhase == WizardPhase.FlatForward;
        if (wizardPhase == WizardPhase.UprightPitchGesture)
        {
            float pitchAngle = Mathf.Asin(Mathf.Clamp(Mathf.Abs(liveRaw.y) / 9.81f, -1f, 1f)) * Mathf.Rad2Deg;
            DrawTiltGauge(ix, ref iy, iw, baseStyle, pitchAngle);
        }
        else if (isMaxGesturePhase)
            DrawTiltGauge(ix, ref iy, iw, baseStyle, debugTiltAngleDeg.x);
        else if (showPeak)
            DrawPushMeter(ix, ref iy, iw, baseStyle, liveRaw.z);
        else if (wizardLastStepResult.Length > 0)
            GUI.Label(new Rect(ix, iy, iw, 18f), "✓ " + wizardLastStepResult, hintStyle);
    }

    private string GetPhaseArrow() => wizardPhase switch
    {
        WizardPhase.UprightBaseline     => "[ 靜止不動 ]",
        WizardPhase.UprightMaxGesture   => "→  向右傾斜（最大舒適）",
        WizardPhase.UprightPitchGesture => "↑  往前傾斜（最大舒適）",
        WizardPhase.UprightForward      => "↑↓  前後移動",
        WizardPhase.FlatBaseline        => "[ 靜止不動 ]",
        WizardPhase.FlatMaxGesture      => "→  往右傾斜（最大舒適）",
        WizardPhase.FlatForward         => "↑↓  前後移動",
        _                               => ""
    };

    private string GetPhaseDetail() => wizardPhase switch
    {
        WizardPhase.UprightBaseline     => "手機豎直拿好\n保持靜止，不要動",
        WizardPhase.UprightMaxGesture   => $"手機螢幕朝向你\n往右傾斜到你的【最大舒適角度】並保持\n最大位移由 Inspector 的 Max Offset Per Axis.x（目前={uprightSettings.maxOffsetPerAxis.x:F1}）決定",
        WizardPhase.UprightPitchGesture => $"手機螢幕朝向你\n往前傾斜（遠離自己）到你的【最大舒適角度】並保持\n最大位移由 Max Offset Per Axis.y（目前={uprightSettings.maxOffsetPerAxis.y:F1}）決定",
        WizardPhase.UprightForward      => "握著手機，往前後來回移動幾次\n用你自然舒適的幅度",
        WizardPhase.FlatBaseline        => "手機平放（螢幕朝上）\n保持靜止，不要動",
        WizardPhase.FlatMaxGesture      => $"手機平放\n往右傾斜到你的【最大舒適角度】並保持\n最大位移由 Max Offset Per Axis.x（目前={flatSettings.maxOffsetPerAxis.x:F1}）決定",
        WizardPhase.FlatForward         => "手機平放\n往前後來回推動幾次，用舒適的自然幅度",
        _                               => ""
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

    // 傾斜角視覺化儀表（MaxGesture / PitchGesture phase 用，angle 由呼叫端傳入）
    private void DrawTiltGauge(float x, ref float y, float w, GUIStyle baseStyle, float angle)
    {
        const float minGood = 15f, maxGood = 45f, fullRange = 90f;
        angle = Mathf.Abs(angle);
        bool  inRange = angle >= minGood && angle <= maxGood;

        Color statusColor = inRange ? Color.green : new Color(1f, 0.55f, 0f);
        string hint = inRange       ? "✓ 角度正確，保持不動"
                    : angle < minGood ? $"↑ 再多傾斜一點（目標 {minGood:F0}°～{maxGood:F0}°）"
                                      : $"↓ 傾斜太多了（目標 {minGood:F0}°～{maxGood:F0}°）";

        var labelStyle = new GUIStyle(baseStyle)
            { fontStyle = FontStyle.Bold, normal = { textColor = statusColor } };
        GUI.Label(new Rect(x, y, w, 18f), $"傾斜角：{angle:F1}°   {hint}", labelStyle);
        y += 22f;

        // 繪製量表條
        float barH = 11f, barW = w - 2f;
        Color prev = GUI.color;

        GUI.color = new Color(0.25f, 0.25f, 0.25f, 0.9f);
        GUI.DrawTexture(new Rect(x, y, barW, barH), Texture2D.whiteTexture);

        float goodL = (minGood / fullRange) * barW;
        float goodR = (maxGood / fullRange) * barW;
        GUI.color = new Color(0.15f, 0.75f, 0.15f, 0.75f);
        GUI.DrawTexture(new Rect(x + goodL, y, goodR - goodL, barH), Texture2D.whiteTexture);

        float markerX = Mathf.Clamp01(angle / fullRange) * barW;
        GUI.color = inRange ? Color.white : new Color(1f, 0.55f, 0f);
        GUI.DrawTexture(new Rect(x + markerX - 1.5f, y - 2f, 3f, barH + 4f), Texture2D.whiteTexture);

        GUI.color = prev;
        y += barH + 3f;

        var tinyStyle = new GUIStyle(baseStyle)
            { fontSize = 10, normal = { textColor = new Color(0.65f, 0.9f, 0.65f) } };
        GUI.Label(new Rect(x + goodL - 4f, y, 28f, 14f), $"{minGood:F0}°", tinyStyle);
        GUI.Label(new Rect(x + goodR - 4f, y, 28f, 14f), $"{maxGood:F0}°", tinyStyle);
        y += 16f;
    }

    // 推動強度視覺化條（Forward phase 用）
    // liveZ：即時 Z 軸訊號（已去基線），供即時力度顯示
    private void DrawPushMeter(float x, ref float y, float w, GUIStyle baseStyle, float liveZ)
    {
        const float fullRange = 8f;
        float thr    = zAnchorEnabled ? zAnchorThreshold : 0.5f; // 錨點觸發閾值作為參考線
        float cur    = wizardMaxPeakDisplay;
        bool  isGood = cur >= thr;
        float liveAbs = Mathf.Abs(liveZ);

        Color statusColor = isGood ? Color.green : new Color(1f, 0.55f, 0f);
        string hint = isGood ? "✓ 幅度足夠" : $"↑ 請用力推（目標 > {thr:F1} m/s²）";
        var labelStyle = new GUIStyle(baseStyle)
            { fontStyle = FontStyle.Bold, normal = { textColor = statusColor } };
        GUI.Label(new Rect(x, y, w, 18f), $"最大推動：{cur:F2} m/s²   {hint}", labelStyle);
        y += 22f;

        float barH = 14f, barW = w - 2f;
        Color prev = GUI.color;

        // 背景
        GUI.color = new Color(0.25f, 0.25f, 0.25f, 0.9f);
        GUI.DrawTexture(new Rect(x, y, barW, barH), Texture2D.whiteTexture);

        // 建議施力區間（閾值 ~ 閾值×3），深綠色提示合適範圍
        float thrX  = (thr / fullRange) * barW;
        float thrX3 = Mathf.Min((thr * 3f / fullRange) * barW, barW);
        GUI.color = new Color(0.1f, 0.38f, 0.13f, 0.75f);
        GUI.DrawTexture(new Rect(x + thrX, y, thrX3 - thrX, barH), Texture2D.whiteTexture);

        // 歷史最大（半透明，依達標與否變色）
        float fillW = Mathf.Clamp01(cur / fullRange) * barW;
        GUI.color = isGood ? new Color(0.15f, 0.75f, 0.15f, 0.45f) : new Color(1f, 0.55f, 0f, 0.45f);
        GUI.DrawTexture(new Rect(x, y, fillW, barH), Texture2D.whiteTexture);

        // 即時推力（亮黃，最顯眼，置中高度）
        float liveW = Mathf.Clamp01(liveAbs / fullRange) * barW;
        GUI.color = new Color(1f, 1f, 0.3f, 0.95f);
        GUI.DrawTexture(new Rect(x, y + 3f, liveW, barH - 6f), Texture2D.whiteTexture);

        // 閾值標線（白色垂直線）
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(x + thrX - 1f, y - 2f, 2f, barH + 4f), Texture2D.whiteTexture);

        GUI.color = prev;
        y += barH + 3f;

        var tinyStyle = new GUIStyle(baseStyle)
            { fontSize = 10, normal = { textColor = new Color(0.9f, 0.9f, 0.9f) } };
        GUI.Label(new Rect(x + thrX - 6f, y, 50f, 14f), $"{thr:F1}▲", tinyStyle);
        y += 15f;

        // 即時推力數值（黃色，實時反映當前施力）
        var liveStyle = new GUIStyle(baseStyle)
            { fontSize = 11, normal = { textColor = new Color(1f, 1f, 0.4f) } };
        GUI.Label(new Rect(x, y, w, 16f), $"即時推力：{liveZ:+0.00;-0.00} m/s²", liveStyle);
        y += 18f;

        var hintStyle2 = new GUIStyle(baseStyle)
            { fontSize = 11, normal = { textColor = new Color(0.7f, 1f, 0.7f) } };
        string minStr = wizardMinPeakDisplay > 0.01f
            ? $"最小衝程：{wizardMinPeakDisplay:F2} m/s²"
            : "—（繼續來回移動）";
        GUI.Label(new Rect(x, y, w, 16f), minStr, hintStyle2);
        y += 18f;
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

    private void DrawScaleTuner()
    {
        bool   isFlat   = _scaleTunerForFlat;
        string modeName = isFlat ? "平放" : "直立";
        float  curX     = isFlat ? flatSettings.axisScale.x : uprightSettings.axisScale.x;
        float  curY     = isFlat ? flatSettings.axisScale.y : uprightSettings.axisScale.y;
        float  curZ     = isFlat ? flatSettings.axisScale.z : uprightSettings.axisScale.z;

        const float pw = 220f, ph = 158f;
        float px = scaleTunerPosition.x, py = scaleTunerPosition.y;
        GUI.Box(new Rect(px, py, pw, ph), "");

        float ix = px + 7f, iy = py + 5f;
        float iw = pw - 14f;

        var titleStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        var labelStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 12, normal = { textColor = new Color(0.85f, 0.85f, 0.85f) } };
        var valStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 14, fontStyle = FontStyle.Bold,
              alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.yellow } };
        var stepLabelStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 10, normal = { textColor = new Color(0.6f, 0.6f, 0.6f) } };

        GUI.Label(new Rect(ix, iy, iw, 18f), $"axisScale  （{modeName}模式）", titleStyle);
        iy += 22f;

        // ── 模式切換按鈕（獨立於手機方向，可手動選擇調哪個模式）──
        float btnW = (iw - 4f) * 0.5f;
        Color prevColor = GUI.color;
        GUI.color = !isFlat ? new Color(0.35f, 1f, 0.35f) : Color.white;
        if (GUI.Button(new Rect(ix, iy, btnW, 18f), "直立"))
            _scaleTunerForFlat = false;
        GUI.color = isFlat ? new Color(0.35f, 1f, 0.35f) : Color.white;
        if (GUI.Button(new Rect(ix + btnW + 4f, iy, btnW, 18f), "平放"))
            _scaleTunerForFlat = true;
        GUI.color = prevColor;
        iy += 22f;

        // 各行佈局：[軸名] [−] [slider 可拖動] [+] [數值]
        const float lw = 14f, bw = 24f, vw = 40f, gap = 2f;
        float sliderW = iw - lw - bw * 2f - vw - gap * 4f;

        curX = DrawScaleRow(ix, iy, lw, bw, sliderW, vw, gap, "X", curX, labelStyle, valStyle);
        iy += 28f;
        curY = DrawScaleRow(ix, iy, lw, bw, sliderW, vw, gap, "Y", curY, labelStyle, valStyle);
        iy += 28f;
        curZ = DrawScaleRow(ix, iy, lw, bw, sliderW, vw, gap, "Z", curZ, labelStyle, valStyle);
        iy += 28f;

        // 步距選擇
        GUI.Label(new Rect(ix, iy, 28f, 16f), "步距", stepLabelStyle);
        float[] steps = { 0.01f, 0.1f, 0.5f };
        float sbX = ix + 30f;
        foreach (float step in steps)
        {
            bool active = Mathf.Abs(scaleTunerStep - step) < 0.001f;
            Color prev = GUI.color;
            if (active) GUI.color = new Color(0.35f, 1f, 0.35f);
            if (GUI.Button(new Rect(sbX, iy, 38f, 16f), step.ToString("G2")))
                scaleTunerStep = step;
            GUI.color = prev;
            sbX += 41f;
        }
        if (GUI.Button(new Rect(ix + iw - 42f, iy, 42f, 16f), "重設 1"))
            { curX = 1f; curY = 1f; curZ = 1f; }

        if (isFlat)
        {
            flatSettings.axisScale.x = curX; flatSettings.axisScale.y = curY;
            flatSettings.axisScale.z = curZ;
        }
        else
        {
            uprightSettings.axisScale.x = curX; uprightSettings.axisScale.y = curY;
            uprightSettings.axisScale.z = curZ;
        }
    }

    // slider 連續拖動不四捨五入（拖動平滑）；+/− 按鈕才做捨入
    private float DrawScaleRow(float ix, float iy, float lw, float bw, float sliderW, float vw,
                                float gap, string axisLabel, float cur,
                                GUIStyle labelStyle, GUIStyle valStyle)
    {
        GUI.Label(new Rect(ix, iy, lw, 24f), axisLabel, labelStyle);
        float bx = ix + lw + gap;

        if (GUI.Button(new Rect(bx, iy, bw, 24f), "−"))
            cur = Mathf.Max(0.1f, Mathf.Round((cur - scaleTunerStep) * 100f) / 100f);
        bx += bw + gap;

        cur = GUI.HorizontalSlider(new Rect(bx, iy + 6f, sliderW, 14f), cur, 0.1f, 5f);
        bx += sliderW + gap;

        if (GUI.Button(new Rect(bx, iy, bw, 24f), "+"))
            cur = Mathf.Round((cur + scaleTunerStep) * 100f) / 100f;
        bx += bw + gap;

        GUI.Label(new Rect(bx, iy, vw, 24f), cur.ToString("F2"), valStyle);
        return cur;
    }
}
