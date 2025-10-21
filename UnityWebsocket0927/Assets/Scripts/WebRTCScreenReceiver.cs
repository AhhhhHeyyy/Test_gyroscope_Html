using Unity.WebRTC;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System;  

public enum DisplayMode
{
    Renderer,    // 3D Renderer 模式
    RawImage     // UI RawImage 模式
}

public class WebRTCScreenReceiver : MonoBehaviour
{
    [Header("WebRTC 設定")]
    public Renderer targetRenderer;
    public RawImage targetRawImage;
    public string roomId = "default-room";
    public float connectionTimeout = 18f;
    
    [Header("顯示模式")]
    public DisplayMode displayMode = DisplayMode.RawImage;
    
    [Header("狀態顯示")]
    public bool showDebugInfo = true;
    
    [Header("狀態")]
    public string iceConnectionState = "new";
    
    private RTCPeerConnection peerConnection;
    private RTCConfiguration config;
    private VideoStreamTrack remoteVideoTrack;
    private bool isWebRTCMode = false;
    private bool isConnected = false;
    private GyroscopeReceiver gyroscopeReceiver;
    
    void Start()
    {
        // Unity WebRTC 包在較新版本中可能不需要手動初始化
        Debug.Log("🚀 WebRTC 準備就緒");
        
        // ICE 配置
        config = new RTCConfiguration
        {
            iceServers = new[] { 
                new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } },
                new RTCIceServer { urls = new[] { "stun:stun1.l.google.com:19302" } }
            },
            iceCandidatePoolSize = 10
        };
        
        // 檢查顯示目標設置
        if (displayMode == DisplayMode.Renderer && targetRenderer == null)
        {
            Debug.LogError("❌ Renderer 模式但 targetRenderer 未設置！請在 Inspector 中設置 Target Renderer");
            return;
        }
        else if (displayMode == DisplayMode.RawImage && targetRawImage == null)
        {
            Debug.LogError("❌ RawImage 模式但 targetRawImage 未設置！請在 Inspector 中設置 Target RawImage");
            return;
        }
        
        // 記錄設置狀態
        if (displayMode == DisplayMode.Renderer)
        {
            Debug.Log($"✅ Renderer 模式已設置: {targetRenderer.name}");
        }
        else
        {
            Debug.Log($"✅ RawImage 模式已設置: {targetRawImage.name}");
        }
        
        // 獲取 GyroscopeReceiver
        gyroscopeReceiver = FindFirstObjectByType<GyroscopeReceiver>();
        if (gyroscopeReceiver == null)
        {
            Debug.LogError("❌ 找不到 GyroscopeReceiver");
            return;
        }
        
        // 訂閱信令事件
        GyroscopeReceiver.OnWebRTCSignaling += HandleSignaling;
        GyroscopeReceiver.OnRawMessage += HandleSignalingText;
        
        Debug.Log("📺 WebRTCScreenReceiver 已初始化");
    }
    
    
    void HandleSignalingText(string text)
    {
        try
        {
            var msg = JsonUtility.FromJson<SignalingBase>(text);
            Debug.Log($"📡 收到信令: {msg.type}");
            
            if (msg.type == "ready")
            {
                Debug.Log("🤝 房間準備就緒，等待 WebRTC offer");
                return;
            }
            else if (msg.type == "offer")
            {
                var offer = JsonUtility.FromJson<OfferMessage>(text);
                Debug.Log("📩 收到 Offer");
                StartCoroutine(AcceptOffer(offer.sdp));
            }
            else if (msg.type == "candidate")
            {
                var cand = JsonUtility.FromJson<CandidateMessage>(text);
                if (peerConnection != null && cand.candidate != null)
                {
                    var candidate = new RTCIceCandidate(new RTCIceCandidateInit
                    {
                        candidate = cand.candidate.candidate,
                        sdpMid = cand.candidate.sdpMid,
                        sdpMLineIndex = cand.candidate.sdpMLineIndex
                    });
                    peerConnection.AddIceCandidate(candidate);
                    Debug.Log("✅ 添加 ICE 候選者");
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ 處理信令文字錯誤: {e.Message}");
        }
    }
    
    void HandleSignaling(GyroscopeReceiver.SignalingMessage msg)
    {
        try
        {
            Debug.Log($"📡 收到 WebRTC 信令: {msg.type}");
            
            if (msg.type == "ready")
            {
                Debug.Log("🤝 WebRTC 信令：房間準備就緒");
                return;
            }
            else if (msg.type == "offer")
            {
                Debug.Log("📩 收到 Offer");
                
                // 檢查 SDP
                if (string.IsNullOrEmpty(msg.sdp))
                {
                    Debug.LogError("❌ Offer SDP 為空！檢查伺服器轉發格式");
                    Debug.Log($"🔍 完整訊息: {JsonUtility.ToJson(msg)}");
                    return;
                }
                
                Debug.Log($"📄 收到 Offer SDP 長度: {msg.sdp.Length}");
                Debug.Log($"📄 SDP 前50字符: {msg.sdp.Substring(0, Math.Min(50, msg.sdp.Length))}...");
                
                StartCoroutine(AcceptOffer(msg.sdp));
            }
            else if (msg.type == "answer")
            {
                Debug.Log("📩 收到 Answer（理論上不該 Unity 收到）");
            }
            else if (msg.type == "candidate")
            {
                if (peerConnection != null && msg.candidate != null)
                {
                    var candidate = new RTCIceCandidate(new RTCIceCandidateInit
                    {
                        candidate = msg.candidate.candidate,
                        sdpMid = msg.candidate.sdpMid,
                        sdpMLineIndex = msg.candidate.sdpMLineIndex
                    });
                    peerConnection.AddIceCandidate(candidate);
                    Debug.Log("✅ 添加 ICE 候選者");
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ 處理信令錯誤: {e.Message}");
            Debug.LogError($"🔍 錯誤堆疊: {e.StackTrace}");
        }
    }
    
    IEnumerator ConnectionTimeoutCheck()
    {
        yield return new WaitForSeconds(connectionTimeout);
        
        if (!isConnected)
        {
            Debug.LogWarning("⚠️ WebRTC 連接超時，降級到 WebSocket");
            FallbackToWebSocket();
        }
    }
    
    void FallbackToWebSocket()
    {
        isWebRTCMode = false;
        
        // 清理 WebRTC 資源
        CleanupWebRTC();
        
        // 啟用 WebSocket 模式
        var handler = GetComponent<ScreenCaptureHandler>();
        if (handler) handler.enabled = true;
    }
    
    void CleanupWebRTC()
    {
        remoteVideoTrack?.Dispose(); 
        remoteVideoTrack = null;
        peerConnection?.Close(); 
        peerConnection?.Dispose(); 
        peerConnection = null;
    }
    
    void Update()
    {
        // WebRTC 3.x 版本建議：每 frame 更新 internal context 以確保穩定運行
        WebRTC.Update();
    }
    
    void OnGUI()
    {
        if (showDebugInfo && Application.isPlaying)
        {
            GUILayout.BeginArea(new Rect(10, 400, 300, 150));
            GUILayout.Label($"WebRTC 模式: {isWebRTCMode}");
            GUILayout.Label($"連接狀態: {isConnected}");
            if (peerConnection != null)
            {
                GUILayout.Label($"ICE 狀態: {peerConnection.IceConnectionState}");
                GUILayout.Label($"連接狀態: {peerConnection.ConnectionState}");
            }
            GUILayout.EndArea();
        }
    }
    
    // 數據結構
    [System.Serializable]
    public class SignalingBase
    {
        public string type;
    }
    
    [System.Serializable]
    public class OfferMessage
    {
        public string type;
        public string sdp;
    }
    
    [System.Serializable]
    public class CandidateMessage
    {
        public string type;
        public IceCandidateData candidate;
    }
    
    [System.Serializable]
    public class IceCandidateData
    {
        public string candidate;
        public string sdpMid;
        public int sdpMLineIndex;
    }
    
    // 接受 Offer 的協程
    System.Collections.IEnumerator AcceptOffer(string sdp)
    {
        Debug.Log($"🎯 開始處理 Offer SDP: {sdp.Substring(0, Math.Min(30, sdp.Length))}...");
        
        // 清理舊的連接
        if (peerConnection != null)
        {
            peerConnection.Close();
            peerConnection.Dispose();
        }
        
        // 創建新的 PeerConnection
        peerConnection = new RTCPeerConnection(ref config);
        
        // ICE 候選者處理
        peerConnection.OnIceCandidate = candidate =>
        {
            if (candidate == null) return;
            
            var candidateData = new
            {
                type = "candidate",
                candidate = new
                {
                    candidate = candidate.Candidate,
                    sdpMid = candidate.SdpMid,
                    sdpMLineIndex = candidate.SdpMLineIndex ?? 0
                }
            };
            gyroscopeReceiver.SendJson(candidateData);
            Debug.Log("📤 發送 ICE 候選者");
        };

        // ICE 連接狀態改變
        peerConnection.OnIceConnectionChange = state =>
        {
            this.iceConnectionState = state.ToString();
            Debug.Log($"🔌 ICE 狀態: {state}");
            
            if (state == RTCIceConnectionState.Connected || state == RTCIceConnectionState.Completed)
            {
                isConnected = true;
                isWebRTCMode = true;
                Debug.Log("🎉 WebRTC 連接成功！");
                
                // 停用 WebSocket 模式
                var handler = GetComponent<ScreenCaptureHandler>();
                if (handler) handler.enabled = false;
            }
            else if (state == RTCIceConnectionState.Failed || state == RTCIceConnectionState.Disconnected)
            {
                Debug.LogWarning("⚠️ ICE 連接失敗，降級到 WebSocket");
                FallbackToWebSocket();
            }
        };
        
        // 接收遠端軌道
        peerConnection.OnTrack = (RTCTrackEvent e) =>
        {
            if (e.Track is VideoStreamTrack vtrack)
            {
                Debug.Log("📺 收到視頻軌道");
                remoteVideoTrack = vtrack;
                
                // 修正：使用 OnVideoReceived 事件，支援兩種顯示模式
                vtrack.OnVideoReceived += (tex) => {
                    Debug.Log("📺 收到視頻幀");
                    Debug.Log($"🔍 紋理尺寸: {tex.width}x{tex.height}");
                    Debug.Log($"🔍 紋理類型: {tex.GetType().Name}");
                    Debug.Log($"🔍 紋理名稱: {tex.name}");
                    
                    // 根據顯示模式應用紋理
                    if (displayMode == DisplayMode.RawImage && targetRawImage != null)
                    {
                        targetRawImage.texture = tex;
                        Debug.Log($"✅ RawImage 模式：已設置紋理到 {targetRawImage.name}");
                    }
                    else if (displayMode == DisplayMode.Renderer && targetRenderer != null)
                    {
                        // 直接設置紋理到現有材質，避免實例化問題
                        Material currentMaterial = targetRenderer.material;
                        
                        // 確保材質使用正確的 Shader
                        if (currentMaterial.shader.name != "Unlit/Texture")
                        {
                            Debug.Log($"🔧 材質 Shader 不是 Unlit/Texture，創建新材質");
                            currentMaterial = new Material(Shader.Find("Unlit/Texture"));
                            targetRenderer.material = currentMaterial;
                        }
                        
                        // 直接設置紋理
                        currentMaterial.mainTexture = tex;
                        currentMaterial.SetTexture("_MainTex", tex);
                        
                        // 強制刷新渲染器
                        targetRenderer.enabled = false;
                        targetRenderer.enabled = true;
                        
                        Debug.Log($"✅ Renderer 模式：已設置紋理到現有材質");
                        Debug.Log($"🔍 材質實例 ID: {currentMaterial.GetInstanceID()}");
                        Debug.Log($"🔍 渲染器材質 ID: {targetRenderer.material.GetInstanceID()}");
                        Debug.Log($"🔍 設置後的 Base Map: {(currentMaterial.GetTexture("_MainTex") != null ? "有內容" : "無內容")}");
                    }
                    else
                    {
                        Debug.LogWarning($"⚠️ 顯示目標未設置或模式不匹配！模式: {displayMode}, Renderer: {targetRenderer != null}, RawImage: {targetRawImage != null}");
                    }
                };
            }
        };
        
        // 設置遠端描述
        var desc = new RTCSessionDescription { type = RTCSdpType.Offer, sdp = sdp };
        var setOp = peerConnection.SetRemoteDescription(ref desc);
        yield return setOp;
        Debug.Log("✅ 已設置遠端描述");
        
        // 創建 Answer
        var answerOp = peerConnection.CreateAnswer();
        yield return answerOp;
        var answer = answerOp.Desc;
        Debug.Log("✅ 已創建 Answer");
        
        // 設置本地描述
        var setLocalOp = peerConnection.SetLocalDescription(ref answer);
        yield return setLocalOp;
        Debug.Log("✅ 已設置本地描述");
        
        // 發送 Answer
        var answerData = new
        {
            type = "answer",
            sdp = answer.sdp
        };
        gyroscopeReceiver.SendJson(answerData);
        Debug.Log("📤 已發送 Answer");
    }
    
    void OnDestroy()
    {
        GyroscopeReceiver.OnWebRTCSignaling -= HandleSignaling;
        GyroscopeReceiver.OnRawMessage -= HandleSignalingText;
        CleanupWebRTC();
        // WebRTC.Dispose() 在較新版本中可能不需要手動調用
        Debug.Log("🧹 WebRTC 已清理");
    }
}