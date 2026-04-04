using System;
using System.Collections;
using UnityEngine;
using TMPro;

public class ShakeDisplay : MonoBehaviour
{
    [Header("UI 組件")]
    [SerializeField] private TextMeshProUGUI shakeStatusText;
    [SerializeField] private TextMeshProUGUI shakeCountText;
    [SerializeField] private TextMeshProUGUI shakeIntensityText;
    [SerializeField] private TextMeshProUGUI shakeTypeText;
    
    [Header("搖晃狀態")]
    [SerializeField] private string currentShakeStatus = "靜止";
    [SerializeField] private int shakeCount = 0;
    [SerializeField] private float shakeIntensity = 0f;
    [SerializeField] private string shakeType = "normal";
    
    [Header("調試")]
    [SerializeField] private bool showDebugInfo = true;
    
    private GyroscopeReceiver gyroReceiver;
    private Coroutine resetStatusCoroutine;
    
    void Start()
    {
        // 自動尋找 GyroscopeReceiver 組件
        gyroReceiver = FindFirstObjectByType<GyroscopeReceiver>();
        if (gyroReceiver == null)
        {
            Debug.LogError("❌ ShakeDisplay: 找不到 GyroscopeReceiver 組件！");
            return;
        }
        
        // 訂閱搖晃事件
        GyroscopeReceiver.OnShakeDataReceived += OnShakeDataReceived;
        
        // 初始化 UI
        UpdateUI();
        
        if (showDebugInfo)
        {
            Debug.Log("✅ ShakeDisplay: 搖晃狀態顯示器已初始化");
        }
    }
    
    void OnDestroy()
    {
        // 取消訂閱事件
        GyroscopeReceiver.OnShakeDataReceived -= OnShakeDataReceived;
        
        // 停止協程
        if (resetStatusCoroutine != null)
        {
            StopCoroutine(resetStatusCoroutine);
        }
    }
    
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
        
        // 更新 UI
        UpdateUI();
        
        // 設定自動重置狀態
        if (resetStatusCoroutine != null)
        {
            StopCoroutine(resetStatusCoroutine);
        }
        resetStatusCoroutine = StartCoroutine(ResetStatusAfterDelay());
        
        if (showDebugInfo)
        {
            Debug.Log($"📳 搖晃偵測: {currentShakeStatus} (第{shakeCount}次, 強度: {shakeIntensity:F2} m/s², 類型: {shakeType})");
        }
    }
    
    private void UpdateUI()
    {
        if (shakeStatusText != null)
        {
            shakeStatusText.text = currentShakeStatus;
        }
        
        if (shakeCountText != null)
        {
            shakeCountText.text = shakeCount.ToString();
        }
        
        if (shakeIntensityText != null)
        {
            shakeIntensityText.text = shakeIntensity.ToString("F2");
        }
        
        if (shakeTypeText != null)
        {
            shakeTypeText.text = GetShakeTypeDisplayName(shakeType);
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
    
    private System.Collections.IEnumerator ResetStatusAfterDelay()
    {
        // 等待 2 秒後重置為靜止狀態
        yield return new WaitForSeconds(2f);
        
        if (currentShakeStatus != "靜止")
        {
            currentShakeStatus = "靜止";
            UpdateUI();
            
            if (showDebugInfo)
            {
                Debug.Log("🔄 搖晃狀態已重置為靜止");
            }
        }
    }
    
    // 手動重置搖晃狀態
    public void ResetShakeStatus()
    {
        currentShakeStatus = "靜止";
        shakeCount = 0;
        shakeIntensity = 0f;
        shakeType = "normal";
        UpdateUI();
        
        if (showDebugInfo)
        {
            Debug.Log("🔄 手動重置搖晃狀態");
        }
    }
    
    // 獲取當前搖晃狀態的公共方法
    public string GetCurrentShakeStatus()
    {
        return currentShakeStatus;
    }
    
    public int GetShakeCount()
    {
        return shakeCount;
    }
    
    public float GetShakeIntensity()
    {
        return shakeIntensity;
    }
    
    public string GetShakeType()
    {
        return shakeType;
    }
    
    // 設定 UI 組件的方法
    public void SetUIComponents(TextMeshProUGUI statusText, TextMeshProUGUI countText, 
                               TextMeshProUGUI intensityText, TextMeshProUGUI typeText)
    {
        shakeStatusText = statusText;
        shakeCountText = countText;
        shakeIntensityText = intensityText;
        shakeTypeText = typeText;
        UpdateUI();
    }
}