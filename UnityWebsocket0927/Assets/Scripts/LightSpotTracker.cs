using UnityEngine;

public class LightSpotTracker : MonoBehaviour
{
    [Header("Camera Input")]
    public WebCamTexture webcamTexture;
    public bool isTracking = false;

    [Header("Tracking Parameters")]
    public float minBrightness = 200f;
    public int downSample = 2;

    [Tooltip("動態 ROI 搜尋半徑 (0~0.5)")]
    [Range(0.05f, 0.5f)]
    public float roiRadius = 0.15f;

    [Tooltip("允許幾幀找不到光點才判定丟失")]
    public int lostTolerance = 4;

    [Header("Performance")]
    [Tooltip("處理間隔（每N幀處理一次，1=每幀，2=每2幀，3=每3幀...）")]
    [Range(1, 5)]
    public int processingInterval = 2;

    [Tooltip("目標處理幀率（0=使用processingInterval，>0=使用協程控制）")]
    public int targetFPS = 0;

    [Header("Filtering")]
    [Tooltip("是否使用高級過濾器（LightSpotFilter）")]
    public bool useAdvancedFilter = false;
    [Tooltip("高級過濾器組件引用（可選）")]
    public LightSpotFilter advancedFilter = null;

    [Header("Debug")]
    public bool showDebug = false;
    [Tooltip("顯示性能統計")]
    public bool showPerformanceStats = false;

    public Vector2 spotUV = Vector2.zero;
    private Vector2 lastValidUV = new Vector2(0.5f, 0.5f);

    private int lostCounter = 0;
    private int frameCounter = 0;

    private int cachedWidth = 0;
    private int cachedHeight = 0;

    // 性能統計
    private float lastStatsTime = 0f;
    private int processedFrameCount = 0;
    private float totalProcessTime = 0f;

    void Start()
    {
        if (webcamTexture == null)
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices.Length > 0)
            {
                webcamTexture = new WebCamTexture(devices[0].name, 1920, 1080, 30);
            }
        }

        webcamTexture.Play();

        // 等待一幀確保紋理已初始化
        StartCoroutine(InitializeTexture());
    }

    System.Collections.IEnumerator InitializeTexture()
    {
        yield return null; // 等待一幀
        
        if (webcamTexture != null && webcamTexture.isPlaying)
        {
            cachedWidth = webcamTexture.width;
            cachedHeight = webcamTexture.height;
        }

        // 初始化性能統計
        lastStatsTime = Time.time;

        // 自動查找過濾器組件（如果未手動指定）
        if (useAdvancedFilter && advancedFilter == null)
        {
            advancedFilter = GetComponent<LightSpotFilter>();
            if (advancedFilter == null)
            {
                Debug.LogWarning("LightSpotTracker: 已啟用高級過濾器，但未找到 LightSpotFilter 組件。請在同一個 GameObject 上添加 LightSpotFilter 組件。");
            }
        }

        // 如果使用目標FPS，啟動協程處理
        if (targetFPS > 0)
        {
            StartCoroutine(ProcessFrameCoroutine());
        }
    }

    void Update()
    {
        if (!webcamTexture.isPlaying) return;

        // 如果使用協程模式，Update中不處理
        if (targetFPS > 0) return;

        // 使用幀跳過機制
        frameCounter++;
        if (frameCounter >= processingInterval)
        {
            frameCounter = 0;
            ProcessFrame();
        }
    }

    System.Collections.IEnumerator ProcessFrameCoroutine()
    {
        float interval = targetFPS > 0 ? (1f / targetFPS) : (1f / 30f);
        WaitForSeconds wait = new WaitForSeconds(interval);

        while (webcamTexture != null && webcamTexture.isPlaying)
        {
            ProcessFrame();
            yield return wait;
        }
    }

    void ProcessFrame()
    {
        float startTime = Time.realtimeSinceStartup;

        // 檢查紋理尺寸是否變化（用於緩存）
        if (webcamTexture.width != cachedWidth || webcamTexture.height != cachedHeight)
        {
            cachedWidth = webcamTexture.width;
            cachedHeight = webcamTexture.height;
        }

        // 優化：直接使用像素數組，避免中間的 SetPixels 和 Apply
        // 獲取像素數據（這是必要的GPU到CPU傳輸，但已通過幀跳過機制優化）
        Color32[] pixels = webcamTexture.GetPixels32();
        int width = webcamTexture.width;
        int height = webcamTexture.height;

        bool found;
        Vector2 rawUV = FindBrightestPointFromPixels(pixels, width, height, out found);

        // 如果啟用高級過濾器，對原始 UV 進行過濾
        Vector2 uv = rawUV;
        if (useAdvancedFilter && advancedFilter != null && found)
        {
            uv = advancedFilter.FilterPosition(rawUV);
        }

        UpdateTrackingState(found, uv);

        // 性能統計
        if (showPerformanceStats)
        {
            float processTime = (Time.realtimeSinceStartup - startTime) * 1000f; // 轉換為毫秒
            processedFrameCount++;
            totalProcessTime += processTime;

            // 每秒更新一次統計
            if (Time.time - lastStatsTime >= 1f)
            {
                float avgProcessTime = totalProcessTime / processedFrameCount;
                float actualFPS = processedFrameCount / (Time.time - lastStatsTime);
                Debug.Log($"📊 處理性能: {actualFPS:F1} FPS, 平均處理時間: {avgProcessTime:F2}ms");
                
                processedFrameCount = 0;
                totalProcessTime = 0f;
                lastStatsTime = Time.time;
            }
        }

        if (showDebug)
        {
            Debug.Log($"🔦 Raw UV = {uv}, Tracking={isTracking}, LostCount={lostCounter}");
        }
    }

    // ================================
    // ⭐ 動態 ROI + 最亮點擷取（優化版：直接使用像素數組）
    // ================================
    Vector2 FindBrightestPointFromPixels(Color32[] px, int w, int h, out bool found)
    {

        float maxBrightness = 0f;
        int maxIndex = -1;

        // 先使用 ROI（上一幀 spot 附近搜尋）
        int xCenter = Mathf.RoundToInt(lastValidUV.x * w);
        int yCenter = Mathf.RoundToInt(lastValidUV.y * h);
        int r = Mathf.RoundToInt(roiRadius * Mathf.Min(w, h));

        int xMin = Mathf.Clamp(xCenter - r, 0, w - 1);
        int xMax = Mathf.Clamp(xCenter + r, 0, w - 1);
        int yMin = Mathf.Clamp(yCenter - r, 0, h - 1);
        int yMax = Mathf.Clamp(yCenter + r, 0, h - 1);

        for (int y = yMin; y < yMax; y += downSample)
        {
            int row = y * w;
            for (int x = xMin; x < xMax; x += downSample)
            {
                Color32 c = px[row + x];
                float b = (c.r + c.g + c.b) * 0.333f;

                if (b > maxBrightness)
                {
                    maxBrightness = b;
                    maxIndex = row + x;
                }
            }
        }

        // ROI 找不到 → fallback 全畫面搜尋
        if (maxBrightness < minBrightness)
        {
            maxBrightness = 0f;
            maxIndex = -1;

            for (int i = 0; i < px.Length; i += downSample)
            {
                Color32 c = px[i];
                float b = (c.r + c.g + c.b) * 0.333f;

                if (b > maxBrightness)
                {
                    maxBrightness = b;
                    maxIndex = i;
                }
            }
        }

        if (maxBrightness >= minBrightness && maxIndex >= 0)
        {
            found = true;

            int pxY = maxIndex / w;
            int pxX = maxIndex % w;

            // 轉 UV
            float u = (float)pxX / w;
            float v = (float)pxY / h;

            return new Vector2(u, v);
        }
        else
        {
            found = false;
            return spotUV;
        }
    }

    // =======================================
    // ⭐ 延遲丟失：避免光點瞬間跳掉
    // =======================================
    void UpdateTrackingState(bool found, Vector2 newUV)
    {
        if (found)
        {
            lostCounter = 0;
            isTracking = true;

            // Clamp UV 避免邊界抖動
            newUV = new Vector2(
                Mathf.Clamp(newUV.x, 0.05f, 0.95f),
                Mathf.Clamp(newUV.y, 0.05f, 0.95f)
            );

            spotUV = newUV;
            lastValidUV = newUV;
            return;
        }

        // 沒找到 → 計數
        lostCounter++;

        if (lostCounter < lostTolerance)
        {
            // 允許短暫丟失 → 繼續沿用上一幀 UV
            isTracking = true;
            spotUV = lastValidUV;
            return;
        }

        // 超過 tolerance → 真的丟失
        isTracking = false;
    }

    public Vector2 GetSpotUV()
    {
        return spotUV;
    }

    public Texture GetCameraTexture()
    {
        return webcamTexture;
    }

}
