using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class GyroscopeDebugger : MonoBehaviour
{
    [Header("UI 元素")]
    public Text statusText;
    public Text dataText;
    public Text connectionText;
    public Button connectButton;
    public Button disconnectButton;
    
    [Header("調試設定")]
    public bool enableDetailedLogging = true;
    public float updateInterval = 0.1f;
    
    private GyroscopeReceiver gyroReceiver;
    private Coroutine debugCoroutine;
    
    void Start()
    {
        // 獲取 GyroscopeReceiver 組件
        gyroReceiver = FindFirstObjectByType<GyroscopeReceiver>();
        if (gyroReceiver == null)
        {
            Debug.LogError("❌ 找不到 GyroscopeReceiver 組件！");
            return;
        }
        
        // 設定按鈕事件
        if (connectButton != null)
            connectButton.onClick.AddListener(() => gyroReceiver.ConnectToServer());
        
        if (disconnectButton != null)
            disconnectButton.onClick.AddListener(() => gyroReceiver.Disconnect());
        
        // 訂閱事件
        GyroscopeReceiver.OnConnected += OnConnected;
        GyroscopeReceiver.OnDisconnected += OnDisconnected;
        GyroscopeReceiver.OnGyroscopeDataReceived += OnGyroscopeDataReceived;
        GyroscopeReceiver.OnError += OnError;
        
        // 開始調試協程
        if (debugCoroutine != null)
            StopCoroutine(debugCoroutine);
        debugCoroutine = StartCoroutine(DebugUpdate());
        
        Debug.Log("🔍 GyroscopeDebugger 已啟動");
    }
    
    void OnDestroy()
    {
        // 取消訂閱事件
        GyroscopeReceiver.OnConnected -= OnConnected;
        GyroscopeReceiver.OnDisconnected -= OnDisconnected;
        GyroscopeReceiver.OnGyroscopeDataReceived -= OnGyroscopeDataReceived;
        GyroscopeReceiver.OnError -= OnError;
        
        if (debugCoroutine != null)
            StopCoroutine(debugCoroutine);
    }
    
    private void OnConnected()
    {
        Debug.Log("✅ GyroscopeDebugger: 連接已建立");
        UpdateConnectionStatus("已連接", Color.green);
    }
    
    private void OnDisconnected()
    {
        Debug.Log("❌ GyroscopeDebugger: 連接已斷開");
        UpdateConnectionStatus("已斷開", Color.red);
    }
    
    private void OnGyroscopeDataReceived(GyroscopeReceiver.GyroscopeData data)
    {
        if (enableDetailedLogging)
        {
            Debug.Log($"📊 GyroscopeDebugger: 收到數據 - Alpha: {data.alpha:F2}, Beta: {data.beta:F2}, Gamma: {data.gamma:F2}");
        }
        UpdateDataDisplay(data);
    }
    
    private void OnError(string error)
    {
        Debug.LogError($"❌ GyroscopeDebugger: 錯誤 - {error}");
        UpdateConnectionStatus($"錯誤: {error}", Color.red);
    }
    
    private IEnumerator DebugUpdate()
    {
        while (true)
        {
            yield return new WaitForSeconds(updateInterval);
            
            if (gyroReceiver != null)
            {
                UpdateStatusDisplay();
            }
        }
    }
    
    private void UpdateStatusDisplay()
    {
        if (statusText != null)
        {
            string status = $"連接狀態: {gyroReceiver.connectionStatus}\n";
            status += $"活躍連接: {gyroReceiver.isConnected}\n";
            status += $"佇列數據: {gyroReceiver.GetAllQueuedData().Count}\n";
            status += $"時間: {System.DateTime.Now:HH:mm:ss}";
            statusText.text = status;
        }
    }
    
    private void UpdateConnectionStatus(string status, Color color)
    {
        if (connectionText != null)
        {
            connectionText.text = status;
            connectionText.color = color;
        }
    }
    
    private void UpdateDataDisplay(GyroscopeReceiver.GyroscopeData data)
    {
        if (dataText != null)
        {
            string dataStr = $"Alpha: {data.alpha:F2}°\n";
            dataStr += $"Beta: {data.beta:F2}°\n";
            dataStr += $"Gamma: {data.gamma:F2}°\n";
            dataStr += $"時間戳: {data.timestamp}\n";
            dataStr += $"客戶端ID: {data.clientId}";
            dataText.text = dataStr;
        }
    }
    
    // 手動測試方法
    [ContextMenu("測試連接")]
    public void TestConnection()
    {
        if (gyroReceiver != null)
        {
            Debug.Log("🔍 開始測試連接...");
            gyroReceiver.ConnectToServer();
        }
    }
    
    [ContextMenu("清空數據佇列")]
    public void ClearDataQueue()
    {
        if (gyroReceiver != null)
        {
            gyroReceiver.ClearDataQueue();
            Debug.Log("🧹 數據佇列已清空");
        }
    }
    
    [ContextMenu("顯示所有佇列數據")]
    public void ShowAllQueuedData()
    {
        if (gyroReceiver != null)
        {
            var allData = gyroReceiver.GetAllQueuedData();
            Debug.Log($"📋 佇列中有 {allData.Count} 個數據");
            
            for (int i = 0; i < allData.Count; i++)
            {
                var data = allData[i];
                Debug.Log($"數據 {i + 1}: Alpha={data.alpha:F2}, Beta={data.beta:F2}, Gamma={data.gamma:F2}");
            }
        }
    }
}
