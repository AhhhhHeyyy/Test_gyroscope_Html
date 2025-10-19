using Unity.WebRTC;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System;

public enum DisplayMode
{
    Renderer,
    RawImage
}

public class WebRTCScreenReceiver : MonoBehaviour
{
    [Header("WebRTC 設定")]
    public string roomId = "default-room";
    public float connectionTimeout = 18f;
    
    [Header("显示设置")]
    public DisplayMode displayMode = DisplayMode.RawImage;
    public Renderer targetRenderer;
    public RawImage targetRawImage;
    
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
    
    // 視頻紋理處理
    private Texture _pendingTex;
    private bool _hasNewTex;
    private MaterialPropertyBlock _mpb;
    private int frameCount = 0;
    private RenderTexture convertedRT;
    private static readonly int ID_MainTex = Shader.PropertyToID("_MainTex");
    private static readonly int ID_BaseMap = Shader.PropertyToID("_BaseMap");
    private static readonly int ID_BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ID_Color = Shader.PropertyToID("_Color");
    
    void Start()
    {
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
        else
        {
            string targetName = displayMode == DisplayMode.Renderer ? targetRenderer.name : targetRawImage.name;
            Debug.Log($"✅ {displayMode} 模式已設置: {targetName}");
        }
        
        // 初始化顯示相關組件
        if (displayMode == DisplayMode.Renderer)
        {
            _mpb = new MaterialPropertyBlock();
            EnsureTargetRendererHasMaterial();
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
                
                if (string.IsNullOrEmpty(msg.sdp))
                {
                    Debug.LogError("❌ Offer SDP 為空！檢查伺服器轉發格式");
                    return;
                }
                
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
        // WebRTC 內部更新
        WebRTC.Update();
        
        // 主線程紋理更新
        if (_hasNewTex && _pendingTex != null)
        {
            ApplyTextureWithMPB(_pendingTex);
            _hasNewTex = false;
        }
    }
    
    void OnGUI()
    {
        if (showDebugInfo && Application.isPlaying)
        {
            GUILayout.BeginArea(new Rect(10, 400, 300, 200));
            GUILayout.Label($"WebRTC 模式: {isWebRTCMode}");
            GUILayout.Label($"連接狀態: {isConnected}");
            GUILayout.Label($"視頻紋理: {(_pendingTex != null ? $"{_pendingTex.width}x{_pendingTex.height}" : "無")}");
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
    IEnumerator AcceptOffer(string sdp)
    {
        Debug.Log($"🎯 開始處理 Offer SDP");
        
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
                
                vtrack.OnVideoReceived += (tex) => {
                    frameCount++;
                    Debug.Log($"📺 收到視頻幀 #{frameCount}: {tex.width}x{tex.height}");
                    _pendingTex = tex;
                    _hasNewTex = true;
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
        var answerDto = new GyroscopeReceiver.SignalingDTO
        {
            type = "answer",
            sdp = answer.sdp
        };
        gyroscopeReceiver.SendSignaling(answerDto);
        Debug.Log("📤 已發送 Answer");
        
        // 啟動超時檢查
        StartCoroutine(ConnectionTimeoutCheck());
    }
    
    // 確保材質存在
    private void EnsureTargetRendererHasMaterial()
    {
        if (targetRenderer == null)
        {
            Debug.LogError("❌ targetRenderer 為空");
            return;
        }
        
        // 檢查是否有材質
        if (targetRenderer.sharedMaterial == null)
        {
            Debug.LogWarning("⚠️ targetRenderer 沒有材質，嘗試創建默認材質");
            
            // 嘗試找到合適的 Shader
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            if (shader == null)
            {
                Debug.LogError("❌ 找不到可用的 Unlit Shader，請手動指定材質");
                return;
            }
            
            // 創建材質
            var mat = new Material(shader);
            if (mat.HasProperty(ID_BaseColor)) mat.SetColor(ID_BaseColor, Color.white);
            if (mat.HasProperty(ID_Color)) mat.SetColor(ID_Color, Color.white);
            
            targetRenderer.sharedMaterial = mat;
            Debug.Log($"✅ 已創建默認材質: {shader.name}");
        }
        else
        {
            Debug.Log($"✅ 材質已存在: {targetRenderer.sharedMaterial.shader.name}");
        }
    }
    
    // 應用紋理到顯示目標
    private void ApplyTextureWithMPB(Texture tex)
    {
        if (tex == null) return;
        
        // 检查RawImage模式下的空引用
        if (displayMode == DisplayMode.RawImage)
        {
            if (targetRawImage == null)
            {
                Debug.LogError("❌ RawImage模式但targetRawImage为null！请在Inspector中分配Target Raw Image字段");
                return;
            }
            
            if (targetRawImage.gameObject == null)
            {
                Debug.LogError("❌ targetRawImage的gameObject为null！");
                return;
            }
            
            Debug.Log($"✅ RawImage检查通过: {targetRawImage.name}");
        }
        
        if (displayMode == DisplayMode.RawImage && targetRawImage != null)
        {
            // 通过RenderTexture转换色彩空间
            Texture finalTex = tex;
            
            // 检查是否需要RenderTexture转换
            if (convertedRT == null || convertedRT.width != tex.width || convertedRT.height != tex.height)
            {
                if (convertedRT != null) 
                {
                    RenderTexture.ReleaseTemporary(convertedRT);
                }
                convertedRT = new RenderTexture(tex.width, tex.height, 0, 
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Debug.Log($"🔧 创建RenderTexture用于色彩转换: {tex.width}x{tex.height}");
            }
            
            // 使用Graphics.Blit转换色彩空间
            Graphics.Blit(tex, convertedRT);
            finalTex = convertedRT;
            
            // 设置纹理
            targetRawImage.texture = finalTex;
            targetRawImage.color = Color.white;
            
            // 强制刷新RawImage
            targetRawImage.SetMaterialDirty();
            targetRawImage.SetVerticesDirty();
            targetRawImage.Rebuild(CanvasUpdate.PreRender);
            Canvas.ForceUpdateCanvases();
            
            // 添加比例控制
            var arf = targetRawImage.GetComponent<AspectRatioFitter>();
            if (arf == null) 
            {
                arf = targetRawImage.gameObject.AddComponent<AspectRatioFitter>();
            }
            arf.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            arf.aspectRatio = (float)tex.width / tex.height;
            
            Debug.Log($"✅ RawImage 设置完成: {tex.width}x{tex.height}");
        }
        else if (displayMode == DisplayMode.Renderer && targetRenderer != null)
        {
            // Renderer 模式：使用 Material 處理
            Material mat = targetRenderer.material;
            
            // 檢查 Shader 是否正確
            if (mat.shader.name != "Unlit/Texture" && 
                mat.shader.name != "Universal Render Pipeline/Unlit")
            {
                Debug.LogWarning($"⚠️ Shader 不正確: {mat.shader.name}，嘗試切換到 Unlit/Texture");
                var unlitShader = Shader.Find("Unlit/Texture");
                if (unlitShader != null)
                {
                    mat.shader = unlitShader;
                }
            }
            
            // 直接設置 mainTexture
            mat.mainTexture = tex;
            
            // 使用 MaterialPropertyBlock 優化
            targetRenderer.GetPropertyBlock(_mpb);
            _mpb.SetTexture(ID_MainTex, tex);
            _mpb.SetTexture(ID_BaseMap, tex);
            targetRenderer.SetPropertyBlock(_mpb);
            
            Debug.Log($"✅ Renderer 套用視頻貼圖成功: {tex.width}x{tex.height}");
        }
        else
        {
            Debug.LogError($"❌ 顯示模式 {displayMode} 但對應的目標組件未設置");
        }
    }
    
    void OnDestroy()
    {
        GyroscopeReceiver.OnWebRTCSignaling -= HandleSignaling;
        GyroscopeReceiver.OnRawMessage -= HandleSignalingText;
        CleanupWebRTC();
        
        // 清理RenderTexture
        if (convertedRT != null)
        {
            RenderTexture.ReleaseTemporary(convertedRT);
            convertedRT = null;
        }
        
        Debug.Log("🧹 WebRTC 已清理");
    }
}
