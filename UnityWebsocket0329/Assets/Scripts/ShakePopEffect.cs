using UnityEngine;

/// <summary>
/// 掛在子物件上。
/// 偵測到搖晃時，子物件朝「自身相對父物件中心的放射方向」彈出，再透過彈簧物理回到原位。
/// 訂閱 GyroscopeReceiver.OnAccelerationReceived（static event），不需要 PoseController。
/// </summary>
public class ShakePopEffect : MonoBehaviour
{
    [Header("Pop Impulse")]
    [SerializeField] private float impulseMin = 4.5f;   // dynamicAcc 剛過 accLow 時的脈衝強度
    [SerializeField] private float impulseMax = 10f;    // dynamicAcc 達 accHigh 時的脈衝強度
    [SerializeField] private float accLow     = 3.43f;  // 觸發門檻（0.35G，與 GyroscopeReceiver 一致）
    [SerializeField] private float accHigh    = 12f;    // 脈衝滿點（約 2.2G total）

    [Header("Pop Behaviour")]
    [SerializeField] private float popCooldown    = 0.4f;   // 兩次 pop 的最短間隔（秒）
    [SerializeField] private bool  allowRetrigger = false;  // 彈簧進行中是否允許重觸發（疊加速度）

    [Header("Spring Physics")]
    [SerializeField] private float springStiffness = 25f;   // 彈簧剛度 k
    [SerializeField] private float springDamping   = 6f;    // 阻尼 b（阻尼比≈0.6，稍微欠阻尼）

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    // ── 運行時狀態 ──
    private Vector3 originLocalPos;     // Start() 時記錄的原始 local position
    private Vector3 popDirection;       // 放射外向方向（local space，normalized）
    private Vector3 springVelocity;     // 彈簧速度（local space）
    private bool    isSpringActive;     // 彈簧是否正在運行
    private float   lastPopTime = -999f; // 上次觸發的時間戳

    private void Start()
    {
        originLocalPos = transform.localPosition;

        // 放射方向 = 子物件 local position 的方向（從父物件中心指向子物件）
        if (originLocalPos.sqrMagnitude > 0.0001f)
            popDirection = originLocalPos.normalized;
        else
        {
            popDirection = Vector3.up;
            Debug.LogWarning($"[ShakePop] '{gameObject.name}' 的 localPosition 為零，"
                           + "無法計算放射方向，fallback 為 Vector3.up");
        }

        GyroscopeReceiver.OnAccelerationReceived += HandleAcceleration;

        if (debugLog)
            Debug.Log($"[ShakePop] Start() on '{gameObject.name}' | "
                    + $"originLocalPos={originLocalPos} | popDirection={popDirection}");
    }

    private void OnDestroy()
    {
        GyroscopeReceiver.OnAccelerationReceived -= HandleAcceleration;
    }

    // ── 加速度事件處理（static event，main thread 安全）──
    private void HandleAcceleration(Vector3 acc)
    {
        // 動態加速度 = 加速度大小與重力基準的偏差
        float dynamicAcc = Mathf.Abs(acc.magnitude - 9.81f);

        if (dynamicAcc < accLow) return;
        if (Time.time - lastPopTime < popCooldown) return;
        if (isSpringActive && !allowRetrigger) return;

        float t       = Mathf.InverseLerp(accLow, accHigh, dynamicAcc);
        float impulse = Mathf.Lerp(impulseMin, impulseMax, t);

        TriggerPop(impulse, dynamicAcc);
        lastPopTime = Time.time;
    }

    private void TriggerPop(float impulse, float dynamicAcc)
    {
        if (allowRetrigger && isSpringActive)
            springVelocity += popDirection * impulse;   // 疊加（連續搖晃越彈越遠）
        else
            springVelocity  = popDirection * impulse;   // 取代

        isSpringActive = true;

        if (debugLog)
            Debug.Log($"[ShakePop] Pop on '{gameObject.name}' | "
                    + $"dynamicAcc={dynamicAcc:F2} m/s² | impulse={impulse:F2} | dir={popDirection}");
    }

    // LateUpdate 確保彈簧位移在其他 Update 之後寫入，不被蓋掉
    private void LateUpdate()
    {
        if (!isSpringActive) return;

        Vector3 displacement = transform.localPosition - originLocalPos;
        Vector3 springForce  = -springStiffness * displacement - springDamping * springVelocity;

        springVelocity          += springForce * Time.deltaTime;
        transform.localPosition += springVelocity * Time.deltaTime;

        // 靜止條件：位移與速度均趨近零
        if (displacement.sqrMagnitude < 0.00001f && springVelocity.sqrMagnitude < 0.00001f)
        {
            transform.localPosition = originLocalPos;
            springVelocity          = Vector3.zero;
            isSpringActive          = false;

            if (debugLog)
                Debug.Log($"[ShakePop] Spring came to rest on '{gameObject.name}'");
        }
    }
}
