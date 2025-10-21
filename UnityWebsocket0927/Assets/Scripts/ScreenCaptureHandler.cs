using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Collections.Concurrent;

public class ScreenCaptureHandler : MonoBehaviour
{
    [Header("顯示設定")]
    public DisplayMode displayMode = DisplayMode.RawImage;
    public Renderer targetRenderer;
    public RawImage targetRawImage;
    public Material screenMaterial;
    
    [Header("性能設定")]
    public int maxQueueSize = 1; // 減少到1，最低延遲
    public float baseUpdateInterval = 0.033f; // 30fps base
    public float maxUpdateInterval = 0.1f; // 最大10fps
    
    private Texture2D screenTexture;
    private ConcurrentQueue<GyroscopeReceiver.ScreenFrame> frameQueue = new ConcurrentQueue<GyroscopeReceiver.ScreenFrame>();
    private float lastUpdateTime = 0f;
    private int frameCount = 0;
    private float adaptiveInterval;
    
    // 性能監控
    private float[] frameTimes = new float[10];
    private int frameTimeIndex = 0;
    
    void Start()
    {
        // WebSocket 模式作為降級方案
        // 初始禁用，等 WebRTC 失敗時啟用
        this.enabled = false;
        
        // 訂閱事件
        GyroscopeReceiver.OnScreenCaptureReceived += HandleScreenFrame;
        
        // 檢查顯示目標設置
        if (displayMode == DisplayMode.Renderer && targetRenderer == null)
            targetRenderer = GetComponent<Renderer>();
            
        if (displayMode == DisplayMode.Renderer && screenMaterial == null)
        {
            screenMaterial = new Material(Shader.Find("Standard"));
            if (targetRenderer != null)
                targetRenderer.material = screenMaterial;
        }
        
        adaptiveInterval = baseUpdateInterval;
        
        Debug.Log("📺 ScreenCaptureHandler 已初始化（WebSocket 降級模式）");
    }
    
    void HandleScreenFrame(GyroscopeReceiver.ScreenFrame frame)
    {
        // 立即處理模式：直接處理最新幀，丟棄舊的
        if (frameQueue.Count >= maxQueueSize)
        {
            frameQueue.TryDequeue(out _);
        }
        
        frameQueue.Enqueue(frame);
        
        // 立即處理最新幀以減少延遲
        ProcessNextFrame();
        
        Debug.Log($"📺 收到螢幕幀: ClientId={frame.clientId}, Size={frame.size} bytes, 佇列={frameQueue.Count}");
    }
    
    void Update()
    {
        // 自適應更新間隔
        if (Time.time - lastUpdateTime >= adaptiveInterval && frameQueue.Count > 0)
        {
            ProcessNextFrame();
            lastUpdateTime = Time.time;
            
            // 更新自適應間隔
            UpdateAdaptiveInterval();
        }
    }
    
    void ProcessNextFrame()
    {
        if (frameQueue.TryDequeue(out GyroscopeReceiver.ScreenFrame frame))
        {
            ProcessFrame(frame);
        }
    }
    
    void ProcessFrame(GyroscopeReceiver.ScreenFrame frame)
    {
        try
        {
            // 重用 Texture2D
            if (screenTexture == null)
            {
                screenTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            }
            
            // 載入圖像（標記為不可讀，減少記憶體使用）
            if (screenTexture.LoadImage(frame.data, true))
            {
                // 根據顯示模式應用紋理
                if (displayMode == DisplayMode.RawImage && targetRawImage != null)
                {
                    targetRawImage.texture = screenTexture;
                    Debug.Log($"📺 RawImage 模式：已設置螢幕紋理到 {targetRawImage.name}");
                }
                else if (displayMode == DisplayMode.Renderer && targetRenderer != null && screenMaterial != null)
                {
                    screenMaterial.mainTexture = screenTexture;
                    targetRenderer.material = screenMaterial;
                    Debug.Log($"📺 Renderer 模式：已設置螢幕紋理到材質");
                }
                else
                {
                    Debug.LogWarning($"⚠️ 顯示目標未設置或模式不匹配！模式: {displayMode}, Renderer: {targetRenderer != null}, RawImage: {targetRawImage != null}");
                }
                
                frameCount++;
                Debug.Log($"📺 處理螢幕幀 #{frameCount} (ClientId: {frame.clientId}, Size: {frame.size} bytes)");
            }
            else
            {
                Debug.LogError("❌ 無法載入螢幕捕獲數據");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ 處理螢幕幀錯誤: {e.Message}");
        }
    }
    
    void UpdateAdaptiveInterval()
    {
        // 計算平均幀時間
        float currentFrameTime = Time.deltaTime;
        frameTimes[frameTimeIndex] = currentFrameTime;
        frameTimeIndex = (frameTimeIndex + 1) % frameTimes.Length;
        
        float avgFrameTime = 0f;
        for (int i = 0; i < frameTimes.Length; i++)
        {
            avgFrameTime += frameTimes[i];
        }
        avgFrameTime /= frameTimes.Length;
        
        // 根據性能調整間隔
        if (avgFrameTime > 0.016f) // 如果幀時間超過16ms
        {
            adaptiveInterval = Mathf.Min(adaptiveInterval * 1.1f, maxUpdateInterval);
        }
        else
        {
            adaptiveInterval = Mathf.Max(adaptiveInterval * 0.95f, baseUpdateInterval);
        }
    }
    
    void OnDestroy()
    {
        GyroscopeReceiver.OnScreenCaptureReceived -= HandleScreenFrame;
    }
    
    // 性能監控
    void OnGUI()
    {
        if (Application.isPlaying)
        {
            GUILayout.BeginArea(new Rect(10, 250, 300, 150));
            GUILayout.Label($"螢幕捕獲幀數: {frameCount}");
            GUILayout.Label($"佇列大小: {frameQueue.Count}");
            GUILayout.Label($"更新間隔: {adaptiveInterval:F3}s");
            GUILayout.Label($"目標FPS: {1f/adaptiveInterval:F1}");
            GUILayout.EndArea();
        }
    }
}