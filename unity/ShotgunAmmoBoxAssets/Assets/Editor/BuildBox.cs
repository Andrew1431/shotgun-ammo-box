using System.IO;
using UnityEditor;
using UnityEngine;

// Run headless: Unity.exe -batchmode -quit -projectPath <this project> -executeMethod BuildBox.Build
// Just the mesh and its flat-colour materials; the plugin adds every component and swaps in HDRP/Lit at runtime.
public static class BuildBox
{
    const string FbxPath = "Assets/AmmoBox/AmmoBox.fbx";
    const string PrefabPath = "Assets/AmmoBox/AmmoBox.prefab";
    const string BundleName = "shotgunammobox";

    [MenuItem("ShotgunAmmoBox/Build Bundle")]
    public static void Build()
    {
        AssetDatabase.Refresh();
        var imp = (ModelImporter)AssetImporter.GetAtPath(FbxPath);
        imp.bakeAxisConversion = true;
        imp.globalScale = 1f;
        imp.useFileScale = true;
        imp.importAnimation = false;
        imp.animationType = ModelImporterAnimationType.None;
        imp.importCameras = false;
        imp.importLights = false;
        imp.isReadable = false;
        imp.SaveAndReimport();

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        go.name = "AmmoBox";
        PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
        Object.DestroyImmediate(go);
        AssetImporter.GetAtPath(PrefabPath).assetBundleName = BundleName;

        Directory.CreateDirectory("AssetBundles");
        BuildPipeline.BuildAssetBundles("AssetBundles", BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
        var r = model.GetComponentInChildren<MeshRenderer>();
        Debug.Log($"[BuildBox] bounds {r.bounds} materials {r.sharedMaterials.Length}");
        Debug.Log("[BuildBox] done");
    }
}
