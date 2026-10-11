#if DEBUG
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ShotgunAmmoBox;

// Debug builds only. Lives on its own GameObject because LC destroys the BepInEx manager object.
internal class DebugKeys : MonoBehaviour
{
    private bool godMode = true;
    private Vector3 defaultPos, defaultRot;

    public static void Install()
    {
        var go = new GameObject("ShotgunAmmoBox.DebugKeys") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        go.AddComponent<DebugKeys>();
        Plugin.Log.LogWarning("Debug keys (spawns are host only): F3 +1000 credits, F4 bring everyone to you, F5 toggle god mode + infinite stamina (on by default), " +
                              "F6 spawn shotgun, F7 spawn 5 shells, F8 spawn ammo box. While holding the box: arrows move left/right/up/down, " +
                              "PgUp/PgDn away/closer, I/K J/L U/O rotate, hold Alt for fine steps, F9 print+copy hold pose, F10 reset hold pose.");
    }

    private void Start()
    {
        defaultPos = BoxRegistry.BoxItem.positionOffset;
        defaultRot = BoxRegistry.BoxItem.rotationOffset;
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        var player = GameNetworkManager.Instance?.localPlayerController;

        if (kb.f5Key.wasPressedThisFrame)
        {
            godMode = !godMode;
            HUDManager.Instance?.DisplayTip("God mode", godMode ? "On" : "Off");
        }
        // The game's own switch: DamagePlayer and KillPlayer both check it before hurting the local player.
        if (StartOfRound.Instance != null) StartOfRound.Instance.allowLocalPlayerDeath = !godMode;
        if (godMode && player != null)
        {
            player.sprintMeter = 1f;
            player.isExhausted = false;
        }

        RegisterSummon();
        if (player == null || player.isTypingChat || player.inTerminalMenu) return;
        if (kb.f4Key.wasPressedThisFrame) RequestSummon();
        if (kb.f3Key.wasPressedThisFrame) AddCredits(1000);
        if (kb.f6Key.wasPressedThisFrame) Spawn(i => i.spawnPrefab.GetComponentInChildren<ShotgunItem>(true) != null, 1, "shotgun");
        if (kb.f7Key.wasPressedThisFrame) Spawn(i => i == BoxRegistry.ShellItem, 5, "shells");
        if (kb.f8Key.wasPressedThisFrame) Spawn(i => i == BoxRegistry.BoxItem, 1, "ammo box");
        if (player.currentlyHeldObjectServer is AmmoBoxItem) HoldPoseKeys(kb);
    }

    // ---- Spawning (host only) ----

    private static void Spawn(System.Func<Item, bool> match, int count, string label)
    {
        var player = GameNetworkManager.Instance?.localPlayerController;
        var sor = StartOfRound.Instance;
        if (player == null || sor == null) return;
        if (!NetworkManager.Singleton.IsServer)
        {
            HUDManager.Instance.DisplayTip("Debug", "Only the host can spawn items.");
            return;
        }
        var item = sor.allItemsList.itemsList.FirstOrDefault(i => i != null && i.spawnPrefab != null && match(i));
        if (item == null)
        {
            HUDManager.Instance.DisplayTip("Debug", $"No {label} item found.");
            return;
        }

        var fwd = Vector3.ProjectOnPlane(player.gameplayCamera.transform.forward, Vector3.up).normalized;
        var right = Vector3.Cross(Vector3.up, fwd);
        bool inShip = player.isInHangarShipRoom || player.isInElevator;
        var parent = inShip ? sor.elevatorTransform : RoundManager.Instance?.spawnedScrapContainer;
        var refs = new NetworkObjectReference[count];
        var values = new int[count];
        for (int n = 0; n < count; n++)
        {
            var pos = player.transform.position + fwd * 1.5f + right * ((n - (count - 1) / 2f) * 0.3f) + Vector3.up * 0.5f;
            var go = Instantiate(item.spawnPrefab, pos, Quaternion.LookRotation(-fwd), parent);
            var obj = go.GetComponent<GrabbableObject>();
            obj.fallTime = 0f;
            obj.isInElevator = inShip;
            obj.isInShipRoom = player.isInHangarShipRoom;
            obj.isInFactory = player.isInsideFactory;
            var netObj = go.GetComponent<NetworkObject>();
            netObj.Spawn();
            refs[n] = netObj;
            values[n] = item.isScrap ? Random.Range(item.minValue, item.maxValue) : 0;
        }
        if (item.isScrap && RoundManager.Instance != null) RoundManager.Instance.SyncScrapValuesClientRpc(refs, values);
        Plugin.Log.LogInfo($"Debug: spawned {count} {label}");
    }

    private static void AddCredits(int amount)
    {
        var terminal = FindObjectOfType<Terminal>();
        if (terminal == null || !NetworkManager.Singleton.IsServer)
        {
            HUDManager.Instance?.DisplayTip("Debug", "Only the host can add credits.");
            return;
        }
        terminal.groupCredits += amount;
        terminal.SyncGroupCreditsClientRpc(terminal.groupCredits, terminal.numberOfItemsInDropship);
        HUDManager.Instance?.DisplayTip("Debug", $"Credits: {terminal.groupCredits}");
    }

    // ---- Hold pose tuning for the box (Item.positionOffset / rotationOffset, live) ----

    private void HoldPoseKeys(Keyboard kb)
    {
        var item = BoxRegistry.BoxItem;
        bool fine = kb.altKey.isPressed;
        float step = fine ? 0.0025f : 0.01f, deg = fine ? 1f : 5f;
        Vector3 move = Vector3.zero, rot = Vector3.zero;
        if (kb.rightArrowKey.wasPressedThisFrame) move.x += step;
        if (kb.leftArrowKey.wasPressedThisFrame) move.x -= step;
        if (kb.upArrowKey.wasPressedThisFrame) move.y += step;
        if (kb.downArrowKey.wasPressedThisFrame) move.y -= step;
        if (kb.pageUpKey.wasPressedThisFrame) move.z += step;
        if (kb.pageDownKey.wasPressedThisFrame) move.z -= step;
        if (kb.iKey.wasPressedThisFrame) rot.x -= deg;
        if (kb.kKey.wasPressedThisFrame) rot.x += deg;
        if (kb.jKey.wasPressedThisFrame) rot.y -= deg;
        if (kb.lKey.wasPressedThisFrame) rot.y += deg;
        if (kb.uKey.wasPressedThisFrame) rot.z += deg;
        if (kb.oKey.wasPressedThisFrame) rot.z -= deg;
        bool changed = move != Vector3.zero || rot != Vector3.zero;
        item.positionOffset += move;
        item.rotationOffset += rot;
        if (kb.f10Key.wasPressedThisFrame)
        {
            item.positionOffset = defaultPos;
            item.rotationOffset = defaultRot;
            changed = true;
        }
        if (changed) ShowPose(false);
        if (kb.f9Key.wasPressedThisFrame) ShowPose(true);
    }

    private static void ShowPose(bool copy)
    {
        Vector3 p = BoxRegistry.BoxItem.positionOffset, r = BoxRegistry.BoxItem.rotationOffset;
        string code = $"item.positionOffset = new Vector3({p.x:0.###}f, {p.y:0.###}f, {p.z:0.###}f); item.rotationOffset = new Vector3({r.x:0.#}f, {r.y:0.#}f, {r.z:0.#}f);";
        Plugin.Log.LogInfo("Hold pose: " + code);
        if (copy) GUIUtility.systemCopyBuffer = code;
        HUDManager.Instance?.DisplayTip(copy ? "Hold pose copied" : "Box hold", $"pos ({p.x:0.000},{p.y:0.000},{p.z:0.000})  rot ({r.x:0},{r.y:0},{r.z:0})");
    }

    // ---- F4: bring every player to whoever pressed it (client -> host -> everyone) ----

    private const string SummonRequestMsg = "ShotgunAmmoBox.SummonRequest", SummonMsg = "ShotgunAmmoBox.Summon";
    private CustomMessagingManager summonRegisteredOn;

    // The messaging manager is new for every hosted/joined session, so register again whenever it changes.
    private void RegisterSummon()
    {
        var cmm = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening ? NetworkManager.Singleton.CustomMessagingManager : null;
        if (cmm == null || cmm == summonRegisteredOn) return;
        summonRegisteredOn = cmm;
        cmm.RegisterNamedMessageHandler(SummonRequestMsg, (sender, reader) =>
        {
            if (!NetworkManager.Singleton.IsServer) return;
            reader.ReadValueSafe(out Vector3 pos);
            reader.ReadValueSafe(out bool inFactory);
            BroadcastSummon(sender, pos, inFactory);
        });
        cmm.RegisterNamedMessageHandler(SummonMsg, (_, reader) =>
        {
            reader.ReadValueSafe(out ulong summoner);
            reader.ReadValueSafe(out Vector3 pos);
            reader.ReadValueSafe(out bool inFactory);
            OnSummoned(summoner, pos, inFactory);
        });
    }

    private void RequestSummon()
    {
        var player = GameNetworkManager.Instance?.localPlayerController;
        var nm = NetworkManager.Singleton;
        if (player == null || nm == null || !nm.IsListening) return;
        if (nm.IsServer)
        {
            BroadcastSummon(nm.LocalClientId, player.transform.position, player.isInsideFactory);
            return;
        }
        using var w = new FastBufferWriter(32, Allocator.Temp);
        w.WriteValueSafe(player.transform.position);
        w.WriteValueSafe(player.isInsideFactory);
        nm.CustomMessagingManager.SendNamedMessage(SummonRequestMsg, NetworkManager.ServerClientId, w);
    }

    private void BroadcastSummon(ulong summoner, Vector3 pos, bool inFactory)
    {
        var nm = NetworkManager.Singleton;
        foreach (ulong id in nm.ConnectedClientsIds)
        {
            if (id == nm.LocalClientId) continue;
            using var w = new FastBufferWriter(48, Allocator.Temp);
            w.WriteValueSafe(summoner);
            w.WriteValueSafe(pos);
            w.WriteValueSafe(inFactory);
            nm.CustomMessagingManager.SendNamedMessage(SummonMsg, id, w);
        }
        OnSummoned(summoner, pos, inFactory);
    }

    private void OnSummoned(ulong summoner, Vector3 pos, bool inFactory)
    {
        if (summoner == NetworkManager.Singleton.LocalClientId)
        {
            HUDManager.Instance?.DisplayTip("Debug", "Bringing everyone to you");
            return;
        }
        StartCoroutine(TeleportTo(pos, inFactory));
    }

    private System.Collections.IEnumerator TeleportTo(Vector3 pos, bool inFactory)
    {
        var player = GameNetworkManager.Instance?.localPlayerController;
        if (player == null || player.isPlayerDead) yield break;
        // Use the real main entrance to swap inside/outside so lighting, audio and culling follow.
        if (inFactory != player.isInsideFactory)
        {
            foreach (var door in FindObjectsOfType<EntranceTeleport>())
            {
                if (door.isEntranceToBuilding != player.isInsideFactory && door.entranceId == 0)
                {
                    door.TeleportPlayer();
                    break;
                }
            }
            yield return null;
            yield return null;
        }
        Vector2 r = Random.insideUnitCircle.normalized * 1.2f;
        player.TeleportPlayer(pos + new Vector3(r.x, 0.2f, r.y));
    }
}
#endif
