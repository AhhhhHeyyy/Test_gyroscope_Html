using UnityEngine;

/// <summary>
/// 掛在子物件上。
/// Layer A：監看父物件世界座標位移，判定揮動並輸出主方向。
/// Layer B：收到方向後施加一次性脈衝，之後純彈簧回彈。
/// </summary>
public class SwingStretchEffect : MonoBehaviour
{
    [Header("A. Swing 偵測")]
    [SerializeField] private int historyFrames = 8;             // 位移方向緩衝幀數
    [SerializeField] private float swingCooldown = 0.4f;

    [Header("B. Stretch 視覺")]
    [SerializeField] private PoseController poseController;   // 必填
    [SerializeField] private float impulseMin = 4.5f;         // dynamicAcc 剛過門檻時的脈衝
    [SerializeField] private float impulseMax = 10f;          // dynamicAcc 達到上限時的脈衝
    [SerializeField] private float accLow = 3.43f;            // 脈衝起始點（0.35G，即 1.35G total）
    [SerializeField] private float accHigh = 12f;             // 脈衝滿點（約 2.2G total）
    [SerializeField] private float springStiffness = 25f;
    [SerializeField] private float springDamping = 6f;

    [Header("Debug")]
    [SerializeField] private bool debugDrawRay = false;

    // --- Layer A ---
    private Vector3[] deltaBuffer;
    private int bufferHead;
    private Vector3 lastParentPos;
    private float lastSwingTime = -999f;

    // --- Layer B ---
    private Vector3 originLocalPos;
    private Vector3 springVelocity;
    private bool isSpringActive;

    private void Start()
    {
        deltaBuffer = new Vector3[historyFrames];
        originLocalPos = transform.localPosition;

        if (transform.parent != null)
            lastParentPos = transform.parent.position;

        Debug.Log($"[SwingStretch] Start() 執行，物件：{gameObject.name}，parent：{(transform.parent ? transform.parent.name : "null")}");

        if (poseController != null)
        {
            poseController.OnSwingDetected += OnSwingDetected;
            Debug.Log($"[SwingStretch] 已訂閱 PoseController.OnSwingDetected");
        }
        else
        {
            Debug.LogWarning("[SwingStretch] poseController 未指定，無法偵測揮動！");
        }
    }

    private void OnDestroy()
    {
        if (poseController != null)
            poseController.OnSwingDetected -= OnSwingDetected;
    }

    private void OnSwingDetected()
    {
        if (debugDrawRay) Debug.Log($"[SwingStretch] 收到 OnSwingDetected，isSpringActive={isSpringActive}，cooldownLeft={swingCooldown - (Time.time - lastSwingTime):F2}");
        if (isSpringActive) return;
        if (Time.time - lastSwingTime < swingCooldown) return;

        // 從 buffer 計算平均位移方向
        Vector3 avgDelta = Vector3.zero;
        for (int i = 0; i < historyFrames; i++)
            avgDelta += deltaBuffer[i];

        Vector3 dir = avgDelta.sqrMagnitude > 0.0001f
            ? avgDelta.normalized
            : -transform.parent.up;

        float dynamicAcc = poseController.RecentPeakDynamicAcc;
        if (dynamicAcc < accLow) return;

        TriggerStretch(dir, dynamicAcc);
        lastSwingTime = Time.time;
    }

    private void Update()
    {
        if (transform.parent == null) return;

        // ── Layer A：每幀持續更新位移 buffer（供方向計算）──
        Vector3 currentParentPos = transform.parent.position;
        Vector3 delta = currentParentPos - lastParentPos;
        lastParentPos = currentParentPos;

        deltaBuffer[bufferHead] = delta;
        bufferHead = (bufferHead + 1) % historyFrames;

        if (debugDrawRay)
        {
            Vector3 avgDelta = Vector3.zero;
            for (int i = 0; i < historyFrames; i++) avgDelta += deltaBuffer[i];
            if (avgDelta.sqrMagnitude > 0.0001f)
                Debug.DrawRay(transform.parent.position, avgDelta * 5f, Color.cyan);
        }

    }

    // LateUpdate 在所有 Update() 之後執行，確保彈簧位移不被其他腳本蓋掉
    private void LateUpdate()
    {
        if (!isSpringActive) return;

        Vector3 displacement = transform.localPosition - originLocalPos;
        Vector3 springForce = -springStiffness * displacement - springDamping * springVelocity;
        springVelocity += springForce * Time.deltaTime;
        transform.localPosition += springVelocity * Time.deltaTime;

        // 停止條件
        if (displacement.sqrMagnitude < 0.00001f && springVelocity.sqrMagnitude < 0.00001f)
        {
            transform.localPosition = originLocalPos;
            springVelocity = Vector3.zero;
            isSpringActive = false;
        }
    }

    private void TriggerStretch(Vector3 swingDirWorld, float dynamicAcc)
    {
        float t = Mathf.InverseLerp(accLow, accHigh, dynamicAcc);
        float impulse = Mathf.Lerp(impulseMin, impulseMax, t);

        // 世界方向 → parent local 方向 → 取反
        Vector3 localDir = transform.parent.InverseTransformDirection(swingDirWorld);
        springVelocity = -localDir.normalized * impulse;
        isSpringActive = true;

        Debug.Log($"[SwingStretch] 脈衝觸發：dynamicAcc={dynamicAcc:F2} m/s²，impulse={impulse:F2}，localDir={localDir}");
        if (debugDrawRay)
            Debug.DrawRay(transform.position, -swingDirWorld * impulse * 0.5f, Color.yellow, 0.5f);
    }
}
