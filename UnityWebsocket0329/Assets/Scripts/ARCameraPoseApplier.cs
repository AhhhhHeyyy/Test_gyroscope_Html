using UnityEngine;

/// <summary>
/// 從 GyroscopeReceiver 取得「AR 相機相對 Marker」的位姿，
/// 並將其套用到指定的目標物件（例如一個 cube）。
/// </summary>
public class ARCameraPoseApplier : MonoBehaviour
{
    [Header("資料來源")]
    [SerializeField] private GyroscopeReceiver receiver;

    [Header("要跟著動的目標物件")]
    [SerializeField] private Transform target;

    [Header("套用選項")]
    [SerializeField] private bool applyPosition = true;
    [SerializeField] private bool applyRotation = true;

    [Header("位置與角度調整")]
    [SerializeField] private Vector3 positionScale = Vector3.one;
    [SerializeField] private Vector3 positionOffset = Vector3.zero;
    [SerializeField] private Vector3 rotationOffset = Vector3.zero;

    [Header("Marker 不可見時的行為")]
    [SerializeField] private bool hideWhenNotVisible = false;

    private void Reset()
    {
        // 預設將 target 指向自身
        if (target == null)
        {
            target = transform;
        }

        if (receiver == null)
        {
            receiver = FindObjectOfType<GyroscopeReceiver>();
        }
    }

    private void Awake()
    {
        if (target == null)
        {
            target = transform;
        }
    }

    private void Update()
    {
        if (receiver == null || target == null)
        {
            return;
        }

        // 如果 Marker 不可見
        if (!receiver.ARMarkerVisible)
        {
            if (hideWhenNotVisible && target.gameObject.activeSelf)
            {
                target.gameObject.SetActive(false);
            }
            // 若選擇不隱藏，就維持在最後一次有效位姿
            return;
        }

        // Marker 可見時，確保目標啟用
        if (hideWhenNotVisible && !target.gameObject.activeSelf)
        {
            target.gameObject.SetActive(true);
        }

        // 讀取 AR 相機相對 Marker 的位置
        Vector3 srcPos = receiver.ARCameraPosition;

        // 位置：先做縮放再加偏移（目前假設 1:1，可在 Inspector 調整）
        if (applyPosition)
        {
            // 需求：
            // - 左右相反：X 軸取負
            // - 上下乘以 5 倍：Y 軸 * 5
            Vector3 mappedPos = new Vector3(
                srcPos.x * 5f,
                -srcPos.y * 5f,
                srcPos.z
            );

            // 再套用自訂縮放與偏移
            Vector3 scaledPos = new Vector3(
                mappedPos.x * positionScale.x,
                mappedPos.y * positionScale.y,
                mappedPos.z * positionScale.z
            );

            target.position = scaledPos + positionOffset;
        }

        // 角度：沿用 GyroToRotation（test.cs）的計算方式
        if (applyRotation)
        {
            Quaternion baseRot = Quaternion.Euler(
                -receiver.m_beta,   // 前後傾斜 → X 軸
                -receiver.m_gamma,  // 左右傾斜 → Y 軸
                -receiver.m_alpha  // 羅盤旋轉 → Z 軸
            );

            // rotationOffset 以度數形式套用在外層
            Quaternion offsetRot = Quaternion.Euler(rotationOffset);
            target.rotation = baseRot * offsetRot;
        }
    }
}

