using UnityEngine;

public class FlashlightController2 : MonoBehaviour
{
    public LightSpotTracker tracker;  // 光點追蹤器
    public Camera sceneCamera;        // 用來射線的相機
    public Transform flashSpot;       // 牆上的光點物件
    public float moveSmooth = 10f;    // 光點移動平滑度

    void Update()
    {
        Vector2 uv = tracker.spotUV;

        // UV → 螢幕座標
        Vector3 screenPos = new Vector3(
            uv.x * Screen.width,
            uv.y * Screen.height,
            0
        );

        // 射線 (像手電筒光線一樣)
        Ray ray = sceneCamera.ScreenPointToRay(screenPos);

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            // 命中牆 → 把光點移到牆上
            flashSpot.position = Vector3.Lerp(
                flashSpot.position,
                hit.point,
                Time.deltaTime * moveSmooth
            );
        }
    }
}
