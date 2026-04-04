using UnityEngine;

public class GyroToRotation : MonoBehaviour
{
    [SerializeField] GyroscopeReceiver receiver;
    
    [Header("陀螺儀數據")]
    [SerializeField] private float alpha = 0f;
    [SerializeField] private float beta = 0f;
    [SerializeField] private float gamma = 0f;
    
    void Update()
    {
        if (receiver == null) return;

        // 與 GyroscopeReceiver 中的數值保持同步
        alpha = receiver.m_alpha;
        beta = receiver.m_beta;
        gamma = receiver.m_gamma;

        // 使用 JS 端預先計算的四元數
        float qx = receiver.m_qx, qy = receiver.m_qy, qz = receiver.m_qz, qw = receiver.m_qw;

        // 確認四元數有效（長度 ≈ 1），無效則不更新
        float mag2 = qx*qx + qy*qy + qz*qz + qw*qw;
        if (mag2 < 0.5f) return;

        // Browser 右手系（X=East, Y=North, Z=Up）→ Unity 左手系（X=Right, Y=Up, Z=Forward）
        // axis 映射：(bx, by, bz) → (bx, bz, by)，det=-1（換手性）→ 角度反向 → 全部 xyz 取負
        transform.localRotation = new Quaternion(qx, -qz, qy, qw);
    }
}