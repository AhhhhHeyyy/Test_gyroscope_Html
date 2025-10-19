using System.Collections;
using UnityEngine;
using Unity.WebRTC;
using System;

public class WebRTCScreenReceiver : MonoBehaviour
{
    [Header("WebRTC 設置")]
    [SerializeField] private MeshRenderer targetRenderer;
    [SerializeField] private GyroscopeReceiver gyroscopeReceiver;
    
    [Header("STUN 服務器")]
    [SerializeField] private string[] stunServers = {
        "stun:stun.l.google.com:19302",
        "stun:stun1.l.google.com:19302",
        "stun:stun2.l.google.com:19302"
    };
    
    private RTCPeerConnection peerConnection;
    private RTCConfiguration config;
    private bool isWebRTCConnected = false;
    
    void Start()
    {
        Debug.Log("🚀 WebRTC 準備就緒");
        
        // 自動尋找 ScreenDisplay 物件
        if (targetRenderer == null)
        {
            GameObject screenDisplay = GameObject.Find("ScreenDisplay");
            if (screenDisplay != null)
            {
                targetRenderer = screenDisplay.GetComponent<MeshRenderer>();
                if (targetRenderer != null)
                {
                    Debug.Log("✅ targetRenderer 已設置: ScreenDisplay");
                }
                else
                {
                    Debug.LogError("❌ ScreenDisplay 物件沒有 MeshRenderer 組件！");
                }
            }
            else
            {
                Debug.LogError("❌ 找不到 ScreenDisplay 物件！請確保場景中有名為 'ScreenDisplay' 的物件");
            }
        }
        
        // 配置 STUN 服務器
        config = new RTCConfiguration
        {
            iceServers = new RTCIceServer[]
            {
                new RTCIceServer { urls = stunServers }
            },
            iceCandidatePoolSize = 10
        };
        
        Debug.Log("📺 WebRTCScreenReceiver 已初始化");
        
        // 訂閱 WebRTC 信令事件
        GyroscopeReceiver.OnWebRTCSignaling += HandleSignaling;
    }
    
    void Update()
    {
        WebRTC.Update();
    }
    
    void OnDestroy()
    {
        // 清理 WebRTC 連接
        if (peerConnection != null)
        {
            peerConnection.Close();
            peerConnection.Dispose();
        }
        
        // 取消訂閱事件
        GyroscopeReceiver.OnWebRTCSignaling -= HandleSignaling;
    }
    
    void HandleSignalingText(string message)
    {
        try
        {
            Debug.Log($"📡 收到信令: {message}");
            
            if (message.Contains("\"type\":\"ready\""))
            {
                Debug.Log("🤝 房間準備就緒，等待 WebRTC offer");
                return;
            }
            
            if (message.Contains("\"type\":\"offer\""))
            {
                Debug.Log("📩 收到 Offer");
                StartCoroutine(AcceptOffer(message));
                return;
            }
            
            if (message.Contains("\"type\":\"candidate\""))
            {
                Debug.Log("✅ 添加 ICE 候選者");
                return;
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
    
    System.Collections.IEnumerator AcceptOffer(string sdp)
    {
        Debug.Log($"🎯 開始處理 Offer SDP: {sdp.Substring(0, Math.Min(30, sdp.Length))}...");
        
        // 清理舊連接
        if (peerConnection != null)
        {
            peerConnection.Close();
            peerConnection.Dispose();
        }
        
        // 創建新的 PeerConnection
        peerConnection = new RTCPeerConnection(ref config);
        
        // 設置事件處理器
        peerConnection.OnIceConnectionChange = state =>
        {
            Debug.Log($"🔌 ICE 狀態: {state}");
            if (state == RTCIceConnectionState.Connected || state == RTCIceConnectionState.Completed)
            {
                Debug.Log("🎉 WebRTC 連接成功！");
                isWebRTCConnected = true;
                
                // 禁用螢幕捕獲處理器（如果存在）
                var screenCaptureHandler = FindFirstObjectByType<ScreenCaptureHandler>();
                if (screenCaptureHandler != null)
                {
                    screenCaptureHandler.enabled = false;
                }
            }
            else if (state == RTCIceConnectionState.Failed)
            {
                Debug.LogError("❌ ICE 連接失敗");
                isWebRTCConnected = false;
            }
        };
        
        // ICE 候選者處理
        peerConnection.OnIceCandidate = candidate =>
        {
            if (candidate == null) return;
            
            var candidateDto = new GyroscopeReceiver.SignalingDTO
            {
                type = "candidate",
                candidate = new GyroscopeReceiver.IceCandidateDTO
                {
                    candidate = candidate.Candidate,
                    sdpMid = candidate.SdpMid,
                    sdpMLineIndex = candidate.SdpMLineIndex ?? 0
                }
            };
            gyroscopeReceiver.SendSignaling(candidateDto);
            Debug.Log("📤 發送 ICE 候選者");
        };
        
        // 視頻軌道處理
        peerConnection.OnTrack = evt =>
        {
            Debug.Log("📺 收到視頻軌道");
            if (evt.Track is VideoStreamTrack videoTrack)
            {
                videoTrack.OnVideoReceived += OnVideoReceived;
            }
        };
        
        // 設置遠端描述
        var offer = new RTCSessionDescription
        {
            type = RTCSdpType.Offer,
            sdp = sdp
        };
        var op = peerConnection.SetRemoteDescription(ref offer);
        yield return op;
        
        if (op.IsError)
        {
            Debug.LogError($"❌ 設置遠端描述失敗: {op.Error.message}");
            yield break;
        }
        
        Debug.Log("✅ 已設置遠端描述");
        
        // 創建 Answer
        var answerOp = peerConnection.CreateAnswer();
        yield return answerOp;
        
        if (answerOp.IsError)
        {
            Debug.LogError($"❌ 創建 Answer 失敗: {answerOp.Error.message}");
            yield break;
        }
        
        var answer = answerOp.Desc;
        Debug.Log("✅ 已創建 Answer");
        
        // 設置本地描述
        var setLocalDescOp = peerConnection.SetLocalDescription(ref answer);
        yield return setLocalDescOp;
        
        if (setLocalDescOp.IsError)
        {
            Debug.LogError($"❌ 設置本地描述失敗: {setLocalDescOp.Error.message}");
            yield break;
        }
        
        Debug.Log("✅ 已設置本地描述");
        
        // 發送 Answer
        var answerDto = new GyroscopeReceiver.SignalingDTO
        {
            type = "answer",
            sdp = answer.sdp
        };
        gyroscopeReceiver.SendSignaling(answerDto);
        Debug.Log("📤 已發送 Answer");
    }
    
    private void OnVideoReceived(Texture texture)
    {
        if (!isWebRTCConnected)
        {
            Debug.LogWarning("⚠️ WebRTC 未連接，忽略視頻幀");
            return;
        }
        
        Debug.Log("📺 收到視頻幀");
        
        if (targetRenderer != null && texture != null)
        {
            targetRenderer.material.mainTexture = texture;
            Debug.Log("✅ 材質已更新");
        }
        else
        {
            Debug.LogWarning($"⚠️ 無法設定材質 - targetRenderer: {targetRenderer}, texture: {texture}");
        }
    }
}
