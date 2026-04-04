using UnityEngine;

/// <summary>
/// PoseControllerBridge - 橋接 GyroscopeReceiver 和 PoseController
///
/// 將 GyroscopeReceiver 的 Euler angles 轉換為 PoseController 需要的 Quaternion
/// 並計算 gyroRotationRate (基於角度變化)
/// </summary>
public class PoseControllerBridge : MonoBehaviour
{
    [SerializeField] private PoseController poseController; // 拖拽 PoseController 到此欄位
    [SerializeField] private bool showDebugLogs = false;    // 是否顯示調試日誌

    // 存儲最新的數據
    private Quaternion latestRotation = Quaternion.identity;
    private Vector3 latestGyroRate = Vector3.zero;
    private Vector3 latestAcceleration = Vector3.zero;

    // 上一次的角度，用於計算角速度
    private Vector3 lastEulerAngles = Vector3.zero;
    private float lastGyroTimestamp = 0f;
    private bool hasPreviousGyroData = false;
    private bool hasValidRotation = false;
    private bool hasNewData = false; // 是否有新數據

    private void Start()
    {
        if (poseController == null)
        {
            Debug.LogError("請將 PoseController 拖拽到 poseController 欄位！");
            enabled = false;
            return;
        }

        // 訂閱 GyroscopeReceiver 的事件
        GyroscopeReceiver.OnGyroscopeDataReceived += OnGyroscopeDataReceived;
        GyroscopeReceiver.OnAccelerationReceived += OnAccelerationReceived;

        Debug.Log("PoseControllerBridge 啟動，開始橋接數據...");
    }

    private void OnDestroy()
    {
        // 取消訂閱
        GyroscopeReceiver.OnGyroscopeDataReceived -= OnGyroscopeDataReceived;
        GyroscopeReceiver.OnAccelerationReceived -= OnAccelerationReceived;
    }

    /// <summary>
    /// 處理陀螺儀數據 (Euler angles)
    /// </summary>
    private void OnGyroscopeDataReceived(GyroscopeReceiver.GyroscopeData data)
    {
        // 手機 Euler -> Unity Quaternion
        latestRotation = Quaternion.Euler(data.beta, data.gamma, -data.alpha);
        hasValidRotation = true;

        Vector3 currentEuler = new Vector3(data.beta, data.gamma, data.alpha);
        float now = Time.unscaledTime;

        if (hasPreviousGyroData)
        {
            float deltaTime = now - lastGyroTimestamp;
            if (deltaTime > 0.0001f)
            {
                // 使用 Mathf.DeltaAngle 處理角度跳躍
                Vector3 deltaAngles = new Vector3(
                    Mathf.DeltaAngle(lastEulerAngles.x, currentEuler.x),
                    Mathf.DeltaAngle(lastEulerAngles.y, currentEuler.y),
                    Mathf.DeltaAngle(lastEulerAngles.z, currentEuler.z)
                );

                latestGyroRate = deltaAngles / deltaTime;
            }
        }

        lastEulerAngles = currentEuler;
        lastGyroTimestamp = now;
        hasPreviousGyroData = true;
        hasNewData = true; // 標記有新數據
    }

    /// <summary>
    /// 處理加速度數據
    /// </summary>
    private void OnAccelerationReceived(Vector3 acceleration)
    {
        latestAcceleration = acceleration;
    }

    private void Update()
    {
        if (!hasValidRotation || poseController == null) return;

        if (!hasNewData) return; // 只有新數據時才推送

        // 每幀統一傳遞完整數據
        poseController.OnPhoneData(latestRotation, latestGyroRate, latestAcceleration);

        hasNewData = false; // 重置標記

        if (showDebugLogs)
        {
            Debug.Log(
                $"rot={latestRotation.eulerAngles:F1}, gyro={latestGyroRate.magnitude:F1}, acc={latestAcceleration.magnitude:F2}"
            );
        }
    }
}