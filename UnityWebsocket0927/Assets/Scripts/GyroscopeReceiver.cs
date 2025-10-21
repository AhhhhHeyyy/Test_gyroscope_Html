using System;
using UnityEngine;
using NativeWebSocket;
using System.Collections.Generic;

public class GyroscopeReceiver : MonoBehaviour
{
    [Header("WebSocket 設置")]
    [SerializeField] private string serverUrl = "wss://testgyroscopehtml-production.up.railway.app"; // Railway線上URL
    [SerializeField] private bool autoConnect = true;
    [SerializeField] private float reconnectInterval = 5f; // 重連間隔（秒）
    
    [Header("Signaling / Room")]
    [SerializeField] private string roomId = "default-room";
    [SerializeField] private string role = "unity-receiver";
    
    [Header("陀螺儀數據")]
    [SerializeField] private float alpha = 0f;
    [SerializeField] private float beta = 0f;
    [SerializeField] private float gamma = 0f;
    
    [Header("旋转控制数据")]
    [SerializeField] private bool spinTriggered = false;
    [SerializeField] private float lastSpinAngle = 0f;
    [SerializeField] private int spinCount = 0;
    
    [Header("連接狀態")]
    [SerializeField] public bool isConnected = false;
    [SerializeField] public string connectionStatus = "未連接";
    
    private WebSocket websocket;
    private Queue<GyroscopeData> dataQueue = new Queue<GyroscopeData>();
    private Coroutine reconnectCoroutine;
    
    [System.Serializable]
    public class GyroscopeData
    {
        public float alpha;
        public float beta;
        public float gamma;
        public long timestamp;
        public int clientId;
        
        // 搖晃數據欄位（當 type 為 "shake" 時使用）
        public int count;
        public float intensity;
        public string shakeType;
        public AccelerationData acceleration;
    }
    
    [System.Serializable]
    public class AccelerationData
    {
        public float x;
        public float y;
        public float z;
    }
    
    [System.Serializable]
    public class ServerMessage
    {
        public string type;
        public string message;
        public GyroscopeData data;
        public long timestamp;
        public int clientId;
        public int size;
        public int[] image; // 螢幕捕獲數據
    }
    
    [System.Serializable]
    public struct ScreenFrame
    {
        public int clientId;
        public long timestamp;
        public byte[] data;
        public int size;
    }
    
    [System.Serializable]
    public class SpinData
    {
        public bool triggered;
        public float angle;
        public long timestamp;
    }
    
    [System.Serializable]
    public class SpinMessage
    {
        public string type;
        public bool triggered;
        public float angle;
        public long timestamp;
    }
    
    // 事件 - 新增搖晃事件和螢幕捕獲事件
    public static event Action<GyroscopeData> OnGyroscopeDataReceived;
    public static event Action<ShakeData> OnShakeDataReceived; // 新增搖晃事件
    public static event Action<ScreenFrame> OnScreenCaptureReceived; // 新增螢幕捕獲事件
    public static event Action<SpinData> OnSpinDataReceived; // 新增旋转事件
    public static event Action<SignalingMessage> OnWebRTCSignaling; // 新增 WebRTC 信令事件
    public static event Action<string> OnRawMessage; // 新增原始訊息事件
    public static event Action OnConnected;
    public static event Action OnDisconnected;
    public static event Action<string> OnError;
    
    [System.Serializable]
    public class SignalingMessage
    {
        public string type; // offer, answer, candidate
        public string sdp;
        public IceCandidate candidate;
    }

    [System.Serializable]
    public class IceCandidate
    {
        public string candidate;
        public string sdpMid;
        public int? sdpMLineIndex;
    }
    
    void Start()
    {
        if (autoConnect)
        {
            ConnectToServer();
        }
    }
    
    public async void ConnectToServer()
    {
        try
        {
            // 設置連接中狀態
            isConnected = false;
            connectionStatus = "連接中...";
            
            websocket = new WebSocket(serverUrl);
            
            websocket.OnOpen += () =>
            {
                Debug.Log("🔌 WebSocket連接已建立");
                Debug.Log($"🔍 連接URL: {serverUrl}");
                Debug.Log($"🔍 WebSocket狀態: {websocket.State}");
                isConnected = true;
                connectionStatus = "已連接";
                
                // 停止重連協程
                if (reconnectCoroutine != null)
                {
                    StopCoroutine(reconnectCoroutine);
                    reconnectCoroutine = null;
                }
                
                // 立即加入房間
                Debug.Log("✅ WS Connected, sending join");
                var join = new { type = "join", room = roomId, role = role };
                websocket.SendText(JsonUtility.ToJson(join));
                Debug.Log($"✅ 已發送加入房間請求: {roomId} as {role}");
                
                OnConnected?.Invoke();
            };
            
            websocket.OnError += (error) =>
            {
                if (this != null) // 檢查物件是否還存在
                {
                    Debug.LogError($"❌ WebSocket錯誤: {error}");
                    isConnected = false;
                    connectionStatus = $"錯誤: {error}";
                    OnError?.Invoke(error);
                }
            };
            
            websocket.OnClose += (closeCode) =>
            {
                if (this != null) // 檢查物件是否還存在
                {
                    Debug.Log($"🔌 WebSocket連接已關閉: {closeCode}");
                    Debug.Log($"🔍 關閉原因代碼: {closeCode} (1000=正常關閉, 1001=離開, 1002=錯誤, 1003=不支援數據)");
                    isConnected = false;
                    connectionStatus = "已斷線";
                    OnDisconnected?.Invoke();
                    
                    // 啟動自動重連
                    if (reconnectCoroutine == null)
                    {
                        reconnectCoroutine = StartCoroutine(AutoReconnect());
                    }
                }
            };
            
            websocket.OnMessage += (bytes) =>
            {
                try
                {
                    string message = System.Text.Encoding.UTF8.GetString(bytes);
                    Debug.Log($"📱 收到原始訊息: {message}");
                    
                    // 觸發原始訊息事件
                    OnRawMessage?.Invoke(message);
                    
                    // 解析服務器消息格式
                    var serverMessage = JsonUtility.FromJson<ServerMessage>(message);
                    Debug.Log($"🔍 解析後的消息類型: {serverMessage.type}");
                    Debug.Log($"🔍 消息內容: {JsonUtility.ToJson(serverMessage, true)}");
                    
                    // 處理不同類型的消息
                    switch (serverMessage.type)
                    {
                        case "connection":
                            Debug.Log($"🔌 連接確認: {serverMessage.message}");
                            break;
                            
                        case "gyroscope":
                            // 處理陀螺儀數據
                            Debug.Log($"🎯 收到陀螺儀消息，數據是否為空: {serverMessage.data == null}");
                            if (serverMessage.data != null)
                            {
                                var gyroData = serverMessage.data;
                                Debug.Log($"📊 原始陀螺儀數據: Alpha={gyroData.alpha}, Beta={gyroData.beta}, Gamma={gyroData.gamma}");
                                Debug.Log($"📊 數據詳情: Timestamp={gyroData.timestamp}, ClientId={gyroData.clientId}");
                                
                                // 更新數據
                                alpha = gyroData.alpha;
                                beta = gyroData.beta;
                                gamma = gyroData.gamma;
                                
                                // 加入佇列
                                dataQueue.Enqueue(gyroData);
                                
                                // 觸發事件
                                OnGyroscopeDataReceived?.Invoke(gyroData);
                                
                                Debug.Log($"📊 更新後陀螺儀數據: Alpha={alpha:F2}, Beta={beta:F2}, Gamma={gamma:F2}");
                                Debug.Log($"📊 事件已觸發，訂閱者數量: {OnGyroscopeDataReceived?.GetInvocationList()?.Length ?? 0}");
                            }
                            else
                            {
                                Debug.LogWarning("⚠️ 陀螺儀數據為空！");
                                Debug.LogWarning($"⚠️ 完整消息內容: {message}");
                            }
                            break;
                            
                        case "shake":
                            // 處理搖晃數據 - 修正解析方式
                            Debug.Log($"📳 收到搖晃消息: {message}");
                            try
                            {
                                // 使用外層已解析的 serverMessage
                                var shakeData = new ShakeData
                                {
                                    count = serverMessage.data.count,
                                    intensity = serverMessage.data.intensity,
                                    shakeType = serverMessage.data.shakeType,
                                    acceleration = new Vector3(
                                        serverMessage.data.acceleration.x,
                                        serverMessage.data.acceleration.y,
                                        serverMessage.data.acceleration.z
                                    ),
                                    timestamp = serverMessage.data.timestamp
                                };
                                
                                Debug.Log($"📳 搖晃數據: Count={shakeData.count}, Intensity={shakeData.intensity:F2}, Type={shakeData.shakeType}");
                                
                                // 觸發搖晃事件
                                OnShakeDataReceived?.Invoke(shakeData);
                            }
                            catch (System.Exception e)
                            {
                                Debug.LogError($"❌ 解析搖晃數據錯誤: {e.Message}");
                            }
                            break;
                            
                        case "screen_capture":
                            // 處理螢幕捕獲數據
                            Debug.Log($"📺 收到螢幕捕獲消息: {message}");
                            try
                            {
                                var screenFrame = new ScreenFrame
                                {
                                    clientId = serverMessage.clientId,
                                    timestamp = serverMessage.timestamp,
                                    size = serverMessage.size,
                                    data = System.Array.ConvertAll(serverMessage.image, x => (byte)x)
                                };
                                
                                Debug.Log($"📺 螢幕捕獲: ClientId={screenFrame.clientId}, Size={screenFrame.size} bytes");
                                
                                // 觸發螢幕捕獲事件
                                OnScreenCaptureReceived?.Invoke(screenFrame);
                            }
                            catch (System.Exception e)
                            {
                                Debug.LogError($"❌ 解析螢幕捕獲數據錯誤: {e.Message}");
                            }
                            break;
                            
                        case "spin":
                            // 處理旋转事件
                            Debug.Log($"🎯 收到旋转事件: {message}");
                            try
                            {
                                // 直接解析spin消息，因为数据结构不同
                                var spinMessage = JsonUtility.FromJson<SpinMessage>(message);
                                var spinData = new SpinData
                                {
                                    triggered = spinMessage.triggered,
                                    angle = spinMessage.angle,
                                    timestamp = spinMessage.timestamp
                                };
                                
                                spinTriggered = true;
                                lastSpinAngle = spinData.angle;
                                spinCount++;
                                
                                Debug.Log($"🎯 旋转触发! Count={spinCount}, Angle={spinData.angle:F2}");
                                
                                OnSpinDataReceived?.Invoke(spinData);
                                
                                // 0.5秒后重置状态
                                StartCoroutine(ResetSpinStatus());
                            }
                            catch (System.Exception e)
                            {
                                Debug.LogError($"❌ 解析旋转数据错误: {e.Message}");
                            }
                            break;
                            
                        case "offer":
                        case "answer":
                        case "candidate":
                            // WebRTC 信令處理
                            var signalingMsg = JsonUtility.FromJson<SignalingMessage>(message);
                            OnWebRTCSignaling?.Invoke(signalingMsg);
                            Debug.Log($"📡 收到 WebRTC 信令: {signalingMsg.type}");
                            break;
                            
                        case "joined":
                            Debug.Log($"✅ 已加入房間");
                            break;
                            
                        case "ready":
                            Debug.Log($"🚀 房間準備就緒: {serverMessage.message}");
                            Debug.Log($"🚀 等待前端發送WebRTC offer");
                            break;
                            
                        case "ack":
                            Debug.Log($"✅ 確認: {serverMessage.message}");
                            break;
                            
                        case "error":
                            Debug.LogError($"❌ 服務器錯誤: {serverMessage.message}");
                            OnError?.Invoke(serverMessage.message);
                            break;
                            
                        default:
                            Debug.LogWarning($"⚠️ 未知消息類型: {serverMessage.type}");
                            break;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"❌ 解析訊息錯誤: {e.Message}");
                    Debug.LogError($"❌ 原始訊息: {System.Text.Encoding.UTF8.GetString(bytes)}");
                    Debug.LogError($"❌ 錯誤堆疊: {e.StackTrace}");
                }
            };
            
            await websocket.Connect();
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ 連接失敗: {e.Message}");
            connectionStatus = $"連接失敗: {e.Message}";
            OnError?.Invoke(e.Message);
        }
    }
    
    void Update()
    {
        #if !UNITY_WEBGL || UNITY_EDITOR
        if (websocket != null)
        {
            websocket.DispatchMessageQueue();
            
            // 檢查連接狀態
            if (websocket.State != WebSocketState.Open && isConnected)
            {
                Debug.LogWarning($"⚠️ WebSocket狀態不同步! Unity認為已連接，但實際狀態: {websocket.State}");
                isConnected = false;
                connectionStatus = "連接狀態不同步";
            }
        }
        else
        {
            Debug.LogWarning("⚠️ WebSocket為空！");
        }
        #endif
    }
    
    public void Disconnect()
    {
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            websocket.Close();
        }
    }
    
    // 獲取最新的陀螺儀數據
    public GyroscopeData GetLatestData()
    {
        if (dataQueue.Count > 0)
        {
            return dataQueue.Dequeue();
        }
        return null;
    }
    
    // 獲取所有排隊的數據
    public List<GyroscopeData> GetAllQueuedData()
    {
        List<GyroscopeData> allData = new List<GyroscopeData>();
        while (dataQueue.Count > 0)
        {
            allData.Add(dataQueue.Dequeue());
        }
        return allData;
    }
    
    // 清空數據佇列
    public void ClearDataQueue()
    {
        dataQueue.Clear();
    }
    
    // 發送原始消息（用於 WebRTC 信令）
    public void SendRaw(string message)
    {
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            websocket.SendText(message);
        }
    }
    
    // 發送 JSON 物件
    public void SendJson(object message)
    {
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            string json = JsonUtility.ToJson(message);
            websocket.SendText(json);
            Debug.Log($"📤 發送 JSON: {json}");
        }
        else
        {
            Debug.LogWarning("⚠️ WebSocket未連接，無法發送JSON");
        }
    }
    
    // 加入房間
    public void JoinRoom(string roomId, string role)
    {
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            var joinMessage = JsonUtility.ToJson(new
            {
                type = "join",
                room = roomId,
                role = role
            });
            
            websocket.SendText(joinMessage);
            Debug.Log($"✅ 已發送加入房間請求: {roomId} as {role}");
        }
        else
        {
            Debug.LogWarning("⚠️ WebSocket未連接，無法加入房間");
        }
    }
    
    private System.Collections.IEnumerator AutoReconnect()
    {
        while (!isConnected)
        {
            yield return new WaitForSeconds(reconnectInterval);
            
            if (!isConnected)
            {
                Debug.Log($"🔄 嘗試重新連接... ({reconnectInterval}秒後)");
                ConnectToServer();
            }
        }
        
        reconnectCoroutine = null;
    }
    
    private System.Collections.IEnumerator ResetSpinStatus()
    {
        yield return new WaitForSeconds(0.5f);
        spinTriggered = false;
    }
    
    private async void OnApplicationQuit()
    {
        if (websocket != null)
        {
            await websocket.Close();
        }
    }
    
    // 在Inspector中顯示連接狀態
    void OnGUI()
    {
        if (Application.isPlaying)
        {
            GUILayout.BeginArea(new Rect(10, 10, 300, 200));
            GUILayout.Label($"連接狀態: {connectionStatus}");
            GUILayout.Label($"Alpha: {alpha:F2}");
            GUILayout.Label($"Beta: {beta:F2}");
            GUILayout.Label($"Gamma: {gamma:F2}");
            GUILayout.Label($"佇列數據: {dataQueue.Count}");
            GUILayout.Label($"旋转状态: {(spinTriggered ? "已触发" : "未触发")}");
            GUILayout.Label($"旋转次数: {spinCount}");
            GUILayout.Label($"最后角度: {lastSpinAngle:F2}°");
            
            if (!isConnected && GUILayout.Button("重新連接"))
            {
                ConnectToServer();
            }
            
            if (isConnected && GUILayout.Button("斷線"))
            {
                Disconnect();
            }
            
            GUILayout.EndArea();
        }
    }
}