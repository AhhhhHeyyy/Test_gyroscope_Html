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

    [Header("Debug")]
    public bool showDebug = false;

    public Vector2 spotUV = Vector2.zero;
    private Vector2 lastValidUV = new Vector2(0.5f, 0.5f);

    private int lostCounter = 0;

    Texture2D frameTex;

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

        frameTex = new Texture2D(webcamTexture.width, webcamTexture.height, TextureFormat.RGB24, false);
    }

    void Update()
    {
        if (!webcamTexture.isPlaying) return;

        ProcessFrame();
    }

    void ProcessFrame()
    {
        frameTex.SetPixels(webcamTexture.GetPixels());
        frameTex.Apply();

        bool found;
        Vector2 uv = FindBrightestPoint(frameTex, out found);

        UpdateTrackingState(found, uv);

        if (showDebug)
        {
            Debug.Log($"🔦 Raw UV = {uv}, Tracking={isTracking}, LostCount={lostCounter}");
        }
    }

    // ================================
    // ⭐ 動態 ROI + 最亮點擷取
    // ================================
    Vector2 FindBrightestPoint(Texture2D tex, out bool found)
    {
        int w = tex.width;
        int h = tex.height;

        Color32[] px = tex.GetPixels32();

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
