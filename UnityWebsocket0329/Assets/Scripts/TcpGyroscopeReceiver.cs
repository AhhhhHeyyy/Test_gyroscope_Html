using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TCP 陀螺儀接收器 — 改自 UdpGyroscopeReceiver。
/// 改用 TCP 的優點：Windows 防火牆首次連線時會彈出「是否允許」視窗，
/// 點允許即可，不需要手動關閉防火牆。
///
/// 封包格式 28 bytes Big-Endian（與 UDP 版完全相同）：
///   [0-15]  四元數 qx, qy, qz, qw  (各 4 bytes float)
///   [16-27] 線性加速度 ax, ay, az   (各 4 bytes float, m/s²)
///
/// 手機斷線後會自動等待重新連入，不需重啟 Unity。
/// </summary>
public class TcpGyroscopeReceiver : MonoBehaviour
{
    [Header("TCP 設置")]
    [SerializeField] private int  tcpPort   = 9999;
    [SerializeField] private bool enableTcp = true;
    [SerializeField] private bool debugLog  = false;

    [Header("Pitch Wave 偵測（Y 軸上下揮動）")]
    [SerializeField] private float pitchWaveThreshold = 28f;
    [SerializeField] private float pitchWaveCooldown  = 0.5f;

    [Header("unityY 計算（pitch 角對應 Y 位移）")]
    [SerializeField] private float unityYSensitivity = 30f;
    [SerializeField] private float unityYMax         = 3f;

    [Header("狀態（唯讀）")]
    public bool   isReceiving  = false;
    public bool   isConnected  = false;
    public string localIP      = "";
    public int    totalPackets = 0;
    public float  currentHz    = 0f;

    [Header("最新四元數（唯讀）")]
    public float m_qx = 0f;
    public float m_qy = 0f;
    public float m_qz = 0f;
    public float m_qw = 1f;

    [Header("最新加速度（唯讀，m/s²）")]
    public float m_accX         = 0f;
    public float m_accY         = 0f;
    public float m_accZ         = 0f;
    public float m_accMagnitude = 0f;

    [Header("unityY（唯讀）")]
    public float currentUnityY = 0f;

    [Header("螢幕提示")]
    [SerializeField] private bool showOnScreenHint = true;
    [SerializeField] private int  hintFontSize     = 22;

    private TcpListener            tcpListener;
    private Thread                 listenThread;
    private TcpClient              connectedClient;
    private readonly Queue<byte[]> receiveQueue = new Queue<byte[]>();
    private readonly object        queueLock    = new object();
    private int   frameCount = 0;
    private float hzTimer    = 0f;

    private float lastPitch         = float.NaN;
    private float lastPitchWaveTime = -999f;
    private int   pitchWaveCount    = 0;
    private float neutralPitch      = float.NaN;

    // -------------------------------------------------------
    void Start()
    {
        localIP = GetLocalIP();
        if (enableTcp) StartReceiver();
    }

    void Update()
    {
        lock (queueLock)
        {
            while (receiveQueue.Count > 0)
                ProcessPacket(receiveQueue.Dequeue());
        }

        hzTimer += Time.deltaTime;
        if (hzTimer >= 1f)
        {
            currentHz  = frameCount / hzTimer;
            frameCount = 0;
            hzTimer    = 0f;
        }
    }

    void OnDestroy() => StopReceiver();

    // -------------------------------------------------------
    public void StartReceiver()
    {
        if (isReceiving) return;
        try
        {
            tcpListener  = new TcpListener(IPAddress.Any, tcpPort);
            tcpListener.Start();
            isReceiving  = true;
            listenThread = new Thread(ListenLoop) { IsBackground = true, Name = "TcpGyroThread" };
            listenThread.Start();
            Debug.Log($"[TcpGyro] 監聽 {localIP}:{tcpPort}  ← 手機 App 請填此 IP（TCP）");
        }
        catch (Exception e)
        {
            Debug.LogError($"[TcpGyro] 啟動失敗: {e.Message}");
        }
    }

    public void StopReceiver()
    {
        isReceiving = false;
        try { connectedClient?.Close(); } catch { }
        try { tcpListener?.Stop();      } catch { }
        listenThread?.Join(200);
    }

    // -------------------------------------------------------
    private void ListenLoop()
    {
        while (isReceiving)
        {
            try
            {
                TcpClient client = tcpListener.AcceptTcpClient();
                try { connectedClient?.Close(); } catch { }
                connectedClient = client;
                isConnected = true;

                if (debugLog)
                    Debug.Log($"[TcpGyro] 手機已連入: {client.Client.RemoteEndPoint}");

                ReceiveFromClient(client);

                isConnected = false;
                if (debugLog)
                    Debug.Log("[TcpGyro] 手機斷線，等待重新連入...");
            }
            catch (Exception e)
            {
                if (isReceiving)
                    Debug.LogWarning($"[TcpGyro] 接聽例外: {e.Message}");
            }
        }
    }

    private void ReceiveFromClient(TcpClient client)
    {
        const int PACKET_SIZE = 28;
        byte[]       buf    = new byte[PACKET_SIZE];
        NetworkStream stream = client.GetStream();

        while (isReceiving)
        {
            try
            {
                if (ReadExactly(stream, buf, PACKET_SIZE) < PACKET_SIZE) break;

                byte[] copy = new byte[PACKET_SIZE];
                Buffer.BlockCopy(buf, 0, copy, 0, PACKET_SIZE);
                lock (queueLock) receiveQueue.Enqueue(copy);
            }
            catch (Exception e)
            {
                if (isReceiving && debugLog)
                    Debug.LogWarning($"[TcpGyro] 接收例外: {e.Message}");
                break;
            }
        }
    }

    private static int ReadExactly(NetworkStream stream, byte[] buffer, int count)
    {
        int total = 0;
        while (total < count)
        {
            int n = stream.Read(buffer, total, count - total);
            if (n == 0) return total;
            total += n;
        }
        return total;
    }

    // -------------------------------------------------------
    private void ProcessPacket(byte[] data)
    {
        float qx = ReadBigEndianFloat(data, 0);
        float qy = ReadBigEndianFloat(data, 4);
        float qz = ReadBigEndianFloat(data, 8);
        float qw = ReadBigEndianFloat(data, 12);

        m_qx = qx; m_qy = qy; m_qz = qz; m_qw = qw;
        totalPackets++;
        frameCount++;

        float ax = ReadBigEndianFloat(data, 16);
        float ay = ReadBigEndianFloat(data, 20);
        float az = ReadBigEndianFloat(data, 24);
        m_accX = ax; m_accY = ay; m_accZ = az;
        m_accMagnitude = Mathf.Sqrt(ax * ax + ay * ay + az * az);

        float pitch = CalcPitch(qx, qy, qz, qw);
        if (float.IsNaN(neutralPitch)) neutralPitch = pitch;
        float unityY = Mathf.Clamp((pitch - neutralPitch) / unityYSensitivity, -unityYMax, unityYMax);
        currentUnityY = unityY;

        if (debugLog)
            Debug.Log($"[TcpGyro] Q({qx:F3},{qy:F3},{qz:F3},{qw:F3})  Acc({ax:F2},{ay:F2},{az:F2})  {currentHz:F0}Hz");

        SensorEvents.RaiseGyroscopeDataReceived(new SensorEvents.GyroscopeData
        {
            qx = qx, qy = qy, qz = qz, qw = qw,
            unityY    = unityY,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        SensorEvents.RaiseAccelerationReceived(new Vector3(ax, ay, az));
        DetectPitchWave(pitch);
    }

    private static float CalcPitch(float qx, float qy, float qz, float qw)
    {
        float sinp = 2f * (qw * qx + qy * qz);
        float cosp = 1f - 2f * (qx * qx + qy * qy);
        return Mathf.Atan2(sinp, cosp) * Mathf.Rad2Deg;
    }

    private void DetectPitchWave(float pitch)
    {
        if (float.IsNaN(lastPitch)) { lastPitch = pitch; return; }

        float change = pitch - lastPitch;
        if (Mathf.Abs(change) >= pitchWaveThreshold)
        {
            float now = Time.time;
            if (now - lastPitchWaveTime >= pitchWaveCooldown)
            {
                lastPitchWaveTime = now;
                pitchWaveCount++;
                SensorEvents.RaisePitchWaveReceived(new SensorEvents.PitchWaveData
                {
                    count     = pitchWaveCount,
                    change    = change,
                    beta      = pitch,
                    direction = change > 0 ? "up" : "down",
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
                if (debugLog)
                    Debug.Log($"[TcpGyro] PitchWave! Dir={( change > 0 ? "up" : "down" )}, Change={change:F1}°");
            }
        }
        lastPitch = pitch;
    }

    // -------------------------------------------------------
    private GUIStyle _hintStyle;
    private GUIStyle _boxStyle;

    void OnGUI()
    {
        if (!showOnScreenHint) return;

        if (_hintStyle == null)
        {
            _hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = hintFontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft
            };
            _hintStyle.normal.textColor = Color.white;
            _boxStyle = new GUIStyle(GUI.skin.box);
            _boxStyle.normal.background = MakeTex(1, 1, new Color(0f, 0f, 0f, 0.55f));
        }

        string conn   = isConnected ? "● 手機已連線" : "○ 等待手機連入";
        string status = isReceiving ? $"✓ 監聽中  {currentHz:F0} Hz\n{conn}" : "● 未啟動";
        string text   = $"手機 App 請填入以下位址\nIP：{localIP}\nPort：{tcpPort}\n{status}";

        float pad = 12f, w = hintFontSize * 14f, h = hintFontSize * 6f;
        var   rect = new Rect(pad, pad, w, h);
        GUI.Box(rect, GUIContent.none, _boxStyle);
        GUI.Label(new Rect(rect.x + pad, rect.y + pad, rect.width - pad * 2, rect.height - pad * 2),
                  text, _hintStyle);
    }

    private static Texture2D MakeTex(int w, int h, Color col)
    {
        var pix = new Color[w * h];
        for (int i = 0; i < pix.Length; i++) pix[i] = col;
        var tex = new Texture2D(w, h);
        tex.SetPixels(pix);
        tex.Apply();
        return tex;
    }

    private static float ReadBigEndianFloat(byte[] buf, int offset)
    {
        byte[] b = new byte[4] { buf[offset], buf[offset + 1], buf[offset + 2], buf[offset + 3] };
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        return BitConverter.ToSingle(b, 0);
    }

    private static string GetLocalIP()
    {
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect("8.8.8.8", 65530);
            return ((IPEndPoint)s.LocalEndPoint).Address.ToString();
        }
        catch { return "127.0.0.1"; }
    }
}
