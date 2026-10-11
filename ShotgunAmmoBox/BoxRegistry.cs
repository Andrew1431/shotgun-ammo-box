using System.Collections.Generic;
using System.Linq;
using LethalLib.Modules;
using Unity.Netcode;
using UnityEngine;

namespace ShotgunAmmoBox;

internal static class BoxRegistry
{
    public static int ShellAmmoType = 1410; // ShotgunItem.gunCompatibleAmmoID's default; replaced by the real shell's
    private const int PropsLayer = 6;
    private const int ScanNodeLayer = 22;

    public static Item BoxItem;
    public static GameObject Prefab;
    public static Item ShellItem;
    private static bool shellsRegistered;

    public static void Register()
    {
        Prefab = LethalLib.Modules.NetworkPrefabs.CreateNetworkPrefab("ShotgunAmmoBox");
        // Like vanilla items: every client reparents props to the ship/props container itself, which NGO would reject.
        Prefab.GetComponent<NetworkObject>().AutoObjectParentSync = false;
        Prefab.tag = "PhysicsProp";
        Prefab.layer = PropsLayer;

        var size = new Vector3(0.304f, 0.224f, 0.142f); // art/ammo_box.blend, handle and latch included
        var model = BoxAssets.BoxModel != null ? Object.Instantiate(BoxAssets.BoxModel, Prefab.transform) : Placeholder(size);
        model.name = "Model";
        model.transform.SetParent(Prefab.transform, false);
        foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = PropsLayer;

        var col = Prefab.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, size.y / 2f, 0f);
        col.size = size;

        var audio = Prefab.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.spatialBlend = 1f;
        audio.rolloffMode = AudioRolloffMode.Linear;
        audio.minDistance = 1f;
        audio.maxDistance = 20f;

        var scanGO = new GameObject("ScanNode") { layer = ScanNodeLayer };
        scanGO.transform.SetParent(Prefab.transform, false);
        scanGO.transform.localPosition = new Vector3(0f, size.y / 2f, 0f);
        var scanCol = scanGO.AddComponent<BoxCollider>();
        scanCol.isTrigger = true;
        scanCol.size = Vector3.one * 0.4f;
        var scan = scanGO.AddComponent<ScanNodeProperties>();
        scan.maxRange = 13;
        scan.minRange = 1;
        scan.requiresLineOfSight = true;
        scan.headerText = "Ammo box";
        scan.subText = "Shells: 0";
        scan.nodeType = 0;

        var item = ScriptableObject.CreateInstance<Item>();
        item.name = "ShotgunAmmoBoxItem";
        item.itemName = "Ammo box";
        item.spawnPrefab = Prefab;
        item.isScrap = false;
        item.minValue = item.maxValue = 0;
        item.creditsWorth = Plugin.Cfg.BoxPrice.Value;
        item.weight = 1.05f; // ~5 lb empty
        item.twoHanded = false;
        item.twoHandedAnimation = false;
        item.canBeGrabbedBeforeGameStart = true;
        item.itemSpawnsOnGround = true;
        item.requiresBattery = false;
        item.saveItemVariable = true;
        item.syncUseFunction = false; // AmmoBoxItem syncs through its own RPCs
        item.syncInteractLRFunction = false;
        item.isConductiveMetal = true;
        item.canBeInspected = false;
        item.restingRotation = Vector3.zero;
        item.verticalOffset = 0.05f;
        item.positionOffset = new Vector3(-0.04f, 0.06f, -0.12f);
        item.rotationOffset = new Vector3(0f, 90f, 0f);
        item.toolTips = AmmoBoxItem.Tips(0, Plugin.Cfg.Capacity.Value);
        item.meshVariants = new Mesh[0];
        item.materialVariants = new Material[0];
        item.clinkAudios = new AudioClip[0];
        BoxItem = item;

        var box = Prefab.AddComponent<AmmoBoxItem>();
        box.itemProperties = item;
        box.grabbable = true;
        box.grabbableToEnemies = true;

        var info = ScriptableObject.CreateInstance<TerminalNode>();
        info.name = "AmmoBoxInfoNode";
        info.clearPreviousText = true;
        info.maxCharactersToType = 25;
        info.displayText = $"A steel ammo can that holds up to {Plugin.Cfg.Capacity.Value} shotgun shells.\n\n" +
                           "Hold it and press [LMB] while looking at a shell to pack it, or with no shell in sight to pack every shell you're carrying. " +
                           "[Q] takes one out. A shotgun reloads straight from a box in your inventory.\n\n";
        Items.RegisterShopItem(item, null, null, info, Plugin.Cfg.BoxPrice.Value);
    }

    private static GameObject Placeholder(Vector3 size)
    {
        var root = new GameObject();
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(cube.GetComponent<Collider>());
        cube.name = "Placeholder";
        cube.transform.SetParent(root.transform, false);
        cube.transform.localPosition = new Vector3(0f, size.y / 2f, 0f);
        cube.transform.localScale = size;
        return root;
    }

    // The vanilla shell only exists once the game has loaded its prefabs, and LethalLib adds shop items to the terminal
    // in Terminal.Awake (in the ship scene), so register it from the main menu, or failing that StartOfRound.Awake.
    public static void RegisterShellShop(IEnumerable<GameObject> prefabs, string where)
    {
        if (shellsRegistered) return;
        var shell = FindShell(prefabs, where);
        if (shell == null) return;
        shellsRegistered = true;
        ShellItem = shell.itemProperties;
        ShellAmmoType = shell.ammoType;
        // LethalLib makes the terminal keyword from the item name; "Ammo" would collide with "Ammo box".
        ShellItem.itemName = "Shotgun shell";
        var info = ScriptableObject.CreateInstance<TerminalNode>();
        info.name = "ShotgunShellInfoNode";
        info.clearPreviousText = true;
        info.maxCharactersToType = 25;
        info.displayText = "A single 12-gauge shell for the shotgun.\n\n";
        Items.RegisterShopItem(ShellItem, null, null, info, Plugin.Cfg.ShellPrice.Value);
        Plugin.Log.LogInfo($"Shotgun shells in the store for {Plugin.Cfg.ShellPrice.Value} credits (found at {where}, ammo type {ShellAmmoType})");
    }

    // The terminal splits on spaces and fuzzy-matches each word to the first keyword sharing 3+ letters, so "buy shotgun shell"
    // reads as "buy shotgun" and lands on "shovel". Exact keywords win over fuzzy ones, so give the shell order a few.
    private static readonly string[] ShellAliases = { "shotgun", "shell", "shells" };

    public static void AddShellAliases(Terminal terminal)
    {
        if (ShellItem == null) return;
        var keywords = terminal.terminalNodes.allKeywords.ToList();
        var buy = keywords.FirstOrDefault(k => k.word == "buy");
        var info = keywords.FirstOrDefault(k => k.word == "info");
        string word = ShellItem.itemName.ToLowerInvariant().Replace(" ", "-");
        var buyNoun = buy?.compatibleNouns.FirstOrDefault(n => n.noun != null && n.noun.word == word);
        if (buyNoun == null)
        {
            Plugin.Log.LogWarning($"No '{word}' buy keyword in the terminal; shell aliases skipped.");
            return;
        }
        var infoNoun = info?.compatibleNouns.FirstOrDefault(n => n.noun != null && n.noun.word == word);
        foreach (string alias in ShellAliases)
        {
            if (keywords.Any(k => k.word == alias)) continue;
            var kw = LethalLib.Modules.TerminalUtils.CreateTerminalKeyword(alias, defaultVerb: buy);
            keywords.Add(kw);
            buy.compatibleNouns = buy.compatibleNouns.Append(new CompatibleNoun(kw, buyNoun.result)).ToArray();
            if (infoNoun != null) info.compatibleNouns = info.compatibleNouns.Append(new CompatibleNoun(kw, infoNoun.result)).ToArray();
        }
        terminal.terminalNodes.allKeywords = keywords.ToArray();
    }

    public static IEnumerable<GameObject> MenuPrefabs(NetworkManager nm)
    {
        var prefabs = new List<GameObject>();
        if (nm != null)
        {
            prefabs.AddRange(nm.NetworkConfig.Prefabs.Prefabs.Select(p => p.Prefab));
            foreach (var list in nm.NetworkConfig.Prefabs.NetworkPrefabsLists)
                if (list != null) prefabs.AddRange(list.PrefabList.Select(p => p.Prefab));
        }
        prefabs.AddRange(Resources.FindObjectsOfTypeAll<GunAmmo>().Select(a => a.gameObject));
        prefabs.AddRange(Resources.FindObjectsOfTypeAll<ShotgunItem>().Select(g => g.gameObject));
        return prefabs;
    }

    // Prefer the ammo the shotgun takes; vanilla only has the one kind of GunAmmo anyway.
    private static GunAmmo FindShell(IEnumerable<GameObject> prefabs, string where)
    {
        var list = prefabs.Where(p => p != null).Distinct().ToList();
        var ammo = list.Select(p => p.GetComponentInChildren<GunAmmo>(true)).Where(a => a != null && a.itemProperties != null).Distinct().ToList();
        var gun = list.Select(p => p.GetComponentInChildren<ShotgunItem>(true)).FirstOrDefault(g => g != null);
        Plugin.Log.LogInfo($"[{where}] {list.Count} prefabs, ammo: {string.Join(", ", ammo.Select(a => $"{a.name}/'{a.itemProperties.itemName}' type {a.ammoType}"))}; " +
                           $"shotgun takes {(gun != null ? gun.gunCompatibleAmmoID.ToString() : "?")}");
        var shell = (gun != null ? ammo.FirstOrDefault(a => a.ammoType == gun.gunCompatibleAmmoID) : null) ?? ammo.FirstOrDefault();
        if (shell == null) Plugin.Log.LogWarning($"[{where}] no shotgun shell found yet");
        return shell;
    }

    // Sounds, hold animation and icon borrowed from the shell; materials swapped for ones that render in HDRP.
    public static void FixupFromGame(StartOfRound sor)
    {
        RegisterShellShop(sor.allItemsList.itemsList.Where(i => i != null).Select(i => i.spawnPrefab), "StartOfRound.Awake");
        UseLitMaterials();
        var shell = ShellItem;
        if (shell == null)
        {
            Plugin.Log.LogWarning("No vanilla shotgun shell found to borrow sounds and animations from.");
            return;
        }
        BoxItem.grabAnim = shell.grabAnim;
        BoxItem.grabAnimationTime = shell.grabAnimationTime;
        BoxItem.grabSFX = shell.grabSFX;
        BoxItem.dropSFX = shell.dropSFX;
        BoxItem.pocketSFX = shell.pocketSFX;
        BoxItem.itemIcon ??= shell.itemIcon;

        var shellAudio = shell.spawnPrefab.GetComponentInChildren<AudioSource>();
        if (shellAudio != null) Prefab.GetComponent<AudioSource>().outputAudioMixerGroup = shellAudio.outputAudioMixerGroup;
    }

    // Saves store items by their index in allItemsList, and LethalLib puts shop items ahead of plain ones (like CozyCats'
    // cats). Keep the box last so installing this mod doesn't shift what an existing save loads as.
    public static void KeepBoxLast(StartOfRound sor)
    {
        var items = sor.allItemsList.itemsList;
        if (!items.Remove(BoxItem)) return;
        items.Add(BoxItem);
    }

    // Bundle (and primitive) materials target the built-in pipeline; swap in HDRP/Lit copies, keeping each colour.
    private static void UseLitMaterials()
    {
        var lit = Shader.Find("HDRP/Lit");
        if (lit == null || litDone) return;
        litDone = true;
        foreach (var r in Prefab.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var src = mats[i];
                bool placeholder = src == null || r.name == "Placeholder";
                bool metal = !placeholder && src.name.Contains("Metal");
                var m = new Material(lit) { name = (placeholder ? "AmmoBoxPlaceholder" : src.name) + " (Lit)" };
                m.SetColor("_BaseColor", placeholder ? new Color(0.13f, 0.15f, 0.07f) : src.HasProperty("_Color") ? src.color : Color.grey);
                m.SetFloat("_Smoothness", metal ? 0.5f : 0.3f);
                if (metal) m.SetFloat("_Metallic", 0.8f);
                mats[i] = m;
            }
            r.sharedMaterials = mats;
        }
    }

    private static bool litDone;
}
