using UnityEngine;

/// <summary>
/// 掛在任意一直啟用的物件上。
/// 每幀偵測物件 A 是否剛被啟用，若是則將 A 傳送到物件 B 位置並往上偏移。
/// </summary>
public class FollowOnEnable : MonoBehaviour
{
    [Header("物件設定")]
    [SerializeField] private GameObject objectA;        // 要被監控的物件 A
    [SerializeField] private Transform targetB;         // 參考位置物件 B
    [SerializeField] private float upwardOffset = 1f;  // 往上偏移距離（單位）

    private bool wasActiveLastFrame = false;

    private void Update()
    {
        if (objectA == null || targetB == null) return;

        bool isActiveNow = objectA.activeSelf;

        // 偵測到「從關閉 → 開啟」的瞬間
        if (isActiveNow && !wasActiveLastFrame)
        {
            objectA.transform.position = targetB.position + Vector3.up * upwardOffset;
        }

        wasActiveLastFrame = isActiveNow;
    }
}
