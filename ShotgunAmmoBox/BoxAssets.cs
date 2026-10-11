using System.IO;
using UnityEngine;

namespace ShotgunAmmoBox;

internal static class BoxAssets
{
    public static GameObject BoxModel;

    // The bundle is optional while the model is in progress; BoxRegistry builds a placeholder without it.
    public static void Load(string path)
    {
        if (!File.Exists(path))
        {
            Plugin.Log.LogWarning($"No asset bundle at {path}; using a placeholder box.");
            return;
        }
        var bundle = AssetBundle.LoadFromFile(path);
        BoxModel = bundle != null ? bundle.LoadAsset<GameObject>("AmmoBox") : null;
        Plugin.Log.LogInfo($"Loaded ammo box model: {BoxModel != null}");
    }
}
