using UnityEngine;

public class PositionController : MonoBehaviour
{
    [Header("追蹤設置")]
    [SerializeField] private bool enablePositionTracking = true;
    [SerializeField] private float positionSensitivity = 1f;
    [SerializeField] private bool useDeltaMovement = true; // 使用相對位移
    
    [Header("平滑設置")]
    [SerializeField] private bool enableSmoothing = true;
    [SerializeField] private float smoothingFactor = 0.1f;
    
    [Header("旋轉設置")]
    [SerializeField] private bool enableRotationTracking = false;
    [SerializeField] private float rotationSensitivity = 1f;
    
    [Header("調試")]
    [SerializeField] private bool showDebugInfo = false;
    
    private Vector3 targetPosition;
    private Vector3 currentPosition;
    private Vector3 initialPosition;
    private Quaternion targetRotation;
    private Quaternion currentRotation;
    private Quaternion initialRotation;
    
    void Start()
    {
        initialPosition = transform.position;
        currentPosition = initialPosition;
        targetPosition = initialPosition;
        
        initialRotation = transform.rotation;
        currentRotation = initialRotation;
        targetRotation = initialRotation;
        
        // 訂閱位置數據事件
        GyroscopeReceiver.OnPositionDataReceived += OnPositionDataReceived;
        
        Debug.Log("📍 PositionController 已啟動");
    }
    
    void OnDestroy()
    {
        GyroscopeReceiver.OnPositionDataReceived -= OnPositionDataReceived;
    }
    
    void Update()
    {
        if (enablePositionTracking)
        {
            ApplyPosition();
        }
        
        if (enableRotationTracking)
        {
            ApplyRotation();
        }
    }
    
    private void OnPositionDataReceived(GyroscopeReceiver.PositionData data)
    {
        if (useDeltaMovement)
        {
            // 使用相對位移（增量移動）
            targetPosition += new Vector3(
                data.delta.x * positionSensitivity,
                data.delta.y * positionSensitivity,
                data.delta.z * positionSensitivity
            );
        }
        else
        {
            // 使用絕對位置（相對於初始位置）
            targetPosition = initialPosition + new Vector3(
                data.position.x * positionSensitivity,
                data.position.y * positionSensitivity,
                data.position.z * positionSensitivity
            );
        }
        
        // 處理旋轉（如果啟用）
        if (enableRotationTracking)
        {
            var rotation = new Quaternion(
                data.rotation.x,
                data.rotation.y,
                data.rotation.z,
                data.rotation.w
            );
            
            // 將旋轉應用到目標旋轉（應用敏感度）
            // 使用 Slerp 來應用敏感度，類似於位置敏感度的處理方式
            targetRotation = initialRotation * Quaternion.Slerp(Quaternion.identity, rotation, rotationSensitivity);
        }
        
        if (showDebugInfo)
        {
            Debug.Log($"📍 收到位置數據: Pos=({data.position.x:F3}, {data.position.y:F3}, {data.position.z:F3}), " +
                     $"Delta=({data.delta.x:F3}, {data.delta.y:F3}, {data.delta.z:F3})");
        }
    }
    
    private void ApplyPosition()
    {
        if (enableSmoothing)
        {
            currentPosition = Vector3.Lerp(currentPosition, targetPosition, smoothingFactor);
        }
        else
        {
            currentPosition = targetPosition;
        }
        
        transform.position = currentPosition;
    }
    
    private void ApplyRotation()
    {
        if (enableSmoothing)
        {
            currentRotation = Quaternion.Lerp(currentRotation, targetRotation, smoothingFactor);
        }
        else
        {
            currentRotation = targetRotation;
        }
        
        transform.rotation = currentRotation;
    }
    
    // 重置到初始位置
    public void ResetPosition()
    {
        targetPosition = initialPosition;
        currentPosition = initialPosition;
        transform.position = initialPosition;
        
        targetRotation = initialRotation;
        currentRotation = initialRotation;
        transform.rotation = initialRotation;
        
        Debug.Log("🔄 位置已重置");
    }
    
    // 設置新的初始位置
    public void SetInitialPosition(Vector3 newInitialPosition)
    {
        initialPosition = newInitialPosition;
        ResetPosition();
    }
    
    void OnGUI()
    {
        if (showDebugInfo && Application.isPlaying)
        {
            GUILayout.BeginArea(new Rect(10, Screen.height - 150, 400, 140));
            GUILayout.Label($"位置追蹤: {(enablePositionTracking ? "啟用" : "停用")}");
            GUILayout.Label($"目標位置: ({targetPosition.x:F2}, {targetPosition.y:F2}, {targetPosition.z:F2})");
            GUILayout.Label($"當前位置: ({currentPosition.x:F2}, {currentPosition.y:F2}, {currentPosition.z:F2})");
            GUILayout.Label($"移動模式: {(useDeltaMovement ? "相對位移" : "絕對位置")}");
            if (GUILayout.Button("重置位置"))
            {
                ResetPosition();
            }
            GUILayout.EndArea();
        }
    }
}

