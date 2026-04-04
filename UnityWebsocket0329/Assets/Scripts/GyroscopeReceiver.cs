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
    [SerializeField] private bool debugLog = false; // 是否顯示調試日誌
    
    [Header("陀螺儀數據")]
    [SerializeField] private float alpha = 0f;
    [SerializeField] private float beta = 0f;
    [SerializeField] private float gamma = 0f;
    
    [Header("加速度數據")]
    [SerializeField] private Vector3 accelerationVector = Vector3.zero;
    [SerializeField] private float accelerationMagnitude = 0f;
    
    [Header("旋转控制数据")]
    [SerializeField] private bool spinTriggered = false;
    [SerializeField] private float lastSpinAngle = 0f;
    [SerializeField] private int spinCount = 0;
    
    [Header("旋钮模式")]
    [SerializeField] private string currentSpinMode = "120° 吸附";
    [SerializeField] private string currentSpinModeKey = "default";
    [SerializeField] private float currentSpinSnapAngle = 120f;
    [SerializeField] private long lastSpinModeTimestamp = 0;

    [Header("AR 相機相對 Marker 位姿（由 ar-threex-camera 頁面經 WebSocket 傳來）")]
    [SerializeField] private bool arMarkerVisible = false;
    [SerializeField] private Vector3 arCameraPosition = Vector3.zero;
    [SerializeField] private Vector3 arCameraRotationDegrees = Vector3.zero;

    // 用於紀錄目前 Web 端模式（false=120°，true=90°），只在 Unity 這邊做切換邏輯用
    private bool webSpinIs90Mode = false;

    [Header("Value")]
    public float m_alpha = 0f;
    public float m_beta = 0f;
    public float m_gamma = 0f;
    public float m_qx = 0f;
    public float m_qy = 0f;
    public float m_qz = 0f;
    public float m_qw = 1f;

    public float m_lastSpinAngle = 0f;
    public int m_spinCount = 0;
    
    // 公共属性：允许外部脚本访问当前旋转角度
    public float LastSpinAngle => lastSpinAngle;

    // AR 相機相對 Marker 位姿（由 ar-threex-camera 頁面經 WebSocket 傳來）
    public bool ARMarkerVisible => arMarkerVisible;
    public Vector3 ARCameraPosition => arCameraPosition;
    public Vector3 ARCameraRotationDegrees => arCameraRotationDegrees;
    
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
        public string mode;
        public float snapAngle;
        public string label;
        
        // 旋转数据字段（当 type 为 "spin" 时使用）
        public bool triggered;
        public float angle;

        // 揮動數據欄位（當 type 為 "pitch_wave" 時使用）
        public float change;
        public string direction;

        // 網頁「上下移動」已映射值，直接對應 Unity Y 軸
        public float unityY;

        // 四元數（由 JS 端從 alpha/beta/gamma 計算，避免萬向節死鎖）
        public float qx;
        public float qy;
        public float qz;
        public float qw;
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
        public PositionData positionData; // 位置數據（當 type 為 "position" 時使用）
        public long timestamp;
        public int clientId;
        public int size;
        public int[] image; // 螢幕捕獲數據
    }
    
    [System.Serializable]
    public class PositionDataMessage
    {
        public string type;
        public PositionDataContent data;
        public long timestamp;
        public int clientId;
    }
    
    [System.Serializable]
    public class PositionDataContent
    {
        public PositionVector position;
        public RotationQuaternion rotation;
        public PositionVector delta;
        public long timestamp;
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
    public class SpinModeStatus
    {
        public string mode;
        public string label;
        public float snapAngle;
        public long timestamp;
    }

    [System.Serializable]
    public class PitchWaveData
    {
        public int count;
        public float change;   // 角度變化量（度）
        public float beta;     // 觸發時的 beta 值
        public string direction; // "up" 或 "down"
        public long timestamp;
    }
    
    [System.Serializable]
    public class PositionData
    {
        public PositionVector position;
        public RotationQuaternion rotation;
        public PositionVector delta;
        public long timestamp;
    }
    
    [System.Serializable]
    public class PositionVector
    {
        public float x;
        public float y;
        public float z;
    }
    
    [System.Serializable]
    public class RotationQuaternion
    {
        public float x;
        public float y;
        public float z;
        public float w;
    }

    /// <summary>AR 頁面傳來的「相機相對 Marker」位姿（位置 + 歐拉角度 + 是否偵測到）</summary>
    [System.Serializable]
    public class ARCameraPoseData
    {
        public PositionVector position;
        public ArEulerDegrees rotation;  // 歐拉角，單位：度
        public bool markerVisible;
    }

    [System.Serializable]
    public class ArEulerDegrees
    {
        public float x;
        public float y;
        public float z;
    }

    [System.Serializable]
    public class ARCameraPoseMessage
    {
        public string type;
        public ARCameraPoseData data;
        public long timestamp;
        public int clientId;
    }
    
    
    // 事件 - 新增搖晃事件和螢幕捕獲事件
    public static event Action<GyroscopeData> OnGyroscopeDataReceived;
    public static event Action<ShakeData> OnShakeDataReceived; // 新增搖晃事件
    public static event Action<Vector3> OnAccelerationReceived; // 新增加速度事件
    public static event Action<ScreenFrame> OnScreenCaptureReceived; // 新增螢幕捕獲事件
    public static event Action<SpinData> OnSpinDataReceived; // 新增旋转事件
    public static event Action<SignalingMessage> OnWebRTCSignaling; // 新增 WebRTC 信令事件
    public static event Action<string> OnRawMessage; // 新增原始訊息事件
    public static event Action<SpinModeStatus> OnSpinModeStatusReceived; // 新增旋钮模式事件
    public static event Action<PitchWaveData> OnPitchWaveReceived; // 新增揮動事件
    public static event Action<PositionData> OnPositionDataReceived; // 新增位置數據事件
    public static event Action<ARCameraPoseData> OnARCameraPoseReceived; // AR 相機相對 Marker 位姿
    public static event Action OnConnected;
    public static event Action OnDisconnected;
    public static event Action<string> OnError;

    // 供外部元件（如 UdpGyroscopeReceiver）觸發事件的靜態方法
    public static void RaiseAccelerationReceived(Vector3 acc)     => OnAccelerationReceived?.Invoke(acc);
    public static void RaiseShakeDataReceived(ShakeData data)     => OnShakeDataReceived?.Invoke(data);
    public static void RaisePitchWaveReceived(PitchWaveData data)  => OnPitchWaveReceived?.Invoke(data);
    public static void RaiseGyroscopeDataReceived(GyroscopeData data) => OnGyroscopeDataReceived?.Invoke(data);
    
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
                    if (debugLog) Debug.Log($"📱 收到原始訊息: {message}");
                    
                    // 檢查是否包含 spin_mode（只對 spin_mode 消息顯示特殊標記）
                    if (message.Contains("spin_mode"))
                    {
                        if (debugLog) Debug.Log($"🎯 檢測到 spin_mode 消息！");
                    }
                    
                    // 觸發原始訊息事件
                    OnRawMessage?.Invoke(message);
                    
                    // 解析服務器消息格式
                    var serverMessage = JsonUtility.FromJson<ServerMessage>(message);
                    
                    if (serverMessage == null)
                    {
                        Debug.LogError($"❌ 解析失敗：serverMessage 為 null");
                        return;
                    }
                    
                    if (debugLog) Debug.Log($"🔍 解析後的消息類型: '{serverMessage.type}' (長度: {(serverMessage.type?.Length ?? 0)})");
                    
                    if (string.IsNullOrEmpty(serverMessage.type))
                    {
                        Debug.LogWarning($"⚠️ 消息類型為空或 null！");
                    }
                    
                    if (serverMessage.type == "spin_mode")
                    {
                        if (debugLog) Debug.Log($"🎯 確認消息類型為 spin_mode，準備處理...");
                    }
                    
                    if (debugLog) Debug.Log($"🔍 消息內容: {JsonUtility.ToJson(serverMessage, true)}");
                    
                    if (serverMessage.data != null)
                    {
                        if (debugLog) Debug.Log($"🔍 data 不為 null，檢查 data 內容...");
                    }
                    else
                    {
                        Debug.LogWarning($"⚠️ serverMessage.data 為 null");
                    }
                    
                    // 處理不同類型的消息
                    switch (serverMessage.type)
                    {
                        case "connection":
                            Debug.Log($"🔌 連接確認: {serverMessage.message}");
                            break;
                            
                        case "gyroscope":
                            // 處理陀螺儀數據
                            if (debugLog) Debug.Log($"🎯 收到陀螺儀消息，數據是否為空: {serverMessage.data == null}");
                            if (serverMessage.data != null)
                            {
                                var gyroData = serverMessage.data;
                                if (debugLog) Debug.Log($"📊 原始陀螺儀數據: Alpha={gyroData.alpha}, Beta={gyroData.beta}, Gamma={gyroData.gamma}");
                                if (debugLog) Debug.Log($"📊 數據詳情: Timestamp={gyroData.timestamp}, ClientId={gyroData.clientId}");
                                
                                // 更新數據
                                alpha = gyroData.alpha;
                                beta = gyroData.beta;
                                gamma = gyroData.gamma;
                                m_qx = gyroData.qx;
                                m_qy = gyroData.qy;
                                m_qz = gyroData.qz;
                                m_qw = gyroData.qw;
                                
                                // 觸發事件
                                OnGyroscopeDataReceived?.Invoke(gyroData);
                                
                                if (debugLog) Debug.Log($"📊 更新後陀螺儀數據: Alpha={alpha:F2}, Beta={beta:F2}, Gamma={gamma:F2}");
                                if (debugLog) Debug.Log($"📊 事件已觸發，訂閱者數量: {OnGyroscopeDataReceived?.GetInvocationList()?.Length ?? 0}");
                            }
                            else
                            {
                                Debug.LogWarning("⚠️ 陀螺儀數據為空！");
                                Debug.LogWarning($"⚠️ 完整消息內容: {message}");
                            }
                            break;
                            
                        case "shake":
                            // 處理搖晃數據 - 修正解析方式
                            if (debugLog) Debug.Log($"📳 收到搖晃消息: {message}");
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

                                if (debugLog) Debug.Log($"📳 搖晃數據: Count={shakeData.count}, Intensity={shakeData.intensity:F2}, Type={shakeData.shakeType}");
                                
                                // 觸發搖晃事件
                                OnShakeDataReceived?.Invoke(shakeData);
                            }
                            catch (System.Exception e)
                            {
                                Debug.LogError($"❌ 解析搖晃數據錯誤: {e.Message}");
                            }
                            break;
                            
                        case "acceleration":
                            // 處理加速度數據
                            if (debugLog) Debug.Log($"📳 收到加速度消息: {message}");
                            try
                            {
                                if (serverMessage.data != null && serverMessage.data.acceleration != null)
                                {
                                    var acc = serverMessage.data.acceleration;
                                    // 手機坐標系到Unity坐標系的轉換
                                    // 手機: X=左右, Y=前後, Z=上下(正值向上)
                                    // Unity: X=左右, Y=上下, Z=前後
                                    accelerationVector = new Vector3(acc.x, acc.y, acc.z); // X=左右, Y=上下, Z=前後
                                    accelerationMagnitude = accelerationVector.magnitude;

                                    if (debugLog) Debug.Log($"📳 原始加速度: X={acc.x:F2}, Y={acc.y:F2}, Z={acc.z:F2}");
                                    if (debugLog) Debug.Log($"📳 Unity坐標系: X={accelerationVector.x:F2}, Y={accelerationVector.y:F2}, Z={accelerationVector.z:F2}, Magnitude={accelerationMagnitude:F2}");

                                    // 觸發加速度事件
                                    OnAccelerationReceived?.Invoke(accelerationVector);
                                }
                                else
                                {
                                    if (debugLog) Debug.LogWarning("⚠️ 加速度數據為空！");
                                }
                            }
                            catch (System.Exception e)
                            {
                                Debug.LogError($"❌ 解析加速度數據錯誤: {e.Message}");
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
                            // 處理旋转事件 - 使用與陀螺儀和搖晃相同的結構
                            if (debugLog) Debug.Log($"🎯 收到旋转事件: {message}");
                            try
                            {
                                // 使用與陀螺儀和搖晃相同的解析方式
                                var spinData = new SpinData
                                {
                                    triggered = serverMessage.data.triggered,
                                    angle = serverMessage.data.angle,
                                    timestamp = serverMessage.data.timestamp
                                };
                                
                                spinTriggered = true;
                                lastSpinAngle = spinData.angle;
                                spinCount++;
                                
                                if (serverMessage.data != null)
                                {
                                    if (serverMessage.data.snapAngle != 0)
                                    {
                                        currentSpinSnapAngle = serverMessage.data.snapAngle;
                                    }

                                    if (!string.IsNullOrEmpty(serverMessage.data.mode))
                                    {
                                        currentSpinModeKey = serverMessage.data.mode;
                                    }

                                    if (!string.IsNullOrEmpty(serverMessage.data.label))
                                    {
                                        currentSpinMode = serverMessage.data.label;
                                    }

                                    if (serverMessage.data.timestamp != 0)
                                    {
                                        lastSpinModeTimestamp = serverMessage.data.timestamp;
                                    }
                                }
                                
                                if (debugLog) Debug.Log($"🎯 旋转触发! Count={spinCount}, Angle={spinData.angle:F2}");
                                
                                OnSpinDataReceived?.Invoke(spinData);
                                
                                // 0.5秒后重置状态
                                StartCoroutine(ResetSpinStatus());
                            }
                            catch (System.Exception e)
                            {
                                Debug.LogError($"❌ 解析旋转数据错误: {e.Message}");
                            }
                            break;
                            
                        case "spin_mode":
                            Debug.Log($"🎚️ 收到旋钮模式訊息: {message}");
                            try
                            {
                                if (serverMessage.data != null)
                                {
                                    // 若是 Unity 自己送出的 toggle_request（用來叫 Web 切換），這裡只做 Debug，不更新模式數值
                                    if (!string.IsNullOrEmpty(serverMessage.data.mode) && serverMessage.data.mode == "toggle_request")
                                    {
                                        Debug.Log($"🔁 [Unity] 收到 echo 的 toggle_request spin_mode（來自伺服器廣播），不更新模式，只用來觀察流程。timestamp={serverMessage.data.timestamp}");
                                        break;
                                    }

                                    Debug.Log($"🔍 解析模式數據: mode={serverMessage.data.mode}, snapAngle={serverMessage.data.snapAngle}, label={serverMessage.data.label}");
                                    
                                    currentSpinModeKey = string.IsNullOrEmpty(serverMessage.data.mode) ? "unknown" : serverMessage.data.mode;
                                    currentSpinMode = string.IsNullOrEmpty(serverMessage.data.label) ? currentSpinModeKey : serverMessage.data.label;
                                    currentSpinSnapAngle = serverMessage.data.snapAngle;
                                    lastSpinModeTimestamp = serverMessage.data.timestamp;
                                    
                                    Debug.Log($"✅ 模式已更新: {currentSpinMode} ({currentSpinModeKey}, {currentSpinSnapAngle}°)");
                                    
                                    var modeStatus = new SpinModeStatus
                                    {
                                        mode = currentSpinModeKey,
                                        label = currentSpinMode,
                                        snapAngle = currentSpinSnapAngle,
                                        timestamp = lastSpinModeTimestamp
                                    };
                                    
                                    OnSpinModeStatusReceived?.Invoke(modeStatus);

                                    Debug.Log($"🌐 [Unity] 從 Web 收到模式更新並已套用: {modeStatus.label} ({modeStatus.mode}, {modeStatus.snapAngle}°) ts={modeStatus.timestamp}");
                                }
                                else
                                {
                                    Debug.LogWarning("⚠️ spin_mode 消息的 data 字段為 null");
                                }
                            }
                            catch (System.Exception e)
                            {
                                Debug.LogError($"❌ 解析旋钮模式訊息錯誤: {e.Message}");
                                Debug.LogError($"❌ 堆疊追蹤: {e.StackTrace}");
                            }
                            break;
                            
                        case "pitch_wave":
                            if (debugLog) Debug.Log($"🏌️ 收到揮動事件: {message}");
                            try
                            {
                                if (serverMessage.data != null)
                                {
                                    var pitchWave = new PitchWaveData
                                    {
                                        count     = serverMessage.data.count,
                                        change    = serverMessage.data.change,
                                        beta      = serverMessage.data.beta,
                                        direction = serverMessage.data.direction,
                                        timestamp = serverMessage.data.timestamp
                                    };
                                    if (debugLog) Debug.Log($"🏌️ 揮動: Count={pitchWave.count}, Direction={pitchWave.direction}, Beta={pitchWave.beta:F2}, Change={pitchWave.change:F2}°");
                                    OnPitchWaveReceived?.Invoke(pitchWave);
                                }
                            }
                            catch (System.Exception e)
                            {
                                Debug.LogError($"❌ 解析揮動數據錯誤: {e.Message}");
                            }
                            break;

                        case "ar_camera_pose":
                            // 處理 AR 頁面：相機相對 Marker 的位置/旋轉（半透明黑底綠字那組數值）
                            try
                            {
                                var arMsg = JsonUtility.FromJson<ARCameraPoseMessage>(message);
                                if (arMsg?.data != null)
                                {
                                    arMarkerVisible = arMsg.data.markerVisible;
                                    if (arMsg.data.position != null)
                                    {
                                        arCameraPosition = new Vector3(
                                            arMsg.data.position.x,
                                            arMsg.data.position.y,
                                            arMsg.data.position.z
                                        );
                                    }
                                    if (arMsg.data.rotation != null)
                                    {
                                        arCameraRotationDegrees = new Vector3(
                                            arMsg.data.rotation.x,
                                            arMsg.data.rotation.y,
                                            arMsg.data.rotation.z
                                        );
                                    }
                                    OnARCameraPoseReceived?.Invoke(arMsg.data);
                                }
                            }
                            catch (System.Exception e)
                            {
                                Debug.LogError($"❌ 解析 ar_camera_pose 錯誤: {e.Message}");
                            }
                            break;

                        case "position":
                            // 處理 8th Wall 位置數據
                            if (debugLog) Debug.Log($"📍 收到位置數據: {message}");
                            try
                            {
                                // 使用 PositionDataMessage 解析位置數據
                                var positionMsg = JsonUtility.FromJson<PositionDataMessage>(message);
                                
                                if (positionMsg.data == null)
                                {
                                    Debug.LogWarning("⚠️ 位置數據的 data 字段為 null");
                                    break;
                                }
                                
                                // 構建 PositionData
                                var posData = new PositionData
                                {
                                    position = positionMsg.data.position != null
                                        ? new PositionVector
                                        {
                                            x = positionMsg.data.position.x,
                                            y = positionMsg.data.position.y,
                                            z = positionMsg.data.position.z
                                        }
                                        : new PositionVector { x = 0, y = 0, z = 0 },
                                    rotation = positionMsg.data.rotation != null
                                        ? new RotationQuaternion
                                        {
                                            x = positionMsg.data.rotation.x,
                                            y = positionMsg.data.rotation.y,
                                            z = positionMsg.data.rotation.z,
                                            w = positionMsg.data.rotation.w != 0 ? positionMsg.data.rotation.w : 1.0f
                                        }
                                        : new RotationQuaternion { x = 0, y = 0, z = 0, w = 1.0f },
                                    delta = positionMsg.data.delta != null
                                        ? new PositionVector
                                        {
                                            x = positionMsg.data.delta.x,
                                            y = positionMsg.data.delta.y,
                                            z = positionMsg.data.delta.z
                                        }
                                        : new PositionVector { x = 0, y = 0, z = 0 },
                                    timestamp = positionMsg.data.timestamp != 0 ? positionMsg.data.timestamp : positionMsg.timestamp
                                };
                                
                                if (debugLog) Debug.Log($"📍 位置數據: Pos=({posData.position.x:F3}, {posData.position.y:F3}, {posData.position.z:F3}), Delta=({posData.delta.x:F3}, {posData.delta.y:F3}, {posData.delta.z:F3})");
                                
                                // 觸發位置數據事件
                                OnPositionDataReceived?.Invoke(posData);
                            }
                            catch (System.Exception e)
                            {
                                Debug.LogError($"❌ 解析位置數據錯誤: {e.Message}");
                                Debug.LogError($"❌ 堆疊追蹤: {e.StackTrace}");
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
        // websocket 在連線建立前為 null，不需要警告

        m_alpha = alpha;
        m_beta = beta;
        m_gamma = gamma;
        m_lastSpinAngle = lastSpinAngle;
        m_spinCount = spinCount;
        #endif

        // 監聽空白鍵：按下一次就要求網頁端在 90° / 120° 模式之間切換一次
        if (Input.GetKeyDown(KeyCode.Space))
        {
            SendSpinModeToggleToWeb();
        }
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

    /// <summary>
    /// 由 Unity 端主動要求前端在「90°模式」與「120°模式」間切換一次。
    /// 按下空白鍵時呼叫：只送一個簡單的 toggle 訊息，由網頁端根據當前狀態決定切到哪一個模式。
    /// </summary>
    private void SendSpinModeToggleToWeb()
    {
        if (websocket == null || websocket.State != WebSocketState.Open)
        {
            Debug.LogWarning("⚠️ WebSocket 未連線，無法發送旋鈕模式切換指令");
            return;
        }

        // 本地記錄目前 Unity 認知的模式狀態（純記錄用，不影響前端實際邏輯）
        webSpinIs90Mode = !webSpinIs90Mode;

        long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // 改用既有會轉發的 spin_mode 通道，夾帶一個自訂的 mode = \"toggle_request\"
        string json = $"{{\"type\":\"spin_mode\",\"data\":{{\"mode\":\"toggle_request\",\"timestamp\":{ts}}}}}";

        websocket.SendText(json);

        string modeLabel = webSpinIs90Mode ? "90° 吸附" : "120° 吸附";
        Debug.Log($"🛰️ [Unity] toggle_request sent，目前預期模式：{modeLabel}，JSON = {json}");
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
            GUILayout.Label($"加速度: X={accelerationVector.x:F2}, Y={accelerationVector.y:F2}, Z={accelerationVector.z:F2} (m/s²)");
            GUILayout.Label($"加速度強度: {accelerationMagnitude:F2} m/s²");
            GUILayout.Label($"旋转状态: {(spinTriggered ? "已触发" : "未触发")}");
            GUILayout.Label($"旋转次数: {spinCount}");
            GUILayout.Label($"最后角度: {lastSpinAngle:F2}°");
            GUILayout.Label($"旋钮模式: {currentSpinMode} ({currentSpinModeKey}, {currentSpinSnapAngle:F0}°)");
            GUILayout.Label($"模式更新時間: {lastSpinModeTimestamp}");
            GUILayout.Label($"AR Marker: {(arMarkerVisible ? "已偵測" : "未偵測")} | Pos={arCameraPosition.x:F2},{arCameraPosition.y:F2},{arCameraPosition.z:F2} | Rot°={arCameraRotationDegrees.x:F1},{arCameraRotationDegrees.y:F1},{arCameraRotationDegrees.z:F1}");
            
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