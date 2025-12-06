using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 光点高级过滤器（可选）
/// 功能：提供更高级的过滤算法，如卡尔曼滤波、移动平均等
/// 用于需要更高精度和稳定性的场景
/// </summary>
public class LightSpotFilter : MonoBehaviour
{
    [Header("过滤设置")]
    [Tooltip("使用移动平均滤波")]
    [SerializeField] private bool useMovingAverage = true;
    
    [Tooltip("移动平均窗口大小")]
    [Range(3, 20)]
    [SerializeField] private int movingAverageWindow = 5;
    
    [Tooltip("使用中值滤波（去除异常值）")]
    [SerializeField] private bool useMedianFilter = true;
    
    [Tooltip("中值滤波窗口大小")]
    [Range(3, 15)]
    [SerializeField] private int medianFilterWindow = 5;
    
    [Tooltip("使用速度限制（防止突然跳跃）")]
    [SerializeField] private bool useVelocityLimit = true;
    
    [Tooltip("最大速度（UV单位/秒）")]
    [SerializeField] private float maxVelocity = 2f;
    
    [Header("调试")]
    [SerializeField] private bool showDebugInfo = false;
    
    // 私有变量
    private Queue<Vector2> positionQueue = new Queue<Vector2>();
    private Queue<Vector2> velocityQueue = new Queue<Vector2>();
    private Vector2 lastFilteredPosition;
    private Vector2 lastRawPosition;
    private float lastUpdateTime;
    
    /// <summary>
    /// 过滤输入位置
    /// </summary>
    /// <param name="rawPosition">原始UV位置</param>
    /// <returns>过滤后的UV位置</returns>
    public Vector2 FilterPosition(Vector2 rawPosition)
    {
        float currentTime = Time.time;
        float deltaTime = currentTime - lastUpdateTime;
        lastUpdateTime = currentTime;
        
        // 速度限制
        if (useVelocityLimit && deltaTime > 0.001f)
        {
            Vector2 velocity = (rawPosition - lastRawPosition) / deltaTime;
            float speed = velocity.magnitude;
            
            if (speed > maxVelocity)
            {
                // 限制速度
                velocity = velocity.normalized * maxVelocity;
                rawPosition = lastRawPosition + velocity * deltaTime;
            }
        }
        
        lastRawPosition = rawPosition;
        
        // 添加到队列
        positionQueue.Enqueue(rawPosition);
        
        // 保持队列大小
        if (positionQueue.Count > Mathf.Max(movingAverageWindow, medianFilterWindow))
        {
            positionQueue.Dequeue();
        }
        
        Vector2 filtered = rawPosition;
        
        // 中值滤波（去除异常值）
        if (useMedianFilter && positionQueue.Count >= medianFilterWindow)
        {
            filtered = GetMedianPosition();
        }
        
        // 移动平均滤波（平滑处理）
        if (useMovingAverage && positionQueue.Count >= movingAverageWindow)
        {
            filtered = GetMovingAveragePosition();
        }
        
        lastFilteredPosition = filtered;
        
        if (showDebugInfo)
        {
            Debug.Log($"🔍 过滤: 原始=({rawPosition.x:F3}, {rawPosition.y:F3}), " +
                     $"过滤后=({filtered.x:F3}, {filtered.y:F3})");
        }
        
        return filtered;
    }
    
    /// <summary>
    /// 获取中值位置（去除异常值）
    /// </summary>
    Vector2 GetMedianPosition()
    {
        if (positionQueue.Count < medianFilterWindow)
        {
            return lastFilteredPosition;
        }
        
        List<Vector2> positions = new List<Vector2>(positionQueue);
        int startIndex = positions.Count - medianFilterWindow;
        
        // 提取最近的N个位置
        List<Vector2> recentPositions = positions.GetRange(startIndex, medianFilterWindow);
        
        // 分别对X和Y进行中值计算
        List<float> xValues = new List<float>();
        List<float> yValues = new List<float>();
        
        foreach (Vector2 pos in recentPositions)
        {
            xValues.Add(pos.x);
            yValues.Add(pos.y);
        }
        
        xValues.Sort();
        yValues.Sort();
        
        float medianX = xValues[xValues.Count / 2];
        float medianY = yValues[yValues.Count / 2];
        
        return new Vector2(medianX, medianY);
    }
    
    /// <summary>
    /// 获取移动平均位置
    /// </summary>
    Vector2 GetMovingAveragePosition()
    {
        if (positionQueue.Count < movingAverageWindow)
        {
            return lastFilteredPosition;
        }
        
        Vector2 sum = Vector2.zero;
        List<Vector2> positions = new List<Vector2>(positionQueue);
        int startIndex = positions.Count - movingAverageWindow;
        
        // 计算最近N个位置的平均值
        for (int i = startIndex; i < positions.Count; i++)
        {
            sum += positions[i];
        }
        
        return sum / movingAverageWindow;
    }
    
    /// <summary>
    /// 重置过滤器
    /// </summary>
    public void Reset()
    {
        positionQueue.Clear();
        velocityQueue.Clear();
        lastFilteredPosition = Vector2.zero;
        lastRawPosition = Vector2.zero;
        lastUpdateTime = Time.time;
    }
    
    void Start()
    {
        lastUpdateTime = Time.time;
    }
}

