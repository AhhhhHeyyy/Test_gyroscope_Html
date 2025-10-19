using System;
using UnityEngine;

[System.Serializable]
public class ShakeData
{
    public int count;
    public float intensity;
    public string shakeType;
    public Vector3 acceleration;
    public long timestamp;
    
    public ShakeData()
    {
        count = 0;
        intensity = 0f;
        shakeType = "normal";
        acceleration = Vector3.zero;
        timestamp = 0;
    }
    
    public ShakeData(int count, float intensity, string shakeType, Vector3 acceleration, long timestamp)
    {
        this.count = count;
        this.intensity = intensity;
        this.shakeType = shakeType;
        this.acceleration = acceleration;
        this.timestamp = timestamp;
    }
    
    public override string ToString()
    {
        return $"ShakeData: Count={count}, Intensity={intensity:F2}, Type={shakeType}, Time={timestamp}";
    }
}
