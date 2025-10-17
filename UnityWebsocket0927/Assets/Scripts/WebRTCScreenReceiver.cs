using Unity.WebRTC;
using UnityEngine;
using System.Collections;

public class WebRTCScreenReceiver : MonoBehaviour
{
    [Header("WebRTC 設定")]
    public Renderer targetRenderer;
    public string roomId = "default-room";
    public float connectionTimeout = 18f;
    
    [Header("狀態顯示")]
    public bool showDebugInfo = true;
    
    private RTCPeerConnection peerConnection;
    private RTCConfiguration config;
    private VideoStreamTrack remoteVideoTrack;
    private Texture2D remoteTexture;
    private bool isWebRTCMode = false;
    private bool isConnected = false;
    private GyroscopeReceiver gyroscopeReceiver;
    
    void Start()
    {
        // ICE 配置
        config = new RTCConfiguration
        {
            iceServers = new[] { 
                new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } }
            }
        };
        
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
            if (msg.type == "offer")
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
            if (msg.type == "offer")
            {
                Debug.Log("📩 收到 Offer");
                HandleOffer(msg.sdp);
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
        }
    }
    
    void HandleOffer(string sdp)
    {
        // 創建 PeerConnection
        peerConnection = new RTCPeerConnection(ref config);
        
        // ICE 候選者處理
        peerConnection.OnIceCandidate = (candidate) =>
        {
            gyroscopeReceiver.SendRaw(JsonUtility.ToJson(new
            {
                type = "candidate",
                candidate = new
                {
                    candidate = candidate.Candidate,
                    sdpMid = candidate.SdpMid,
                    sdpMLineIndex = candidate.SdpMLineIndex
                }
            }));
            Debug.Log("📤 發送 ICE 候選者");
        };
        
        // ICE 連接狀態監控
        peerConnection.OnIceConnectionChange = (state) =>
        {
            Debug.Log($"🔌 ICE 狀態: {state}");
            if (state == RTCIceConnectionState.Connected || state == RTCIceConnectionState.Completed)
            {
                isConnected = true;
                isWebRTCMode = true;
                StopCoroutine("ConnectionTimeoutCheck");
                
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
                
                // 直接使用 VideoStreamTrack 的 Texture 屬性
                remoteTexture = vtrack.Texture as Texture2D;
            }
        };
        
        // 設置遠端描述
        var offer = new RTCSessionDescription
        {
            type = RTCSdpType.Offer,
            sdp = sdp
        };
        peerConnection.SetRemoteDescription(ref offer);
        
        // 創建 Answer
        var answerOp = peerConnection.CreateAnswer();
        var answer = answerOp.Desc;
        
        // 設置本地描述
        peerConnection.SetLocalDescription(ref answer);
        
        // 發送 Answer
        gyroscopeReceiver.SendRaw(JsonUtility.ToJson(new
        {
            type = "answer",
            sdp = answer.sdp
        }));
        
        Debug.Log("📤 已發送 Answer");
        
        // 啟動超時檢查
        StartCoroutine(ConnectionTimeoutCheck());
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
        remoteTexture = null;
        peerConnection?.Close(); 
        peerConnection?.Dispose(); 
        peerConnection = null;
    }
    
    void Update()
    {
        // 持續更新材質（如果使用 WebRTC）
        if (remoteTexture != null && targetRenderer != null)
        {
            if (targetRenderer.material.mainTexture != remoteTexture)
                targetRenderer.material.mainTexture = remoteTexture;
        }
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
        // 創建 PeerConnection
        peerConnection = new RTCPeerConnection(ref config);
        
        // ICE 候選者處理
        peerConnection.OnIceCandidate = candidate =>
        {
            if (candidate == null) return;
            gyroscopeReceiver.SendJson(new { 
                type = "candidate", 
                candidate = new {
                    candidate = candidate.Candidate, 
                    sdpMid = candidate.SdpMid, 
                    sdpMLineIndex = candidate.SdpMLineIndex
                }
            });
        };

        // ICE 連接狀態改變
        peerConnection.OnIceConnectionChange = state =>
        {
            iceConnectionState = state.ToString();
            Debug.Log($"🔌 ICE 連接狀態改變: {state}");
            if (state == RTCIceConnectionState.Failed || state == RTCIceConnectionState.Disconnected)
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
                
                // 直接使用 VideoStreamTrack 的 Texture 屬性
                remoteTexture = vtrack.Texture as Texture2D;
            }
        };
        
        // 設置遠端描述
        var desc = new RTCSessionDescription { type = RTCSdpType.Offer, sdp = sdp };
        var setOp = peerConnection.SetRemoteDescription(ref desc);
        yield return setOp;
        
        // 創建 Answer
        var answerOp = peerConnection.CreateAnswer();
        yield return answerOp;
        var answer = answerOp.Desc;
        
        // 設置本地描述
        var setLocalOp = peerConnection.SetLocalDescription(ref answer);
        yield return setLocalOp;
        
        // 發送 Answer
        gyroscopeReceiver.SendJson(new { type = "answer", sdp = answer.sdp });
        Debug.Log("📤 已發送 Answer");
    }
    
    void OnDestroy()
    {
        GyroscopeReceiver.OnWebRTCSignaling -= HandleSignaling;
        GyroscopeReceiver.OnRawMessage -= HandleSignalingText;
        CleanupWebRTC();
    }
}
