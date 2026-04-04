using UnityEngine;
using Unity.WebRTC;
using System.Collections;
using System.Threading.Tasks;
using NativeWebSocket;

public class TestScreenCon : MonoBehaviour
{
    public string signalingUrl = "ws://localhost:8081"; // 本地测试服务器
    public string roomId = "default-room";
    public Renderer targetRenderer;

    private RTCPeerConnection peerConnection;
    private WebSocket ws;

    IEnumerator Start()
    {
        Debug.Log("🚀 TestScreenCon Start() 方法开始执行");
        Debug.Log($"🔗 准备连接到: {signalingUrl}");
        Debug.Log($"🏠 房间ID: {roomId}");
        Debug.Log($"🎯 目标渲染器: {(targetRenderer != null ? targetRenderer.name : "未设置")}");
        
        // 不再需要 WebRTC.Initialize()
        ws = new WebSocket(signalingUrl);

        ws.OnMessage += (bytes) =>
        {
            var msg = System.Text.Encoding.UTF8.GetString(bytes);
            HandleSignalingMessage(msg);
        };

        ws.OnOpen += () =>
        {
            Debug.Log("✅ WebSocket连接成功");
            Debug.Log($"📤 发送加入房间消息: room={roomId}, role=unity-receiver");
            SendJoin(); // ✅ 改用固定格式
        };

        ws.OnError += (e) =>
        {
            Debug.LogError($"❌ WebSocket连接错误: {e}");
        };

        ws.OnClose += (e) =>
        {
            Debug.Log($"🔌 WebSocket连接关闭: {e}");
        };

        // 使用协程等待连接
        yield return StartCoroutine(ConnectWebSocket());
    }

    private IEnumerator ConnectWebSocket()
    {
        Debug.Log("🔄 开始WebSocket连接...");
        var connectTask = ws.Connect();
        yield return new WaitUntil(() => connectTask.IsCompleted);
        if (connectTask.IsFaulted)
        {
            Debug.LogError($"❌ WebSocket连接失败: {connectTask.Exception}");
        }
        else
        {
            Debug.Log("✅ WebSocket连接任务完成");
        }
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        ws?.DispatchMessageQueue();
#endif
    }

    void HandleSignalingMessage(string json)
    {
        Debug.Log($"📩 收到信令消息: {json}");
        try
        {
            var msg = JsonUtility.FromJson<SignalingMessage>(json);
            switch (msg.type)
            {
                case "joined":
                    Debug.Log($"✅ 已加入房间: {msg.room}");
                    break;

                case "ready":
                    Debug.Log("📡 房间已就绪，等待 Offer...");
                    break;

                case "offer":
                    Debug.Log($"🎯 收到 Offer，开始处理");
                    StartCoroutine(HandleOffer(msg.sdp));
                    break;

                case "candidate":
                    if (peerConnection != null && msg.candidate != null)
                    {
                        var candidate = new RTCIceCandidate(new RTCIceCandidateInit
                        {
                            candidate = msg.candidate.candidate,
                            sdpMid = msg.candidate.sdpMid,
                            sdpMLineIndex = msg.candidate.sdpMLineIndex
                        });
                        peerConnection.AddIceCandidate(candidate);
                        Debug.Log("📨 已添加 ICE Candidate");
                    }
                    break;

                default:
                    Debug.LogWarning($"⚠️ 未知消息类型: {msg.type}");
                    break;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ 解析信令失败: {e.Message}");
        }
    }

    IEnumerator HandleOffer(string sdp)
    {
        Debug.Log("🧩 收到 SDP 內容:\n" + sdp);
        
        // 初始化 PeerConnection（Unity WebRTC 3.0.0 安全相容配置）
        var config = new RTCConfiguration
        {
            iceServers = new[] { new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } } }
        };
        
        peerConnection = new RTCPeerConnection(ref config);

        peerConnection.OnIceCandidate = (candidate) =>
        {
            if (candidate == null) return;

            SendCandidate(candidate);
        };

        peerConnection.OnTrack = e =>
        {
            if (e.Track is VideoStreamTrack videoTrack)
            {
                Debug.Log("🎥 收到远端视频流");
                videoTrack.OnVideoReceived += tex =>
                {
                    if (targetRenderer != null && targetRenderer.material != null)
                    {
                        var mat = targetRenderer.material;

                        // 通用 BaseMap (URP/HDRP)
                        if (mat.HasProperty("_BaseMap"))
                            mat.SetTexture("_BaseMap", tex);
                        
                        // 傳統 Standard
                        if (mat.HasProperty("_MainTex"))
                            mat.SetTexture("_MainTex", tex);
                        
                        // 備用路徑
                        mat.mainTexture = tex;

                        // ✅ 格式偵測：只有 Texture2D 才有 format
                        if (tex is Texture2D tex2D)
                            Debug.Log($"✅ 视频纹理已应用: {tex2D.width}x{tex2D.height}, Format={tex2D.format}");
                        else
                            Debug.Log($"✅ 视频纹理已应用: {tex.width}x{tex.height}");
                    }
                    else
                    {
                        Debug.LogWarning("⚠️ targetRenderer 或 material 為空，無法應用貼圖！");
                    }
                };
            }
        };

        // 設定遠端描述 (Offer)
        var desc = new RTCSessionDescription { type = RTCSdpType.Offer, sdp = sdp };
        var setRemoteOp = peerConnection.SetRemoteDescription(ref desc);
        yield return setRemoteOp;

        // 建立 Answer
        var answerOp = peerConnection.CreateAnswer();
        yield return answerOp;
        var answerDesc = answerOp.Desc;

        // 設定本地描述
        var setLocalOp = peerConnection.SetLocalDescription(ref answerDesc);
        yield return setLocalOp;

        // 傳送 Answer 給對端
        SendAnswer(answerDesc.sdp);

        Debug.Log("📤 已发送 Answer");
    }

    async void SendCandidate(RTCIceCandidate candidate)
    {
        var candidateJson = "{\"candidate\":\"" + candidate.Candidate + "\",\"sdpMid\":\"" + candidate.SdpMid + "\",\"sdpMLineIndex\":" + (candidate.SdpMLineIndex ?? 0) + "}";
        var json = "{\"type\":\"candidate\",\"room\":\"" + roomId + "\",\"from\":\"unity-receiver\",\"candidate\":" + candidateJson + "}";
        Debug.Log($"📤 发送ICE Candidate: {json}");
        if (ws != null && ws.State == WebSocketState.Open)
        {
            await ws.SendText(json);
        }
    }

    async void SendAnswer(string sdp)
    {
        // 🔧 將所有換行符轉為 \n，確保 JSON 合法
        var escapedSdp = sdp
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");

        var json = $"{{\"type\":\"answer\",\"room\":\"{roomId}\",\"from\":\"unity-receiver\",\"sdp\":\"{escapedSdp}\"}}";
        Debug.Log($"📤 发送Answer: {json}");

        if (ws != null && ws.State == WebSocketState.Open)
        {
            await ws.SendText(json);
        }
    }

    async void SendJoin()
    {
        var json = "{\"type\":\"join\",\"room\":\"" + roomId + "\",\"role\":\"unity-receiver\"}";
        Debug.Log($"📤 发送JSON消息: {json}");
        if (ws != null && ws.State == WebSocketState.Open)
        {
            await ws.SendText(json);
        }
        else
        {
            Debug.LogWarning($"⚠️ WebSocket未连接，无法发送消息。状态: {ws?.State}");
        }
    }

    async void SendJSON(object obj)
    {
        if (ws != null && ws.State == WebSocketState.Open)
        {
            var json = JsonUtility.ToJson(obj);
            Debug.Log($"📤 发送JSON消息: {json}");
            await ws.SendText(json);
        }
        else
        {
            Debug.LogWarning($"⚠️ WebSocket未连接，无法发送消息。状态: {ws?.State}");
        }
    }

    private async void OnDestroy()
    {
        if (ws != null)
        {
            await ws.Close();
        }
        peerConnection?.Close();
    }

    [System.Serializable]
    public class SignalingMessage
    {
        public string type;
        public string room;
        public string from;
        public string sdp;
        public Candidate candidate;
    }

    [System.Serializable]
    public class Candidate
    {
        public string candidate;
        public string sdpMid;
        public int sdpMLineIndex;
    }
}
