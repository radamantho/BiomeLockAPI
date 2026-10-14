# BiomeLockAPI

Small API DLL that lets other Valheim mods talk to [BiomeLock](https://github.com/radamantho/BiomeLock).

- Reference `BiomeLockAPI.dll` and merge it into your mod with ILRepack.
- BiomeLock is **not** required: without it, queries return neutral values and events never fire.
- BiomeLock is found at runtime, so updating BiomeLock does not require rebuilding your mod.

## Setup

1. Copy `BiomeLockAPI.dll` to your project (for example `Libs/`) and add it as a reference.
2. Merge it in your `ILRepack.targets`:
   ```xml
   <InputAssemblies Include="$(OutputPath)BiomeLockAPI.dll" />
   ```
3. Make BiomeLock load before your mod:
   ```csharp
   [BepInDependency("radamanto.BiomeLock", BepInDependency.DependencyFlags.SoftDependency)]
   ```

## Usage

```csharp
using BiomeLockAPI;

if (API.HasProgressionKey("defeated_bonemass"))
{
    // unlock something
}

if (API.IsBiomeLocked(Heightmap.Biome.Mountain))
{
    // ...
}

API.OnPrivateKeyAdded += key =>
{
    if (key == "bl_defeated_dragon") { /* ... */ }
};
```

All queries refer to the local player.

### Queries

| Member | Description |
|---|---|
| `IsLoaded` | BiomeLock is installed and loaded. |
| `IsReady` | The local player is in the world and its keys are available. |
| `HasPrivateKey(key)` | The local player has the private key. Accepts `defeated_eikthyr` or `bl_defeated_eikthyr`. |
| `HasProgressionKey(key)` | The local player has this progression, following BiomeLock's mode (private keys or world global keys). Without BiomeLock, checks the world global key. |
| `GetPrivateKeys()` | Private keys of the local player, sorted. |
| `IsBiomeLocked(biome)` | The biome is locked for the local player (config, admin bypass and keys considered). |
| `IsRestricted()` | The local player currently has the BiomeLock restriction effect. |
| `ToPrivateKey(key)` / `ToGlobalKey(key)` | Converts `defeated_eikthyr` ⇄ `bl_defeated_eikthyr`. |
| `PrivatePrefix` | `bl_` |

### Events

| Event | Raised when |
|---|---|
| `OnPrivateKeyAdded(string key)` | The local player gains a private key. |
| `OnPrivateKeyRemoved(string key)` | The local player loses a private key. |
| `OnPrivateKeysReset()` | All private keys of the local player are removed. |
| `OnPrivateKeysLoaded()` | The local player's keys are loaded after entering the world. |

Subscribing before BiomeLock is loaded is fine: the handlers are attached as soon as BiomeLock is found.

### Restriction bypass

Lifts the BiomeLock restriction at chosen world positions, for example inside a player territory. Requires BiomeLock 1.2.1 or newer; with older versions or without BiomeLock the calls do nothing.

```csharp
private static bool InsideMyArea(Vector3 position) => MyAreas.Contains(position);

API.AddRestrictionBypass(InsideMyArea);
API.RemoveRestrictionBypass(InsideMyArea);
```

| Member | Description |
|---|---|
| `AddRestrictionBypass(Func<Vector3, bool>)` | Registers a check. Return `true` to allow the local player at that position. |
| `RemoveRestrictionBypass(Func<Vector3, bool>)` | Removes a registered check. Pass the same delegate used to register. |

- The check runs during BiomeLock checks, many times per second, so it **must be fast** (no allocations, no physics queries).
- A check that throws an exception is removed and the error is logged.
- Registering before BiomeLock is loaded is fine: the check is added as soon as BiomeLock is found.

## Building

Open `BiomeLockAPI.sln` and build. The references are in `Libs/`. A prebuilt `BiomeLockAPI.dll` is included at the repository root.
