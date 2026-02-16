# Future Improvement Suggestions
Here are a few suggestions for future improvements/features that may be useful for the mod.

## Complete Error Handling Coverage
There are still a few remaining empty catches that should probably display errors/warnings if they catch something. I added outputs for the ones on the critical path, but adding output for the rest would be useful.

## Expand Test Coverage
There are unit tests covering core services, but migrating more logic into Core and outside of the base mod would also be useful, because you could then add more unit test coverage over it

### Dependency Injection for Testability
One issue with testing things is how much reliance there is on Unity and MelonLoader. I've migrated some code into its own class to try and begin isolating this, but that work is not done. If you were to add something like this:
```
public interface IGameDataCache
{
    object DataManager { get; }
    void CacheDataManager(object dm);
}

// Tests can mock this
public class MockGameDataCache : IGameDataCache { }
```
Then you could use the real `GameDataCache` in actual code execution, but in the Test project, you could use `MockGameDataCache` to pass in a fake version of the object that can do/return any data you want. This would allow for much easier unit testing and better isolation.

## User Configuration System
Currently all behavior is hardcoded in the application, you could use MelonPreferences for customization. For example:
```
public static class ModConfig
{
    // Timing
    public static float HoverDelay { get; set; } = 0.5f;
    public static float CollectionHoverDelay { get; set; } = 1.0f;
    
    // UI
    public static float TooltipScale { get; set; } = 1.0f;
    public static bool ShowOwnedIndicators { get; set; } = true;
    public static bool ShowBannedOverlay { get; set; } = true;
    
    // Features
    public static bool EnableArcanaTooltips { get; set; } = true;
    public static bool EnableControllerSupport { get; set; } = true;
    public static bool ShowEvolutionFormulas { get; set; } = true;
    
    // Debug
    public static bool VerboseLogging { get; set; } = false;
    public static bool ShowDebugInfo { get; set; } = false;
}
```

## Keyboard Shortcut Customization
Currently keys are hardcoded (tab, backspace, space) with the following:
```
        private static bool IsInteractButtonPressed()
        {
            return UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Tab) ||
                   UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.JoystickButton3); // Y/Triangle
        }
        private static bool IsBackButtonPressed()
        {
            return UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Backspace) ||
                   UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.JoystickButton1);
        }
        private static bool IsSubmitButtonPressed()
        {
            return UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Space) ||
                   UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Return) ||
                   UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.JoystickButton0); // A/Cross
        }
```

This could instead allow rebinding in config
```
public static class KeyBindings
{
    public static UnityEngine.KeyCode InteractKey { get; set; } = UnityEngine.KeyCode.Tab;
    public static UnityEngine.KeyCode BackKey { get; set; } = UnityEngine.KeyCode.Backspace;
    public static UnityEngine.KeyCode SubmitKey { get; set; } = UnityEngine.KeyCode.Space;
}
```

## Mod Compatibility Layer
Currently the mod doesn't check to see if there are any other mods related to tooltips, which may cause issues if there are. So something like this to log a possible incompatibility could be useful:
```
public static class ModCompatibility
{
    public static void CheckCompatibility()
    {
        var mods = MelonLoader.MelonMod.RegisteredMelons;
        foreach (var mod in mods)
        {
            if (mod.Info.Name.Contains("Tooltip") && mod != this)
                MelonLogger.Warning($"[Compat] Detected tooltip mod '{mod.Info.Name}' - may conflict");
        }
    }
}
```

## Rich Tooltip
You could maybe have rich text display inside the tooltips, along with some other enhanced visual information like:
* Show stat changes (e.g. "+50% Damage")
* Display synergies (items that work well together)
* Show unlock conditions for locked items
* Add rarity indicators
* Display cooldown/area/duration stats visually

## Migrate to C# 10 / .NET 8
Currently the mod targets .NET 6 and C# 8.0, but I believe that it could be migrated to a more recent C# and .NET version.

## Async/Await for Expensive Operations
Synchronous reflection operations can cause frame drops, especially on older hardware/machines. If anything could take longer than expected, moving them to background threads may be useful. For example:
```
public static async System.Threading.Tasks.Task BuildLookupTablesAsync()
{
    await System.Threading.Tasks.Task.Run(() => 
    {
        BuildWeaponLookup(cachedWeaponsDict);
        BuildPowerUpLookup(cachedPowerUpsDict);
    });
}
```

## Tooltip Caching
Popups are rebuild every time they're needed. If you were to cache rendered tooltip gameobjects, you could have a smoother UX. For example, you'd build popup once per weapon/item, pool and reuse instead of destroy/recreate, and only rebuild if data changes. This would produce faster popup display (no UI construction cost) and smother UI.

## Event-Driven Architecture
Currently the mod polls in OnUpdate for state changes, if you move this to an event-driven implementation, it can reduce update overhead, have cleaner dependencies, easier to trace execution flow, and is overall better for testing. For example:
```
public static event Action<object> OnDataManagerCached;
public static event Action<object> OnGameSessionCached;
public static event Action OnGamePaused;
public static event Action OnGameUnpaused;

// Subscribers react instead of polling
public static void CacheDataManager(object dm)
{
    cachedDataManager = dm;
    OnDataManagerCached?.Invoke(dm);
}
```

## Strategy Pattern for Type Discovery
Currently it uses Try-catch chains for finding types, but instead you could use a pluggable discovery strategy for discovering types (see the [Strategy Design Pattern](https://refactoring.guru/design-patterns/strategy)). For example:
```
public interface ITypeDiscoveryStrategy
{
    Type FindType(string typeName);
    object FindInstance(Type type);
}

public class FindObjectOfTypeStrategy : ITypeDiscoveryStrategy { }
public class StaticInstanceStrategy : ITypeDiscoveryStrategy { }
public class GameObjectSearchStrategy : ITypeDiscoveryStrategy { }

// Chain of responsibility
public class TypeDiscoveryChain
{
    public object Discover(string typeName)
    {
        foreach (var strategy in strategies)
        {
            var result = strategy.TryDiscover(typeName);
            if (result != null) return result;
        }
        return null;
    }
}
```

## Automated Release Pipeline
You can use Github Actions to automate releases of the DLLs, for example:
```
on:
  push:
    tags: ['v*']

jobs:
  release:
    - build mod DLL
    - run tests
    - create GitHub release
    - upload artifacts
    - notify Discord/community
```

This way, when you're ready to generate a new release, you can create a tag (like, say, `v1.2.1`), and the Github Action will automatically build and publish the release for that specific branch.

## Version Compatibility Checker
Currently there's nothing saying if this is no longer compatible with the current game version. Having some way of checking the current game version would be useful, allowing the user to be informed of any possible compatibility issues. For example:
```
public static class GameVersionChecker
{
    private static Dictionary<string, string> testedVersions = new()
    {
        { "1.10.0", "Compatible" },
        { "1.9.0", "Compatible" },
        { "1.11.0", "Unknown - please report!" }
    };
    
    public static void CheckVersion()
    {
        string gameVersion = GetGameVersion();
        if (!testedVersions.ContainsKey(gameVersion))
            MelonLogger.Warning($"Mod not tested on game version {gameVersion}");
    }
}
```

## Hot Reload Support
Okay, so this would just be nice-to-have from my short experience testing/developing this and wanting to change something in the DLL and having to reload the game to test it. It would be nice if the code could watch for DLL changes and reload the new code without needing to restart the game. MelonLoader supports this already with proper setup. For example:
* Watch for DLL changes
* Unload old Harmony patches
* Reload new code
* Re-apply patches

## Video/GIF Demonstrations
You could update the README.md with a video or gif showing what the mod looks like. As someone that has searched for mods, having that shown is VERY helpful for knowing if I want to actually use a mod or not.