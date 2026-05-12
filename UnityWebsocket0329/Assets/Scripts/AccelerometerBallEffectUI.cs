using System.Reflection;
using UnityEngine;

/// <summary>
/// 在 Game 視窗顯示 IMGUI 面板，即時調整 AccelerometerBallEffect 的所有參數。
/// 新增「鎖定 XYZ」功能：axisDeadzone / axisScale / maxOffsetPerAxis 可三軸同步調整。
/// 使用 Reflection 存取私有欄位，AccelerometerBallEffect.cs 不需任何修改。
///
/// 使用方式：
///   1. 將此腳本掛到任意 GameObject（建議與 AccelerometerBallEffect 同一物件）
///   2. 在 Inspector 將 target 欄位指定為含有 AccelerometerBallEffect 的物件
///   3. 進入 Play Mode 後，Game 視窗左上角會出現可拖動的參數面板
/// </summary>
public class AccelerometerBallEffectUI : MonoBehaviour
{
    [SerializeField] private AccelerometerBallEffect target;
    [SerializeField] [Range(0.5f, 2.5f)] private float uiScale = 1f;

    private bool    _showPanel = true;
    private int     _tab       = 0;
    private Rect    _win       = new Rect(10f, 10f, 410f, 30f);
    private Vector2 _scroll;

    // _link[modeIndex, fieldIndex]：modeIndex 0=直立 1=平放，fieldIndex 0=deadzone 1=scale 2=maxOffset
    private readonly bool[,] _link = new bool[2, 3];

    private static readonly BindingFlags Bf = BindingFlags.NonPublic | BindingFlags.Instance;

    // 模式 struct 欄位
    private FieldInfo _uprightFi, _flatFi;
    private FieldInfo _fSens, _fSmooth, _fFilter;
    private FieldInfo _fMask, _fFlip, _fDz, _fSc, _fMax;

    // 全域欄位
    private FieldInfo _fAutoDelay, _fFlatGrav, _fFlatThr;
    private FieldInfo _fDebounce, _fTransDur, _fPosFilt;

    private bool _ready;

    private void Start() => Init();

    private void Init()
    {
        if (target == null) return;
        var t = typeof(AccelerometerBallEffect);

        _uprightFi = t.GetField("uprightSettings", Bf);
        _flatFi    = t.GetField("flatSettings",    Bf);
        if (_uprightFi == null || _flatFi == null) return;

        var ms   = _uprightFi.FieldType;
        _fSens   = ms.GetField("sensitivity");
        _fSmooth = ms.GetField("smoothSpeed");
        _fFilter = ms.GetField("inputFilterTime");
        _fMask   = ms.GetField("movementAxesMask");
        _fFlip   = ms.GetField("axisFlip");
        _fDz     = ms.GetField("axisDeadzone");
        _fSc     = ms.GetField("axisScale");
        _fMax    = ms.GetField("maxOffsetPerAxis");

        _fAutoDelay  = t.GetField("autoCalibrationDelay",        Bf);
        _fFlatGrav   = t.GetField("flatGravityWeight",            Bf);
        _fFlatThr    = t.GetField("flatnessThreshold",            Bf);
        _fDebounce   = t.GetField("modeSwitchDebounceTime",       Bf);
        _fTransDur   = t.GetField("modeSwitchTransitionDuration", Bf);
        _fPosFilt    = t.GetField("positionFilterTime",           Bf);

        _ready = true;
    }

    private void OnGUI()
    {
        if (target == null) return;
        if (!_ready) Init();
        if (!_ready) return;

        var prev = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));
        _win = GUILayout.Window(98765, _win, DrawWindow, "Accel 參數調整面板");
        GUI.matrix = prev;
    }

    private void DrawWindow(int _)
    {
        // ── 頂部工具列 ──
        GUILayout.BeginHorizontal();
        string collapseLabel = _showPanel ? "[ 收合 ]" : "[ 展開 ]";
        if (GUILayout.Button(collapseLabel, GUILayout.Width(68f)))
            _showPanel = !_showPanel;

        GUILayout.Label("縮放", GUILayout.Width(28f));
        uiScale = GUILayout.HorizontalSlider(uiScale, 0.5f, 2.5f, GUILayout.Width(66f));
        GUILayout.Label(uiScale.ToString("F1"), GUILayout.Width(24f));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("校正", GUILayout.Width(54f)))
            target.Recalibrate();
        GUILayout.EndHorizontal();

        if (!_showPanel)
        {
            GUI.DragWindow();
            return;
        }

        // ── Tab 切換 ──
        _tab = GUILayout.Toolbar(_tab, new[] { "直立模式", "平放模式", "全域設定" });

        // ── 可捲動內容區 ──
        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(370f));

        if      (_tab == 0) DrawMode(_uprightFi, 0);
        else if (_tab == 1) DrawMode(_flatFi,    1);
        else                DrawGlobal();

        GUILayout.EndScrollView();

        GUI.DragWindow();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 模式設定（直立 / 平放）
    // ─────────────────────────────────────────────────────────────────────────

    private void DrawMode(FieldInfo modeField, int modeIdx)
    {
        // 從 target 取出已 box 的 struct，直接對 box 修改欄位，最後寫回
        object box = modeField.GetValue(target);

        Section("── 基本參數 ──");
        _fSens.SetValue(box,   Slider("靈敏度 sensitivity",    GetF(box, _fSens),   0f,    2f));
        _fSmooth.SetValue(box, Slider("平滑速度 smoothSpeed",  GetF(box, _fSmooth), 1f,   30f));
        _fFilter.SetValue(box, Slider("輸入濾波 filterTime",   GetF(box, _fFilter), 0.01f, 0.5f));

        Section("── XYZ 參數（可鎖定同步）──");
        _fDz.SetValue(box,  Vec3("死區 axisDeadzone",   GetV(box, _fDz),  modeIdx, 0, 0f,   2f));
        _fSc.SetValue(box,  Vec3("軸縮放 axisScale",    GetV(box, _fSc),  modeIdx, 1, 0f,   5f));
        _fMax.SetValue(box, Vec3("最大偏移 maxOffset",  GetV(box, _fMax), modeIdx, 2, 0f,  10f));

        Section("── 方向控制 ──");
        _fFlip.SetValue(box, AxisFlip(GetV(box, _fFlip)));
        _fMask.SetValue(box, AxisMask(GetV(box, _fMask)));

        // 寫回 struct（value type 必須整個寫回）
        modeField.SetValue(target, box);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 全域設定
    // ─────────────────────────────────────────────────────────────────────────

    private void DrawGlobal()
    {
        Section("── 校正 ──");
        SetG(_fAutoDelay, Slider("自動校正延遲 (s)",    GetG(_fAutoDelay), 0f,   5f));

        Section("── 平放判斷 ──");
        SetG(_fFlatGrav,  Slider("平放重力權重",         GetG(_fFlatGrav),  0f,   3f));
        SetG(_fFlatThr,   Slider("平放閾值",              GetG(_fFlatThr),   0f,   1f));

        Section("── 模式切換 ──");
        SetG(_fDebounce,  Slider("防抖時間 (s)",          GetG(_fDebounce),  0f,   1f));
        SetG(_fTransDur,  Slider("切換過渡時長 (s)",      GetG(_fTransDur),  0.1f, 2f));

        Section("── 輸出平滑 ──");
        SetG(_fPosFilt,   Slider("位置濾波時間 (s)",      GetG(_fPosFilt),   0f,   0.3f));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Vector3 帶鎖定功能
    // ─────────────────────────────────────────────────────────────────────────

    private Vector3 Vec3(string label, Vector3 v, int mi, int fi, float min, float max)
    {
        bool linked = _link[mi, fi];

        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(160f));
        // 鎖定切換按鈕
        if (GUILayout.Button(linked ? "[鎖定 XYZ]" : "[解鎖 XYZ]", GUILayout.Width(82f)))
            _link[mi, fi] = !linked;
        GUILayout.EndHorizontal();

        if (_link[mi, fi])
        {
            // 鎖定：顯示單一 slider，三軸同步
            GUILayout.BeginHorizontal();
            GUILayout.Space(12f);
            GUILayout.Label("XYZ", GUILayout.Width(28f));
            float val = GUILayout.HorizontalSlider(v.x, min, max);
            GUILayout.Label(val.ToString("F3"), GUILayout.Width(46f));
            GUILayout.EndHorizontal();
            return new Vector3(val, val, val);
        }

        // 解鎖：三軸獨立 slider
        v.x = AxisRow("  X", v.x, min, max);
        v.y = AxisRow("  Y", v.y, min, max);
        v.z = AxisRow("  Z", v.z, min, max);
        return v;
    }

    private float AxisRow(string ax, float val, float min, float max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(ax, GUILayout.Width(20f));
        val = GUILayout.HorizontalSlider(val, min, max);
        GUILayout.Label(val.ToString("F3"), GUILayout.Width(46f));
        GUILayout.EndHorizontal();
        return val;
    }

    private Vector3 AxisFlip(Vector3 v)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("方向翻轉 axisFlip", GUILayout.Width(130f));
        v.x = GUILayout.Toggle(v.x < 0f, "X反轉") ? -1f : 1f;
        v.y = GUILayout.Toggle(v.y < 0f, "Y反轉") ? -1f : 1f;
        v.z = GUILayout.Toggle(v.z < 0f, "Z反轉") ? -1f : 1f;
        GUILayout.EndHorizontal();
        return v;
    }

    private Vector3 AxisMask(Vector3 v)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("軸遮罩 movementMask", GUILayout.Width(130f));
        v.x = GUILayout.Toggle(v.x > 0.5f, "X") ? 1f : 0f;
        v.y = GUILayout.Toggle(v.y > 0.5f, "Y") ? 1f : 0f;
        v.z = GUILayout.Toggle(v.z > 0.5f, "Z") ? 1f : 0f;
        GUILayout.EndHorizontal();
        return v;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 通用 Helper
    // ─────────────────────────────────────────────────────────────────────────

    private float Slider(string label, float val, float min, float max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(180f));
        val = GUILayout.HorizontalSlider(val, min, max);
        GUILayout.Label(val.ToString("F3"), GUILayout.Width(46f));
        GUILayout.EndHorizontal();
        return val;
    }

    private void Section(string text) { GUILayout.Space(4f); GUILayout.Label(text); }

    private float   GetF(object b, FieldInfo f) => (float)f.GetValue(b);
    private Vector3 GetV(object b, FieldInfo f) => (Vector3)f.GetValue(b);
    private float   GetG(FieldInfo f)            => (float)f.GetValue(target);
    private void    SetG(FieldInfo f, float v)  => f.SetValue(target, v);
}
