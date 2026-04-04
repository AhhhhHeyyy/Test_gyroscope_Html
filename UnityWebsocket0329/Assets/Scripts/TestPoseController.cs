using UnityEngine;

/// <summary>
/// TestPoseController - 測試 PoseController 的腳本
/// 在 Unity Editor 中運行，模擬手機數據
/// </summary>
public class TestPoseController : MonoBehaviour
{
    [SerializeField] private PoseController poseController; // 拖拽 PoseController 到此欄位

    [Header("模擬參數")]
    [SerializeField] private float rotationSpeed = 30f;     // 模擬旋轉速度
    [SerializeField] private float gyroSwingSpeed = 200f;   // 模擬揮動陀螺速度
    [SerializeField] private float accelerationIntensity = 2f; // 模擬加速度強度

    private void Start()
    {
        if (poseController == null)
        {
            Debug.LogError("請將 PoseController 拖拽到 poseController 欄位！");
            return;
        }

        // 訂閱事件
        poseController.OnSwingDetected += OnSwing;
        poseController.OnAccelerationEffect += OnAcceleration;

        Debug.Log("TestPoseController 啟動。按 C 鍵校準，按 R 鍵模擬旋轉，按 S 鍵模擬揮動。");
    }

    private void Update()
    {
        if (poseController == null) return;

        // 按鍵控制
        if (Input.GetKeyDown(KeyCode.C))
        {
            // 模擬校準數據 (手機朝前)
            Quaternion calibrateRot = Quaternion.Euler(0, 0, 0);
            poseController.OnPhoneData(calibrateRot, Vector3.zero, Vector3.zero);
            poseController.Calibrate();
            Debug.Log("校準完成！");
        }

        if (Input.GetKey(KeyCode.R))
        {
            // 模擬旋轉 (手機繞 Y 軸旋轉)
            float angle = Time.time * rotationSpeed;
            Quaternion rot = Quaternion.Euler(0, angle, 0);
            Vector3 gyro = new Vector3(0, rotationSpeed, 0); // 簡單模擬陀螺
            Vector3 acc = Vector3.zero;
            poseController.OnPhoneData(rot, gyro, acc);
        }

        if (Input.GetKeyDown(KeyCode.S))
        {
            // 模擬揮動 (快速陀螺變化)
            Quaternion rot = poseController.PhoneRotation;
            Vector3 gyro = new Vector3(gyroSwingSpeed, 0, 0); // 模擬快速旋轉
            Vector3 acc = new Vector3(0, 0, accelerationIntensity);
            poseController.OnPhoneData(rot, gyro, acc);
            Debug.Log("模擬揮動！");
        }
    }

    private void OnSwing()
    {
        Debug.Log("🎯 揮動事件觸發！");
        // 這裡可以添加視覺效果，如改變顏色或播放音效
        GetComponent<Renderer>().material.color = Color.red;
        Invoke("ResetColor", 0.2f);
    }

    private void OnAcceleration(Vector3 acc)
    {
        Debug.Log($"📳 加速度效果: {acc.magnitude:F2}");
        // 這裡可以添加粒子效果等
    }

    private void ResetColor()
    {
        GetComponent<Renderer>().material.color = Color.white;
    }

    private void OnDestroy()
    {
        // 取消訂閱事件
        if (poseController != null)
        {
            poseController.OnSwingDetected -= OnSwing;
            poseController.OnAccelerationEffect -= OnAcceleration;
        }
    }
}