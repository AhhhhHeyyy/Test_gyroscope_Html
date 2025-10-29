using System.Collections;
using UnityEngine;

public class GyroscopeController : MonoBehaviour
{
    [Header("陀螺儀控制設定")]
    [SerializeField] private GyroscopeReceiver gyroReceiver;
    [SerializeField] private bool enableRotation = true;
    [SerializeField] private bool enablePosition = false;
    [SerializeField] private float rotationSensitivity = 1f;
    [SerializeField] private float positionSensitivity = 0.01f;
    
    [Header("平滑設定")]
    [SerializeField] private bool enableSmoothing = true;
    [SerializeField] private float smoothingFactor = 0.1f;
    
    [Header("限制設定")]
    [SerializeField] private bool enableRotationLimits = false;
    [SerializeField] private Vector2 xRotationLimits = new Vector2(-90f, 90f);
    [SerializeField] private Vector2 yRotationLimits = new Vector2(-180f, 180f);
    [SerializeField] private Vector2 zRotationLimits = new Vector2(-180f, 180f);
    
    [Header("旋转盘控制設定")]
    [SerializeField] private bool enableSpinRotation = true;
    [SerializeField] private bool useSpinSmoothing = true;
    [SerializeField] private float spinSmoothingFactor = 0.1f;
    
    [Header("調試")]
    [SerializeField] private bool showDebugInfo = false;
    
    // 搖晃狀態變數
    private string currentShakeStatus = "靜止";
    private int shakeCount = 0;
    private float shakeIntensity = 0f;
    private string shakeType = "normal";
    
    private Vector3 currentRotation = Vector3.zero;
    private Vector3 targetRotation = Vector3.zero;
    private Vector3 smoothedRotation = Vector3.zero;
    private Vector3 lastPosition = Vector3.zero;
    
    // 旋转盘角度控制
    private float targetSpinAngle = 0f;
    private float currentSpinAngle = 0f;
    
    void Start()
    {
        // 自動尋找 GyroscopeReceiver 組件
        if (gyroReceiver == null)
        {
            gyroReceiver = FindFirstObjectByType<GyroscopeReceiver>();
            if (gyroReceiver == null)
            {
                Debug.LogError("❌ GyroscopeController: 找不到 GyroscopeReceiver 組件！");
                return;
            }
        }
        
        // 訂閱陀螺儀數據事件
        GyroscopeReceiver.OnGyroscopeDataReceived += OnGyroscopeDataReceived;
        
        // 訂閱搖晃數據事件
        GyroscopeReceiver.OnShakeDataReceived += OnShakeDataReceived;
        
        // 訂閱旋轉盤數據事件
        GyroscopeReceiver.OnSpinDataReceived += OnSpinDataReceived;
        
        // 初始化旋轉
        currentRotation = transform.eulerAngles;
        targetRotation = currentRotation;
        smoothedRotation = currentRotation;
        
        Debug.Log("🎮 GyroscopeController 已啟動");
    }
    
    void OnDestroy()
    {
        // 取消訂閱事件
        GyroscopeReceiver.OnGyroscopeDataReceived -= OnGyroscopeDataReceived;
        GyroscopeReceiver.OnShakeDataReceived -= OnShakeDataReceived;
        GyroscopeReceiver.OnSpinDataReceived -= OnSpinDataReceived;
    }
    
    void Update()
    {
        if (enableRotation)
        {
            ApplyRotation();
        }
        
        if (enablePosition)
        {
            ApplyPosition();
        }
        
        // 应用旋转盘角度（持续读取）
        if (enableSpinRotation && gyroReceiver != null)
        {
            ApplySpinRotation();
        }
        
        if (showDebugInfo)
        {
            ShowDebugInfo();
        }
    }
    
    private void OnGyroscopeDataReceived(GyroscopeReceiver.GyroscopeData data)
    {
        Debug.Log($"🎮 GyroscopeController 收到數據: Alpha={data.alpha:F2}, Beta={data.beta:F2}, Gamma={data.gamma:F2}");
        
        // 更新目標旋轉
        targetRotation = new Vector3(
            -data.beta * rotationSensitivity,  // 前後傾斜 → X軸旋轉
            data.gamma * rotationSensitivity,  // 左右傾斜 → Y軸旋轉
            -data.alpha * rotationSensitivity  // 羅盤旋轉 → Z軸旋轉
        );
        
        Debug.Log($"🎮 計算後目標旋轉: {targetRotation}");
        
        // 應用旋轉限制
        if (enableRotationLimits)
        {
            targetRotation.x = Mathf.Clamp(targetRotation.x, xRotationLimits.x, xRotationLimits.y);
            targetRotation.y = Mathf.Clamp(targetRotation.y, yRotationLimits.x, yRotationLimits.y);
            targetRotation.z = Mathf.Clamp(targetRotation.z, zRotationLimits.x, zRotationLimits.y);
            Debug.Log($"🎮 限制後目標旋轉: {targetRotation}");
        }
        
        // 更新位置（如果啟用）
        if (enablePosition)
        {
            Vector3 deltaPosition = new Vector3(
                data.gamma * positionSensitivity,
                data.beta * positionSensitivity,
                0f
            );
            lastPosition += deltaPosition;
            Debug.Log($"🎮 更新位置: {lastPosition}");
        }
    }
    
    // 旋转盘事件处理函数
    private void OnSpinDataReceived(GyroscopeReceiver.SpinData spinData)
    {
        // 更新目标旋转角度为网页端传来的角度
        targetSpinAngle = spinData.angle;
        
        Debug.Log($"🎯 收到旋转盘事件: 角度={spinData.angle:F2}°");
    }
    
    // 应用旋转盘角度
    private void ApplySpinRotation()
    {
        // 持续读取 GyroscopeReceiver 的 LastSpinAngle（保持最新值）
        if (gyroReceiver != null)
        {
            targetSpinAngle = gyroReceiver.LastSpinAngle;
        }
        
        if (useSpinSmoothing)
        {
            // 平滑插值到目标角度
            currentSpinAngle = Mathf.LerpAngle(currentSpinAngle, targetSpinAngle, spinSmoothingFactor * Time.deltaTime * 60f);
        }
        else
        {
            // 直接应用角度
            currentSpinAngle = targetSpinAngle;
        }
        
        // 应用到 Transform 的 Y 轴旋转
        transform.rotation = Quaternion.Euler(transform.eulerAngles.x, currentSpinAngle, transform.eulerAngles.z);
    }
    
    // 搖晃事件處理函數
    private void OnShakeDataReceived(ShakeData shakeData)
    {
        // 更新搖晃數據
        shakeCount = shakeData.count;
        shakeIntensity = shakeData.intensity;
        shakeType = shakeData.shakeType;
        
        // 根據搖晃類型設定狀態文字
        switch (shakeType)
        {
            case "intense":
                currentShakeStatus = "劇烈搖晃！";
                break;
            case "strong":
                currentShakeStatus = "強烈搖晃！";
                break;
            default:
                currentShakeStatus = "搖晃中！";
                break;
        }
        
        Debug.Log($"📳 搖晃偵測: {currentShakeStatus} (第{shakeCount}次, 強度: {shakeIntensity:F2} m/s²)");
        
        // 2秒後重置為靜止狀態
        StartCoroutine(ResetShakeStatusAfterDelay());
    }
    
    private System.Collections.IEnumerator ResetShakeStatusAfterDelay()
    {
        yield return new WaitForSeconds(2f);
        currentShakeStatus = "靜止";
    }
    
    private void ApplyRotation()
    {
        if (enableSmoothing)
        {
            // 平滑旋轉
            smoothedRotation = Vector3.Lerp(smoothedRotation, targetRotation, smoothingFactor * Time.deltaTime);
            transform.rotation = Quaternion.Euler(smoothedRotation);
        }
        else
        {
            // 直接應用旋轉
            transform.rotation = Quaternion.Euler(targetRotation);
        }
    }
    
    private void ApplyPosition()
    {
        if (enablePosition)
        {
            transform.position = lastPosition;
        }
    }
    
    private void ShowDebugInfo()
    {
        Debug.Log($"🎮 GyroscopeController - 目標旋轉: {targetRotation}, 當前旋轉: {transform.eulerAngles}");
    }
    
    // 公共方法
    public void SetRotationSensitivity(float sensitivity)
    {
        rotationSensitivity = sensitivity;
    }
    
    public void SetPositionSensitivity(float sensitivity)
    {
        positionSensitivity = sensitivity;
    }
    
    public void SetSmoothing(bool enabled, float factor = 0.1f)
    {
        enableSmoothing = enabled;
        smoothingFactor = factor;
    }
    
    public void ResetRotation()
    {
        targetRotation = Vector3.zero;
        smoothedRotation = Vector3.zero;
        transform.rotation = Quaternion.identity;
    }
    
    public void ResetPosition()
    {
        lastPosition = Vector3.zero;
        transform.position = Vector3.zero;
    }
    
    public void ResetAll()
    {
        ResetRotation();
        ResetPosition();
    }
    
    // 手動測試方法
    [ContextMenu("測試旋轉")]
    public void TestRotation()
    {
        targetRotation = new Vector3(45f, 45f, 45f);
        Debug.Log("🧪 測試旋轉已啟動");
    }
    
    [ContextMenu("重置所有")]
    public void TestReset()
    {
        ResetAll();
        Debug.Log("🔄 重置所有已執行");
    }
    
    // 在Inspector中顯示當前狀態
    void OnGUI()
    {
        if (showDebugInfo && Application.isPlaying)
        {
            // 陀螺儀數據顯示
            GUILayout.BeginArea(new Rect(10, 220, 300, 150));
            GUILayout.Label($"目標旋轉: {targetRotation}");
            GUILayout.Label($"當前旋轉: {transform.eulerAngles}");
            GUILayout.Label($"平滑旋轉: {smoothedRotation}");
            GUILayout.Label($"位置: {transform.position}");
            
            if (GUILayout.Button("重置旋轉"))
            {
                ResetRotation();
            }
            
            if (GUILayout.Button("重置位置"))
            {
                ResetPosition();
            }
            
            GUILayout.EndArea();
            
            // 搖晃狀態顯示
            GUILayout.BeginArea(new Rect(10, 380, 300, 120));
            GUILayout.Label($"搖晃狀態: {currentShakeStatus}");
            GUILayout.Label($"搖晃次數: {shakeCount}");
            GUILayout.Label($"搖晃強度: {shakeIntensity:F2} m/s²");
            GUILayout.Label($"搖晃類型: {GetShakeTypeDisplayName(shakeType)}");
            GUILayout.EndArea();
        }
    }
    
    private string GetShakeTypeDisplayName(string type)
    {
        switch (type)
        {
            case "intense":
                return "劇烈";
            case "strong":
                return "強烈";
            case "normal":
                return "一般";
            default:
                return "未知";
        }
    }
}