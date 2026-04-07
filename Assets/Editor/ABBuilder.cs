using System.IO;
using UnityEditor;
using UnityEngine;

public class ABBuilder {
    // AB输出目录（各平台）
    private static readonly string baseOutputPath = Application.dataPath + "/StreamingAssets";
    
    // 资源目录配置
    private static readonly string[] assetFolders = new[] {
        "Assets/LuaScripts",
        "Assets/Resources/Prefab",
        "Assets/Resources/UI",
        "Assets/Resources/Audio",
        "Assets/Resources/Fonts",
    };

    [MenuItem("Build/Build All AB")]
    public static void BuildAllAB() {
        BuildABForTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64, "Win");
        BuildABForTarget(BuildTargetGroup.Android, BuildTarget.Android, "Android");
        BuildABForTarget(BuildTargetGroup.iOS, BuildTarget.iOS, "IOS");
    }

    [MenuItem("Build/Build Windows AB")]
    public static void BuildWindowsAB() {
        BuildABForTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64, "Win");
    }

    [MenuItem("Build/Build Android AB")]
    public static void BuildAndroidAB() {
        BuildABForTarget(BuildTargetGroup.Android, BuildTarget.Android, "Android");
    }

    private static void BuildABForTarget(BuildTargetGroup group, BuildTarget target, string platform) {
        string outputPath = Path.Combine(baseOutputPath, platform);
        
        // 1. 设置所有资源的AB标签（按目录结构，小写路径）
        foreach (string folder in assetFolders) {
            SetAssetBundleLabels(folder);
        }

        // 2. 确保输出目录存在
        if (!Directory.Exists(outputPath)) {
            Directory.CreateDirectory(outputPath);
        }

        // 3. 构建AB
        BuildPipeline.BuildAssetBundles(
            outputPath,
            BuildAssetBundleOptions.None,
            target
        );

        Debug.Log($"[ABBuilder] {platform} AB 构建完成，输出到: {outputPath}");
    }

    private static void SetAssetBundleLabels(string folderPath) {
        if (!Directory.Exists(folderPath)) return;

        DirectoryInfo dir = new DirectoryInfo(folderPath);

        foreach (FileInfo file in dir.GetFiles("*", SearchOption.AllDirectories)) {
            if (file.Extension == ".meta") continue;

            // 计算相对路径（去掉 Assets/ 前缀），作为AB包名
            string relativePath = file.FullName.Replace(Application.dataPath + "/", "");
            string abName = relativePath.Substring(0, relativePath.LastIndexOf('.')).ToLower();

            // 设置AssetBundle标签
            AssetImporter importer = AssetImporter.GetAtPath(relativePath);
            if (importer != null) {
                importer.assetBundleName = abName;
            }
        }
    }
}
