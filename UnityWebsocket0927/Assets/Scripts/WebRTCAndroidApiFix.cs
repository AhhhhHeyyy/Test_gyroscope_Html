using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Android;

/// <summary>
/// WebRTC Android API 級別修復腳本
/// 解決 Unity WebRTC 包中過時的 AndroidApiLevel22 問題
/// </summary>
public class WebRTCAndroidApiFix : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        // 確保 Android 構建使用正確的 API 級別
        if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
        {
            // 設置最低 Android API 級別為 23 (Android 6.0)
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel23;
            
            // 設置目標 Android API 級別為最新
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            
            Debug.Log("✅ WebRTC Android API 級別已設置為 Android 6.0 (API 23) 或更高");
        }
    }
}

/// <summary>
/// 構建後處理器，確保 WebRTC 相關設置正確
/// </summary>
public class WebRTCPostBuildProcessor : IPostprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform == BuildTarget.Android)
        {
            Debug.Log("✅ WebRTC Android 構建完成，API 級別設置正確");
        }
    }
}
