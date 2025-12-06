using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 手电筒光点追踪器（优化版）
/// 功能：实时追踪相机画面中最亮的点（手电筒光点）
/// 优化：降采样、ROI区域、时间稳定性、噪声过滤
/// </summary>
public class LightSpotTracker : MonoBehaviour
{
    [Header("相机设置")]
    [SerializeField] private int requestedWidth = 640;
    [SerializeField] private int requestedHeight = 480;
    [SerializeField] private int requestedFPS = 30;
    
    [Header("追踪设置")]
    [Tooltip("亮度阈值，手电筒通常很亮（200-230）")]
    [SerializeField] private float threshold = 200f;
    
    [Tooltip("平滑系数，值越大响应越快（建议8-10，平衡平滑度和响应速度）")]
    [SerializeField] private float smooth = 8f;
    
    [Tooltip("降采样步长，每N个像素采样一次（提升性能）")]
    [Range(1, 8)]
    [SerializeField] private int downSampleStep = 4;
    
    [Header("ROI区域（感兴趣区域）")]
    [Tooltip("是否只处理画面中心区域")]
    [SerializeField] private bool useROI = true;
    
    [Tooltip("ROI区域大小（0-1，相对于画面大小）")]
    [Range(0.3f, 1f)]
    [SerializeField] private float roiSize = 0.8f;
    
    [Header("噪声过滤")]
    [Tooltip("最小亮度差阈值（避免追踪微弱光源，建议降低到15-20以减少频繁丢失）")]
    [SerializeField] private float minBrightnessDelta = 20f;
    
    [Tooltip("时间稳定性：光点位置变化不能超过此值（像素，建议300-600，太小会导致频繁丢失追踪）")]
    [SerializeField] private float maxPositionDelta = 400f;
    
    [Tooltip("连续丢失帧数超过此值则重置追踪（建议60，给更多容错时间，减少频繁重置）")]
    [SerializeField] private int maxLostFrames = 60;
    
    [Header("高级过滤（可选）")]
    [Tooltip("使用高级过滤器（LightSpotFilter组件）")]
    [SerializeField] private bool useAdvancedFilter = false;
    
    [Tooltip("高级过滤器组件（可选）")]
    [SerializeField] private LightSpotFilter advancedFilter;
    
    [Header("调试")]
    [SerializeField] private bool showDebugInfo = false;
    [SerializeField] private bool showROI = false;
    
    // 公共属性
    public Vector2 spotUV { get; private set; }
    public bool isTracking { get; private set; }
    public float currentBrightness { get; private set; }
    
    // 私有变量
    private WebCamTexture cam;
    private Vector2 lastValidUV;
    private int lostFrameCount = 0;
    private Queue<Vector2> positionHistory = new Queue<Vector2>();
    private const int HISTORY_SIZE = 5;
    
    void Start()
    {
        InitializeCamera();
        
        // 如果没有指定高级过滤器，尝试自动获取
        if (useAdvancedFilter && advancedFilter == null)
        {
            advancedFilter = GetComponent<LightSpotFilter>();
            if (advancedFilter == null)
            {
                Debug.LogWarning("⚠️ 启用了高级过滤但未找到 LightSpotFilter 组件");
            }
        }
    }
    
    void InitializeCamera()
    {
        // 获取可用的相机设备
        if (WebCamTexture.devices.Length == 0)
        {
            Debug.LogError("❌ 未找到可用的相机设备");
            return;
        }
        
        // 创建WebCamTexture
        cam = new WebCamTexture(WebCamTexture.devices[0].name, requestedWidth, requestedHeight, requestedFPS);
        cam.Play();
        
        Debug.Log($"📷 相机已启动: {cam.deviceName}, 分辨率: {cam.width}x{cam.height}");
    }
    
    void Update()
    {
        if (cam == null || !cam.isPlaying || cam.width <= 16)
        {
            return;
        }
        
        // 等待相机初始化完成
        if (cam.width <= 16 || cam.height <= 16)
        {
            return;
        }
        
        TrackLightSpot();
    }
    
    void TrackLightSpot()
    {
        Color32[] pixels = cam.GetPixels32();
        int w = cam.width;
        int h = cam.height;
        
        // 计算ROI区域
        int roiStartX = useROI ? (int)(w * (1f - roiSize) * 0.5f) : 0;
        int roiStartY = useROI ? (int)(h * (1f - roiSize) * 0.5f) : 0;
        int roiEndX = useROI ? (int)(w * (1f + roiSize) * 0.5f) : w;
        int roiEndY = useROI ? (int)(h * (1f + roiSize) * 0.5f) : h;
        
        // 限制在有效范围内
        roiEndX = Mathf.Min(roiEndX, w);
        roiEndY = Mathf.Min(roiEndY, h);
        
        float brightMax = threshold;
        int brightX = -1, brightY = -1;
        float totalBrightness = 0f;
        int sampleCount = 0;
        
        // 降采样遍历（性能优化）
        for (int y = roiStartY; y < roiEndY; y += downSampleStep)
        {
            int row = y * w;
            for (int x = roiStartX; x < roiEndX; x += downSampleStep)
            {
                Color32 px = pixels[row + x];
                float brightness = (px.r + px.g + px.b) / 3f;
                
                totalBrightness += brightness;
                sampleCount++;
                
                // 检查是否超过阈值
                if (brightness > brightMax)
                {
                    brightMax = brightness;
                    brightX = x;
                    brightY = y;
                }
            }
        }
        
        // 计算平均亮度（用于噪声过滤）
        float avgBrightness = sampleCount > 0 ? totalBrightness / sampleCount : 0f;
        currentBrightness = brightMax;
        
        // 检查是否找到光点
        if (brightX >= 0)
        {
            // 噪声过滤：检查亮度差
            if (brightMax - avgBrightness < minBrightnessDelta)
            {
                // 亮度差太小，可能是环境光而非手电筒
                HandleLostTracking();
                return;
            }
            
            Vector2 newUV = new Vector2((float)brightX / w, (float)brightY / h);
            
            // 时间稳定性检查（改进：小幅度移动时不要重置）
            if (positionHistory.Count > 0)
            {
                Vector2 lastUV = positionHistory.Peek();
                float pixelDeltaX = Mathf.Abs(newUV.x - lastUV.x) * w;
                float pixelDeltaY = Mathf.Abs(newUV.y - lastUV.y) * h;
                float pixelDelta = Mathf.Sqrt(pixelDeltaX * pixelDeltaX + pixelDeltaY * pixelDeltaY);
                
                // 小幅度移动（小于10像素）时，认为是正常移动，不重置
                float smallMoveThreshold = 10f;
                if (pixelDelta > smallMoveThreshold && pixelDelta > maxPositionDelta)
                {
                    // 位置变化太大，可能是噪声或错误检测
                    HandleLostTracking();
                    return;
                }
                // 如果移动幅度很小，继续追踪（可能是静止或缓慢移动）
            }
            
            // 更新位置历史
            positionHistory.Enqueue(newUV);
            if (positionHistory.Count > HISTORY_SIZE)
            {
                positionHistory.Dequeue();
            }
            
            // 应用高级过滤（如果启用）
            Vector2 filteredUV = newUV;
            if (useAdvancedFilter && advancedFilter != null)
            {
                filteredUV = advancedFilter.FilterPosition(newUV);
            }
            
            // 平滑处理
            spotUV = Vector2.Lerp(spotUV, filteredUV, Time.deltaTime * smooth);
            lastValidUV = spotUV;
            lostFrameCount = 0;
            isTracking = true;
            
            if (showDebugInfo)
            {
                Debug.Log($"🔦 追踪光点: UV=({spotUV.x:F3}, {spotUV.y:F3}), 亮度={brightMax:F1}, 像素=({brightX}, {brightY})");
            }
        }
        else
        {
            HandleLostTracking();
        }
    }
    
    void HandleLostTracking()
    {
        lostFrameCount++;
        
        if (lostFrameCount > maxLostFrames)
        {
            // 丢失追踪时间过长，重置所有状态
            isTracking = false;
            positionHistory.Clear();
            // 保持 spotUV 不变，以便重新找到光点时能平滑过渡
            
            if (showDebugInfo)
            {
                Debug.LogWarning("⚠️ 光点丢失，已重置追踪");
            }
        }
        else
        {
            // 使用最后有效位置（保持平滑）
            if (isTracking)
            {
                spotUV = Vector2.Lerp(spotUV, lastValidUV, Time.deltaTime * smooth * 0.5f);
            }
        }
    }
    
    void OnDestroy()
    {
        if (cam != null && cam.isPlaying)
        {
            cam.Stop();
        }
    }
    
    void OnGUI()
    {
        if (!showDebugInfo || !Application.isPlaying) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 400, 200));
        GUILayout.Label($"相机状态: {(cam != null && cam.isPlaying ? "运行中" : "未启动")}");
        if (cam != null)
        {
            GUILayout.Label($"分辨率: {cam.width}x{cam.height}");
        }
        GUILayout.Label($"追踪状态: {(isTracking ? "追踪中" : "未追踪")}");
        GUILayout.Label($"光点UV: ({spotUV.x:F3}, {spotUV.y:F3})");
        GUILayout.Label($"当前亮度: {currentBrightness:F1}");
        GUILayout.Label($"丢失帧数: {lostFrameCount}/{maxLostFrames}");
        
        if (GUILayout.Button("重置追踪"))
        {
            ResetTracking();
        }
        GUILayout.EndArea();
        
        // 显示ROI区域
        if (showROI && cam != null && cam.isPlaying)
        {
            float roiWidth = Screen.width * roiSize;
            float roiHeight = Screen.height * roiSize;
            float roiX = Screen.width * (1f - roiSize) * 0.5f;
            float roiY = Screen.height * (1f - roiSize) * 0.5f;
            
            // 绘制ROI边框（使用GUI.Box的简单方式）
            GUI.color = Color.green;
            GUI.Box(new Rect(roiX, roiY, roiWidth, roiHeight), "");
            GUI.color = Color.white;
        }
    }
    
    /// <summary>
    /// 重置追踪状态
    /// </summary>
    public void ResetTracking()
    {
        spotUV = Vector2.zero;
        lastValidUV = Vector2.zero;
        lostFrameCount = 0;
        isTracking = false;
        positionHistory.Clear();
        
        // 重置高级过滤器
        if (advancedFilter != null)
        {
            advancedFilter.Reset();
        }
        
        Debug.Log("🔄 追踪已重置");
    }
    
    /// <summary>
    /// 获取相机纹理（用于显示）
    /// </summary>
    public Texture GetCameraTexture()
    {
        return cam;
    }
}

