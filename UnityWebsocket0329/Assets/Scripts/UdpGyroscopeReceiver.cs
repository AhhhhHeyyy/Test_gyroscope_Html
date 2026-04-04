using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UDP 陀螺儀接收器 — 搭配 Android App 直接傳送 binary 封包。
///
/// 封包格式 28 bytes Big-Endian：
///   [0-15]  四元數 qx, qy, qz, qw  (各 4 bytes float)
///   [16-27] 線性加速度 ax, ay, az   (各 4 bytes float, m/s²)
///
/// 支援舊版 16 bytes（只有四元數）封包，加速度自動為零。
///
/// 功能：
///   - 接收四元數並同步到 GyroscopeReceiver
///   - 觸發 GyroscopeReceiver.OnAccelerationReceived 事件
///   - 從加速度自動偵測搖晃，觸發 OnShakeDataReceived
///   - 從四元數 pitch 角偵測 Y 軸上下揮動，觸發 OnPitchWaveReceived
/// </summary>
public class UdpGyroscopeReceiver : MonoBehaviour
{
    [Header("UDP 設置")]
    [SerializeField] private int udpPort = 9999;
    [SerializeField] private bool enableUdp = true;
    [SerializeField] private bool debugLog = false;

    [Header("同步到現有 GyroscopeReceiver（可選）")]
    [Tooltip("拖入場景中的 GyroscopeReceiver，UDP 資料會同步寫入其公開欄位")]
    [SerializeField] private GyroscopeReceiver gyroReceiver;
    [SerializeField] private bool syncToGyroReceiver = true;

    [Header("搖晃偵測")]
    [SerializeField] private float shakeThresholdNormal  = 15f;  // m/s²
    [SerializeField] private float shakeThresholdStrong  = 22f;
    [SerializeField] private float shakeThresholdIntense = 30f;
    [SerializeField] private float shakeCooldown = 0.4f;         // 秒

    [Header("Pitch Wave 偵測（Y 軸上下）")]
    [SerializeField] private float pitchWaveThreshold = 28f;     // 度，角度變化量
    [SerializeField] private float pitchWaveCooldown  = 0.5f;    // 秒

    [Header("狀態（唯讀）")]
    [SerializeField] public bool isReceiving = false;
    [SerializeField] public string localIP = "";
    [SerializeField] public int totalPackets = 0;
    [SerializeField] public float currentHz = 0f;

    [Header("最新四元數（唯讀）")]
    public float m_qx = 0f;
    public float m_qy = 0f;
    public float m_qz = 0f;
    public float m_qw = 1f;

    [Header("最新加速度（唯讀，m/s²）")]
    public float m_accX = 0f;
    public float m_accY = 0f;
    public float m_accZ = 0f;
    public float m_accMagnitude = 0f;

    // ---- 私有狀態 ----
    private UdpClient udpClient;
    private Thread receiveThread;
    private readonly Queue<byte[]> receiveQueue = new Queue<byte[]>();
    private readonly object queueLock = new object();

    private int frameCount = 0;
    private float hzTimer = 0f;

    // 搖晃偵測
    private int shakeCount = 0;
    private float lastShakeTime = -999f;

    // Pitch wave 偵測
    private float lastPitch = float.NaN;
    private float lastPitchWaveTime = -999f;
    private int pitchWaveCount = 0;

    // unityY 計算（手機傾斜對應 Y 軸位移）
    private float neutralPitch = float.NaN;  // 第一次收到封包時設定基準
    [Header("上下移動設定")]
    [SerializeField] private float unityYSensitivity = 30f;  // 幾度對應 1 unit
    [SerializeField] private float unityYMax = 3f;           // 最大值限制
    [SerializeField] public float currentUnityY = 0f;        // 目前 unityY（唯讀）

    // -------------------------------------------------------
    void Start()
    {
        localIP = GetLocalIP();
        if (enableUdp)
            StartReceiver();
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
            currentHz = frameCount / hzTimer;
            frameCount = 0;
            hzTimer = 0f;
        }
    }

    void OnDestroy() => StopReceiver();

    // -------------------------------------------------------
    public void StartReceiver()
    {
        if (isReceiving) return;
        try
        {
            udpClient = new UdpClient(udpPort);
            isReceiving = true;
            receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "UdpGyroThread" };
            receiveThread.Start();
            Debug.Log($"[UdpGyro] 監聽 {localIP}:{udpPort}  ← 手機 App 請填此 IP");
        }
        catch (Exception e)
        {
            Debug.LogError($"[UdpGyro] 啟動失敗: {e.Message}");
        }
    }

    public void StopReceiver()
    {
        isReceiving = false;
        try { udpClient?.Close(); } catch { }
        receiveThread?.Join(200);

        if (syncToGyroReceiver && gyroReceiver != null)
        {
            gyroReceiver.isConnected = false;
            gyroReceiver.connectionStatus = "UDP 已停止";
        }
    }

    // -------------------------------------------------------
    // 背景執行緒
    // -------------------------------------------------------
    private void ReceiveLoop()
    {
        var endPoint = new IPEndPoint(IPAddress.Any, 0);
        while (isReceiving)
        {
            try
            {
                byte[] data = udpClient.Receive(ref endPoint);
                if (data.Length == 28 || data.Length == 16)
                {
                    lock (queueLock)
                        receiveQueue.Enqueue(data);
                }
                else if (debugLog)
                    Debug.LogWarning($"[UdpGyro] 非預期封包長度: {data.Length} bytes（預期 28 或 16）");
            }
            catch (Exception e)
            {
                if (isReceiving)
                    Debug.LogWarning($"[UdpGyro] 接收例外: {e.Message}");
            }
        }
    }

    // -------------------------------------------------------
    // 主執行緒：解析封包
    // -------------------------------------------------------
    private void ProcessPacket(byte[] data)
    {
        // --- 四元數 ---
        float qx = ReadBigEndianFloat(data, 0);
        float qy = ReadBigEndianFloat(data, 4);
        float qz = ReadBigEndianFloat(data, 8);
        float qw = ReadBigEndianFloat(data, 12);

        m_qx = qx;
        m_qy = qy;
        m_qz = qz;
        m_qw = qw;
        totalPackets++;
        frameCount++;

        // --- 加速度（28 bytes 新格式）---
        float ax = 0f, ay = 0f, az = 0f;
        if (data.Length >= 28)
        {
            ax = ReadBigEndianFloat(data, 16);
            ay = ReadBigEndianFloat(data, 20);
            az = ReadBigEndianFloat(data, 24);
        }
        m_accX = ax;
        m_accY = ay;
        m_accZ = az;
        m_accMagnitude = Mathf.Sqrt(ax * ax + ay * ay + az * az);

        if (debugLog)
            Debug.Log($"[UdpGyro] Q({qx:F3},{qy:F3},{qz:F3},{qw:F3})  Acc({ax:F2},{ay:F2},{az:F2})  {currentHz:F0}Hz");

        // --- 同步到 GyroscopeReceiver ---
        if (syncToGyroReceiver && gyroReceiver != null)
        {
            gyroReceiver.m_qx = qx;
            gyroReceiver.m_qy = qy;
            gyroReceiver.m_qz = qz;
            gyroReceiver.m_qw = qw;
            gyroReceiver.isConnected = true;
            gyroReceiver.connectionStatus = $"UDP {currentHz:F0} Hz";
        }

        // --- 觸發加速度事件 ---
        if (data.Length >= 28)
        {
            var accVec = new Vector3(ax, ay, az);
            GyroscopeReceiver.RaiseAccelerationReceived(accVec);

            // 搖晃偵測
            DetectShake(accVec);
        }

        // --- Pitch Wave 偵測 + unityY 計算（Y 軸上下）---
        float pitch = CalcPitch(qx, qy, qz, qw);
        if (float.IsNaN(neutralPitch)) neutralPitch = pitch;

        float unityY = Mathf.Clamp((pitch - neutralPitch) / unityYSensitivity, -unityYMax, unityYMax);
        currentUnityY = unityY;

        // 觸發 OnGyroscopeDataReceived（AccelerometerBallEffect 用 unityY）
        var gyroData = new GyroscopeReceiver.GyroscopeData
        {
            qx = qx, qy = qy, qz = qz, qw = qw,
            unityY = unityY,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        GyroscopeReceiver.RaiseGyroscopeDataReceived(gyroData);

        DetectPitchWave(pitch);
    }

    // -------------------------------------------------------
    // 搖晃偵測：從線性加速度大小判斷
    // -------------------------------------------------------
    private void DetectShake(Vector3 acc)
    {
        float mag = acc.magnitude;
        if (mag < shakeThresholdNormal) return;

        float now = Time.time;
        if (now - lastShakeTime < shakeCooldown) return;

        lastShakeTime = now;
        shakeCount++;

        string type = mag >= shakeThresholdIntense ? "intense"
                    : mag >= shakeThresholdStrong  ? "strong"
                    : "normal";

        var shakeData = new ShakeData(
            count:        shakeCount,
            intensity:    mag,
            shakeType:    type,
            acceleration: acc,
            timestamp:    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        GyroscopeReceiver.RaiseShakeDataReceived(shakeData);

        if (debugLog)
            Debug.Log($"[UdpGyro] 搖晃! Type={type}, Intensity={mag:F2} m/s²");
    }

    // -------------------------------------------------------
    // 從四元數計算 pitch 角（手機前後傾斜，度）
    // -------------------------------------------------------
    private static float CalcPitch(float qx, float qy, float qz, float qw)
    {
        float sinp = 2f * (qw * qx + qy * qz);
        float cosp = 1f - 2f * (qx * qx + qy * qy);
        return Mathf.Atan2(sinp, cosp) * Mathf.Rad2Deg;
    }

    // -------------------------------------------------------
    // Pitch Wave 偵測：傾斜角快速變化時觸發
    // -------------------------------------------------------
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
                string direction = change > 0 ? "up" : "down";
                var pitchWave = new GyroscopeReceiver.PitchWaveData
                {
                    count = pitchWaveCount, change = change,
                    beta = pitch, direction = direction,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                GyroscopeReceiver.RaisePitchWaveReceived(pitchWave);
                if (debugLog)
                    Debug.Log($"[UdpGyro] PitchWave! Dir={direction}, Change={change:F1}°");
            }
        }
        lastPitch = pitch;
    }

    // -------------------------------------------------------
    // 工具
    // -------------------------------------------------------
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
