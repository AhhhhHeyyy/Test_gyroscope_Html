using System;
using UnityEngine;

/// <summary>
/// 感測器靜態事件匯流排。
/// UdpGyroscopeReceiver 透過 Raise* 方法觸發事件；
/// 其他腳本訂閱 On* 事件接收資料，無需知道資料來源。
/// </summary>
public static class SensorEvents
{
    // ── 資料型別 ──────────────────────────────────────────────

    [Serializable]
    public class GyroscopeData
    {
        public float alpha, beta, gamma;   // WebSocket 備用傾斜角（度）
        public float qx, qy, qz, qw;      // UDP 四元數
        public float unityY;
        public long  timestamp;
    }

    [Serializable]
    public class PitchWaveData
    {
        public int    count;
        public float  change;
        public float  beta;
        public string direction;
        public long   timestamp;
    }

    // ── 靜態事件 ──────────────────────────────────────────────

    public static event Action<GyroscopeData> OnGyroscopeDataReceived;
    public static event Action<Vector3>       OnAccelerationReceived;
    public static event Action<PitchWaveData> OnPitchWaveReceived;

    // ── 觸發方法（供 UdpGyroscopeReceiver 呼叫）──────────────

    public static void RaiseGyroscopeDataReceived(GyroscopeData data) => OnGyroscopeDataReceived?.Invoke(data);
    public static void RaiseAccelerationReceived(Vector3 acc)         => OnAccelerationReceived?.Invoke(acc);
    public static void RaisePitchWaveReceived(PitchWaveData data)     => OnPitchWaveReceived?.Invoke(data);
}
