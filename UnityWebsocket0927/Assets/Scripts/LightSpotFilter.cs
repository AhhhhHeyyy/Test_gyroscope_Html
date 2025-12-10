using UnityEngine;
using System.Collections.Generic;

public class LightSpotFilter : MonoBehaviour
{
    [Header("平滑設定")]
    [Tooltip("是否使用指數平滑(比移動平均更即時)")]
    [SerializeField] private bool useExponentialSmoothing = true;

    [Tooltip("平滑時間常數(秒)，越小越貼合，越大越穩定")]
    [SerializeField] private float smoothingTime = 0.05f; // 約 50ms

    [Tooltip("是否使用中值濾波去除明顯突刺")]
    [SerializeField] private bool useMedianFilter = true;

    [Range(3, 15)]
    [SerializeField] private int medianFilterWindow = 5;

    [Header("速度限制(選用)")]
    [SerializeField] private bool useVelocityLimit = false;
    [SerializeField] private float maxVelocity = 10f; // UV/秒, 先調大一點

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;

    private Queue<Vector2> positionQueue = new Queue<Vector2>();

    private Vector2 lastFilteredPosition;
    private Vector2 lastRawPosition;
    private float lastUpdateTime;
    private bool initialized = false;

    public Vector2 FilterPosition(Vector2 rawPosition)
    {
        float currentTime = Time.time;
        float deltaTime = currentTime - lastUpdateTime;
        lastUpdateTime = currentTime;

        // 第一次樣本直接初始化，不要做任何濾波，避免奇怪抖動
        if (!initialized || deltaTime <= 0.0001f)
        {
            initialized = true;
            lastRawPosition = rawPosition;
            lastFilteredPosition = rawPosition;
            return rawPosition;
        }

        // 速度限制（可關掉或調很大）
        if (useVelocityLimit)
        {
            Vector2 velocity = (rawPosition - lastRawPosition) / deltaTime;
            float speed = velocity.magnitude;

            if (speed > maxVelocity)
            {
                velocity = velocity.normalized * maxVelocity;
                rawPosition = lastRawPosition + velocity * deltaTime;
            }
        }

        lastRawPosition = rawPosition;

        // ----------- 中值濾波：只用來去掉怪異突刺 -----------
        if (useMedianFilter)
        {
            positionQueue.Enqueue(rawPosition);
            if (positionQueue.Count > medianFilterWindow)
                positionQueue.Dequeue();

            if (positionQueue.Count == medianFilterWindow)
            {
                rawPosition = GetMedianPosition();
            }
        }

        // ----------- 指數平滑 / SmoothDamp 類似效果 -----------
        Vector2 filtered = rawPosition;

        if (useExponentialSmoothing)
        {
            // deltaTime / smoothingTime 越大 → 越貼合原始點
            float t = 1f - Mathf.Exp(-deltaTime / Mathf.Max(0.0001f, smoothingTime));
            filtered = Vector2.Lerp(lastFilteredPosition, rawPosition, t);
        }

        lastFilteredPosition = filtered;

        if (showDebugInfo)
        {
            Debug.Log($"🔍 Filter: raw=({lastRawPosition.x:F3},{lastRawPosition.y:F3}) " +
                      $"filtered=({filtered.x:F3},{filtered.y:F3})");
        }

        return filtered;
    }

    private Vector2 GetMedianPosition()
    {
        List<Vector2> positions = new List<Vector2>(positionQueue);
        List<float> xs = new List<float>();
        List<float> ys = new List<float>();

        foreach (var p in positions)
        {
            xs.Add(p.x);
            ys.Add(p.y);
        }

        xs.Sort();
        ys.Sort();

        float mx = xs[xs.Count / 2];
        float my = ys[ys.Count / 2];
        return new Vector2(mx, my);
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
