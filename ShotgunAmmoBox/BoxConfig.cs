using BepInEx.Configuration;

namespace ShotgunAmmoBox;

internal class BoxConfig
{
    public readonly ConfigEntry<int> ShellPrice;
    public readonly ConfigEntry<int> BoxPrice;
    public readonly ConfigEntry<int> Capacity;

    public BoxConfig(ConfigFile cfg)
    {
        ShellPrice = cfg.Bind("Store", "ShellPrice", 100,
            new ConfigDescription("Credits per shotgun shell in the terminal store ('buy ammo'). Requires restart.",
                new AcceptableValueRange<int>(0, 10000)));
        BoxPrice = cfg.Bind("Store", "BoxPrice", 400,
            new ConfigDescription("Credits for an ammo box in the terminal store ('buy ammo box'). Requires restart.",
                new AcceptableValueRange<int>(0, 10000)));
        Capacity = cfg.Bind("Box", "Capacity", 20,
            new ConfigDescription("Shells one box holds. The host's value is used in multiplayer.",
                new AcceptableValueRange<int>(1, 99)));
    }
}
