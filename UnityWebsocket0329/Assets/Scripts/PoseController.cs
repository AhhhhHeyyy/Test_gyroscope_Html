using System;
using UnityEngine;

public class PoseController : MonoBehaviour
{
    [Header("旋轉控制設定")]
    [SerializeField] private float rotationSmoothSpeed = 30f;
    [SerializeField] private bool useLocalRotation = true;

    [Header("揮動偵測設定")]
    [SerializeField] private float swingCooldown = 0.18f;
    [SerializeField] private float gravityBaseline = 9.81f;
    [SerializeField] private float dynamicAccelerationThreshold = 3.43f;
    [SerializeField] private float peakHoldTime = 0.06f;

    [Header("加速度效果設定")]
    [SerializeField] private bool enableAccelerationEffects = false;
    [SerializeField] [Range(0f, 5f)] private float accelerationSpeedInfluence = 0f;

    [Header("揮動轉動設定")]
    [SerializeField] private bool enableSwingRotation = true;
    [SerializeField] private float swingRotationMultiplier = 18f;
    [SerializeField] private float swingRotationDecay = 8f;
    [SerializeField] private float maxSwingRotationSpeed = 360f;

    [Tooltip("若開啟，依局部 X/Y 平面推算甩動；若關閉，使用整體局部加速度方向")]
    [SerializeField] private bool usePlanarSwingDirection = true;

    [Tooltip("局部 X 影響繞哪個軸旋轉")]
    [SerializeField] private Vector3 rollAxis = Vector3.forward;

    [Tooltip("局部 Y 影響繞哪個軸旋轉")]
    [SerializeField] private Vector3 pitchAxis = Vector3.right;

    [Header("移動控制設定")]
    [SerializeField] private bool enableTiltMovement = true;
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float moveDeadZone = 3f;
    [SerializeField] private float maxTiltAngle = 25f;
    [SerializeField] private bool moveInLocalSpace = false;
    [SerializeField] private bool useXZPlane = false;
    [SerializeField] private float moveSmoothSpeed = 20f;

    private Quaternion phoneRotation = Quaternion.identity;
    private Vector3 gyroRotationRate = Vector3.zero;
    private Vector3 acceleration = Vector3.zero;

    private Quaternion calibrationOffset = Quaternion.identity;
    private bool isCalibrated = false;

    private float lastSwingTime = -999f;
    private Vector3 currentMoveVelocity = Vector3.zero;

    // 不再直接 Rotate 世界座標，而是維護一個額外的 swing 偏移
    private Quaternion swingOffsetRotation = Quaternion.identity;
    private Vector3 swingAngularVelocity = Vector3.zero;

    private float recentPeakDynamicAcc = 0f;
    private float peakTimer = 0f;

    public event Action OnSwingDetected;
    public event Action<Vector3> OnAccelerationEffect;

    private void Update()
    {
        Quaternion baseRotation = isCalibrated ? calibrationOffset * phoneRotation : phoneRotation;

        UpdateDynamicAccelerationPeak();

        if (ShouldTriggerSwing())
        {
            Swing(baseRotation);
            lastSwingTime = Time.time;
        }

        UpdateSwingOffset();

        Quaternion finalRotation = baseRotation * swingOffsetRotation;

        if (useLocalRotation)
        {
            transform.localRotation = Quaternion.Slerp(
                transform.localRotation,
                finalRotation,
                rotationSmoothSpeed * Time.deltaTime
            );
        }
        else
        {
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                finalRotation,
                rotationSmoothSpeed * Time.deltaTime
            );
        }

        if (enableAccelerationEffects && acceleration != Vector3.zero)
            OnAccelerationEffect?.Invoke(acceleration);

        if (enableTiltMovement && isCalibrated)
        {
            Vector3 euler = baseRotation.eulerAngles;
            float pitch = NormalizeAngle(euler.x);
            float roll = NormalizeAngle(euler.z);

            float inputX = Mathf.Abs(roll) < moveDeadZone ? 0f : Mathf.Clamp(roll / maxTiltAngle, -1f, 1f);
            float inputY = Mathf.Abs(pitch) < moveDeadZone ? 0f : Mathf.Clamp(-pitch / maxTiltAngle, -1f, 1f);

            float effectiveMoveSpeed = accelerationSpeedInfluence > 0f
                ? moveSpeed * (1f + acceleration.magnitude * accelerationSpeedInfluence)
                : moveSpeed;

            Vector3 targetMove = useXZPlane
                ? new Vector3(inputX, 0f, inputY) * effectiveMoveSpeed
                : new Vector3(inputX, inputY, 0f) * effectiveMoveSpeed;

            currentMoveVelocity = Vector3.MoveTowards(
                currentMoveVelocity,
                targetMove,
                moveSmoothSpeed * Time.deltaTime
            );

            if (moveInLocalSpace)
                transform.Translate(currentMoveVelocity * Time.deltaTime, Space.Self);
            else
                transform.Translate(currentMoveVelocity * Time.deltaTime, Space.World);
        }
    }

    private void UpdateDynamicAccelerationPeak()
    {
        float dynamicAcc = CurrentDynamicAcceleration;

        if (dynamicAcc > recentPeakDynamicAcc)
        {
            recentPeakDynamicAcc = dynamicAcc;
            peakTimer = peakHoldTime;
        }
        else
        {
            peakTimer -= Time.deltaTime;
            if (peakTimer <= 0f)
                recentPeakDynamicAcc = dynamicAcc;
        }
    }

    private bool ShouldTriggerSwing()
    {
        if (Time.time - lastSwingTime <= swingCooldown)
            return false;

        return recentPeakDynamicAcc >= dynamicAccelerationThreshold;
    }

    private void UpdateSwingOffset()
    {
        if (!enableSwingRotation)
            return;

        if (swingAngularVelocity.sqrMagnitude < 0.0001f)
        {
            swingAngularVelocity = Vector3.zero;
            // 讓 offset 也慢慢回正
            swingOffsetRotation = Quaternion.Slerp(
                swingOffsetRotation,
                Quaternion.identity,
                swingRotationDecay * Time.deltaTime
            );
            return;
        }

        Quaternion deltaRotation = Quaternion.Euler(swingAngularVelocity * Time.deltaTime);
        swingOffsetRotation = deltaRotation * swingOffsetRotation;

        // 指數衰減，比原本更乾脆
        float damping = Mathf.Exp(-swingRotationDecay * Time.deltaTime);
        swingAngularVelocity *= damping;

        // 避免殘留飄動
        swingOffsetRotation = Quaternion.Slerp(
            swingOffsetRotation,
            Quaternion.identity,
            (swingRotationDecay * 0.35f) * Time.deltaTime
        );
    }

    public void OnPhoneData(Quaternion rot, Vector3 gyro, Vector3 acc)
    {
        phoneRotation = rot;
        gyroRotationRate = gyro;
        acceleration = acc;
    }

    public void Calibrate()
    {
        calibrationOffset = Quaternion.Inverse(phoneRotation);
        isCalibrated = true;

        // 校準時也把脈衝偏移清掉
        swingOffsetRotation = Quaternion.identity;
        swingAngularVelocity = Vector3.zero;

        Debug.Log($"姿態校準完成。偏移: {calibrationOffset.eulerAngles}");
    }

    private void Swing(Quaternion baseRotation)
    {
        OnSwingDetected?.Invoke();

        if (!enableSwingRotation)
            return;

        float dynamicAcc = recentPeakDynamicAcc;
        if (dynamicAcc < 0.001f)
            return;

        // 把加速度轉到「物件局部空間」判斷方向
        Vector3 localAcc = Quaternion.Inverse(baseRotation) * acceleration;

        Vector3 localRotationAxis;

        if (usePlanarSwingDirection)
        {
            // 你可以把它想成：
            // 橫向甩 -> 繞某軸轉
            // 縱向甩 -> 繞某軸轉
            localRotationAxis =
                rollAxis * localAcc.x +
                pitchAxis * -localAcc.y;
        }
        else
        {
            localRotationAxis = new Vector3(localAcc.y, -localAcc.x, 0f);
        }

        if (localRotationAxis.sqrMagnitude < 0.0001f)
            return;

        localRotationAxis.Normalize();

        float rotationSpeed = Mathf.Min(dynamicAcc * swingRotationMultiplier, maxSwingRotationSpeed);

        // 疊加到局部角速度
        swingAngularVelocity += localRotationAxis * rotationSpeed;

        Debug.Log(
            $"揮動轉動觸發：totalAcc={acceleration.magnitude:F2}，dynamicAcc={dynamicAcc:F2}，localAcc={localAcc:F2}，gyro={gyroRotationRate.magnitude:F2}，角速度={rotationSpeed:F1} deg/s"
        );
    }

    public Quaternion PhoneRotation => phoneRotation;
    public Vector3 GyroRotationRate => gyroRotationRate;
    public Vector3 Acceleration => acceleration;
    public float CurrentDynamicAcceleration => Mathf.Abs(acceleration.magnitude - gravityBaseline);
    public float RecentPeakDynamicAcc => recentPeakDynamicAcc;
    public bool IsCalibrated => isCalibrated;

    private float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f) angle -= 360f;
        return angle;
    }
}