using UnityEngine;

public class GyroToRotation : MonoBehaviour
{
    private Quaternion pendingRotation = Quaternion.identity;
    private bool hasData = false;

    void OnEnable()
    {
        SensorEvents.OnGyroscopeDataReceived += HandleGyroscopeData;
    }

    void OnDisable()
    {
        SensorEvents.OnGyroscopeDataReceived -= HandleGyroscopeData;
    }

    private void HandleGyroscopeData(SensorEvents.GyroscopeData data)
    {
        float qx = data.qx, qy = data.qy, qz = data.qz, qw = data.qw;
        float mag2 = qx*qx + qy*qy + qz*qz + qw*qw;
        if (mag2 < 0.5f) return;
        // Browser right-hand (X=East, Y=North, Z=Up) → Unity left-hand (X=Right, Y=Up, Z=Forward)
        pendingRotation = new Quaternion(qx, -qz, qy, qw);
        hasData = true;
    }

    void Update()
    {
        if (hasData)
            transform.localRotation = pendingRotation;
    }
}
