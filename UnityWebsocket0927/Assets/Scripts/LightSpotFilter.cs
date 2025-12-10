using UnityEngine;
using System.Collections.Generic;

public class LightSpotFilter : MonoBehaviour
{
    [Header("平滑設定")]
    [Tooltip("是否使用指數平滑(比移動平均更即時)")]
    [SerializeField] private bool useExponentialSmoothing = true;

    [Tooltip("平滑時間常數(秒)，越小越貼合，越大越穩定")]
    [SerializeField] private float smoothingTime = 0.05f;

    [Tooltip("是否使用中值濾波去除明顯突刺")]
    [SerializeField] private bool useMedianFilter = true;

    [Range(3, 15)]
    [SerializeField] private int medianFilterWindow = 5;

    [Header("速度限制(選用)")]
    [SerializeField] private bool useVelocityLimit = false;
    [SerializeField] private float maxVelocity = 10f;

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;

    private Queue<Vector2> positionQueue = new Queue<Vector2>();

    private Vector2 lastFilteredPosition;
    private Vector2 lastRawPosition;
    private float lastUpdateTime;
    private bool initialized = false;

    public Vector2 FilterPosition(Vector2 raw)
    {
        float now = Time.time;
        float dt = now - lastUpdateTime;
        lastUpdateTime = now;

        // 初始化
        if (!initialized || dt <= 0.0001f)
        {
            initialized = true;
            lastRawPosition = raw;
            lastFilteredPosition = raw;
            return raw;
        }

        // ★ Step 1：突刺偵測（anti-spike）
        float jumpDist = Vector2.Distance(raw, lastRawPosition);
        if (jumpDist > 0.25f) // UV 跳太多 → 異常
        {
            raw = lastRawPosition + (raw - lastRawPosition).normalized * 0.25f;
        }

        lastRawPosition = raw;

        // ★ Step 2：中值濾波（移除邊界噪聲）
        if (useMedianFilter)
        {
            positionQueue.Enqueue(raw);
            if (positionQueue.Count > medianFilterWindow)
                positionQueue.Dequeue();

            if (positionQueue.Count == medianFilterWindow)
                raw = GetMedianPosition();
        }

        // ★ Step 3：速度自適應平滑（速度快 → 少平滑）
        Vector2 delta = raw - lastFilteredPosition;
        float speed = delta.magnitude / Mathf.Max(dt, 0.0001f);

        float dynamicSmooth = smoothingTime / Mathf.Clamp(speed + 1f, 1f, 10f);

        // ★ Step 4：指數平滑（最穩、最少延遲）
        float t = 1f - Mathf.Exp(-dt / dynamicSmooth);
        Vector2 filtered = Vector2.Lerp(lastFilteredPosition, raw, t);

        lastFilteredPosition = filtered;

        if (showDebugInfo)
        {
            Debug.Log($"🔍 Filtered UV = ({filtered.x:F3}, {filtered.y:F3}), speed={speed:F2}");
        }

        return filtered;
    }

    private Vector2 GetMedianPosition()
    {
        List<Vector2> list = new List<Vector2>(positionQueue);
        List<float> xs = new List<float>();
        List<float> ys = new List<float>();

        foreach (var p in list)
        {
            xs.Add(p.x);
            ys.Add(p.y);
        }

        xs.Sort();
        ys.Sort();

        return new Vector2(xs[xs.Count / 2], ys[ys.Count / 2]);
    }

    public void Reset()
    {
        positionQueue.Clear();
        lastFilteredPosition = Vector2.zero;
        lastRawPosition = Vector2.zero;
        lastUpdateTime = Time.time;
        initialized = false;
    }

    private void Start()
    {
        lastUpdateTime = Time.time;
    }
}
