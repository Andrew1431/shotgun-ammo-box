using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;

namespace ShotgunAmmoBox;

// Holds shotgun shells as a count plus their combined scrap value, so a shell taken back out is worth what went in.
public class AmmoBoxItem : GrabbableObject
{
    // PlayerControllerB's interactableObjectsMask (private): the layers the vanilla grab ray looks at.
    private const int GrabMask = 1073742656;
    private const int SaveCountBits = 7; // Capacity tops out at 99

    private readonly NetworkVariable<int> netShells = new NetworkVariable<int>(0);
    private readonly NetworkVariable<int> netValue = new NetworkVariable<int>(0);
    private readonly NetworkVariable<int> netCapacity = new NetworkVariable<int>(20);

    public int Shells => netShells.Value;
    public int Capacity => netCapacity.Value;

    public static string[] Tips(int shells, int capacity) =>
        new[] { $"Shells: {shells}/{capacity}", "Pack shells : [LMB]", "Take shell : [Q]" };

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer) netCapacity.Value = Plugin.Cfg.Capacity.Value;
        netShells.OnValueChanged += (_, _) => Refresh();
        netCapacity.OnValueChanged += (_, _) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        var scan = GetComponentInChildren<ScanNodeProperties>();
        if (scan != null) scan.subText = $"Shells: {Shells}/{Capacity}";
        if (IsOwner && isHeld && !isPocketed && playerHeldBy == GameNetworkManager.Instance.localPlayerController)
            SetControlTipsForItem();
    }

    public override void SetControlTipsForItem() =>
        HUDManager.Instance.ChangeControlTipMultiple(Tips(Shells, Capacity), holdingItem: true, itemProperties);

    public override void EquipItem()
    {
        base.EquipItem();
        if (playerHeldBy != null) playerHeldBy.equippedUsableItemQE = true;
    }

    public override void PocketItem()
    {
        if (playerHeldBy != null) playerHeldBy.equippedUsableItemQE = false;
        base.PocketItem();
    }

    public override void DiscardItem()
    {
        if (playerHeldBy != null) playerHeldBy.equippedUsableItemQE = false;
        base.DiscardItem();
    }

    // LMB: pack the shell under the crosshair, or every shell in your inventory if you aren't looking at one.
    public override void ItemActivate(bool used, bool buttonDown = true)
    {
        if (!buttonDown || playerHeldBy == null || !IsOwner) return;
        if (Shells >= Capacity)
        {
            HUDManager.Instance.DisplayTip("Ammo box", "It's full.");
            return;
        }

        var cam = playerHeldBy.gameplayCamera.transform;
        if (Physics.Raycast(new Ray(cam.position, cam.forward), out var hit, playerHeldBy.grabDistance, GrabMask))
        {
            var shell = hit.collider.GetComponentInParent<GunAmmo>();
            if (IsShotgunShell(shell) && !shell.isHeld)
            {
                StoreShellServerRpc(shell.NetworkObject);
                return;
            }
        }
        PackInventory(playerHeldBy);
    }

    private void PackInventory(PlayerControllerB player)
    {
        int room = Capacity - Shells, packed = 0, value = 0;
        for (int i = 0; i < player.ItemSlots.Length && packed < room; i++)
        {
            if (!(player.ItemSlots[i] is GunAmmo shell) || !IsShotgunShell(shell)) continue;
            value += shell.scrapValue;
            packed++;
            player.DestroyItemInSlotAndSync(i);
        }
        if (packed == 0)
        {
            HUDManager.Instance.DisplayTip("Ammo box", "Look at a shotgun shell, or carry some, to pack them.");
            return;
        }
        AddShellsServerRpc(packed, value);
    }

    // Q takes a shell out and drops it in front of you; E does nothing.
    public override void ItemInteractLeftRight(bool right)
    {
        base.ItemInteractLeftRight(right);
        if (right || playerHeldBy == null || !IsOwner) return;
        if (Shells <= 0)
        {
            HUDManager.Instance.DisplayTip("Ammo box", "It's empty.");
            return;
        }
        TakeShellServerRpc((int)playerHeldBy.playerClientId);
    }

    // Called by the shotgun's reload (via Patches) on the reloading player's machine.
    public void ConsumeForReload() => ConsumeServerRpc();

    [ServerRpc(RequireOwnership = false)]
    private void StoreShellServerRpc(NetworkObjectReference shellRef)
    {
        if (!shellRef.TryGet(out var shellObj) || !(shellObj.GetComponent<GunAmmo>() is { } shell) || shell.isHeld || Shells >= Capacity) return;
        netShells.Value++;
        netValue.Value += shell.scrapValue;
        shellObj.Despawn();
        ClinkClientRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void AddShellsServerRpc(int count, int value)
    {
        // The shells are already gone from the sender's inventory, so overfilling only loses the excess.
        netShells.Value = Mathf.Min(Shells + count, Capacity);
        netValue.Value += value;
        ClinkClientRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void ConsumeServerRpc() => TakeOne();

    [ServerRpc(RequireOwnership = false)]
    private void TakeShellServerRpc(int playerId)
    {
        if (Shells <= 0 || BoxRegistry.ShellItem == null) return;
        int value = TakeOne();
        var player = StartOfRound.Instance.allPlayerScripts[playerId];
        var fwd = Vector3.ProjectOnPlane(player.gameplayCamera.transform.forward, Vector3.up).normalized;
        var pos = player.transform.position + fwd * 0.6f + Vector3.up * 0.8f;
        bool inShip = player.isInHangarShipRoom || player.isInElevator;
        var parent = inShip ? StartOfRound.Instance.elevatorTransform : RoundManager.Instance.spawnedScrapContainer;
        var go = Instantiate(BoxRegistry.ShellItem.spawnPrefab, pos, Quaternion.identity, parent);
        var shell = go.GetComponent<GrabbableObject>();
        shell.fallTime = 0f;
        shell.isInElevator = inShip;
        shell.isInShipRoom = player.isInHangarShipRoom;
        shell.isInFactory = player.isInsideFactory;
        var netObj = go.GetComponent<NetworkObject>();
        netObj.Spawn();
        RoundManager.Instance.SyncScrapValuesClientRpc(new NetworkObjectReference[] { netObj }, new[] { value });
        ClinkClientRpc();
    }

    // Server only. Each shell takes its share of the stored value, the last one whatever is left.
    private int TakeOne()
    {
        if (Shells <= 0) return 0;
        int value = netValue.Value / Shells;
        netShells.Value--;
        netValue.Value -= value;
        return value;
    }

    [ClientRpc]
    private void ClinkClientRpc()
    {
        var clip = BoxRegistry.ShellItem != null ? BoxRegistry.ShellItem.grabSFX : null;
        if (clip != null) GetComponent<AudioSource>().PlayOneShot(clip);
    }

    public override int GetItemDataToSave() => Shells | netValue.Value << SaveCountBits;

    public override void LoadItemSaveData(int saveData)
    {
        if (IsSpawned && !IsServer) return;
        netShells.Value = saveData & ((1 << SaveCountBits) - 1);
        netValue.Value = saveData >> SaveCountBits;
    }

    public static bool IsShotgunShell(GunAmmo ammo) => ammo != null && ammo.ammoType == BoxRegistry.ShellAmmoType;
}
