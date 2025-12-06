using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 手电筒控制器（增强版）
/// 功能：将光点UV坐标转换为屏幕坐标，使用Raycast控制3D物体
/// 增强：正确的坐标转换、LayerMask过滤、交互反馈、平滑移动
/// </summary>
public class FlashlightController : MonoBehaviour
{
    [Header("追踪器引用")]
    [Tooltip("光点追踪器组件")]
    [SerializeField] private LightSpotTracker tracker;
    
    [Header("相机设置")]
    [Tooltip("用于Raycast的场景相机")]
    [SerializeField] private Camera sceneCamera;
    
    [Header("Raycast设置")]
    [Tooltip("射线最大距离")]
    [SerializeField] private float rayDistance = 20f;
    
    [Tooltip("可交互的Layer（留空则检测所有Layer）")]
    [SerializeField] private LayerMask interactableLayers = -1;
    
    [Header("物体控制")]
    [Tooltip("是否让物体跟随光点移动")]
    [SerializeField] private bool moveObjectToHitPoint = false;
    
    [Tooltip("是否改变物体颜色")]
    [SerializeField] private bool changeColorOnHit = true;
    
    [Tooltip("命中时的颜色")]
    [SerializeField] private Color hitColor = Color.yellow;
    
    [Tooltip("未命中时的颜色")]
    [SerializeField] private Color defaultColor = Color.white;
    
    [Header("交互反馈")]
    [Tooltip("是否显示射线（调试用）")]
    [SerializeField] private bool showRay = true;
    
    [Tooltip("是否显示命中点")]
    [SerializeField] private bool showHitPoint = true;
    
    [Tooltip("命中点标记大小")]
    [SerializeField] private float hitPointSize = 0.1f;
    
    [Header("高级设置")]
    [Tooltip("坐标转换时考虑相机画面宽高比")]
    [SerializeField] private bool useAspectRatioCorrection = true;
    
    [Tooltip("最小移动距离（避免微小抖动，建议0.05-0.1，太小会导致频繁移动）")]
    [SerializeField] private float minMoveDistance = 0.08f;
    
    [Header("平滑设置")]
    [Tooltip("目标位置平滑系数（先平滑目标位置，再应用SmoothDamp，建议4-8，值越小响应越快）")]
    [SerializeField] private float targetSmooth = 6f;
    
    [Tooltip("位置平滑时间（SmoothDamp的平滑时间，建议0.03-0.06，值越小响应越快）")]
    [SerializeField] private float positionSmoothTime = 0.05f;
    
    [Tooltip("快速移动时的平滑时间倍数（快速移动时使用更短的平滑时间，建议0.5-0.7）")]
    [SerializeField] private float fastMoveSmoothMultiplier = 0.6f;
    
    [Tooltip("快速移动速度阈值（单位/秒，超过此速度视为快速移动）")]
    [SerializeField] private float fastMoveSpeedThreshold = 2f;
    
    [Header("轴敏感度设置")]
    [Tooltip("Y轴敏感度/缩放系数（增大此值可以让Y轴变化更明显，建议3.0-5.0）")]
    [SerializeField] private float yAxisSensitivity = 3.5f;
    
    [Tooltip("是否使用非线性Y轴缩放（平方函数，放大Y轴变化）")]
    [SerializeField] private bool useNonLinearYScaling = true;
    
    [Tooltip("是否分别处理X和Y轴的平滑（Y轴可以更敏感）")]
    [SerializeField] private bool separateAxisSmoothing = true;
    
    [Header("调试")]
    [SerializeField] private bool showDebugInfo = false;
    
    // 私有变量
    private RaycastHit currentHit;
    private bool isHitting = false;
    private GameObject lastHitObject = null;
    private Dictionary<GameObject, Color> originalColors = new Dictionary<GameObject, Color>();
    private Vector3 targetPosition;
    private Vector3 smoothedTargetPosition; // 平滑后的目标位置（用于减少抖动）
    private Vector3 currentPosition;
    private Vector3 velocity = Vector3.zero; // 用于 SmoothDamp
    private Vector3 lastValidTargetPosition; // 最后有效的目标位置（用于丢失追踪时继续平滑）
    private bool hasValidTarget = false; // 是否有有效的目标位置
    private Vector3 lastTargetPosition; // 上一帧的目标位置（用于速度检测）
    private float lastUpdateTime; // 上一帧的更新时间
    
    void Start()
    {
        // 如果没有指定相机，使用主相机
        if (sceneCamera == null)
        {
            sceneCamera = Camera.main;
            if (sceneCamera == null)
            {
                Debug.LogError("❌ 未找到场景相机，请指定 sceneCamera");
            }
        }
        
        // 如果没有指定追踪器，尝试获取
        if (tracker == null)
        {
            tracker = FindFirstObjectByType<LightSpotTracker>();
            if (tracker == null)
            {
                Debug.LogError("❌ 未找到 LightSpotTracker，请指定 tracker");
            }
        }
        
        if (moveObjectToHitPoint)
        {
            targetPosition = transform.position;
            smoothedTargetPosition = transform.position;
            currentPosition = transform.position;
            lastTargetPosition = transform.position;
            lastUpdateTime = Time.time;
        }
    }
    
    void Update()
    {
        if (tracker == null || sceneCamera == null)
        {
            return;
        }
        
        // 执行Raycast（即使追踪丢失也尝试，以便平滑过渡）
        if (tracker.isTracking)
        {
            PerformRaycast();
        }
        else
        {
            // 追踪丢失时，恢复物体颜色
            if (isHitting)
            {
                RestoreLastHitObject();
            }
        }
        
        // 处理物体移动（即使追踪丢失也继续平滑移动到最后位置）
        if (moveObjectToHitPoint)
        {
            if (isHitting)
            {
                MoveObjectToHitPoint();
            }
            else if (hasValidTarget)
            {
                // 追踪丢失时，继续平滑移动到最后一个有效位置
                ContinueSmoothMovement();
            }
        }
    }
    
    void PerformRaycast()
    {
        Vector2 uv = tracker.spotUV;
        
        // 修正上下颠倒：WebCamTexture 的 Y 轴是反的，需要翻转
        uv.y = 1f - uv.y;
        
        // 应用Y轴敏感度：将Y轴坐标从中心点向外扩展
        // 这样可以增大Y轴的变化幅度
        float centerY = 0.5f;
        float yOffset = uv.y - centerY;
        float normalizedY;
        
        if (useNonLinearYScaling)
        {
            // 使用非线性缩放（平方函数）：放大Y轴变化，特别是远离中心时
            float sign = yOffset >= 0 ? 1f : -1f;
            float normalizedOffset = sign * Mathf.Pow(Mathf.Abs(yOffset), 0.7f) * yAxisSensitivity;
            normalizedY = centerY + normalizedOffset;
        }
        else
        {
            // 线性缩放
            normalizedY = yOffset * yAxisSensitivity + centerY;
        }
        
        // 限制在有效范围内
        normalizedY = Mathf.Clamp01(normalizedY);
        
        Vector3 screenPos = new Vector3(
            uv.x * Screen.width,
            normalizedY * Screen.height,
            0
        );
        
        // 如果启用宽高比校正
        if (useAspectRatioCorrection && tracker.GetCameraTexture() != null)
        {
            Texture camTex = tracker.GetCameraTexture();
            float camAspect = (float)camTex.width / camTex.height;
            float screenAspect = (float)Screen.width / Screen.height;
            
            // 如果宽高比不同，需要调整坐标
            if (Mathf.Abs(camAspect - screenAspect) > 0.01f)
            {
                // 计算缩放因子
                float scaleX = screenAspect / camAspect;
                if (scaleX > 1f)
                {
                    // 屏幕更宽，需要水平缩放
                    screenPos.x = Screen.width * 0.5f + (screenPos.x - Screen.width * 0.5f) * scaleX;
                }
                else
                {
                    // 屏幕更高，需要垂直缩放
                    float scaleY = 1f / scaleX;
                    screenPos.y = Screen.height * 0.5f + (screenPos.y - Screen.height * 0.5f) * scaleY;
                }
            }
        }
        
        // 从相机发出射线
        Ray ray = sceneCamera.ScreenPointToRay(screenPos);
        
        // 执行Raycast
        if (Physics.Raycast(ray, out RaycastHit hit, rayDistance, interactableLayers))
        {
            HandleHit(hit);
        }
        else
        {
            HandleMiss();
        }
    }
    
    void HandleHit(RaycastHit hit)
    {
        isHitting = true;
        currentHit = hit;
        
        // 如果命中了新物体
        if (hit.collider.gameObject != lastHitObject)
        {
            // 恢复上一个物体的颜色
            RestoreLastHitObject();
            
            // 保存新物体的原始颜色
            lastHitObject = hit.collider.gameObject;
            Renderer r = lastHitObject.GetComponent<Renderer>();
            if (r != null && r.material != null)
            {
                if (!originalColors.ContainsKey(lastHitObject))
                {
                    originalColors[lastHitObject] = r.material.color;
                }
                
                // 改变颜色
                if (changeColorOnHit)
                {
                    r.material.color = hitColor;
                }
            }
        }
        
        if (showDebugInfo)
        {
            Debug.Log($"🎯 命中: {hit.collider.name} at {hit.point}");
        }
    }
    
    void HandleMiss()
    {
        if (isHitting)
        {
            RestoreLastHitObject();
        }
        isHitting = false;
    }
    
    void RestoreLastHitObject()
    {
        if (lastHitObject != null)
        {
            Renderer r = lastHitObject.GetComponent<Renderer>();
            if (r != null && r.material != null && originalColors.ContainsKey(lastHitObject))
            {
                r.material.color = originalColors[lastHitObject];
            }
            lastHitObject = null;
        }
    }
    
    void MoveObjectToHitPoint()
    {
        if (!isHitting) return;
        
        // 第一步：获取原始目标位置
        targetPosition = currentHit.point;
        
        // 检测移动速度（用于自适应平滑）
        float currentTime = Time.time;
        float deltaTime = currentTime - lastUpdateTime;
        float moveSpeed = 0f;
        if (deltaTime > 0.001f && hasValidTarget)
        {
            moveSpeed = Vector3.Distance(targetPosition, lastTargetPosition) / deltaTime;
        }
        lastTargetPosition = targetPosition;
        lastUpdateTime = currentTime;
        
        // 第二步：先对目标位置进行平滑处理（减少抖动）
        // 快速移动时减少平滑，提高响应速度
        float effectiveTargetSmooth = targetSmooth;
        if (moveSpeed > fastMoveSpeedThreshold)
        {
            // 快速移动时，减少目标位置平滑，提高响应
            effectiveTargetSmooth = targetSmooth * 0.5f;
        }
        
        if (separateAxisSmoothing)
        {
            // 分别处理X、Y、Z轴的平滑，Y轴可以更敏感
            smoothedTargetPosition = new Vector3(
                Mathf.Lerp(smoothedTargetPosition.x, targetPosition.x, Time.deltaTime * effectiveTargetSmooth),
                Mathf.Lerp(smoothedTargetPosition.y, targetPosition.y, Time.deltaTime * effectiveTargetSmooth * 1.5f), // Y轴平滑更快
                Mathf.Lerp(smoothedTargetPosition.z, targetPosition.z, Time.deltaTime * effectiveTargetSmooth)
            );
        }
        else
        {
            smoothedTargetPosition = Vector3.Lerp(
                smoothedTargetPosition,
                targetPosition,
                Time.deltaTime * effectiveTargetSmooth
            );
        }
        
        lastValidTargetPosition = smoothedTargetPosition;
        hasValidTarget = true;
        
        // 检查移动距离
        float distance = Vector3.Distance(currentPosition, smoothedTargetPosition);
        if (distance < minMoveDistance)
        {
            return; // 距离太小，不移动
        }
        
        // 第三步：使用 SmoothDamp 平滑移动到目标位置
        // 快速移动时使用更短的平滑时间，提高响应速度
        float effectiveSmoothTime = positionSmoothTime;
        if (moveSpeed > fastMoveSpeedThreshold)
        {
            effectiveSmoothTime = positionSmoothTime * fastMoveSmoothMultiplier;
        }
        
        if (separateAxisSmoothing)
        {
            // 分别处理各轴的平滑，Y轴使用更短的平滑时间以响应更快
            Vector3 targetVel = velocity;
            currentPosition = new Vector3(
                Mathf.SmoothDamp(currentPosition.x, smoothedTargetPosition.x, ref targetVel.x, effectiveSmoothTime),
                Mathf.SmoothDamp(currentPosition.y, smoothedTargetPosition.y, ref targetVel.y, effectiveSmoothTime * 0.6f), // Y轴响应更快
                Mathf.SmoothDamp(currentPosition.z, smoothedTargetPosition.z, ref targetVel.z, effectiveSmoothTime)
            );
            velocity = targetVel;
        }
        else
        {
            currentPosition = Vector3.SmoothDamp(
                currentPosition,
                smoothedTargetPosition,
                ref velocity,
                effectiveSmoothTime
            );
        }
        transform.position = currentPosition;
    }
    
    /// <summary>
    /// 追踪丢失时继续平滑移动到最后有效位置
    /// </summary>
    void ContinueSmoothMovement()
    {
        if (!hasValidTarget) return;
        
        // 继续平滑移动到最后一个有效位置，但速度逐渐减慢
        float remainingDistance = Vector3.Distance(currentPosition, lastValidTargetPosition);
        
        if (remainingDistance > 0.01f)
        {
            // 使用更长的平滑时间，让移动逐渐停止
            currentPosition = Vector3.SmoothDamp(
                currentPosition,
                lastValidTargetPosition,
                ref velocity,
                0.2f  // 更长的平滑时间，让停止更自然
            );
            transform.position = currentPosition;
        }
        else
        {
            // 已经到达最后位置，停止移动
            hasValidTarget = false;
        }
    }
    
    void OnDrawGizmos()
    {
        if (!showRay || tracker == null || sceneCamera == null || !tracker.isTracking)
        {
            return;
        }
        
        Vector2 uv = tracker.spotUV;
        // 修正上下颠倒：WebCamTexture 的 Y 轴是反的，需要翻转
        uv.y = 1f - uv.y;
        Vector3 screenPos = new Vector3(
            uv.x * Screen.width,
            uv.y * Screen.height,
            0
        );
        
        Ray ray = sceneCamera.ScreenPointToRay(screenPos);
        
        // 绘制射线
        Gizmos.color = isHitting ? Color.green : Color.red;
        Gizmos.DrawRay(ray.origin, ray.direction * rayDistance);
        
        // 绘制命中点
        if (isHitting && showHitPoint)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(currentHit.point, hitPointSize);
        }
    }
    
    void OnGUI()
    {
        if (!showDebugInfo || !Application.isPlaying) return;
        
        GUILayout.BeginArea(new Rect(10, 220, 400, 150));
        GUILayout.Label($"Raycast状态: {(isHitting ? "命中" : "未命中")}");
        if (isHitting)
        {
            GUILayout.Label($"命中物体: {currentHit.collider.name}");
            GUILayout.Label($"命中点: {currentHit.point}");
            GUILayout.Label($"距离: {currentHit.distance:F2}");
        }
        if (moveObjectToHitPoint)
        {
            GUILayout.Label($"目标位置: {targetPosition}");
            GUILayout.Label($"当前位置: {currentPosition}");
        }
        GUILayout.EndArea();
    }
    
    /// <summary>
    /// 获取当前命中的物体
    /// </summary>
    public GameObject GetCurrentHitObject()
    {
        return isHitting ? currentHit.collider.gameObject : null;
    }
    
    /// <summary>
    /// 获取当前命中点
    /// </summary>
    public Vector3 GetCurrentHitPoint()
    {
        return isHitting ? currentHit.point : Vector3.zero;
    }
    
    /// <summary>
    /// 是否正在命中物体
    /// </summary>
    public bool IsHitting()
    {
        return isHitting;
    }
}

