#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.PackageManager;

public class PackageInstaller
{
    [InitializeOnLoadMethod]
    public static void Init()
    {
        Client.AddAndRemove(new[] {
            "com.unity.toonshader",
            "https://github.com/unity3d-jp/UnityChanSpringBone.git"
        });
    }
}
#endif