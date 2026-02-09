using MelonLoader;
using HarmonyLib;
using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using Il2CppVampireSurvivors.Data;
using Il2CppVampireSurvivors.Data.Weapons;
using Il2CppVampireSurvivors.Objects;
using Il2CppVampireSurvivors.UI;

[assembly: MelonInfo(typeof(VSItemTooltips.ItemTooltipsMod), "VS Item Tooltips", "1.0.0", "Nihil")]
[assembly: MelonGame("poncle", "Vampire Survivors")]

namespace VSItemTooltips
{
    /// <summary>
    /// Main mod class - adds hover tooltips to all weapon/item icons when game is paused.
    /// Instead of patching specific UI windows, this hooks into any Image component
    /// that displays a weapon/item sprite and makes it hoverable.
    /// </summary>
    public class ItemTooltipsMod : MelonMod
    {
        private static HarmonyLib.Harmony harmonyInstance;

        // State tracking
        private static bool wasGamePaused = false;

        // Popup stack for recursive popups
        private static List<UnityEngine.GameObject> popupStack = new List<UnityEngine.GameObject>();
        private static List<int> popupAnchorIds = new List<int>();
        private static int mouseOverPopupIndex = -1; // -1 = not over any popup, 0+ = index in stack

        // Legacy single popup references (for compatibility)
        private static UnityEngine.GameObject currentPopup { get { return popupStack.Count > 0 ? popupStack[popupStack.Count - 1] : null; } }
        private static int currentPopupAnchorId { get { return popupAnchorIds.Count > 0 ? popupAnchorIds[popupAnchorIds.Count - 1] : 0; } }

        // Tracked icons
        private static Dictionary<int, TrackedIcon> trackedIcons = new Dictionary<int, TrackedIcon>();
        private static float lastScanTime = 0f;
        private static float scanInterval = 0.1f; // Scan every 100ms

        // UI element to weapon/item type mapping (set by SetWeaponData/SetItemData patches)
        private static Dictionary<int, WeaponType> uiToWeaponType = new Dictionary<int, WeaponType>();
        private static Dictionary<int, ItemType> uiToItemType = new Dictionary<int, ItemType>();

        // Sprite name to item type lookup (built on first use)
        private static Dictionary<string, WeaponType> spriteToWeaponType = null;
        private static Dictionary<string, ItemType> spriteToItemType = null;
        private static bool lookupTablesBuilt = false;
        private static bool loggedLookupTables = false;

        // Data manager cache
        private static object cachedDataManager = null;
        private static object cachedWeaponsDict = null;
        private static object cachedPowerUpsDict = null;

        // Game session cache (for accessing player inventory)
        private static object cachedGameSession = null;

        // Arcana cache
        private static System.Type cachedArcanaTypeEnum = null;
        private static object cachedAllArcanas = null;
        private static object cachedGameManager = null;
        private static bool arcanaDebugLogged = false;
        private static HashSet<WeaponType> arcanaWeaponDebugLogged = new HashSet<WeaponType>();
        private static Dictionary<int, (HashSet<WeaponType> weapons, HashSet<ItemType> items)> arcanaUICache =
            new Dictionary<int, (HashSet<WeaponType>, HashSet<ItemType>)>();
        private static Dictionary<string, int> arcanaNameToInt = new Dictionary<string, int>();

        // Collection screen hover tracking (no EventTriggers - uses per-frame raycast)
        private static Dictionary<int, (UnityEngine.GameObject go, WeaponType? weapon, ItemType? item, object arcanaType)> collectionIcons =
            new Dictionary<int, (UnityEngine.GameObject, WeaponType?, ItemType?, object)>();
        private static int currentCollectionHoverId = -1;
        private static UnityEngine.GameObject collectionPopup = null;

        // Styling
        private static readonly UnityEngine.Color PopupBgColor = new UnityEngine.Color(0.08f, 0.08f, 0.12f, 0.98f);
        private static readonly UnityEngine.Color PopupBorderColor = new UnityEngine.Color(0.5f, 0.5f, 0.7f, 1f);
        private static readonly float IconSize = 48f;
        private static readonly float SmallIconSize = 40f;
        private static readonly float Padding = 12f;
        private static readonly float Spacing = 8f;

        public override void OnInitializeMelon()
        {
            harmonyInstance = new HarmonyLib.Harmony("com.nihil.vsitemtooltips");
            ApplyPatches();
            MelonLogger.Msg("VS Item Tooltips initialized!");
        }

        private void ApplyPatches()
        {
            try
            {
                // Patch to capture DataManager when LevelUpPage is shown
                var levelUpPageType = typeof(LevelUpPage);
                // Try OnShowStart first (called when page is shown)
                var showMethod = levelUpPageType.GetMethod("OnShowStart", BindingFlags.Public | BindingFlags.Instance);
                if (showMethod != null)
                {
                    harmonyInstance.Patch(showMethod,
                        postfix: new HarmonyMethod(typeof(LevelUpPagePatches), nameof(LevelUpPagePatches.Show_Postfix)));
                }
                else
                {
                    MelonLogger.Warning("LevelUpPage.OnShowStart method not found!");
                }

                // Try to find and patch MerchantPage (name may vary)
                TryPatchMerchantPage();

                // Patch LevelUpItemUI.SetWeaponData to track which UI elements have which weapons
                TryPatchLevelUpItemUI();

                // Patch EquipmentIconPause for pause screen icons
                TryPatchEquipmentIconPause();

                MelonLogger.Msg("Patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to apply patches: {ex}");
            }
        }

        private void TryPatchLevelUpItemUI()
        {
            try
            {
                // Search for LevelUpItemUI type
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        var itemUIType = assembly.GetTypes()
                            .FirstOrDefault(t => t.Name == "LevelUpItemUI");

                        if (itemUIType != null)
                        {
                            var setWeaponMethod = itemUIType.GetMethod("SetWeaponData", BindingFlags.Public | BindingFlags.Instance);
                            if (setWeaponMethod != null)
                            {
                                harmonyInstance.Patch(setWeaponMethod,
                                    postfix: new HarmonyMethod(typeof(LevelUpItemUIPatches), nameof(LevelUpItemUIPatches.SetWeaponData_Postfix)));
                            }
                            else
                            {
                                MelonLogger.Warning("SetWeaponData method not found on LevelUpItemUI");
                            }

                            var setItemMethod = itemUIType.GetMethod("SetItemData", BindingFlags.Public | BindingFlags.Instance);
                            if (setItemMethod != null)
                            {
                                harmonyInstance.Patch(setItemMethod,
                                    postfix: new HarmonyMethod(typeof(LevelUpItemUIPatches), nameof(LevelUpItemUIPatches.SetItemData_Postfix)));
                            }
                            else
                            {
                                MelonLogger.Warning("SetItemData method not found on LevelUpItemUI");
                            }

                            return;
                        }
                    }
                    catch { }
                }
                MelonLogger.Warning("LevelUpItemUI type not found in any assembly");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error patching LevelUpItemUI: {ex}");
            }
        }

        private void TryPatchEquipmentIconPause()
        {
            try
            {
                // Discover ArcanaType enum early so we can patch arcana methods
                System.Type arcanaTypeEnum = null;
                try
                {
                    var gameAssembly = typeof(WeaponData).Assembly;
                    arcanaTypeEnum = gameAssembly.GetTypes().FirstOrDefault(t => t.Name == "ArcanaType");
                    if (arcanaTypeEnum != null)
                    {
                        cachedArcanaTypeEnum = arcanaTypeEnum;
                    }
                }
                catch { }

                // Search ALL types that might be icon/equipment related
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!assembly.FullName.Contains("Il2Cpp")) continue;

                    try
                    {
                        var iconTypes = assembly.GetTypes()
                            .Where(t => t.Name.ToLower().Contains("icon") ||
                                       t.Name.ToLower().Contains("equipment") ||
                                       t.Name.ToLower().Contains("itemui") ||
                                       t.Name.ToLower().Contains("arcana") ||
                                       t.Name.ToLower().Contains("evolution") ||
                                       t.Name.ToLower().Contains("weaponui") ||
                                       t.Name.ToLower().Contains("powerup"))
                            .Take(30);

                        foreach (var iconType in iconTypes)
                        {
                            // Look for methods that take WeaponType, ItemType, or ArcanaType
                            var interestingMethods = iconType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                                .Where(m => m.GetParameters().Any(p =>
                                    p.ParameterType == typeof(WeaponType) ||
                                    p.ParameterType == typeof(ItemType) ||
                                    (arcanaTypeEnum != null && p.ParameterType == arcanaTypeEnum) ||
                                    p.ParameterType.Name.Contains("Weapon") ||
                                    p.ParameterType.Name.Contains("Item")))
                                .Take(5);

                            foreach (var m in interestingMethods)
                            {
                                // Try to patch methods that look like they set weapon/item data
                                if (m.Name.Contains("Set") || m.Name.Contains("Init") || m.Name.Contains("Setup") ||
                                    m.Name.Contains("Add") || m.Name.Contains("Spawn") || m.Name.Contains("Create"))
                                {
                                    try
                                    {
                                        // Find which parameter is WeaponType, ItemType, or ArcanaType
                                        var allParams = m.GetParameters();
                                        int weaponParamIndex = -1;
                                        int itemParamIndex = -1;
                                        int arcanaParamIndex = -1;

                                        for (int pi = 0; pi < allParams.Length; pi++)
                                        {
                                            if (allParams[pi].ParameterType == typeof(WeaponType))
                                                weaponParamIndex = pi;
                                            else if (allParams[pi].ParameterType == typeof(ItemType))
                                                itemParamIndex = pi;
                                            else if (arcanaTypeEnum != null && allParams[pi].ParameterType == arcanaTypeEnum)
                                                arcanaParamIndex = pi;
                                        }

                                        if (weaponParamIndex >= 0)
                                        {
                                            // Use different patch method based on parameter position
                                            string patchMethod;
                                            switch (weaponParamIndex)
                                            {
                                                case 0: patchMethod = nameof(GenericIconPatches.SetWeapon_Postfix); break;
                                                case 1: patchMethod = nameof(GenericIconPatches.SetWeapon_Postfix_Arg1); break;
                                                default: patchMethod = nameof(GenericIconPatches.SetWeapon_Postfix_ArgN); break;
                                            }

                                            harmonyInstance.Patch(m,
                                                postfix: new HarmonyMethod(typeof(GenericIconPatches), patchMethod));
                                        }
                                        else if (itemParamIndex >= 0)
                                        {
                                            string patchMethod;
                                            switch (itemParamIndex)
                                            {
                                                case 0: patchMethod = nameof(GenericIconPatches.SetItem_Postfix); break;
                                                case 1: patchMethod = nameof(GenericIconPatches.SetItem_Postfix_Arg1); break;
                                                default: patchMethod = nameof(GenericIconPatches.SetItem_Postfix_ArgN); break;
                                            }

                                            harmonyInstance.Patch(m,
                                                postfix: new HarmonyMethod(typeof(GenericIconPatches), patchMethod));
                                        }
                                        else if (arcanaParamIndex >= 0)
                                        {
                                            harmonyInstance.Patch(m,
                                                postfix: new HarmonyMethod(typeof(GenericIconPatches), nameof(GenericIconPatches.SetArcana_Postfix_ArgN)));
                                        }
                                    }
                                    catch (Exception patchEx)
                                    {
                                        MelonLogger.Warning($"  Failed to patch: {patchEx.Message}");
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error searching for icon types: {ex.Message}");
            }
        }

        private void TryPatchMerchantPage()
        {
            try
            {
                // Search for MerchantPage type in loaded assemblies
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        var merchantType = assembly.GetTypes()
                            .FirstOrDefault(t => t.Name.Contains("Merchant") && t.Name.Contains("Page"));

                        if (merchantType != null)
                        {
                            var showMethod = merchantType.GetMethod("Show", BindingFlags.Public | BindingFlags.Instance);
                            if (showMethod != null)
                            {
                                harmonyInstance.Patch(showMethod,
                                    postfix: new HarmonyMethod(typeof(GenericPagePatches), nameof(GenericPagePatches.Show_Postfix)));
                                return;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not patch MerchantPage: {ex.Message}");
            }
        }

        private static float lastTimeScaleLog = 0f;
        private static bool escWasPressed = false;
        private static bool triedEarlyCaching = false;

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            // Reset caching state when a new scene loads
            triedEarlyCaching = false;
        }

        private void TryEarlyCaching()
        {
            if (triedEarlyCaching) return;
            if (cachedDataManager != null && cachedWeaponsDict != null) return;

            triedEarlyCaching = true;

            try
            {
                // Method 1: Try FindObjectOfType for DataManager directly
                try
                {
                    var dataManagerType = System.AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => { try { return a.GetTypes(); } catch { return new System.Type[0]; } })
                        .FirstOrDefault(t => t.Name == "DataManager" && t.Namespace != null && t.Namespace.Contains("VampireSurvivors"));

                    if (dataManagerType != null)
                    {

                        var findMethod = typeof(UnityEngine.Object).GetMethod("FindObjectOfType", new System.Type[0]);
                        if (findMethod != null)
                        {
                            var genericMethod = findMethod.MakeGenericMethod(dataManagerType);
                            var dm = genericMethod.Invoke(null, null);
                            if (dm != null)
                            {
                                CacheDataManager(dm);
                                return;
                            }
                        }
                    }
                }
                catch { }

                // Method 2: Try GameManager.Instance.Data
                var gameManagerType = System.AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return new System.Type[0]; } })
                    .FirstOrDefault(t => t.Name == "GameManager");

                if (gameManagerType != null)
                {
                    // Try all static properties and fields to find instance
                    var allStaticMembers = gameManagerType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                    object instance = null;
                    foreach (var member in allStaticMembers)
                    {
                        try
                        {
                            if (member is PropertyInfo prop && prop.PropertyType == gameManagerType)
                            {
                                instance = prop.GetValue(null);
                                if (instance != null)
                                {
                                    break;
                                }
                            }
                            else if (member is FieldInfo field && field.FieldType == gameManagerType)
                            {
                                instance = field.GetValue(null);
                                if (instance != null)
                                {
                                    break;
                                }
                            }
                        }
                        catch { }
                    }

                    if (instance != null)
                    {
                        // Try to get Data property
                        var allProps = instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        foreach (var prop in allProps)
                        {
                            if (prop.Name == "Data" || prop.PropertyType.Name.Contains("DataManager"))
                            {
                                try
                                {
                                    var val = prop.GetValue(instance);
                                    if (val != null)
                                    {
                                        CacheDataManager(val);
                                        return;
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }

                // Method 3: Search all MonoBehaviours for one with GetConvertedWeapons method
                var allBehaviours = UnityEngine.Object.FindObjectsOfType<UnityEngine.MonoBehaviour>();
                foreach (var mb in allBehaviours.Take(100))
                {
                    var getWeaponsMethod = mb.GetType().GetMethod("GetConvertedWeapons");
                    if (getWeaponsMethod != null)
                    {
                        CacheDataManager(mb);
                        return;
                    }
                }

            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Early caching failed: {ex.Message}");
            }
        }

        public override void OnUpdate()
        {
            // Try to cache data early (once per scene)
            if (!triedEarlyCaching && cachedDataManager == null)
            {
                TryEarlyCaching();
            }

            // Detect ESC key press to find pause menu
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Escape))
            {
                escWasPressed = true;

                // Search for Safe Area and find pause view
                var safeArea = UnityEngine.GameObject.Find("GAME UI/Canvas - Game UI/Safe Area");
                if (safeArea != null)
                {
                    for (int i = 0; i < safeArea.transform.childCount; i++)
                    {
                        var child = safeArea.transform.GetChild(i);

                        // If this child is active and looks like a pause view, cache it
                        if (child.gameObject.activeInHierarchy &&
                            (child.name.ToLower().Contains("map") || child.name.ToLower().Contains("pause")))
                        {
                            pauseView = child.gameObject;
                        }
                    }
                }
            }

            bool isGamePaused = IsGamePaused();

            // State change: became paused (entering game clears collection icons)
            if (isGamePaused && !wasGamePaused)
            {
                collectionIcons.Clear();
                HideCollectionPopup();
                ClearTrackedIcons();

                // If paused, scan the pause view for equipment icons
                if (pauseView != null && pauseView.activeInHierarchy)
                {
                    ScanPauseViewForEquipment(pauseView);
                }

                // Allow re-searching for HUD if not found yet
                if (hudInventory == null && inGameUIFound)
                {
                    hudSearched = false;
                }

                // Try to get game session if we don't have it yet
                if (cachedGameSession == null)
                {
                    TryFindGameSession();
                }

                // Setup HUD hovers when paused
                if (hudInventory != null && cachedGameSession != null)
                {
                    SetupHUDHovers();
                }
            }

            // State change: became unpaused
            if (!isGamePaused && wasGamePaused)
            {
                HidePopup();
                ClearTrackedIcons();
                loggedScanStatus = false; // Reset so we can warn again if needed
                loggedScanResults = false;
                scannedPauseView = false; // Rescan pause view next time
            }

            // Reset view caches if we've returned to main menu (views destroyed)
            if (!isGamePaused && levelUpView == null && merchantView == null && pauseView == null && itemFoundView == null)
            {
                if (inGameUIFound)
                {
                    inGameUIFound = false;
                    hudSearched = false;
                    hudInventory = null;
                }
            }

            wasGamePaused = isGamePaused;

            // Collection screen hover detection (runs even when not in-game)
            if (!isGamePaused && collectionIcons.Count > 0)
            {
                UpdateCollectionHover();
            }

            // Only process when game is paused
            if (!isGamePaused) return;

            // Throttle scanning
            float currentTime = UnityEngine.Time.unscaledTime;
            if (currentTime - lastScanTime >= scanInterval)
            {
                lastScanTime = currentTime;
                ScanForIcons();
            }
        }

        #region Pause Detection

        // Cached UI view references (popup/menu views)
        private static UnityEngine.GameObject levelUpView = null;
        private static UnityEngine.GameObject merchantView = null;
        private static UnityEngine.GameObject pauseView = null;
        private static UnityEngine.GameObject itemFoundView = null;
        private static UnityEngine.GameObject arcanaView = null;

        // Cached HUD elements (always visible, but only hoverable when paused)
        private static UnityEngine.GameObject hudInventory = null;
        private static bool hudSearched = false;
        private static bool inGameUIFound = false; // True once we've confirmed game UI exists

        // Currently active views (for targeted scanning)
        private static List<UnityEngine.Transform> activeUIContainers = new List<UnityEngine.Transform>();

        private static bool triedFindingPauseView = false;

        private static bool IsGamePaused()
        {
            activeUIContainers.Clear();

            // Find views if not cached
            if (levelUpView == null)
                levelUpView = UnityEngine.GameObject.Find("GAME UI/Canvas - Game UI/Safe Area/View - Level Up");
            if (merchantView == null)
                merchantView = UnityEngine.GameObject.Find("GAME UI/Canvas - Game UI/Safe Area/View - Merchant");
            if (itemFoundView == null)
                itemFoundView = UnityEngine.GameObject.Find("GAME UI/Canvas - Game UI/Safe Area/View - ItemFound");
            if (arcanaView == null)
                arcanaView = UnityEngine.GameObject.Find("GAME UI/Canvas - Game UI/Safe Area/View - ArcanaMainSelection");

            // Try multiple paths for pause view
            if (pauseView == null)
            {
                string[] pausePaths = new string[]
                {
                    "GAME UI/Canvas - Game UI/Safe Area/View - Paused",  // Correct path!
                    "GAME UI/Canvas - Game UI/Safe Area/View - Pause",
                    "GAME UI/Canvas - Game UI/Safe Area/View - Map"
                };

                foreach (var path in pausePaths)
                {
                    pauseView = UnityEngine.GameObject.Find(path);
                    if (pauseView != null)
                    {
                        break;
                    }
                }

                // If still not found, search for any view with "pause" or "map" in name
                if (pauseView == null && !triedFindingPauseView)
                {
                    triedFindingPauseView = true;
                    var safeArea = UnityEngine.GameObject.Find("GAME UI/Canvas - Game UI/Safe Area");
                    if (safeArea != null)
                    {
                        for (int i = 0; i < safeArea.transform.childCount; i++)
                        {
                            var child = safeArea.transform.GetChild(i);
                            if (child.name.ToLower().Contains("pause") || child.name.ToLower().Contains("map"))
                            {
                                pauseView = child.gameObject;
                            }
                        }
                    }
                }
            }

            // Check if we're in a game by seeing if any view was found
            bool anyViewFound = levelUpView != null || merchantView != null || pauseView != null || itemFoundView != null;

            // Only search for HUD once we've confirmed game UI exists (i.e., we're in a game)
            if (!hudSearched && anyViewFound && !inGameUIFound)
            {
                inGameUIFound = true;
                hudSearched = true;

                // Common paths for the HUD inventory display
                string[] hudPaths = new string[]
                {
                    "GAME UI/Canvas - Game UI/Safe Area/View - Game/PlayerGUI/Inventory",
                    "GAME UI/Canvas - Game UI/Safe Area/View - Game/Inventory",
                    "GAME UI/Canvas - Game UI/Safe Area/PlayerGUI/Inventory",
                    "GAME UI/Canvas - Game UI/Safe Area/View - Game/PlayerGUI",
                    "GAME UI/Canvas - Game UI/Safe Area/View - Game"
                };

                foreach (var path in hudPaths)
                {
                    hudInventory = UnityEngine.GameObject.Find(path);
                    if (hudInventory != null)
                    {
                        break;
                    }
                }
            }

            bool isPaused = false;

            // Check each popup/menu view and track which are active
            if (levelUpView != null && levelUpView.activeInHierarchy)
            {
                activeUIContainers.Add(levelUpView.transform);
                isPaused = true;
            }

            if (merchantView != null && merchantView.activeInHierarchy)
            {
                activeUIContainers.Add(merchantView.transform);
                isPaused = true;
            }

            if (pauseView != null && pauseView.activeInHierarchy)
            {
                activeUIContainers.Add(pauseView.transform);
                isPaused = true;
            }

            if (itemFoundView != null && itemFoundView.activeInHierarchy)
            {
                activeUIContainers.Add(itemFoundView.transform);
                isPaused = true;
            }

            if (arcanaView != null && arcanaView.activeInHierarchy)
            {
                activeUIContainers.Add(arcanaView.transform);
                isPaused = true;
            }

            // Also check time scale as fallback, but ONLY if we're in an actual game run
            // (prevents activating on main menu / Collection screen where timeScale may also be 0)
            if (!isPaused && inGameUIFound && UnityEngine.Time.timeScale == 0f)
            {
                // Log once when we detect pause via timeScale
                if (!wasGamePaused)
                {
                    // Search for any active view that might be the pause screen
                    var safeArea = UnityEngine.GameObject.Find("GAME UI/Canvas - Game UI/Safe Area");
                    if (safeArea != null)
                    {
                        for (int i = 0; i < safeArea.transform.childCount; i++)
                        {
                            var child = safeArea.transform.GetChild(i);
                            if (child.gameObject.activeInHierarchy)
                            {
                                // Add any active view as a container
                                if (child.name.Contains("View") || child.name.Contains("Map") || child.name.Contains("Pause"))
                                {
                                    activeUIContainers.Add(child);
                                    if (pauseView == null && (child.name.ToLower().Contains("map") || child.name.ToLower().Contains("pause")))
                                    {
                                        pauseView = child.gameObject;
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        MelonLogger.Warning("Safe Area not found!");
                    }
                }
                isPaused = true;
            }

            // If paused by any means, also include the HUD inventory for scanning
            // (it's always visible, but we only want hovers when paused)
            if (isPaused && hudInventory != null && hudInventory.activeInHierarchy)
            {
                activeUIContainers.Add(hudInventory.transform);
            }

            return isPaused;
        }

        #endregion

        #region Icon Tracking

        /// <summary>
        /// Tracks a UI Image that displays a weapon/item icon.
        /// </summary>
        private class TrackedIcon
        {
            public UnityEngine.UI.Image Image;
            public WeaponType? WeaponType;
            public ItemType? ItemType;
            public string SpriteName;
            public int InstanceId;
            public UnityEngine.EventSystems.EventTrigger EventTrigger;
        }

        private static bool loggedScanStatus = false;
        private static bool loggedScanResults = false;

        private static void ScanForIcons()
        {
            // Only scan if we have active UI containers
            if (activeUIContainers.Count == 0)
                return;

            // Build lookup tables if needed
            if (!lookupTablesBuilt)
            {
                BuildLookupTables();
                if (!lookupTablesBuilt && !loggedScanStatus)
                {
                    MelonLogger.Warning("Lookup tables not built - no DataManager cached yet. Hovers won't work until level-up.");
                    loggedScanStatus = true;
                }
            }

            int totalImages = 0;
            int matchedImages = 0;

            // Only scan Image components within active UI containers
            foreach (var container in activeUIContainers)
            {
                if (container == null) continue;

                // Get all Image components in this container's hierarchy
                var images = container.GetComponentsInChildren<UnityEngine.UI.Image>(false); // false = only active
                totalImages += images.Length;

                foreach (var image in images)
                {
                    if (image == null || image.sprite == null) continue;

                    int instanceId = image.GetInstanceID();

                    // Skip if already tracked and still valid
                    if (trackedIcons.TryGetValue(instanceId, out var existing))
                    {
                        // Check if sprite changed
                        if (existing.SpriteName == image.sprite.name)
                            continue;
                        else
                        {
                            // Sprite changed, remove old tracking
                            RemoveTracking(existing);
                            trackedIcons.Remove(instanceId);
                        }
                    }

                    // Try to identify this sprite
                    string spriteName = image.sprite.name;
                    WeaponType? weaponType = null;
                    ItemType? itemType = null;

                    if (spriteToWeaponType != null && spriteToWeaponType.TryGetValue(spriteName, out var wt))
                    {
                        weaponType = wt;
                    }
                    else if (spriteToItemType != null && spriteToItemType.TryGetValue(spriteName, out var it))
                    {
                        itemType = it;
                    }

                    // If we identified this as an item, add hover tracking
                    if (weaponType.HasValue || itemType.HasValue)
                    {
                        matchedImages++;
                        var tracked = new TrackedIcon
                        {
                            Image = image,
                            WeaponType = weaponType,
                            ItemType = itemType,
                            SpriteName = spriteName,
                            InstanceId = instanceId
                        };

                        // Add event trigger for hover
                        AddHoverToIcon(tracked);
                        trackedIcons[instanceId] = tracked;
                    }
                }
            }

            if (!loggedScanResults && totalImages > 0)
            {
                loggedScanResults = true;
            }

            // Clean up stale entries
            var staleIds = new List<int>();
            foreach (var kvp in trackedIcons)
            {
                if (kvp.Value.Image == null || !kvp.Value.Image)
                {
                    staleIds.Add(kvp.Key);
                }
            }
            foreach (var id in staleIds)
            {
                trackedIcons.Remove(id);
            }
        }

        private static void AddHoverToIcon(TrackedIcon tracked)
        {
            var go = tracked.Image.gameObject;

            // Check if already has event trigger
            var existing = go.GetComponent<UnityEngine.EventSystems.EventTrigger>();
            if (existing != null) return;

            // Make sure image is raycast target
            tracked.Image.raycastTarget = true;

            var eventTrigger = go.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            tracked.EventTrigger = eventTrigger;

            // Capture values for closure
            var weaponType = tracked.WeaponType;
            var itemType = tracked.ItemType;

            var enterEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
            enterEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter;
            enterEntry.callback.AddListener((UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData>)((data) =>
            {
                ShowItemPopup(tracked.Image.transform, weaponType, itemType);
            }));
            eventTrigger.triggers.Add(enterEntry);

            var exitEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
            exitEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit;
            exitEntry.callback.AddListener((UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData>)((data) =>
            {
                // Delay hide to allow moving to popup
                MelonLoader.MelonCoroutines.Start(DelayedHideCheck());
            }));
            eventTrigger.triggers.Add(exitEntry);
        }

        private static void RemoveTracking(TrackedIcon tracked)
        {
            if (tracked.EventTrigger != null)
            {
                UnityEngine.Object.Destroy(tracked.EventTrigger);
            }
        }

        private static void ClearTrackedIcons()
        {
            foreach (var kvp in trackedIcons)
            {
                RemoveTracking(kvp.Value);
            }
            trackedIcons.Clear();
        }

        private static bool scannedPauseView = false;

        private static void ScanPauseViewForEquipment(UnityEngine.GameObject pauseViewGo)
        {
            if (scannedPauseView) return;
            scannedPauseView = true;

            // Find the EquipmentPanel
            var equipmentPanel = FindChildRecursive(pauseViewGo.transform, "EquipmentPanel");
            if (equipmentPanel == null) return;

            // Find WeaponsPanel and AccessoryPanel
            var weaponsPanel = equipmentPanel.Find("WeaponsPanel");
            var accessoryPanel = equipmentPanel.Find("AccessoryPanel");

            int weaponCount = 0;
            int accessoryCount = 0;

            if (weaponsPanel != null)
            {
                weaponCount = SetupEquipmentIconHovers(weaponsPanel, true);
            }

            if (accessoryPanel != null)
            {
                accessoryCount = SetupEquipmentIconHovers(accessoryPanel, false);
            }

        }

        private static int SetupEquipmentIconHovers(UnityEngine.Transform panel, bool isWeapons)
        {
            int count = 0;

            for (int i = 0; i < panel.childCount; i++)
            {
                var child = panel.GetChild(i);
                if (!child.name.Contains("EquipmentIconPause")) continue;

                // Try to get the component and its type property
                var components = child.GetComponents<UnityEngine.Component>();
                WeaponType? weaponType = null;
                ItemType? itemType = null;

                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    var compType = comp.GetType();

                    // Skip Unity built-in components
                    if (compType.Namespace != null && compType.Namespace.StartsWith("UnityEngine")) continue;

                    // Try to get Type or WeaponType or ItemType property
                    var typeProp = compType.GetProperty("Type", BindingFlags.Public | BindingFlags.Instance);
                    if (typeProp != null)
                    {
                        var typeVal = typeProp.GetValue(comp);
                        if (typeVal is WeaponType wt)
                        {
                            weaponType = wt;
                        }
                        else if (typeVal is ItemType it)
                        {
                            itemType = it;
                        }
                    }

                    // Also try _type field
                    var typeField = compType.GetField("_type", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (typeField != null && weaponType == null && itemType == null)
                    {
                        var typeVal = typeField.GetValue(comp);
                        if (typeVal is WeaponType wt)
                        {
                            weaponType = wt;
                        }
                        else if (typeVal is ItemType it)
                        {
                            itemType = it;
                        }
                    }
                }

                // Add hover if we found a type
                if (weaponType.HasValue || itemType.HasValue)
                {
                    AddHoverToGameObject(child.gameObject, weaponType, itemType);
                    count++;
                }
            }

            return count;
        }

        private static void LogHierarchy(UnityEngine.Transform t, int depth, int maxDepth)
        {
            if (depth > maxDepth) return;

            string indent = new string(' ', depth * 2);
            var img = t.GetComponent<UnityEngine.UI.Image>();
            string imgInfo = img != null && img.sprite != null ? $" [sprite: {img.sprite.name}]" : "";

            MelonLogger.Msg($"{indent}{t.name}{imgInfo}");

            for (int i = 0; i < t.childCount && i < 10; i++)
            {
                LogHierarchy(t.GetChild(i), depth + 1, maxDepth);
            }

            if (t.childCount > 10)
            {
                MelonLogger.Msg($"{indent}  ... and {t.childCount - 10} more children");
            }
        }

        private static UnityEngine.Transform FindChildRecursive(UnityEngine.Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name.ToLower().Contains(name.ToLower()))
                    return child;

                var found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static string GetFullPath(UnityEngine.Transform t)
        {
            string path = t.name;
            var parent = t.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }

        private static System.Collections.IEnumerator DelayedHideCheck()
        {
            // Capture current stack size
            int stackSizeAtStart = popupStack.Count;

            // Wait frames to give time to reach popup
            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            // Only hide all if:
            // 1. Not hovering over any popup (mouseOverPopupIndex == -1)
            // 2. Stack hasn't grown (no new popup opened)
            if (mouseOverPopupIndex < 0 && popupStack.Count <= stackSizeAtStart && popupStack.Count > 0)
            {
                HideAllPopups();
            }
        }

        private static System.Collections.IEnumerator DelayedStackHideCheck(int exitedPopupIndex)
        {
            // Capture current stack size
            int stackSizeAtStart = popupStack.Count;

            // Wait frames to allow moving to child/sibling elements
            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            // If stack grew (new child popup opened), don't hide anything
            if (popupStack.Count > stackSizeAtStart)
            {
                yield break;
            }

            // If mouse is over this popup or a child, don't hide
            if (mouseOverPopupIndex >= exitedPopupIndex)
            {
                yield break;
            }

            // Close everything ABOVE the currently hovered popup
            // This handles the case where you jump from Child directly to Grandparent -
            // both Child and Parent should close
            int closeFromIndex = mouseOverPopupIndex + 1;
            if (closeFromIndex < 0) closeFromIndex = 0; // If not over any popup, close all

            while (popupStack.Count > closeFromIndex)
            {
                HideTopPopup();
            }
        }

        #endregion

        #region Lookup Tables

        private static void BuildLookupTables()
        {
            if (lookupTablesBuilt) return;

            spriteToWeaponType = new Dictionary<string, WeaponType>();
            spriteToItemType = new Dictionary<string, ItemType>();

            try
            {
                // Build from weapons dictionary if available
                if (cachedWeaponsDict != null)
                {
                    BuildWeaponLookup(cachedWeaponsDict);
                }

                // Build from powerups dictionary if available
                if (cachedPowerUpsDict != null)
                {
                    BuildPowerUpLookup(cachedPowerUpsDict);
                }

                lookupTablesBuilt = spriteToWeaponType.Count > 0 || spriteToItemType.Count > 0;

                if (lookupTablesBuilt && !loggedLookupTables)
                {
                    loggedLookupTables = true;
                    MelonLogger.Msg($"Built lookup tables: {spriteToWeaponType.Count} weapons, {spriteToItemType.Count} items");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to build lookup tables: {ex}");
            }
        }

        private static void BuildWeaponLookup(object weaponsDict)
        {
            try
            {
                // Get dictionary entries using reflection
                var dictType = weaponsDict.GetType();

                var keysProperty = dictType.GetProperty("Keys");
                if (keysProperty == null) return;

                var keys = keysProperty.GetValue(weaponsDict);

                int count = 0;
                var enumerator = keys.GetType().GetMethod("GetEnumerator").Invoke(keys, null);
                var moveNext = enumerator.GetType().GetMethod("MoveNext");
                var current = enumerator.GetType().GetProperty("Current");

                while ((bool)moveNext.Invoke(enumerator, null))
                {
                    count++;
                    var key = current.GetValue(enumerator);

                    if (key is WeaponType wt)
                    {
                        var indexer = dictType.GetProperty("Item");
                        if (indexer != null)
                        {
                            // Value is a List<WeaponData>, not a single WeaponData
                            var dataList = indexer.GetValue(weaponsDict, new object[] { key });
                            if (dataList != null)
                            {
                                // Get count and iterate the list
                                var listType = dataList.GetType();
                                var countProp = listType.GetProperty("Count");
                                var listIndexer = listType.GetProperty("Item");

                                if (countProp != null && listIndexer != null)
                                {
                                    int listCount = (int)countProp.GetValue(dataList);
                                    for (int i = 0; i < listCount; i++)
                                    {
                                        var data = listIndexer.GetValue(dataList, new object[] { i }) as WeaponData;
                                        if (data != null && !string.IsNullOrEmpty(data.frameName))
                                        {
                                            spriteToWeaponType[data.frameName] = wt;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error building weapon lookup: {ex}");
            }
        }

        private static void BuildPowerUpLookup(object powerUpsDict)
        {
            try
            {
                var dictType = powerUpsDict.GetType();

                var keysProperty = dictType.GetProperty("Keys");
                if (keysProperty == null) return;

                var keys = keysProperty.GetValue(powerUpsDict);
                var enumerator = keys.GetType().GetMethod("GetEnumerator").Invoke(keys, null);
                var moveNext = enumerator.GetType().GetMethod("MoveNext");
                var current = enumerator.GetType().GetProperty("Current");

                int count = 0;
                while ((bool)moveNext.Invoke(enumerator, null))
                {
                    count++;
                    var key = current.GetValue(enumerator);
                    if (key is ItemType it)
                    {
                        var indexer = dictType.GetProperty("Item");
                        if (indexer != null)
                        {
                            var dataOrList = indexer.GetValue(powerUpsDict, new object[] { key });
                            if (dataOrList != null)
                            {
                                // Check if it's a List or single item
                                var dataType = dataOrList.GetType();
                                // Try as List first
                                var countProp = dataType.GetProperty("Count");
                                if (countProp != null)
                                {
                                    // It's a list
                                    var listIndexer = dataType.GetProperty("Item");
                                    int listCount = (int)countProp.GetValue(dataOrList);
                                    for (int i = 0; i < listCount; i++)
                                    {
                                        var data = listIndexer.GetValue(dataOrList, new object[] { i });
                                        AddPowerUpToLookup(data, it);
                                    }
                                }
                                else
                                {
                                    // Single item
                                    AddPowerUpToLookup(dataOrList, it);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error building powerup lookup: {ex.Message}");
            }
        }

        private static void AddPowerUpToLookup(object data, ItemType it)
        {
            if (data == null) return;

            string frameName = null;

            // Try property first
            var frameNameProp = data.GetType().GetProperty("frameName", BindingFlags.Public | BindingFlags.Instance);
            if (frameNameProp != null)
            {
                frameName = frameNameProp.GetValue(data) as string;
            }

            // Try field if property didn't work
            if (string.IsNullOrEmpty(frameName))
            {
                var frameNameField = data.GetType().GetField("frameName", BindingFlags.Public | BindingFlags.Instance);
                if (frameNameField != null)
                {
                    frameName = frameNameField.GetValue(data) as string;
                }
            }

            if (!string.IsNullOrEmpty(frameName))
            {
                spriteToItemType[frameName] = it;
            }
        }

        public static void CacheGameSession(object gameSession)
        {
            cachedGameSession = gameSession;
            // Also cache DataManager from the session if we don't have it yet
            if (cachedDataManager == null && gameSession != null)
            {
                try
                {
                    var sessionType = gameSession.GetType();

                    // For IL2CPP, try calling get_Data() method directly
                    var getDataMethod = sessionType.GetMethod("get_Data", BindingFlags.Public | BindingFlags.Instance);
                    if (getDataMethod != null)
                    {
                        var dataManager = getDataMethod.Invoke(gameSession, null);
                        if (dataManager != null)
                        {
                            CacheDataManager(dataManager);
                            return;
                        }
                    }

                    MelonLogger.Warning("[CacheGameSession] Could not find Data on GameSession");
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Error caching DataManager from session: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Tries to find and cache the game session from various sources.
        /// Called when paused but we don't have the session cached yet.
        /// </summary>
        private static void TryFindGameSession()
        {
            try
            {
                // Method 0: Try to find GameSessionData using Unity's FindObjectOfType
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!assembly.FullName.Contains("Il2Cpp")) continue;

                    try
                    {
                        var gameSessionType = assembly.GetTypes()
                            .FirstOrDefault(t => t.Name == "GameSessionData");

                        if (gameSessionType != null)
                        {
                            // Use reflection to call UnityEngine.Object.FindObjectOfType<GameSessionData>()
                            var findMethod = typeof(UnityEngine.Object).GetMethods()
                                .FirstOrDefault(m => m.Name == "FindObjectOfType" && m.IsGenericMethod && m.GetParameters().Length == 0);

                            if (findMethod != null)
                            {
                                var genericMethod = findMethod.MakeGenericMethod(gameSessionType);
                                var session = genericMethod.Invoke(null, null);

                                if (session != null)
                                {
                                    var charProp = session.GetType().GetProperty("ActiveCharacter", BindingFlags.Public | BindingFlags.Instance);
                                    if (charProp != null)
                                    {
                                        cachedGameSession = session;
                                        return;
                                    }
                                }
                            }
                            break;
                        }
                    }
                    catch { }
                }

                // Method 0.5: Try to find "Game" GameObject and get GameManager component
                // Based on UnityExplorer showing path: Game/GameManager.GameSessionData
                try
                {
                    var gameObj = UnityEngine.GameObject.Find("Game");
                    if (gameObj != null)
                    {
                        // Get all components and look for GameManager
                        var components = gameObj.GetComponents<UnityEngine.Component>();
                        foreach (var comp in components)
                        {
                            if (comp == null) continue;
                            var compType = comp.GetType();

                            if (compType.Name.Contains("GameManager"))
                            {

                                // Try to get GameSessionData property
                                var sessionProp = compType.GetProperty("GameSessionData", BindingFlags.Public | BindingFlags.Instance);
                                if (sessionProp != null)
                                {
                                    var session = sessionProp.GetValue(comp);
                                    if (session != null)
                                    {
                                        CacheGameSession(session);

                                        // Also try to get DataManager
                                        GenericIconPatches.TryCacheDataManagerFromGameManager(comp);
                                        return;
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }

                // Method 1: Look for GameSessionData type with static Instance
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!assembly.FullName.Contains("Il2Cpp")) continue;

                    try
                    {
                        // Look for types that might be game sessions
                        var sessionTypes = assembly.GetTypes()
                            .Where(t => t.Name.Contains("GameSession") || t.Name == "GameManager")
                            .Take(5);

                        foreach (var sessionType in sessionTypes)
                        {
                            // Try static Instance property
                            var instanceProp = sessionType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                            if (instanceProp != null)
                            {
                                try
                                {
                                    var instance = instanceProp.GetValue(null);
                                    if (instance != null)
                                    {
                                        var activeCharProp = instance.GetType().GetProperty("ActiveCharacter", BindingFlags.Public | BindingFlags.Instance);
                                        if (activeCharProp != null)
                                        {
                                            cachedGameSession = instance;
                                            return;
                                        }
                                    }
                                }
                                catch { }
                            }

                            // Try static _instance field
                            var instanceField = sessionType.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                            if (instanceField == null)
                                instanceField = sessionType.GetField("instance", BindingFlags.NonPublic | BindingFlags.Static);

                            if (instanceField != null)
                            {
                                try
                                {
                                    var instance = instanceField.GetValue(null);
                                    if (instance != null)
                                    {
                                        var activeCharProp = instance.GetType().GetProperty("ActiveCharacter", BindingFlags.Public | BindingFlags.Instance);
                                        if (activeCharProp != null)
                                        {
                                            cachedGameSession = instance;
                                            return;
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }

                // Method 2: Search all components on active views for _gameSession property
                var viewsToSearch = new List<UnityEngine.GameObject>();
                if (pauseView != null && pauseView.activeInHierarchy) viewsToSearch.Add(pauseView);
                if (merchantView != null && merchantView.activeInHierarchy) viewsToSearch.Add(merchantView);
                if (levelUpView != null && levelUpView.activeInHierarchy) viewsToSearch.Add(levelUpView);
                if (arcanaView != null && arcanaView.activeInHierarchy) viewsToSearch.Add(arcanaView);

                foreach (var view in viewsToSearch)
                {
                    // Get ALL components on this view and children
                    var components = view.GetComponentsInChildren<UnityEngine.Component>(true);
                    foreach (var comp in components)
                    {
                        if (comp == null) continue;
                        if (TryGetSessionFromComponent(comp))
                        {
                            return;
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error finding game session: {ex.Message}");
            }
        }

        /// <summary>
        /// Tries to get game session from a component. Returns true if found.
        /// </summary>
        private static bool TryGetSessionFromComponent(UnityEngine.Component component)
        {
            var type = component.GetType();

            // Try common property names for game session
            string[] sessionNames = { "_gameSession", "GameSession", "Session", "gameSession" };

            foreach (var name in sessionNames)
            {
                try
                {
                    var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (prop != null)
                    {
                        var session = prop.GetValue(component);
                        if (session != null)
                        {
                            // Verify it has ActiveCharacter
                            var charProp = session.GetType().GetProperty("ActiveCharacter", BindingFlags.Public | BindingFlags.Instance);
                            if (charProp != null)
                            {
                                cachedGameSession = session;
                                return true;
                            }
                        }
                    }

                    var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                    {
                        var session = field.GetValue(component);
                        if (session != null)
                        {
                            var charProp = session.GetType().GetProperty("ActiveCharacter", BindingFlags.Public | BindingFlags.Instance);
                            if (charProp != null)
                            {
                                cachedGameSession = session;
                                return true;
                            }
                        }
                    }
                }
                catch { }
            }

            return false;
        }

        /// <summary>
        /// Scans the HUD inventory and adds hovers to weapon/item icons.
        /// Called when the game is paused and HUD is found.
        /// </summary>
        public static void SetupHUDHovers()
        {
            if (cachedGameSession == null || hudInventory == null) return;

            try
            {
                // Get ActiveCharacter from game session
                var activeCharProp = cachedGameSession.GetType().GetProperty("ActiveCharacter", BindingFlags.Public | BindingFlags.Instance);
                if (activeCharProp == null) return;

                var activeChar = activeCharProp.GetValue(cachedGameSession);
                if (activeChar == null) return;

                // Get weapons from WeaponsManager.ActiveEquipment
                var weaponsManagerProp = activeChar.GetType().GetProperty("WeaponsManager", BindingFlags.Public | BindingFlags.Instance);
                if (weaponsManagerProp != null)
                {
                    var weaponsManager = weaponsManagerProp.GetValue(activeChar);
                    if (weaponsManager != null)
                    {
                        var activeEquipProp = weaponsManager.GetType().GetProperty("ActiveEquipment", BindingFlags.Public | BindingFlags.Instance);
                        if (activeEquipProp != null)
                        {
                            var equipList = activeEquipProp.GetValue(weaponsManager);
                            SetupHUDSlots(equipList, true);
                        }
                    }
                }

                // Get accessories from AccessoriesManager.ActiveEquipment
                var accessoriesManagerProp = activeChar.GetType().GetProperty("AccessoriesManager", BindingFlags.Public | BindingFlags.Instance);
                if (accessoriesManagerProp != null)
                {
                    var accessoriesManager = accessoriesManagerProp.GetValue(activeChar);
                    if (accessoriesManager != null)
                    {
                        var activeEquipProp = accessoriesManager.GetType().GetProperty("ActiveEquipment", BindingFlags.Public | BindingFlags.Instance);
                        if (activeEquipProp != null)
                        {
                            var equipList = activeEquipProp.GetValue(accessoriesManager);
                            SetupHUDSlots(equipList, false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error setting up HUD hovers: {ex.Message}");
            }
        }

        private static void SetupHUDSlots(object equipList, bool isWeapons)
        {
            if (equipList == null || hudInventory == null) return;

            try
            {
                var listType = equipList.GetType();
                var countProp = listType.GetProperty("Count");
                var indexer = listType.GetProperty("Item");

                if (countProp == null || indexer == null) return;

                int count = (int)countProp.GetValue(equipList);

                // Find slot containers in HUD (look for children with slot-like names)
                var slotContainer = hudInventory.transform.Find(isWeapons ? "Weapons" : "Accessories");
                if (slotContainer == null)
                    slotContainer = hudInventory.transform.Find(isWeapons ? "WeaponSlots" : "AccessorySlots");
                if (slotContainer == null)
                    slotContainer = hudInventory.transform; // Use the inventory itself

                var slots = new List<UnityEngine.Transform>();
                for (int i = 0; i < slotContainer.childCount; i++)
                {
                    slots.Add(slotContainer.GetChild(i));
                }

                // Match equipment to slots
                for (int i = 0; i < count && i < slots.Count; i++)
                {
                    var equipment = indexer.GetValue(equipList, new object[] { i });
                    if (equipment == null) continue;

                    var typeProp = equipment.GetType().GetProperty("Type", BindingFlags.Public | BindingFlags.Instance);
                    if (typeProp == null) continue;

                    var typeValue = typeProp.GetValue(equipment);
                    var slotGo = slots[i].gameObject;

                    // Find icon in slot
                    var iconGo = FindIconInUI(slotGo);
                    var targetGo = iconGo ?? slotGo;

                    if (isWeapons && typeValue is WeaponType wt)
                    {
                        AddHoverToGameObject(targetGo, wt, null);
                    }
                    else if (!isWeapons && typeValue is ItemType it)
                    {
                        AddHoverToGameObject(targetGo, null, it);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetupHUDSlots: {ex.Message}");
            }
        }

        public static bool HasCachedDataManager()
        {
            return cachedDataManager != null;
        }

        // Static version of data manager caching for use outside OnUpdate
        private static void TryCacheDataManagerStatic()
        {
            if (cachedDataManager != null) return;

            try
            {
                // Try GameManager.Instance.Data
                var gameManagerType = System.AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return new System.Type[0]; } })
                    .FirstOrDefault(t => t.Name == "GameManager");

                if (gameManagerType != null)
                {
                    object instance = null;
                    var allStaticMembers = gameManagerType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    foreach (var member in allStaticMembers)
                    {
                        try
                        {
                            if (member is PropertyInfo prop && prop.PropertyType == gameManagerType)
                            {
                                instance = prop.GetValue(null);
                                if (instance != null) break;
                            }
                            else if (member is FieldInfo field && field.FieldType == gameManagerType)
                            {
                                instance = field.GetValue(null);
                                if (instance != null) break;
                            }
                        }
                        catch { }
                    }

                    if (instance != null)
                    {
                        var allProps = instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        foreach (var prop in allProps)
                        {
                            if (prop.Name == "Data" || prop.PropertyType.Name.Contains("DataManager"))
                            {
                                try
                                {
                                    var val = prop.GetValue(instance);
                                    if (val != null)
                                    {
                                        CacheDataManager(val);
                                        return;
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }

                // Fallback: search MonoBehaviours
                var allBehaviours = UnityEngine.Object.FindObjectsOfType<UnityEngine.MonoBehaviour>();
                for (int i = 0; i < allBehaviours.Count && i < 200; i++)
                {
                    var mb = allBehaviours[i];
                    if (mb == null) continue;
                    var getWeaponsMethod = mb.GetType().GetMethod("GetConvertedWeapons");
                    if (getWeaponsMethod != null)
                    {
                        CacheDataManager(mb);
                        return;
                    }
                }

            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Collection] Data caching failed: {ex.Message}");
            }
        }

        public static void CacheDataManager(object dataManager)
        {
            if (dataManager == null) return;

            cachedDataManager = dataManager;

            try
            {
                var dmType = dataManager.GetType();

                var getWeaponsMethod = dmType.GetMethod("GetConvertedWeapons");
                var getPowerUpsMethod = dmType.GetMethod("GetConvertedPowerUpData");

                if (getWeaponsMethod != null)
                {
                    cachedWeaponsDict = getWeaponsMethod.Invoke(dataManager, null);
                }

                if (getPowerUpsMethod != null)
                {
                    cachedPowerUpsDict = getPowerUpsMethod.Invoke(dataManager, null);
                }

                // Rebuild lookup tables with new data
                lookupTablesBuilt = false;
                BuildLookupTables();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error caching data manager: {ex}");
            }
        }

        #endregion

        #region Popup System

        public static void ShowItemPopup(UnityEngine.Transform anchor, WeaponType? weaponType, ItemType? itemType)
        {
            // If not in-game, route to collection popup system (for Collection screen clicks)
            if (!IsGamePaused())
            {
                if (collectionIcons.Count > 0 && (weaponType.HasValue || itemType.HasValue))
                {
                    // Clicked a formula icon inside a collection popup - show new collection popup
                    currentCollectionHoverId = -1; // Reset so it doesn't conflict
                    pendingCollectionHoverId = -1;
                    ShowCollectionPopup(weaponType, itemType);
                    return;
                }
                HideAllPopups();
                return;
            }

            int anchorId = anchor?.GetInstanceID() ?? 0;

            // Check if this anchor already has a popup in the stack
            for (int i = 0; i < popupAnchorIds.Count; i++)
            {
                if (popupAnchorIds[i] == anchorId)
                {
                    // Already showing this popup, don't recreate
                    return;
                }
            }

            // Check if anchor is inside an existing popup (for recursive popups)
            int parentPopupIndex = -1;
            for (int i = 0; i < popupStack.Count; i++)
            {
                if (popupStack[i] != null && anchor != null && anchor.IsChildOf(popupStack[i].transform))
                {
                    parentPopupIndex = i;
                    // Don't break - we want the deepest (highest index) parent
                }
            }

            if (parentPopupIndex >= 0)
            {
                // Opening a child popup - close any existing children of this parent (one child per parent)
                while (popupStack.Count > parentPopupIndex + 1)
                {
                    HideTopPopup();
                }
            }
            else if (popupStack.Count > 0)
            {
                // Not from inside a popup, close all existing popups first
                HideAllPopups();
            }

            // Find appropriate parent for popup (walk up to find a known view)
            var popupParent = FindPopupParent(anchor);
            if (popupParent == null) return;

            // Create popup
            var newPopup = CreatePopup(popupParent, weaponType, itemType);
            if (newPopup == null) return;

            // Add to stack
            popupStack.Add(newPopup);
            popupAnchorIds.Add(anchorId);

            // Position near anchor
            PositionPopup(newPopup, anchor);

            // Add hover tracking to popup itself
            AddPopupHoverTracking(newPopup);
        }

        private static UnityEngine.GameObject CreatePopup(UnityEngine.Transform parent, WeaponType? weaponType, ItemType? itemType)
        {
            var popup = new UnityEngine.GameObject("ItemTooltipPopup");
            popup.transform.SetParent(parent, false);

            var rect = popup.AddComponent<UnityEngine.RectTransform>();
            rect.anchorMin = new UnityEngine.Vector2(0.5f, 0.5f);
            rect.anchorMax = new UnityEngine.Vector2(0.5f, 0.5f);
            rect.pivot = new UnityEngine.Vector2(0f, 1f);

            // Background
            var bg = popup.AddComponent<UnityEngine.UI.Image>();
            bg.color = PopupBgColor;
            bg.raycastTarget = true;

            // Add outline
            var outline = popup.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = PopupBorderColor;
            outline.effectDistance = new UnityEngine.Vector2(2f, 2f);

            // Content - we'll build this dynamically
            float yOffset = -Padding;
            float maxWidth = 420f;

            // Get item data
            string itemName = "Unknown";
            string description = "";
            UnityEngine.Sprite itemSprite = null;

            if (weaponType.HasValue && cachedWeaponsDict != null)
            {
                var data = GetWeaponData(weaponType.Value);
                if (data != null)
                {
                    itemName = data.name ?? weaponType.Value.ToString();
                    description = data.description ?? "";
                    itemSprite = GetSpriteForWeapon(weaponType.Value);
                }
            }
            else if (itemType.HasValue && cachedPowerUpsDict != null)
            {
                var data = GetPowerUpData(itemType.Value);
                if (data != null)
                {
                    itemName = GetPropertyValue<string>(data, "name") ?? itemType.Value.ToString();
                    description = GetPropertyValue<string>(data, "description") ?? "";
                    itemSprite = GetSpriteForItem(itemType.Value);
                }
            }

            var font = GetFont();

            // Title row: [Icon] Name
            if (font != null)
            {
                var titleRow = new UnityEngine.GameObject("TitleRow");
                titleRow.transform.SetParent(popup.transform, false);
                var titleRect = titleRow.AddComponent<UnityEngine.RectTransform>();
                titleRect.anchorMin = new UnityEngine.Vector2(0f, 1f);
                titleRect.anchorMax = new UnityEngine.Vector2(0f, 1f);
                titleRect.pivot = new UnityEngine.Vector2(0f, 1f);
                titleRect.anchoredPosition = new UnityEngine.Vector2(Padding, yOffset);
                titleRect.sizeDelta = new UnityEngine.Vector2(maxWidth - Padding * 2, IconSize);

                // Title text
                var titleText = new UnityEngine.GameObject("Title");
                titleText.transform.SetParent(titleRow.transform, false);
                var titleTextRect = titleText.AddComponent<UnityEngine.RectTransform>();
                titleTextRect.anchorMin = UnityEngine.Vector2.zero;
                titleTextRect.anchorMax = UnityEngine.Vector2.one;
                titleTextRect.offsetMin = new UnityEngine.Vector2(IconSize + Spacing, 0f);
                titleTextRect.offsetMax = UnityEngine.Vector2.zero;

                var titleTmp = titleText.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                titleTmp.font = font;
                titleTmp.text = itemName;
                titleTmp.fontSize = 20f;
                titleTmp.fontStyle = Il2CppTMPro.FontStyles.Bold;
                titleTmp.color = UnityEngine.Color.white;
                titleTmp.alignment = Il2CppTMPro.TextAlignmentOptions.Left;
                titleTmp.enableAutoSizing = true;
                titleTmp.fontSizeMin = 12f;
                titleTmp.fontSizeMax = 20f;
                titleTmp.overflowMode = Il2CppTMPro.TextOverflowModes.Ellipsis;

                // Add item icon to the left of the title
                if (itemSprite != null)
                {
                    var iconObj = new UnityEngine.GameObject("HeaderIcon");
                    iconObj.transform.SetParent(titleRow.transform, false);
                    var iconRect = iconObj.AddComponent<UnityEngine.RectTransform>();
                    iconRect.anchorMin = new UnityEngine.Vector2(0f, 0.5f);
                    iconRect.anchorMax = new UnityEngine.Vector2(0f, 0.5f);
                    iconRect.pivot = new UnityEngine.Vector2(0f, 0.5f);
                    iconRect.anchoredPosition = new UnityEngine.Vector2(0f, 0f);
                    iconRect.sizeDelta = new UnityEngine.Vector2(IconSize, IconSize);

                    var iconImage = iconObj.AddComponent<UnityEngine.UI.Image>();
                    iconImage.sprite = itemSprite;
                    iconImage.preserveAspect = true;
                    iconImage.raycastTarget = false;
                }

                yOffset -= IconSize + Spacing;

                // Description
                if (!string.IsNullOrEmpty(description))
                {
                    var descObj = new UnityEngine.GameObject("Description");
                    descObj.transform.SetParent(popup.transform, false);
                    var descRect = descObj.AddComponent<UnityEngine.RectTransform>();
                    descRect.anchorMin = new UnityEngine.Vector2(0f, 1f);
                    descRect.anchorMax = new UnityEngine.Vector2(0f, 1f);  // Don't stretch horizontally
                    descRect.pivot = new UnityEngine.Vector2(0f, 1f);
                    descRect.anchoredPosition = new UnityEngine.Vector2(Padding, yOffset);
                    float descWidth = maxWidth - Padding * 2;
                    descRect.sizeDelta = new UnityEngine.Vector2(descWidth, 0f);

                    var descTmp = descObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                    descTmp.font = font;
                    descTmp.text = description;
                    descTmp.fontSize = 14f;
                    descTmp.color = new UnityEngine.Color(0.85f, 0.85f, 0.9f, 1f);
                    descTmp.alignment = Il2CppTMPro.TextAlignmentOptions.TopLeft;
                    descTmp.enableWordWrapping = true;
                    descTmp.overflowMode = Il2CppTMPro.TextOverflowModes.Truncate;
                    descTmp.rectTransform.sizeDelta = new UnityEngine.Vector2(descWidth, 0f);

                    var descFitter = descObj.AddComponent<UnityEngine.UI.ContentSizeFitter>();
                    descFitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
                    descFitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

                    // Force TMP to recalculate with the constrained width
                    descTmp.ForceMeshUpdate();
                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(descRect);

                    float descHeight = descTmp.preferredHeight > 0 ? descTmp.preferredHeight : 40f;
                    descRect.sizeDelta = new UnityEngine.Vector2(descWidth, descHeight);
                    yOffset -= descHeight + Spacing;
                }

                // Evolution paths section
                if (weaponType.HasValue)
                {
                    yOffset = AddWeaponEvolutionSection(popup.transform, font, weaponType.Value, yOffset, maxWidth);
                }
                else if (itemType.HasValue)
                {
                    yOffset = AddItemEvolutionSection(popup.transform, font, itemType.Value, yOffset, maxWidth);
                }

                // Arcana section - show active arcanas affecting this item
                List<ArcanaInfo> activeArcanas = null;
                if (weaponType.HasValue)
                {
                    activeArcanas = GetActiveArcanasForWeapon(weaponType.Value);
                }
                else if (itemType.HasValue)
                {
                    activeArcanas = GetActiveArcanasForItem(itemType.Value);
                }

                if (activeArcanas != null && activeArcanas.Count > 0)
                {
                    yOffset = AddArcanaSection(popup.transform, font, activeArcanas, yOffset, maxWidth);
                }
            }

            // Set final size
            yOffset -= Padding;
            rect.sizeDelta = new UnityEngine.Vector2(maxWidth, -yOffset);

            return popup;
        }

        // Structure to hold a single passive requirement in an evolution formula
        private struct PassiveRequirement
        {
            public WeaponType? WeaponType;
            public ItemType? ItemType;
            public UnityEngine.Sprite Sprite;
            public bool Owned;
        }

        // Structure to hold evolution formula data
        private struct EvolutionFormula
        {
            public WeaponType BaseWeapon;
            public System.Collections.Generic.List<PassiveRequirement> Passives;
            public WeaponType EvolvedWeapon;
            public string BaseName;
            public string EvolvedName;
            public UnityEngine.Sprite BaseSprite;
            public UnityEngine.Sprite EvolvedSprite;
        }

        // Collect all passive requirements from an evoSynergy array
        private static System.Collections.Generic.List<PassiveRequirement> CollectPassiveRequirements(
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<WeaponType> evoSynergy)
        {
            var passives = new System.Collections.Generic.List<PassiveRequirement>();
            if (evoSynergy == null) return passives;

            for (int i = 0; i < evoSynergy.Length; i++)
            {
                var reqType = evoSynergy[i];
                var passive = new PassiveRequirement();

                var reqData = GetWeaponData(reqType);
                if (reqData != null)
                {
                    passive.WeaponType = reqType;
                    passive.Sprite = GetSpriteForWeapon(reqType);
                    passive.Owned = PlayerOwnsWeapon(reqType);
                }
                else
                {
                    int enumValue = (int)reqType;
                    if (System.Enum.IsDefined(typeof(ItemType), enumValue))
                    {
                        var itemType = (ItemType)enumValue;
                        passive.ItemType = itemType;
                        passive.Sprite = GetSpriteForItem(itemType);
                        passive.Owned = PlayerOwnsItem(itemType);
                    }
                }

                passives.Add(passive);
            }
            return passives;
        }

        // Check if player owns a weapon
        private static bool PlayerOwnsWeapon(WeaponType weaponType)
        {
            if (cachedGameSession == null)
            {
                return false;
            }

            try
            {
                var activeCharProp = cachedGameSession.GetType().GetProperty("ActiveCharacter", BindingFlags.Public | BindingFlags.Instance);
                if (activeCharProp == null) return false;

                var activeChar = activeCharProp.GetValue(cachedGameSession);
                if (activeChar == null) return false;

                var weaponsManagerProp = activeChar.GetType().GetProperty("WeaponsManager", BindingFlags.Public | BindingFlags.Instance);
                if (weaponsManagerProp == null) return false;

                var weaponsManager = weaponsManagerProp.GetValue(activeChar);
                if (weaponsManager == null) return false;

                var activeEquipProp = weaponsManager.GetType().GetProperty("ActiveEquipment", BindingFlags.Public | BindingFlags.Instance);
                if (activeEquipProp == null) return false;

                var equipment = activeEquipProp.GetValue(weaponsManager);
                if (equipment == null) return false;

                // Iterate through equipment list
                var countProp = equipment.GetType().GetProperty("Count");
                var indexer = equipment.GetType().GetProperty("Item");
                if (countProp == null || indexer == null) return false;

                int count = (int)countProp.GetValue(equipment);
                string searchStr = weaponType.ToString();

                for (int i = 0; i < count; i++)
                {
                    var item = indexer.GetValue(equipment, new object[] { i });
                    if (item == null) continue;

                    var typeProp = item.GetType().GetProperty("Type", BindingFlags.Public | BindingFlags.Instance);
                    if (typeProp != null)
                    {
                        var itemType = typeProp.GetValue(item);
                        if (itemType != null)
                        {
                            string itemTypeStr = itemType.ToString();
                            if (itemTypeStr == searchStr)
                            {
                                return true;
                            }
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[PlayerOwnsWeapon] Error checking {weaponType}: {ex.Message}");
            }

            return false;
        }

        // Check if player owns a passive item
        private static bool PlayerOwnsItem(ItemType itemType)
        {
            if (cachedGameSession == null) return false;

            try
            {
                var activeCharProp = cachedGameSession.GetType().GetProperty("ActiveCharacter", BindingFlags.Public | BindingFlags.Instance);
                if (activeCharProp == null) return false;

                var activeChar = activeCharProp.GetValue(cachedGameSession);
                if (activeChar == null) return false;

                var accessoriesManagerProp = activeChar.GetType().GetProperty("AccessoriesManager", BindingFlags.Public | BindingFlags.Instance);
                if (accessoriesManagerProp == null) return false;

                var accessoriesManager = accessoriesManagerProp.GetValue(activeChar);
                if (accessoriesManager == null) return false;

                var activeEquipProp = accessoriesManager.GetType().GetProperty("ActiveEquipment", BindingFlags.Public | BindingFlags.Instance);
                if (activeEquipProp == null) return false;

                var equipment = activeEquipProp.GetValue(accessoriesManager);
                if (equipment == null) return false;

                // Iterate through equipment list
                var countProp = equipment.GetType().GetProperty("Count");
                var indexer = equipment.GetType().GetProperty("Item");
                if (countProp == null || indexer == null) return false;

                int count = (int)countProp.GetValue(equipment);
                for (int i = 0; i < count; i++)
                {
                    var item = indexer.GetValue(equipment, new object[] { i });
                    if (item == null) continue;

                    var typeProp = item.GetType().GetProperty("Type", BindingFlags.Public | BindingFlags.Instance);
                    if (typeProp != null)
                    {
                        var equipType = typeProp.GetValue(item);
                        if (equipType != null && equipType.ToString() == itemType.ToString())
                            return true;
                    }
                }
            }
            catch { }

            return false;
        }

        // Create an icon with optional yellow circle background for owned items
        private static UnityEngine.GameObject CreateFormulaIcon(UnityEngine.Transform parent, string name,
            UnityEngine.Sprite sprite, bool isOwned, float size, float x, float y)
        {
            var container = new UnityEngine.GameObject(name);
            container.transform.SetParent(parent, false);
            var containerRect = container.AddComponent<UnityEngine.RectTransform>();
            containerRect.anchorMin = new UnityEngine.Vector2(0f, 1f);
            containerRect.anchorMax = new UnityEngine.Vector2(0f, 1f);
            containerRect.pivot = new UnityEngine.Vector2(0f, 1f);
            containerRect.anchoredPosition = new UnityEngine.Vector2(x, y);
            containerRect.sizeDelta = new UnityEngine.Vector2(size, size);

            // Add transparent image for raycast detection (needed for hover)
            var containerImage = container.AddComponent<UnityEngine.UI.Image>();
            containerImage.color = new UnityEngine.Color(0f, 0f, 0f, 0f); // Fully transparent
            containerImage.raycastTarget = true;

            // Yellow circle background for owned items
            if (isOwned)
            {
                var bgObj = new UnityEngine.GameObject("OwnedBg");
                bgObj.transform.SetParent(container.transform, false);
                var bgImage = bgObj.AddComponent<UnityEngine.UI.Image>();
                bgImage.sprite = GetCircleSprite();
                bgImage.color = new UnityEngine.Color(1f, 0.85f, 0f, 0.7f); // Golden yellow
                bgImage.raycastTarget = false;
                var bgRect = bgObj.GetComponent<UnityEngine.RectTransform>();
                bgRect.anchorMin = UnityEngine.Vector2.zero;
                bgRect.anchorMax = UnityEngine.Vector2.one;
                bgRect.offsetMin = new UnityEngine.Vector2(-4f, -4f);
                bgRect.offsetMax = new UnityEngine.Vector2(4f, 4f);
            }

            // Icon sprite
            if (sprite != null)
            {
                var iconObj = new UnityEngine.GameObject("Icon");
                iconObj.transform.SetParent(container.transform, false);
                var iconImage = iconObj.AddComponent<UnityEngine.UI.Image>();
                iconImage.sprite = sprite;
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
                var iconRect = iconObj.GetComponent<UnityEngine.RectTransform>();
                iconRect.anchorMin = UnityEngine.Vector2.zero;
                iconRect.anchorMax = UnityEngine.Vector2.one;
                iconRect.offsetMin = UnityEngine.Vector2.zero;
                iconRect.offsetMax = UnityEngine.Vector2.zero;
            }

            return container;
        }

        /// <summary>
        /// Counts how many weapons use this type as a passive requirement in their evoSynergy.
        /// </summary>
        private static int CountPassiveUses(WeaponType passiveType)
        {
            if (cachedWeaponsDict == null) return 0;

            int count = 0;
            string passiveTypeStr = passiveType.ToString();

            try
            {
                var keysProperty = cachedWeaponsDict.GetType().GetProperty("Keys");
                if (keysProperty == null) return 0;

                var keys = keysProperty.GetValue(cachedWeaponsDict);
                var enumerator = keys.GetType().GetMethod("GetEnumerator").Invoke(keys, null);
                var moveNext = enumerator.GetType().GetMethod("MoveNext");
                var current = enumerator.GetType().GetProperty("Current");

                while ((bool)moveNext.Invoke(enumerator, null))
                {
                    var weaponType = (WeaponType)current.GetValue(enumerator);
                    if (weaponType == passiveType) continue; // Skip self

                    var weaponDataList = GetWeaponDataList(weaponType);
                    if (weaponDataList == null) continue;

                    for (int i = 0; i < weaponDataList.Count; i++)
                    {
                        var weaponData = weaponDataList[i];
                        if (weaponData == null) continue;

                        try
                        {
                            var synergyProp = weaponData.GetType().GetProperty("evoSynergy");
                            if (synergyProp == null) continue;

                            var synergy = synergyProp.GetValue(weaponData) as Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<WeaponType>;
                            if (synergy == null || synergy.Length == 0) continue;

                            string evoInto = GetPropertyValue<string>(weaponData, "evoInto");
                            if (string.IsNullOrEmpty(evoInto)) continue;

                            for (int j = 0; j < synergy.Length; j++)
                            {
                                if (synergy[j].ToString() == passiveTypeStr)
                                {
                                    count++;
                                    break; // Count each weapon only once
                                }
                            }
                        }
                        catch { }

                        break; // Only check first level of each weapon
                    }
                }
            }
            catch { }

            return count;
        }

        private static float AddWeaponEvolutionSection(UnityEngine.Transform parent, Il2CppTMPro.TMP_FontAsset font, WeaponType weaponType, float yOffset, float maxWidth)
        {
            var data = GetWeaponData(weaponType);
            if (data == null) return yOffset;

            // Check if this weapon has evolution info
            string evoInto = null;
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<WeaponType> evoSynergy = null;

            try
            {
                evoInto = GetPropertyValue<string>(data, "evoInto");
                var synergyProp = data.GetType().GetProperty("evoSynergy");
                if (synergyProp != null)
                {
                    evoSynergy = synergyProp.GetValue(data) as Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<WeaponType>;
                }
            }
            catch { }

            bool hasOwnEvolution = !string.IsNullOrEmpty(evoInto);


            // Check if this item is used as a passive requirement by OTHER weapons
            // This handles items like Empty Tome, Spinach, etc. that enable multiple evolutions
            // We do this REGARDLESS of whether the item has its own evoInto
            int passiveUseCount = CountPassiveUses(weaponType);

            if (passiveUseCount > 0)
            {
                // This is a passive item - show all evolutions it enables
                return AddPassiveEvolutionSection(parent, font, weaponType, yOffset, maxWidth);
            }

            // If this weapon doesn't evolve itself and isn't used as a passive, nothing to show
            if (!hasOwnEvolution)
            {
                return yOffset;
            }

            // Parse evolution data
            WeaponType? evoType = null;
            UnityEngine.Sprite evoSprite = null;
            if (System.Enum.TryParse<WeaponType>(evoInto, out var parsed))
            {
                evoType = parsed;
                evoSprite = GetSpriteForWeapon(parsed);
            }

            // Get this weapon's sprite and ownership
            var weaponSprite = GetSpriteForWeapon(weaponType);
            bool ownsWeapon = PlayerOwnsWeapon(weaponType);

            // Collect ALL passive requirements from evoSynergy
            var passiveRequirements = CollectPassiveRequirements(evoSynergy);

            // Add section header
            yOffset -= Spacing;
            var headerObj = CreateTextElement(parent, "EvoHeader", "Evolutions: (click for details)", font, 14f,
                new UnityEngine.Color(0.9f, 0.75f, 0.3f, 1f), Il2CppTMPro.FontStyles.Bold);
            var headerRect = headerObj.GetComponent<UnityEngine.RectTransform>();
            headerRect.anchorMin = new UnityEngine.Vector2(0f, 1f);
            headerRect.anchorMax = new UnityEngine.Vector2(1f, 1f);
            headerRect.pivot = new UnityEngine.Vector2(0f, 1f);
            headerRect.anchoredPosition = new UnityEngine.Vector2(Padding, yOffset);
            headerRect.sizeDelta = new UnityEngine.Vector2(maxWidth - Padding * 2, 20f);
            yOffset -= 22f;

            // Create formula row: + [Passive1] + [Passive2] → [Evolved]
            // The base weapon is already shown in the header
            float iconSize = 38f;
            float rowHeight = iconSize + 8f;
            float xOffset = Padding + 5f;

            // Render each passive requirement with a plus sign
            for (int i = 0; i < passiveRequirements.Count; i++)
            {
                var passive = passiveRequirements[i];

                // Plus sign
                var plusObj = CreateTextElement(parent, $"Plus{i}", "+", font, 18f,
                    new UnityEngine.Color(0.8f, 0.8f, 0.8f, 1f), Il2CppTMPro.FontStyles.Bold);
                var plusRect = plusObj.GetComponent<UnityEngine.RectTransform>();
                plusRect.anchorMin = new UnityEngine.Vector2(0f, 1f);
                plusRect.anchorMax = new UnityEngine.Vector2(0f, 1f);
                plusRect.pivot = new UnityEngine.Vector2(0f, 1f);
                plusRect.anchoredPosition = new UnityEngine.Vector2(xOffset, yOffset - 8f);
                plusRect.sizeDelta = new UnityEngine.Vector2(20f, iconSize);
                xOffset += 22f;

                // Passive icon (with ownership highlight)
                var passiveIcon = CreateFormulaIcon(parent, $"PassiveIcon{i}", passive.Sprite, passive.Owned, iconSize, xOffset, yOffset);
                if (passive.WeaponType.HasValue)
                    AddHoverToGameObject(passiveIcon, passive.WeaponType.Value, null, useClick: true);
                else if (passive.ItemType.HasValue)
                    AddHoverToGameObject(passiveIcon, null, passive.ItemType.Value, useClick: true);
                xOffset += iconSize + 4f;
            }

            // Arrow
            var arrowObj = CreateTextElement(parent, "Arrow", "→", font, 18f,
                new UnityEngine.Color(0.8f, 0.8f, 0.8f, 1f), Il2CppTMPro.FontStyles.Normal);
            var arrowRect = arrowObj.GetComponent<UnityEngine.RectTransform>();
            arrowRect.anchorMin = new UnityEngine.Vector2(0f, 1f);
            arrowRect.anchorMax = new UnityEngine.Vector2(0f, 1f);
            arrowRect.pivot = new UnityEngine.Vector2(0f, 1f);
            arrowRect.anchoredPosition = new UnityEngine.Vector2(xOffset, yOffset - 8f);
            arrowRect.sizeDelta = new UnityEngine.Vector2(24f, iconSize);
            xOffset += 26f;

            // Evolved weapon icon (no highlight - only ingredients get highlighted)
            var evoIcon = CreateFormulaIcon(parent, "EvoIcon", evoSprite, false, iconSize, xOffset, yOffset);
            if (evoType.HasValue)
                AddHoverToGameObject(evoIcon, evoType.Value, null, useClick: true);

            yOffset -= rowHeight;

            return yOffset;
        }

        /// <summary>
        /// Shows evolutions for passive items (items that don't evolve themselves but enable other weapons to evolve).
        /// This handles WeaponType values like DURATION (Spellbinder), MAGNET (Attractorb), etc.
        /// </summary>
        private static float AddPassiveEvolutionSection(UnityEngine.Transform parent, Il2CppTMPro.TMP_FontAsset font, WeaponType passiveType, float yOffset, float maxWidth)
        {
            if (cachedWeaponsDict == null) return yOffset;


            var formulas = new System.Collections.Generic.List<EvolutionFormula>();
            string passiveTypeStr = passiveType.ToString();

            try
            {
                var keysProperty = cachedWeaponsDict.GetType().GetProperty("Keys");
                if (keysProperty == null) return yOffset;

                var keys = keysProperty.GetValue(cachedWeaponsDict);
                var enumerator = keys.GetType().GetMethod("GetEnumerator").Invoke(keys, null);
                var moveNext = enumerator.GetType().GetMethod("MoveNext");
                var current = enumerator.GetType().GetProperty("Current");

                while ((bool)moveNext.Invoke(enumerator, null))
                {
                    var weaponType = (WeaponType)current.GetValue(enumerator);
                    var weaponDataList = GetWeaponDataList(weaponType);
                    if (weaponDataList == null) continue;

                    for (int i = 0; i < weaponDataList.Count; i++)
                    {
                        var weaponData = weaponDataList[i];
                        if (weaponData == null) continue;

                        try
                        {
                            var synergyProp = weaponData.GetType().GetProperty("evoSynergy");
                            if (synergyProp == null) continue;

                            var synergy = synergyProp.GetValue(weaponData) as Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<WeaponType>;
                            if (synergy == null || synergy.Length == 0) continue;

                            string evoInto = GetPropertyValue<string>(weaponData, "evoInto");
                            if (string.IsNullOrEmpty(evoInto)) continue;

                            // Check if this passive type is in the synergy list
                            bool found = false;
                            for (int j = 0; j < synergy.Length; j++)
                            {
                                if (synergy[j].ToString() == passiveTypeStr)
                                {
                                    found = true;
                                    break;
                                }
                            }

                            if (found)
                            {
                                if (System.Enum.TryParse<WeaponType>(evoInto, out var evoType))
                                {

                                    var formula = new EvolutionFormula
                                    {
                                        BaseWeapon = weaponType,
                                        Passives = CollectPassiveRequirements(synergy),
                                        EvolvedWeapon = evoType,
                                        BaseName = GetPropertyValue<string>(weaponData, "name") ?? weaponType.ToString(),
                                        BaseSprite = GetSpriteForWeapon(weaponType),
                                        EvolvedSprite = GetSpriteForWeapon(evoType)
                                    };

                                    var evoData = GetWeaponData(evoType);
                                    if (evoData != null)
                                        formula.EvolvedName = GetPropertyValue<string>(evoData, "name") ?? evoInto;
                                    else
                                        formula.EvolvedName = evoInto;

                                    // Avoid duplicates - dedup by EvolvedWeapon since recipes like
                                    // LEFT+LAUREL+RIGHT→SHROUD and RIGHT+LAUREL+LEFT→SHROUD are the same evolution
                                    bool isDupe = false;
                                    foreach (var f in formulas)
                                    {
                                        if (f.EvolvedWeapon == formula.EvolvedWeapon)
                                        {
                                            isDupe = true;
                                            break;
                                        }
                                    }
                                    if (!isDupe) formulas.Add(formula);
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[AddPassiveEvo] Error: {ex.Message}");
            }


            if (formulas.Count == 0) return yOffset;

            // Check if player owns this passive
            bool ownsPassive = PlayerOwnsWeapon(passiveType);

            // Add section header
            yOffset -= Spacing;
            var headerObj = CreateTextElement(parent, "EvoHeader", "Evolutions: (click for details)", font, 14f,
                new UnityEngine.Color(0.9f, 0.75f, 0.3f, 1f), Il2CppTMPro.FontStyles.Bold);
            var headerRect = headerObj.GetComponent<UnityEngine.RectTransform>();
            headerRect.anchorMin = new UnityEngine.Vector2(0f, 1f);
            headerRect.anchorMax = new UnityEngine.Vector2(1f, 1f);
            headerRect.pivot = new UnityEngine.Vector2(0f, 1f);
            headerRect.anchoredPosition = new UnityEngine.Vector2(Padding, yOffset);
            headerRect.sizeDelta = new UnityEngine.Vector2(maxWidth - Padding * 2, 20f);
            yOffset -= 22f;

            // Create formula rows: [Weapon] + [Other Passives] → [Evolved]
            float iconSize = 36f;
            float rowHeight = iconSize + 4f;
            int formulaIndex = 0;

            foreach (var formula in formulas)
            {
                float xOffset = Padding + 5f;
                bool ownsWeapon = PlayerOwnsWeapon(formula.BaseWeapon);

                // Base weapon icon
                var weaponIcon = CreateFormulaIcon(parent, $"Weapon{formulaIndex}", formula.BaseSprite, ownsWeapon, iconSize, xOffset, yOffset);
                AddHoverToGameObject(weaponIcon, formula.BaseWeapon, null, useClick: true);
                xOffset += iconSize + 3f;

                // Show ALL passive requirements (including this one)
                if (formula.Passives != null)
                {
                    for (int p = 0; p < formula.Passives.Count; p++)
                    {
                        var passive = formula.Passives[p];

                        // Plus sign
                        var plusObj = CreateTextElement(parent, $"Plus{formulaIndex}_{p}", "+", font, 14f,
                            new UnityEngine.Color(0.8f, 0.8f, 0.8f, 1f), Il2CppTMPro.FontStyles.Bold);
                        var plusRect = plusObj.GetComponent<UnityEngine.RectTransform>();
                        plusRect.anchorMin = new UnityEngine.Vector2(0f, 1f);
                        plusRect.anchorMax = new UnityEngine.Vector2(0f, 1f);
                        plusRect.pivot = new UnityEngine.Vector2(0f, 1f);
                        plusRect.anchoredPosition = new UnityEngine.Vector2(xOffset, yOffset - 4f);
                        plusRect.sizeDelta = new UnityEngine.Vector2(14f, iconSize);
                        xOffset += 14f;

                        // Passive icon
                        var passiveIcon = CreateFormulaIcon(parent, $"Passive{formulaIndex}_{p}", passive.Sprite, passive.Owned, iconSize, xOffset, yOffset);
                        if (passive.WeaponType.HasValue)
                            AddHoverToGameObject(passiveIcon, passive.WeaponType.Value, null, useClick: true);
                        else if (passive.ItemType.HasValue)
                            AddHoverToGameObject(passiveIcon, null, passive.ItemType.Value, useClick: true);
                        xOffset += iconSize + 3f;
                    }
                }

                // Arrow
                var arrowObj = CreateTextElement(parent, $"Arrow{formulaIndex}", "→", font, 14f,
                    new UnityEngine.Color(0.8f, 0.8f, 0.8f, 1f), Il2CppTMPro.FontStyles.Normal);
                var arrowRect = arrowObj.GetComponent<UnityEngine.RectTransform>();
                arrowRect.anchorMin = new UnityEngine.Vector2(0f, 1f);
                arrowRect.anchorMax = new UnityEngine.Vector2(0f, 1f);
                arrowRect.pivot = new UnityEngine.Vector2(0f, 1f);
                arrowRect.anchoredPosition = new UnityEngine.Vector2(xOffset, yOffset - 4f);
                arrowRect.sizeDelta = new UnityEngine.Vector2(20f, iconSize);
                xOffset += 20f;

                // Evolved weapon icon
                var evoIcon = CreateFormulaIcon(parent, $"Evo{formulaIndex}", formula.EvolvedSprite, false, iconSize, xOffset, yOffset);
                AddHoverToGameObject(evoIcon, formula.EvolvedWeapon, null, useClick: true);

                yOffset -= rowHeight;
                formulaIndex++;
            }

            return yOffset;
        }

        private static float AddItemEvolutionSection(UnityEngine.Transform parent, Il2CppTMPro.TMP_FontAsset font, ItemType itemType, float yOffset, float maxWidth)
        {
            // For items/powerups, find ALL weapons that use this item to evolve
            if (cachedWeaponsDict == null)
            {
                return yOffset;
            }


            var formulas = new System.Collections.Generic.List<EvolutionFormula>();
            int weaponsChecked = 0;
            int matchesFound = 0;

            try
            {
                var keysProperty = cachedWeaponsDict.GetType().GetProperty("Keys");
                if (keysProperty == null) return yOffset;

                var keys = keysProperty.GetValue(cachedWeaponsDict);
                var enumerator = keys.GetType().GetMethod("GetEnumerator").Invoke(keys, null);
                var moveNext = enumerator.GetType().GetMethod("MoveNext");
                var current = enumerator.GetType().GetProperty("Current");

                while ((bool)moveNext.Invoke(enumerator, null))
                {
                    weaponsChecked++;
                    var weaponType = (WeaponType)current.GetValue(enumerator);
                    var weaponDataList = GetWeaponDataList(weaponType);
                    if (weaponDataList == null) continue;

                    for (int i = 0; i < weaponDataList.Count; i++)
                    {
                        var weaponData = weaponDataList[i];
                        if (weaponData == null) continue;

                        try
                        {
                            var synergyProp = weaponData.GetType().GetProperty("evoSynergy");
                            if (synergyProp == null) continue;

                            var synergy = synergyProp.GetValue(weaponData) as Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<WeaponType>;
                            if (synergy == null || synergy.Length == 0) continue;

                            // Log what this weapon's synergy contains
                            string evoIntoCheck = GetPropertyValue<string>(weaponData, "evoInto");
                            if (!string.IsNullOrEmpty(evoIntoCheck))
                            {
                                string synergyContents = "";
                                for (int s = 0; s < synergy.Length; s++)
                                {
                                    synergyContents += synergy[s].ToString() + " ";
                                }
                            }

                            // Check if this item type is in the synergy list
                            string itemTypeStr = itemType.ToString();
                            bool found = false;
                            for (int j = 0; j < synergy.Length; j++)
                            {
                                if (synergy[j].ToString() == itemTypeStr)
                                {
                                    found = true;
                                    break;
                                }
                            }

                            if (found)
                            {
                                matchesFound++;
                                string evoInto = GetPropertyValue<string>(weaponData, "evoInto");
                                if (string.IsNullOrEmpty(evoInto)) continue;

                                if (System.Enum.TryParse<WeaponType>(evoInto, out var evoType))
                                {

                                    var formula = new EvolutionFormula
                                    {
                                        BaseWeapon = weaponType,
                                        Passives = CollectPassiveRequirements(synergy),
                                        EvolvedWeapon = evoType,
                                        BaseName = GetPropertyValue<string>(weaponData, "name") ?? weaponType.ToString(),
                                        BaseSprite = GetSpriteForWeapon(weaponType),
                                        EvolvedSprite = GetSpriteForWeapon(evoType)
                                    };

                                    var evoData = GetWeaponData(evoType);
                                    if (evoData != null)
                                        formula.EvolvedName = GetPropertyValue<string>(evoData, "name") ?? evoInto;
                                    else
                                        formula.EvolvedName = evoInto;

                                    // Avoid duplicates - dedup by EvolvedWeapon since multi-passive
                                    // recipes appear once per base weapon but are the same evolution
                                    bool isDupe = false;
                                    foreach (var f in formulas)
                                    {
                                        if (f.EvolvedWeapon == formula.EvolvedWeapon)
                                        {
                                            isDupe = true;
                                            break;
                                        }
                                    }
                                    if (!isDupe) formulas.Add(formula);
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[AddItemEvo] Error: {ex.Message}");
            }


            if (formulas.Count == 0) return yOffset;

            // Add section header
            yOffset -= Spacing;
            var headerObj = CreateTextElement(parent, "EvoHeader", "Evolutions: (click for details)", font, 14f,
                new UnityEngine.Color(0.9f, 0.75f, 0.3f, 1f), Il2CppTMPro.FontStyles.Bold);
            var headerRect = headerObj.GetComponent<UnityEngine.RectTransform>();
            headerRect.anchorMin = new UnityEngine.Vector2(0f, 1f);
            headerRect.anchorMax = new UnityEngine.Vector2(1f, 1f);
            headerRect.pivot = new UnityEngine.Vector2(0f, 1f);
            headerRect.anchoredPosition = new UnityEngine.Vector2(Padding, yOffset);
            headerRect.sizeDelta = new UnityEngine.Vector2(maxWidth - Padding * 2, 20f);
            yOffset -= 22f;

            // Create formula rows: [Weapon] + [All Passives] → [Evolved]
            float iconSize = 36f;
            float rowHeight = iconSize + 4f;
            int formulaIndex = 0;

            foreach (var formula in formulas)
            {
                float xOffset = Padding + 5f;
                bool ownsWeapon = PlayerOwnsWeapon(formula.BaseWeapon);

                // Base weapon icon
                var weaponIcon = CreateFormulaIcon(parent, $"Weapon{formulaIndex}", formula.BaseSprite, ownsWeapon, iconSize, xOffset, yOffset);
                AddHoverToGameObject(weaponIcon, formula.BaseWeapon, null, useClick: true);
                xOffset += iconSize + 3f;

                // Show ALL passive requirements
                if (formula.Passives != null)
                {
                    for (int p = 0; p < formula.Passives.Count; p++)
                    {
                        var passive = formula.Passives[p];

                        // Plus sign
                        var plusObj = CreateTextElement(parent, $"Plus{formulaIndex}_{p}", "+", font, 14f,
                            new UnityEngine.Color(0.8f, 0.8f, 0.8f, 1f), Il2CppTMPro.FontStyles.Bold);
                        var plusRect = plusObj.GetComponent<UnityEngine.RectTransform>();
                        plusRect.anchorMin = new UnityEngine.Vector2(0f, 1f);
                        plusRect.anchorMax = new UnityEngine.Vector2(0f, 1f);
                        plusRect.pivot = new UnityEngine.Vector2(0f, 1f);
                        plusRect.anchoredPosition = new UnityEngine.Vector2(xOffset, yOffset - 4f);
                        plusRect.sizeDelta = new UnityEngine.Vector2(14f, iconSize);
                        xOffset += 14f;

                        // Passive icon
                        var passiveIcon = CreateFormulaIcon(parent, $"Passive{formulaIndex}_{p}", passive.Sprite, passive.Owned, iconSize, xOffset, yOffset);
                        if (passive.WeaponType.HasValue)
                            AddHoverToGameObject(passiveIcon, passive.WeaponType.Value, null, useClick: true);
                        else if (passive.ItemType.HasValue)
                            AddHoverToGameObject(passiveIcon, null, passive.ItemType.Value, useClick: true);
                        xOffset += iconSize + 3f;
                    }
                }

                // Arrow
                var arrowObj = CreateTextElement(parent, $"Arrow{formulaIndex}", "→", font, 14f,
                    new UnityEngine.Color(0.8f, 0.8f, 0.8f, 1f), Il2CppTMPro.FontStyles.Normal);
                var arrowRect = arrowObj.GetComponent<UnityEngine.RectTransform>();
                arrowRect.anchorMin = new UnityEngine.Vector2(0f, 1f);
                arrowRect.anchorMax = new UnityEngine.Vector2(0f, 1f);
                arrowRect.pivot = new UnityEngine.Vector2(0f, 1f);
                arrowRect.anchoredPosition = new UnityEngine.Vector2(xOffset, yOffset - 4f);
                arrowRect.sizeDelta = new UnityEngine.Vector2(20f, iconSize);
                xOffset += 20f;

                // Evolved weapon icon
                var evoIcon = CreateFormulaIcon(parent, $"Evo{formulaIndex}", formula.EvolvedSprite, false, iconSize, xOffset, yOffset);
                AddHoverToGameObject(evoIcon, formula.EvolvedWeapon, null, useClick: true);
                xOffset += iconSize + 6f;

                // Evolution name
                var nameObj = CreateTextElement(parent, $"EvoName{formulaIndex}", formula.EvolvedName, font, 11f,
                    new UnityEngine.Color(0.75f, 0.75f, 0.8f, 1f), Il2CppTMPro.FontStyles.Normal);
                var nameRect = nameObj.GetComponent<UnityEngine.RectTransform>();
                nameRect.anchorMin = new UnityEngine.Vector2(0f, 1f);
                nameRect.anchorMax = new UnityEngine.Vector2(1f, 1f);
                nameRect.pivot = new UnityEngine.Vector2(0f, 1f);
                nameRect.anchoredPosition = new UnityEngine.Vector2(xOffset, yOffset - 5f);
                nameRect.sizeDelta = new UnityEngine.Vector2(maxWidth - xOffset - Padding, 16f);

                yOffset -= rowHeight;
                formulaIndex++;
            }

            return yOffset;
        }

        private static float AddArcanaSection(UnityEngine.Transform parent, Il2CppTMPro.TMP_FontAsset font,
            List<ArcanaInfo> arcanas, float yOffset, float maxWidth)
        {
            if (arcanas == null || arcanas.Count == 0) return yOffset;

            // Section header
            yOffset -= Spacing;
            var headerObj = CreateTextElement(parent, "ArcanaHeader", "Arcana: (click for details)", font, 14f,
                new UnityEngine.Color(0.7f, 0.5f, 0.9f, 1f), Il2CppTMPro.FontStyles.Bold);
            var headerRect = headerObj.GetComponent<UnityEngine.RectTransform>();
            headerRect.anchorMin = new UnityEngine.Vector2(0f, 1f);
            headerRect.anchorMax = new UnityEngine.Vector2(1f, 1f);
            headerRect.pivot = new UnityEngine.Vector2(0f, 1f);
            headerRect.anchoredPosition = new UnityEngine.Vector2(Padding, yOffset);
            headerRect.sizeDelta = new UnityEngine.Vector2(maxWidth - Padding * 2, 20f);
            yOffset -= 26f;

            // Display arcana icons
            float iconSize = 52f;
            float xOffset = Padding;

            for (int i = 0; i < arcanas.Count; i++)
            {
                var arcana = arcanas[i];
                var arcanaIcon = CreateFormulaIcon(parent, $"ArcanaIcon{i}", arcana.Sprite, false, iconSize, xOffset, yOffset);
                AddArcanaHoverToGameObject(arcanaIcon, arcana.ArcanaData);

                // Add arcana name next to icon, vertically centered
                var nameObj = CreateTextElement(parent, $"ArcanaName{i}", arcana.Name, font, 13f,
                    new UnityEngine.Color(0.8f, 0.7f, 0.95f, 1f), Il2CppTMPro.FontStyles.Normal);
                var nameRect = nameObj.GetComponent<UnityEngine.RectTransform>();
                nameRect.anchorMin = new UnityEngine.Vector2(0f, 1f);
                nameRect.anchorMax = new UnityEngine.Vector2(0f, 1f);
                nameRect.pivot = new UnityEngine.Vector2(0f, 1f);
                nameRect.anchoredPosition = new UnityEngine.Vector2(xOffset + iconSize + 8f, yOffset - (iconSize / 2f - 8f));
                nameRect.sizeDelta = new UnityEngine.Vector2(maxWidth - xOffset - iconSize - Padding - 8f, 20f);

                yOffset -= iconSize + 8f;
            }

            return yOffset;
        }

        private static void ShowArcanaPopup(UnityEngine.Transform anchor, object arcanaData)
        {
            if (!IsGamePaused())
            {
                HideAllPopups();
                return;
            }

            int anchorId = anchor?.GetInstanceID() ?? 0;

            // Check if this anchor already has a popup in the stack
            for (int i = 0; i < popupAnchorIds.Count; i++)
            {
                if (popupAnchorIds[i] == anchorId) return;
            }

            // Check if anchor is inside an existing popup (for recursive popups)
            int parentPopupIndex = -1;
            for (int i = 0; i < popupStack.Count; i++)
            {
                if (popupStack[i] != null && anchor != null && anchor.IsChildOf(popupStack[i].transform))
                {
                    parentPopupIndex = i;
                }
            }

            if (parentPopupIndex >= 0)
            {
                while (popupStack.Count > parentPopupIndex + 1)
                {
                    HideTopPopup();
                }
            }
            else if (popupStack.Count > 0)
            {
                HideAllPopups();
            }

            var popupParent = FindPopupParent(anchor);
            if (popupParent == null) return;

            var newPopup = CreateArcanaPopup(popupParent, arcanaData);
            if (newPopup == null) return;

            popupStack.Add(newPopup);
            popupAnchorIds.Add(anchorId);

            PositionPopup(newPopup, anchor);
            AddPopupHoverTracking(newPopup);
        }

        private static UnityEngine.GameObject CreateArcanaPopup(UnityEngine.Transform parent, object arcanaData)
        {
            if (arcanaData == null) return null;

            var popup = new UnityEngine.GameObject("ArcanaTooltipPopup");
            popup.transform.SetParent(parent, false);

            var rect = popup.AddComponent<UnityEngine.RectTransform>();
            rect.anchorMin = new UnityEngine.Vector2(0.5f, 0.5f);
            rect.anchorMax = new UnityEngine.Vector2(0.5f, 0.5f);
            rect.pivot = new UnityEngine.Vector2(0f, 1f);

            var bg = popup.AddComponent<UnityEngine.UI.Image>();
            bg.color = PopupBgColor;
            bg.raycastTarget = true;

            var outline = popup.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = new UnityEngine.Color(0.6f, 0.4f, 0.8f, 1f); // Purple border for arcana
            outline.effectDistance = new UnityEngine.Vector2(2f, 2f);

            float yOffset = -Padding;
            float maxWidth = 420f;

            // Get arcana info
            var nameProp = arcanaData.GetType().GetProperty("name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var descProp = arcanaData.GetType().GetProperty("description", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var frameProp = arcanaData.GetType().GetProperty("frameName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var textureProp = arcanaData.GetType().GetProperty("texture", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            string arcanaName = nameProp?.GetValue(arcanaData)?.ToString() ?? "Unknown Arcana";
            string arcanaDesc = descProp?.GetValue(arcanaData)?.ToString() ?? "";
            string frameName = frameProp?.GetValue(arcanaData)?.ToString() ?? "";
            string textureName = textureProp?.GetValue(arcanaData)?.ToString() ?? "";

            var arcanaSprite = LoadArcanaSprite(textureName, frameName);
            var font = GetFont();

            if (font != null)
            {
                // Title row: [Icon] Name
                var titleRow = new UnityEngine.GameObject("TitleRow");
                titleRow.transform.SetParent(popup.transform, false);
                var titleRect = titleRow.AddComponent<UnityEngine.RectTransform>();
                titleRect.anchorMin = new UnityEngine.Vector2(0f, 1f);
                titleRect.anchorMax = new UnityEngine.Vector2(0f, 1f);
                titleRect.pivot = new UnityEngine.Vector2(0f, 1f);
                titleRect.anchoredPosition = new UnityEngine.Vector2(Padding, yOffset);
                titleRect.sizeDelta = new UnityEngine.Vector2(maxWidth - Padding * 2, IconSize);

                var titleText = new UnityEngine.GameObject("Title");
                titleText.transform.SetParent(titleRow.transform, false);
                var titleTextRect = titleText.AddComponent<UnityEngine.RectTransform>();
                titleTextRect.anchorMin = UnityEngine.Vector2.zero;
                titleTextRect.anchorMax = UnityEngine.Vector2.one;
                titleTextRect.offsetMin = new UnityEngine.Vector2(IconSize + Spacing, 0f);
                titleTextRect.offsetMax = UnityEngine.Vector2.zero;

                var titleTmp = titleText.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                titleTmp.font = font;
                titleTmp.text = arcanaName;
                titleTmp.fontSize = 20f;
                titleTmp.fontStyle = Il2CppTMPro.FontStyles.Bold;
                titleTmp.color = new UnityEngine.Color(0.8f, 0.7f, 0.95f, 1f); // Purple-ish for arcana
                titleTmp.alignment = Il2CppTMPro.TextAlignmentOptions.Left;
                titleTmp.enableAutoSizing = true;
                titleTmp.fontSizeMin = 12f;
                titleTmp.fontSizeMax = 20f;
                titleTmp.overflowMode = Il2CppTMPro.TextOverflowModes.Ellipsis;

                // Arcana icon
                if (arcanaSprite != null)
                {
                    var iconObj = new UnityEngine.GameObject("ArcanaHeaderIcon");
                    iconObj.transform.SetParent(titleRow.transform, false);
                    var iconRect = iconObj.AddComponent<UnityEngine.RectTransform>();
                    iconRect.anchorMin = new UnityEngine.Vector2(0f, 0.5f);
                    iconRect.anchorMax = new UnityEngine.Vector2(0f, 0.5f);
                    iconRect.pivot = new UnityEngine.Vector2(0f, 0.5f);
                    iconRect.anchoredPosition = new UnityEngine.Vector2(0f, 0f);
                    iconRect.sizeDelta = new UnityEngine.Vector2(IconSize, IconSize);

                    var iconImage = iconObj.AddComponent<UnityEngine.UI.Image>();
                    iconImage.sprite = arcanaSprite;
                    iconImage.preserveAspect = true;
                    iconImage.raycastTarget = false;
                }

                yOffset -= IconSize + Spacing;

                // Description
                if (!string.IsNullOrEmpty(arcanaDesc))
                {
                    var descObj = new UnityEngine.GameObject("ArcanaDescription");
                    descObj.transform.SetParent(popup.transform, false);
                    var descRect = descObj.AddComponent<UnityEngine.RectTransform>();
                    descRect.anchorMin = new UnityEngine.Vector2(0f, 1f);
                    descRect.anchorMax = new UnityEngine.Vector2(0f, 1f);
                    descRect.pivot = new UnityEngine.Vector2(0f, 1f);
                    descRect.anchoredPosition = new UnityEngine.Vector2(Padding, yOffset);
                    float descWidth = maxWidth - Padding * 2;
                    descRect.sizeDelta = new UnityEngine.Vector2(descWidth, 0f);

                    var descTmp = descObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                    descTmp.font = font;
                    descTmp.text = arcanaDesc;
                    descTmp.fontSize = 14f;
                    descTmp.color = new UnityEngine.Color(0.85f, 0.85f, 0.9f, 1f);
                    descTmp.alignment = Il2CppTMPro.TextAlignmentOptions.TopLeft;
                    descTmp.enableWordWrapping = true;
                    descTmp.overflowMode = Il2CppTMPro.TextOverflowModes.Truncate;
                    descTmp.rectTransform.sizeDelta = new UnityEngine.Vector2(descWidth, 0f);

                    var descFitter = descObj.AddComponent<UnityEngine.UI.ContentSizeFitter>();
                    descFitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
                    descFitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

                    descTmp.ForceMeshUpdate();
                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(descRect);

                    float descHeight = descTmp.preferredHeight > 0 ? descTmp.preferredHeight : 40f;
                    descRect.sizeDelta = new UnityEngine.Vector2(descWidth, descHeight);
                    yOffset -= descHeight + Spacing;
                }

                // "Affects:" section with grid of all affected items
                var affectedWeapons = GetAllArcanaAffectedWeaponTypes(arcanaData);
                var affectedItems = GetAllArcanaAffectedItemTypes(arcanaData);
                int totalAffected = affectedWeapons.Count + affectedItems.Count;
                if (totalAffected > 0)
                {
                    yOffset -= Spacing;
                    var affectsHeader = CreateTextElement(popup.transform, "AffectsHeader", "Affects: (click for details)", font, 14f,
                        new UnityEngine.Color(0.7f, 0.5f, 0.9f, 1f), Il2CppTMPro.FontStyles.Bold);
                    var affectsRect = affectsHeader.GetComponent<UnityEngine.RectTransform>();
                    affectsRect.anchorMin = new UnityEngine.Vector2(0f, 1f);
                    affectsRect.anchorMax = new UnityEngine.Vector2(1f, 1f);
                    affectsRect.pivot = new UnityEngine.Vector2(0f, 1f);
                    affectsRect.anchoredPosition = new UnityEngine.Vector2(Padding, yOffset);
                    affectsRect.sizeDelta = new UnityEngine.Vector2(maxWidth - Padding * 2, 20f);
                    yOffset -= 22f;

                    // Grid layout for affected items
                    float iconSize = 38f;
                    float iconSpacing = 6f;
                    float availableWidth = maxWidth - Padding * 2;
                    int iconsPerRow = (int)(availableWidth / (iconSize + iconSpacing));
                    if (iconsPerRow < 1) iconsPerRow = 1;

                    int col = 0;
                    int itemIndex = 0;

                    // Weapons first
                    foreach (var wt in affectedWeapons)
                    {
                        float x = Padding + col * (iconSize + iconSpacing);
                        bool owned = PlayerOwnsWeapon(wt);
                        var sprite = GetSpriteForWeapon(wt);
                        var icon = CreateFormulaIcon(popup.transform, $"AffectedWeapon{itemIndex}", sprite, owned, iconSize, x, yOffset);
                        AddHoverToGameObject(icon, wt, null, useClick: true);

                        col++;
                        if (col >= iconsPerRow)
                        {
                            col = 0;
                            yOffset -= iconSize + iconSpacing;
                        }
                        itemIndex++;
                    }

                    // Then passive items
                    foreach (var it in affectedItems)
                    {
                        float x = Padding + col * (iconSize + iconSpacing);
                        bool owned = PlayerOwnsItem(it);
                        var sprite = GetSpriteForItem(it);
                        var icon = CreateFormulaIcon(popup.transform, $"AffectedItem{itemIndex}", sprite, owned, iconSize, x, yOffset);
                        AddHoverToGameObject(icon, null, it, useClick: true);

                        col++;
                        if (col >= iconsPerRow)
                        {
                            col = 0;
                            yOffset -= iconSize + iconSpacing;
                        }
                        itemIndex++;
                    }

                    // If last row wasn't complete, still account for its height
                    if (col > 0)
                    {
                        yOffset -= iconSize + iconSpacing;
                    }
                }
            }

            // Set final size
            yOffset -= Padding;
            rect.sizeDelta = new UnityEngine.Vector2(maxWidth, -yOffset);

            return popup;
        }

        private static void AddArcanaHoverToGameObject(UnityEngine.GameObject go, object arcanaData)
        {
            var existing = go.GetComponent<UnityEngine.EventSystems.EventTrigger>();
            if (existing != null) return;

            var eventTrigger = go.AddComponent<UnityEngine.EventSystems.EventTrigger>();

            // Capture arcanaData for the closure
            var capturedData = arcanaData;

            // Use click instead of hover for icons inside popups
            var clickEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
            clickEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerClick;
            clickEntry.callback.AddListener((UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData>)((data) =>
            {
                ShowArcanaPopup(go.transform, capturedData);
            }));
            eventTrigger.triggers.Add(clickEntry);
        }

        private static UnityEngine.GameObject CreateTextElement(UnityEngine.Transform parent, string name, string text,
            Il2CppTMPro.TMP_FontAsset font, float fontSize, UnityEngine.Color color, Il2CppTMPro.FontStyles style)
        {
            var obj = new UnityEngine.GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<UnityEngine.RectTransform>();

            var tmp = obj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
            tmp.font = font;
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.fontStyle = style;
            tmp.alignment = Il2CppTMPro.TextAlignmentOptions.Left;

            return obj;
        }

        private static Il2CppSystem.Collections.Generic.List<WeaponData> GetWeaponDataList(WeaponType type)
        {
            if (cachedWeaponsDict == null) return null;
            try
            {
                var indexer = cachedWeaponsDict.GetType().GetProperty("Item");
                if (indexer != null)
                {
                    return indexer.GetValue(cachedWeaponsDict, new object[] { type }) as Il2CppSystem.Collections.Generic.List<WeaponData>;
                }
            }
            catch { }
            return null;
        }

        private static void PositionPopup(UnityEngine.GameObject popup, UnityEngine.Transform anchor)
        {
            var popupRect = popup.GetComponent<UnityEngine.RectTransform>();
            if (popupRect == null) return;

            var popupParent = popup.transform.parent;
            if (popupParent == null) return;

            // Position relative to anchor, but offset so the popup appears
            // with the mouse cursor inside it (near top-left of popup)
            var localPos = popupParent.InverseTransformPoint(anchor.position);

            // Convert from pivot-relative (InverseTransformPoint) to anchor-relative (anchoredPosition)
            // This accounts for parents whose pivot isn't at center
            var parentRect = popupParent.GetComponent<UnityEngine.RectTransform>();
            if (parentRect != null)
            {
                var anchorOffset = parentRect.rect.center;
                localPos.x -= anchorOffset.x;
                localPos.y -= anchorOffset.y;
            }

            // Initial position - offset so mouse ends up inside the popup
            float posX = localPos.x - 15f;
            float posY = localPos.y + 40f;

            // Get popup size
            float popupWidth = popupRect.sizeDelta.x;
            float popupHeight = popupRect.sizeDelta.y;

            // Clamp to parent bounds
            if (parentRect != null)
            {
                float parentWidth = parentRect.rect.width;
                float parentHeight = parentRect.rect.height;

                // Clamp to keep popup within parent bounds
                // Right edge
                if (posX + popupWidth > parentWidth / 2)
                {
                    posX = parentWidth / 2 - popupWidth;
                }
                // Left edge
                if (posX < -parentWidth / 2)
                {
                    posX = -parentWidth / 2;
                }
                // Top edge (remember Y is inverted in UI - popup grows downward)
                if (posY > parentHeight / 2)
                {
                    posY = parentHeight / 2;
                }
                // Bottom edge
                if (posY - popupHeight < -parentHeight / 2)
                {
                    posY = -parentHeight / 2 + popupHeight;
                }

                // Ensure mouse is still inside popup (adjust if clamping moved it too far)
                // Keep at least 20px margin from edges where possible
                float minX = posX + 20f;
                float maxX = posX + popupWidth - 20f;
                float minY = posY - popupHeight + 20f;
                float maxY = posY - 20f;

                // If anchor is outside the popup after clamping, adjust
                if (localPos.x < minX || localPos.x > maxX || localPos.y < minY || localPos.y > maxY)
                {
                    // Try to keep mouse inside by shifting popup
                    if (localPos.x < minX) posX = localPos.x - 20f;
                    if (localPos.x > maxX) posX = localPos.x - popupWidth + 20f;
                }
            }

            popupRect.anchoredPosition = new UnityEngine.Vector2(posX, posY);
        }

        /// <summary>
        /// Finds an appropriate parent for the popup by walking up the hierarchy.
        /// </summary>
        private static UnityEngine.Transform FindPopupParent(UnityEngine.Transform anchor)
        {
            // Known parent names we want to attach popup to
            string[] knownParents = new string[]
            {
                "View - Level Up",
                "View - Merchant",
                "View - Pause",
                "View - Paused",
                "View - ItemFound",
                "View - Game",
                "Safe Area"
            };

            var current = anchor;
            while (current != null)
            {
                foreach (var name in knownParents)
                {
                    if (current.name == name)
                        return current;
                }
                current = current.parent;
            }

            // Fallback to root
            return anchor.root;
        }

        private static void AddPopupHoverTracking(UnityEngine.GameObject popup)
        {
            var eventTrigger = popup.AddComponent<UnityEngine.EventSystems.EventTrigger>();

            // Capture the index when this popup was created
            int thisPopupIndex = popupStack.Count - 1;

            var enterEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
            enterEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter;
            enterEntry.callback.AddListener((UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData>)((data) =>
            {
                mouseOverPopupIndex = thisPopupIndex;
            }));
            eventTrigger.triggers.Add(enterEntry);

            var exitEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
            exitEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit;
            exitEntry.callback.AddListener((UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData>)((data) =>
            {
                // If we're leaving this popup and not entering a child, mark as not over this popup
                if (mouseOverPopupIndex == thisPopupIndex)
                {
                    mouseOverPopupIndex = -1;
                }
                // Start delayed hide check for this popup level
                MelonLoader.MelonCoroutines.Start(DelayedStackHideCheck(thisPopupIndex));
            }));
            eventTrigger.triggers.Add(exitEntry);
        }

        public static void HidePopup()
        {
            HideAllPopups();
        }

        private static void HideAllPopups()
        {
            mouseOverPopupIndex = -1;
            foreach (var popup in popupStack)
            {
                if (popup != null)
                {
                    UnityEngine.Object.Destroy(popup);
                }
            }
            popupStack.Clear();
            popupAnchorIds.Clear();
        }

        private static void HideTopPopup()
        {
            if (popupStack.Count > 0)
            {
                var topPopup = popupStack[popupStack.Count - 1];
                if (topPopup != null)
                {
                    UnityEngine.Object.Destroy(topPopup);
                }
                popupStack.RemoveAt(popupStack.Count - 1);
                popupAnchorIds.RemoveAt(popupAnchorIds.Count - 1);
            }
        }

        #region Collection Screen Hover

        private static float collectionHoverStartTime = 0f;
        private static readonly float CollectionHoverDelay = 1.0f; // 1 second before showing new popup
        private static int pendingCollectionHoverId = -1; // Icon waiting for delay to elapse
        private static WeaponType? pendingCollectionWeapon = null;
        private static ItemType? pendingCollectionItem = null;
        private static object pendingCollectionArcana = null;

        private static void UpdateCollectionHover()
        {
            // Clean up destroyed icons
            var toRemove = new System.Collections.Generic.List<int>();
            foreach (var kvp in collectionIcons)
            {
                if (kvp.Value.go == null)
                    toRemove.Add(kvp.Key);
            }
            foreach (var key in toRemove)
                collectionIcons.Remove(key);

            // Find which icon the mouse is over
            var mousePos = UnityEngine.Input.mousePosition;
            int hoveredId = -1;
            WeaponType? hoveredWeapon = null;
            ItemType? hoveredItem = null;
            object hoveredArcana = null;

            foreach (var kvp in collectionIcons)
            {
                var go = kvp.Value.go;
                if (go == null || !go.activeInHierarchy) continue;

                var rectTransform = go.GetComponent<UnityEngine.RectTransform>();
                if (rectTransform == null) continue;

                if (UnityEngine.RectTransformUtility.RectangleContainsScreenPoint(rectTransform, mousePos, null) ||
                    UnityEngine.RectTransformUtility.RectangleContainsScreenPoint(rectTransform, mousePos, UnityEngine.Camera.main))
                {
                    hoveredId = kvp.Key;
                    hoveredWeapon = kvp.Value.weapon;
                    hoveredItem = kvp.Value.item;
                    hoveredArcana = kvp.Value.arcanaType;
                    break;
                }
            }

            // If hovering a new icon that's different from what's currently showing
            if (hoveredId != -1 && hoveredId != currentCollectionHoverId)
            {
                // Start or reset the pending timer for this new icon
                if (hoveredId != pendingCollectionHoverId)
                {
                    pendingCollectionHoverId = hoveredId;
                    pendingCollectionWeapon = hoveredWeapon;
                    pendingCollectionItem = hoveredItem;
                    pendingCollectionArcana = hoveredArcana;
                    collectionHoverStartTime = UnityEngine.Time.unscaledTime;
                }
                else
                {
                    // Still waiting on the same pending icon - check delay
                    float elapsed = UnityEngine.Time.unscaledTime - collectionHoverStartTime;
                    if (elapsed >= CollectionHoverDelay)
                    {
                        // Delay elapsed - replace current popup with new one
                        currentCollectionHoverId = hoveredId;
                        pendingCollectionHoverId = -1;
                        ShowCollectionPopup(hoveredWeapon, hoveredItem, pendingCollectionArcana);
                    }
                }
            }
            else if (hoveredId == currentCollectionHoverId)
            {
                // Still hovering the currently shown icon - clear any pending
                pendingCollectionHoverId = -1;
            }
            else
            {
                // Not hovering any icon - clear pending but keep current popup visible
                pendingCollectionHoverId = -1;
            }
        }

        private static void ShowCollectionPopup(WeaponType? weaponType, ItemType? itemType, object arcanaType = null)
        {
            HideCollectionPopup();

            // Try to cache data if not cached yet (needed for popup content)
            if (cachedDataManager == null)
            {
                TryCacheDataManagerStatic();
            }

            // Find the Collection view's canvas or fall back to any active canvas
            UnityEngine.Transform popupParent = null;
            var collectionsView = UnityEngine.GameObject.Find("UI/Canvas - App/Safe Area/View - Collections");
            if (collectionsView != null)
            {
                popupParent = collectionsView.transform;
            }
            else
            {
                var canvases = UnityEngine.Object.FindObjectsOfType<UnityEngine.Canvas>();
                for (int i = 0; i < canvases.Count; i++)
                {
                    if (canvases[i] != null && canvases[i].gameObject.activeInHierarchy)
                    {
                        popupParent = canvases[i].transform;
                        break;
                    }
                }
            }
            if (popupParent == null) return;

            // Create appropriate popup type
            if (arcanaType != null)
            {
                var arcanaData = GetArcanaData(arcanaType);
                if (arcanaData == null)
                {
                    MelonLogger.Warning($"[CollectionPopup] Could not get arcana data for {arcanaType}");
                    return;
                }
                collectionPopup = CreateArcanaPopup(popupParent, arcanaData);
            }
            else
            {
                collectionPopup = CreatePopup(popupParent, weaponType, itemType);
            }
            if (collectionPopup == null) return;

            // Position: right side of screen, left-aligned with the filter/icon panel
            var popupRect = collectionPopup.GetComponent<UnityEngine.RectTransform>();
            var filterPanel = UnityEngine.GameObject.Find("UI/Canvas - App/Safe Area/View - Collections/FilterPanel");
            popupRect.anchorMin = new UnityEngine.Vector2(0f, 0f);
            popupRect.anchorMax = new UnityEngine.Vector2(0f, 0f);
            popupRect.pivot = new UnityEngine.Vector2(0f, 1f); // left-top pivot
            if (filterPanel != null)
            {
                var fpRect = filterPanel.GetComponent<UnityEngine.RectTransform>();
                float leftX = fpRect.offsetMin.x;
                float belowFilterY = fpRect.offsetMin.y;
                popupRect.anchoredPosition = new UnityEngine.Vector2(leftX, belowFilterY - 15f);
            }
            else
            {
                popupRect.anchoredPosition = new UnityEngine.Vector2(1450f, 930f);
            }
        }

        private static void HideCollectionPopup()
        {
            if (collectionPopup != null)
            {
                UnityEngine.Object.Destroy(collectionPopup);
                collectionPopup = null;
            }
        }

        #endregion

        // Check if a transform is under the "GAME UI" hierarchy (not on Collection/menu screens)
        private static bool IsUnderGameUI(UnityEngine.Transform t)
        {
            var current = t;
            while (current != null)
            {
                if (current.name == "GAME UI")
                    return true;
                current = current.parent;
            }
            return false;
        }

        /// <summary>
        /// Called by SetWeaponData patch to register a UI element with its weapon type.
        /// </summary>
        public static void RegisterWeaponUI(int instanceId, UnityEngine.GameObject go, WeaponType type, bool isAddMethod = false)
        {
            // Collection/menu screen icons: track for raycast hover but don't add EventTriggers
            if (!IsUnderGameUI(go.transform))
            {
                collectionIcons[instanceId] = (go, type, null, null);
                return;
            }

            uiToWeaponType[instanceId] = type;

            UnityEngine.GameObject iconGo = null;

            if (isAddMethod)
            {
                // For "Add" methods (like AddAffectedWeapon), the icon was just created as the last child
                iconGo = FindLastImageChild(go);
            }
            else
            {
                // Find the icon image within this UI element (usually named "Icon" or similar)
                iconGo = FindIconInUI(go);
            }

            if (iconGo != null)
            {
                AddHoverToGameObject(iconGo, type, null);
            }
            else
            {
                // Fallback to whole card if icon not found
                AddHoverToGameObject(go, type, null);
            }
        }

        private static UnityEngine.GameObject FindLastImageChild(UnityEngine.GameObject parent)
        {
            // Find the last child that has an Image component (most recently added icon)
            // Search recursively since icons might be nested (e.g., under "Info Panel")
            return FindLastImageChildRecursive(parent.transform, 0);
        }

        private static UnityEngine.GameObject FindLastImageChildRecursive(UnityEngine.Transform parent, int depth)
        {
            if (depth > 3) return null; // Don't go too deep

            // Search children from last to first
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                string lowerName = child.name.ToLower();

                // Skip certain containers but search inside them
                bool isContainer = lowerName.Contains("panel") || lowerName.Contains("container") || lowerName.Contains("group");

                if (isContainer)
                {
                    // Search inside this container
                    var found = FindLastImageChildRecursive(child, depth + 1);
                    if (found != null) return found;
                    continue;
                }

                // Skip backgrounds/frames
                if (lowerName.Contains("background") || lowerName.Contains("frame"))
                    continue;

                // Check if this has an Image with a sprite
                var img = child.GetComponent<UnityEngine.UI.Image>();
                if (img != null && img.sprite != null)
                {
                    return child.gameObject;
                }
            }

            return null;
        }

        /// <summary>
        /// Called by SetItemData patch to register a UI element with its item type.
        /// </summary>
        public static void RegisterItemUI(int instanceId, UnityEngine.GameObject go, ItemType type, bool isAddMethod = false)
        {
            // Collection/menu screen icons: track for raycast hover but don't add EventTriggers
            if (!IsUnderGameUI(go.transform))
            {
                collectionIcons[instanceId] = (go, null, type, null);
                return;
            }

            uiToItemType[instanceId] = type;

            UnityEngine.GameObject iconGo = null;

            if (isAddMethod)
            {
                iconGo = FindLastImageChild(go);
            }
            else
            {
                iconGo = FindIconInUI(go);
            }

            if (iconGo != null)
            {
                AddHoverToGameObject(iconGo, null, type);
            }
            else
            {
                AddHoverToGameObject(go, null, type);
            }
        }

        /// <summary>
        /// Called by SetArcana patch to register an arcana UI element on the Collection screen.
        /// </summary>
        public static void RegisterArcanaUI(int instanceId, UnityEngine.GameObject go, object arcanaType)
        {
            if (!IsUnderGameUI(go.transform))
            {
                collectionIcons[instanceId] = (go, null, null, arcanaType);
                return;
            }
        }

        /// <summary>
        /// Public accessor for cached ArcanaType enum so GenericIconPatches can use it.
        /// </summary>
        public static System.Type GetCachedArcanaTypeEnum()
        {
            return cachedArcanaTypeEnum;
        }

        /// <summary>
        /// Finds the icon Image within a LevelUpItemUI.
        /// </summary>
        private static UnityEngine.GameObject FindIconInUI(UnityEngine.GameObject parent)
        {
            // Common icon names to look for
            string[] iconNames = { "Icon", "icon", "ItemIcon", "WeaponIcon", "Sprite", "Image" };

            foreach (var name in iconNames)
            {
                var found = parent.transform.Find(name);
                if (found != null)
                    return found.gameObject;
            }

            // Try to find any child with an Image component that looks like an icon
            // (usually the first significant Image child)
            var images = parent.GetComponentsInChildren<UnityEngine.UI.Image>(false);
            foreach (var img in images)
            {
                // Skip backgrounds and frames (usually have "bg", "frame", "background" in name)
                var goName = img.gameObject.name.ToLower();
                if (goName.Contains("bg") || goName.Contains("frame") || goName.Contains("background"))
                    continue;

                // Skip if it's the parent itself
                if (img.gameObject == parent)
                    continue;

                // Log what we found for debugging
                if (images.Length > 0)
                {
                }

                return img.gameObject;
            }

            return null;
        }

        private static void AddHoverToGameObject(UnityEngine.GameObject go, WeaponType? weaponType, ItemType? itemType, bool useClick = false)
        {
            // Check if already has event trigger
            var existing = go.GetComponent<UnityEngine.EventSystems.EventTrigger>();
            if (existing != null)
            {
                return;
            }

            var eventTrigger = go.AddComponent<UnityEngine.EventSystems.EventTrigger>();

            if (useClick)
            {
                // Click to open popup (for icons inside popups)
                var clickEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
                clickEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerClick;
                clickEntry.callback.AddListener((UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData>)((data) =>
                {
                    ShowItemPopup(go.transform, weaponType, itemType);
                }));
                eventTrigger.triggers.Add(clickEntry);
            }
            else
            {
                // Hover to open popup (for game UI icons)
                var enterEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
                enterEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter;
                enterEntry.callback.AddListener((UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData>)((data) =>
                {
                    ShowItemPopup(go.transform, weaponType, itemType);
                }));
                eventTrigger.triggers.Add(enterEntry);

                var exitEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
                exitEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit;
                exitEntry.callback.AddListener((UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData>)((data) =>
                {
                    MelonLoader.MelonCoroutines.Start(DelayedHideCheck());
                }));
                eventTrigger.triggers.Add(exitEntry);
            }
        }

        #endregion

        #region Data Access Helpers

        private static WeaponData GetWeaponData(WeaponType type)
        {
            if (cachedWeaponsDict == null) return null;

            try
            {
                var dictType = cachedWeaponsDict.GetType();
                var containsMethod = dictType.GetMethod("ContainsKey");
                if (containsMethod != null && (bool)containsMethod.Invoke(cachedWeaponsDict, new object[] { type }))
                {
                    var indexer = dictType.GetProperty("Item");
                    if (indexer != null)
                    {
                        // Dictionary value is List<WeaponData>, get the first item
                        var list = indexer.GetValue(cachedWeaponsDict, new object[] { type }) as Il2CppSystem.Collections.Generic.List<WeaponData>;
                        if (list != null && list.Count > 0)
                        {
                            return list[0];
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        private static object GetPowerUpData(ItemType type)
        {
            if (cachedPowerUpsDict == null) return null;

            try
            {
                var dictType = cachedPowerUpsDict.GetType();
                var containsMethod = dictType.GetMethod("ContainsKey");
                if (containsMethod != null && (bool)containsMethod.Invoke(cachedPowerUpsDict, new object[] { type }))
                {
                    var indexer = dictType.GetProperty("Item");
                    if (indexer != null)
                    {
                        return indexer.GetValue(cachedPowerUpsDict, new object[] { type });
                    }
                }
            }
            catch { }

            return null;
        }

        private static System.Type spriteManagerType = null;

        private static bool spriteManagerDebugLogged = false;

        // Cached circle sprite for owned item highlights
        private static UnityEngine.Sprite cachedCircleSprite = null;

        /// <summary>
        /// Creates or returns a cached circular sprite for highlighting owned items.
        /// </summary>
        private static UnityEngine.Sprite GetCircleSprite()
        {
            if (cachedCircleSprite != null)
                return cachedCircleSprite;

            try
            {
                // Create a circular texture
                int size = 64;
                var texture = new UnityEngine.Texture2D(size, size, UnityEngine.TextureFormat.RGBA32, false);
                texture.filterMode = UnityEngine.FilterMode.Bilinear;

                float center = size / 2f;
                float radius = center - 1f;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = x - center;
                        float dy = y - center;
                        float dist = UnityEngine.Mathf.Sqrt(dx * dx + dy * dy);

                        if (dist <= radius)
                        {
                            // Inside circle - white with soft edge
                            float alpha = 1f;
                            if (dist > radius - 2f)
                            {
                                // Soft edge for anti-aliasing
                                alpha = (radius - dist) / 2f;
                            }
                            texture.SetPixel(x, y, new UnityEngine.Color(1f, 1f, 1f, alpha));
                        }
                        else
                        {
                            // Outside circle - transparent
                            texture.SetPixel(x, y, new UnityEngine.Color(0f, 0f, 0f, 0f));
                        }
                    }
                }

                texture.Apply();

                // Create sprite from texture
                cachedCircleSprite = UnityEngine.Sprite.Create(
                    texture,
                    new UnityEngine.Rect(0, 0, size, size),
                    new UnityEngine.Vector2(0.5f, 0.5f),
                    100f
                );

                return cachedCircleSprite;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error creating circle sprite: {ex.Message}");
                return null;
            }
        }

        private static UnityEngine.Sprite LoadSpriteFromAtlas(string frameName, string atlasName)
        {
            try
            {
                // Initialize spriteManagerType if needed
                if (spriteManagerType == null)
                {
                    foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                    {
                        spriteManagerType = assembly.GetTypes().FirstOrDefault(t => t.Name == "SpriteManager");
                        if (spriteManagerType != null)
                        {
                            if (!spriteManagerDebugLogged)
                            {
                                spriteManagerDebugLogged = true;
                            }
                            break;
                        }
                    }
                    if (spriteManagerType == null && !spriteManagerDebugLogged)
                    {
                        MelonLogger.Warning("[LoadSpriteFromAtlas] SpriteManager type not found!");
                        spriteManagerDebugLogged = true;
                    }
                }

                if (spriteManagerType == null) return null;

                var getSpriteFastMethod = spriteManagerType.GetMethod("GetSpriteFast",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new System.Type[] { typeof(string), typeof(string) },
                    null);

                if (getSpriteFastMethod != null)
                {
                    var result = getSpriteFastMethod.Invoke(null, new object[] { frameName, atlasName }) as UnityEngine.Sprite;
                    if (result != null) return result;

                    // Try without extension
                    if (frameName.Contains("."))
                    {
                        var nameWithoutExt = frameName.Substring(0, frameName.LastIndexOf('.'));
                        result = getSpriteFastMethod.Invoke(null, new object[] { nameWithoutExt, atlasName }) as UnityEngine.Sprite;
                    }
                    return result;
                }
            }
            catch { }
            return null;
        }

        private static bool spriteLoadDebugLogged = false;

        private static UnityEngine.Sprite GetSpriteForWeapon(WeaponType weaponType)
        {
            var data = GetWeaponData(weaponType);
            if (data == null)
            {
                if (!spriteLoadDebugLogged)
                {
                }
                return null;
            }

            try
            {
                string frameName = data.frameName;
                // Property is "texture" not "textureName"
                string atlasName = GetPropertyValue<string>(data, "texture");

                if (!spriteLoadDebugLogged)
                {
                    spriteLoadDebugLogged = true;
                }

                if (!string.IsNullOrEmpty(frameName) && !string.IsNullOrEmpty(atlasName))
                {
                    return LoadSpriteFromAtlas(frameName, atlasName);
                }

                // Fallback: try common atlas names
                if (!string.IsNullOrEmpty(frameName))
                {
                    string[] fallbackAtlases = { "weapons", "items", "characters", "ui" };
                    foreach (var atlas in fallbackAtlases)
                    {
                        var sprite = LoadSpriteFromAtlas(frameName, atlas);
                        if (sprite != null) return sprite;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[GetSpriteForWeapon] Error: {ex.Message}");
            }
            return null;
        }

        private static UnityEngine.Sprite GetSpriteForItem(ItemType itemType)
        {
            var data = GetPowerUpData(itemType);
            if (data == null) return null;

            try
            {
                string frameName = GetPropertyValue<string>(data, "frameName");
                // Property is "texture" not "textureName"
                string atlasName = GetPropertyValue<string>(data, "texture");

                if (!string.IsNullOrEmpty(frameName) && !string.IsNullOrEmpty(atlasName))
                {
                    return LoadSpriteFromAtlas(frameName, atlasName);
                }

                // Fallback: try common atlas names
                if (!string.IsNullOrEmpty(frameName))
                {
                    string[] fallbackAtlases = { "items", "powerups", "weapons", "ui" };
                    foreach (var atlas in fallbackAtlases)
                    {
                        var sprite = LoadSpriteFromAtlas(frameName, atlas);
                        if (sprite != null) return sprite;
                    }
                }
            }
            catch { }
            return null;
        }

        private static T GetPropertyValue<T>(object obj, string propertyName)
        {
            if (obj == null) return default;

            try
            {
                var prop = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null)
                    return (T)prop.GetValue(obj);

                var field = obj.GetType().GetField(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                    return (T)field.GetValue(obj);
            }
            catch { }

            return default;
        }

        private static Il2CppTMPro.TMP_FontAsset GetFont()
        {
            // Try to find an existing TMP text component to get its font
            var existingTmp = UnityEngine.Object.FindObjectOfType<Il2CppTMPro.TextMeshProUGUI>();
            return existingTmp?.font;
        }

        #endregion

        #region Arcana Data Access

        private static object GetGameManager()
        {
            if (cachedGameManager != null) return cachedGameManager;

            try
            {
                var assembly = typeof(WeaponData).Assembly;
                var gameManagerType = assembly.GetTypes().FirstOrDefault(t => t.Name == "GameManager" && !t.IsInterface && typeof(UnityEngine.Component).IsAssignableFrom(t));
                if (gameManagerType == null) return null;

                var findMethod = typeof(UnityEngine.Object).GetMethods()
                    .FirstOrDefault(m => m.Name == "FindObjectOfType" && m.IsGenericMethod && m.GetParameters().Length == 0);
                if (findMethod == null) return null;

                var genericFind = findMethod.MakeGenericMethod(gameManagerType);
                cachedGameManager = genericFind.Invoke(null, null);
                return cachedGameManager;
            }
            catch
            {
                return null;
            }
        }

        private static List<object> GetAllActiveArcanaTypes()
        {
            var result = new List<object>();
            try
            {
                var assembly = typeof(WeaponData).Assembly;

                if (cachedArcanaTypeEnum == null)
                {
                    cachedArcanaTypeEnum = assembly.GetTypes().FirstOrDefault(t => t.Name == "ArcanaType");
                }
                if (cachedArcanaTypeEnum == null) return result;

                var gameMgr = GetGameManager();
                if (gameMgr == null) return result;

                var amProp = gameMgr.GetType().GetProperty("_arcanaManager", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (amProp == null) return result;

                var arcanaMgr = amProp.GetValue(gameMgr);
                if (arcanaMgr == null) return result;

                if (!arcanaDebugLogged)
                {
                    arcanaDebugLogged = true;

                    // Dump raw weapon and item counts/values from arcana data
                    var testArcanas = GetAllActiveArcanaTypesInternal(arcanaMgr);
                    foreach (var testArcana in testArcanas)
                    {
                        var testData = GetArcanaData(testArcana);
                        if (testData == null) continue;

                        var arcName = testData.GetType().GetProperty("name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(testData);

                        // Dump raw weapons list with ALL int values
                        var wProp = testData.GetType().GetProperty("weapons", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (wProp != null)
                        {
                            var wList = wProp.GetValue(testData);
                            if (wList != null)
                            {
                                var wCount = (int)wList.GetType().GetProperty("Count").GetValue(wList);
                                var wItem = wList.GetType().GetProperty("Item");
                                var rawValues = new List<string>();
                                for (int wi = 0; wi < wCount; wi++)
                                {
                                    var w = wItem.GetValue(wList, new object[] { wi });
                                    if (w == null) { rawValues.Add("null"); continue; }
                                    var il2cppObj = w as Il2CppSystem.Object;
                                    if (il2cppObj != null)
                                    {
                                        try
                                        {
                                            unsafe
                                            {
                                                IntPtr ptr = il2cppObj.Pointer;
                                                int* valuePtr = (int*)((byte*)ptr.ToPointer() + 16);
                                                int raw = *valuePtr;
                                                bool defined = System.Enum.IsDefined(typeof(WeaponType), raw);
                                                string name = defined ? ((WeaponType)raw).ToString() : "UNDEFINED";
                                                rawValues.Add($"{name}({raw})");
                                            }
                                        }
                                        catch { rawValues.Add("decode_error"); }
                                    }
                                    else
                                    {
                                        rawValues.Add($"not_il2cpp:{w}");
                                    }
                                }
                            }
                        }

                        // Dump raw items list
                        var iProp = testData.GetType().GetProperty("items", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (iProp != null)
                        {
                            var iList = iProp.GetValue(testData);
                            if (iList != null)
                            {
                                var iCount = (int)iList.GetType().GetProperty("Count").GetValue(iList);
                                var iItem = iList.GetType().GetProperty("Item");
                                var rawValues = new List<string>();
                                for (int ii = 0; ii < iCount; ii++)
                                {
                                    var item = iItem.GetValue(iList, new object[] { ii });
                                    if (item == null) { rawValues.Add("null"); continue; }
                                    var il2cppObj = item as Il2CppSystem.Object;
                                    if (il2cppObj != null)
                                    {
                                        try
                                        {
                                            unsafe
                                            {
                                                IntPtr ptr = il2cppObj.Pointer;
                                                int* valuePtr = (int*)((byte*)ptr.ToPointer() + 16);
                                                int raw = *valuePtr;
                                                bool defined = System.Enum.IsDefined(typeof(ItemType), raw);
                                                string name = defined ? ((ItemType)raw).ToString() : "UNDEFINED";
                                                rawValues.Add($"{name}({raw})");
                                            }
                                        }
                                        catch { rawValues.Add("decode_error"); }
                                    }
                                    else
                                    {
                                        rawValues.Add($"not_il2cpp:{item}");
                                    }
                                }
                            }
                        }
                    }
                }

                // Try to find active arcanas collection
                // Try common property names for the active/chosen arcanas
                string[] activeArcanaProps = { "ActiveArcanas", "_activeArcanas", "ChosenArcanas", "_chosenArcanas",
                    "PlayerArcanas", "_playerArcanas", "SelectedArcanas", "_selectedArcanas",
                    "OwnedArcanas", "_ownedArcanas", "CurrentArcanas", "_currentArcanas" };

                object activeArcanasList = null;
                foreach (var propName in activeArcanaProps)
                {
                    var prop = arcanaMgr.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (prop != null)
                    {
                        activeArcanasList = prop.GetValue(arcanaMgr);
                        if (activeArcanasList != null)
                        {
                            break;
                        }
                    }
                    var field = arcanaMgr.GetType().GetField(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                    {
                        activeArcanasList = field.GetValue(arcanaMgr);
                        if (activeArcanasList != null)
                        {
                            break;
                        }
                    }
                }

                // If we found a list/collection, iterate it
                if (activeArcanasList != null)
                {
                    var countProp = activeArcanasList.GetType().GetProperty("Count");
                    var itemProp = activeArcanasList.GetType().GetProperty("Item");
                    if (countProp != null && itemProp != null)
                    {
                        int count = (int)countProp.GetValue(activeArcanasList);
                        for (int i = 0; i < count; i++)
                        {
                            var arcana = itemProp.GetValue(activeArcanasList, new object[] { i });
                            if (arcana != null)
                            {
                                result.Add(arcana);
                            }
                        }
                        if (result.Count > 0) return result;
                    }
                }

                // Fallback: use SelectedArcana from Config (the pre-run pick)
                var poProp = arcanaMgr.GetType().GetProperty("_playerOptions", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (poProp == null) return result;

                var playerOpts = poProp.GetValue(arcanaMgr);
                if (playerOpts == null) return result;

                var configProp = playerOpts.GetType().GetProperty("Config", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (configProp == null) return result;

                var config = configProp.GetValue(playerOpts);
                if (config == null) return result;

                // Check if arcanas are enabled
                var selectedMazzoProp = config.GetType().GetProperty("SelectedMazzo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (selectedMazzoProp != null)
                {
                    var mazzoEnabled = (bool)selectedMazzoProp.GetValue(config);
                    if (!mazzoEnabled) return result;
                }

                var selectedArcanaProp = config.GetType().GetProperty("SelectedArcana", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (selectedArcanaProp == null) return result;

                var selectedArcanaInt = (int)selectedArcanaProp.GetValue(config);

                var arcanaValues = System.Enum.GetValues(cachedArcanaTypeEnum);
                foreach (var val in arcanaValues)
                {
                    if ((int)val == selectedArcanaInt)
                    {
                        result.Add(val);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Arcana] Error getting active arcanas: {ex.Message}");
            }

            return result;
        }

        private static List<object> GetAllActiveArcanaTypesInternal(object arcanaMgr)
        {
            var result = new List<object>();
            try
            {
                var prop = arcanaMgr.GetType().GetProperty("ActiveArcanas", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop == null) return result;
                var list = prop.GetValue(arcanaMgr);
                if (list == null) return result;
                var countProp = list.GetType().GetProperty("Count");
                var itemProp = list.GetType().GetProperty("Item");
                if (countProp == null || itemProp == null) return result;
                int count = (int)countProp.GetValue(list);
                for (int i = 0; i < count; i++)
                {
                    var item = itemProp.GetValue(list, new object[] { i });
                    if (item != null) result.Add(item);
                }
            }
            catch { }
            return result;
        }

        private static object GetArcanaData(object arcanaType)
        {
            try
            {
                if (cachedDataManager == null || arcanaType == null) return null;

                if (cachedAllArcanas == null)
                {
                    var allArcanasProp = cachedDataManager.GetType().GetProperty("AllArcanas", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (allArcanasProp != null)
                    {
                        cachedAllArcanas = allArcanasProp.GetValue(cachedDataManager);
                    }
                }
                if (cachedAllArcanas == null) return null;

                var indexer = cachedAllArcanas.GetType().GetProperty("Item");
                if (indexer == null) return null;

                return indexer.GetValue(cachedAllArcanas, new object[] { arcanaType });
            }
            catch
            {
                return null;
            }
        }

        private static bool IsWeaponAffectedByArcana(WeaponType weaponType, object arcanaData)
        {
            try
            {
                string targetName = weaponType.ToString();
                var affected = GetArcanaAffectedWeaponTypes(arcanaData);
                foreach (var wt in affected)
                {
                    if (wt == weaponType) return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsItemAffectedByArcana(ItemType itemType, object arcanaData)
        {
            try
            {
                var affected = GetArcanaAffectedItemTypes(arcanaData);
                foreach (var it in affected)
                {
                    if (it == itemType) return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static List<WeaponType> GetArcanaAffectedWeaponTypes(object arcanaData)
        {
            var weaponTypes = new List<WeaponType>();
            var seenNames = new HashSet<string>();
            try
            {
                if (arcanaData == null) return weaponTypes;

                var weaponsProp = arcanaData.GetType().GetProperty("weapons", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (weaponsProp == null) return weaponTypes;

                var weapons = weaponsProp.GetValue(arcanaData);
                if (weapons == null) return weaponTypes;

                var countProp = weapons.GetType().GetProperty("Count");
                if (countProp == null) return weaponTypes;

                int count = (int)countProp.GetValue(weapons);
                var itemProp = weapons.GetType().GetProperty("Item");
                if (itemProp == null) return weaponTypes;

                for (int i = 0; i < count; i++)
                {
                    var w = itemProp.GetValue(weapons, new object[] { i });
                    if (w == null) continue;

                    var il2cppObj = w as Il2CppSystem.Object;
                    if (il2cppObj == null) continue;

                    try
                    {
                        string name = il2cppObj.ToString();
                        if (string.IsNullOrEmpty(name) || !seenNames.Add(name)) continue;

                        if (System.Enum.TryParse<WeaponType>(name, out var wt))
                        {
                            weaponTypes.Add(wt);
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return weaponTypes;
        }

        private static List<ItemType> GetArcanaAffectedItemTypes(object arcanaData)
        {
            var itemTypes = new List<ItemType>();
            var seenNames = new HashSet<string>();
            try
            {
                if (arcanaData == null) return itemTypes;

                var itemsProp = arcanaData.GetType().GetProperty("items", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (itemsProp == null) return itemTypes;

                var items = itemsProp.GetValue(arcanaData);
                if (items == null) return itemTypes;

                var countProp = items.GetType().GetProperty("Count");
                if (countProp == null) return itemTypes;

                int count = (int)countProp.GetValue(items);
                var itemProp = items.GetType().GetProperty("Item");
                if (itemProp == null) return itemTypes;

                for (int i = 0; i < count; i++)
                {
                    var item = itemProp.GetValue(items, new object[] { i });
                    if (item == null) continue;

                    var il2cppObj = item as Il2CppSystem.Object;
                    if (il2cppObj == null) continue;

                    try
                    {
                        string name = il2cppObj.ToString();
                        if (string.IsNullOrEmpty(name) || !seenNames.Add(name)) continue;

                        if (System.Enum.TryParse<ItemType>(name, out var it))
                        {
                            itemTypes.Add(it);
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return itemTypes;
        }

        private static UnityEngine.Sprite LoadArcanaSprite(string textureName, string frameName)
        {
            string cleanFrameName = frameName;
            if (cleanFrameName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                cleanFrameName = cleanFrameName.Substring(0, cleanFrameName.Length - 4);
            }

            // Try the specific texture atlas first
            if (!string.IsNullOrEmpty(textureName))
            {
                string[] frameNamesToTry = { frameName, cleanFrameName, $"{cleanFrameName}.png" };
                foreach (var fn in frameNamesToTry)
                {
                    var sprite = LoadSpriteFromAtlas(fn, textureName);
                    if (sprite != null) return sprite;
                }
            }

            // Search all loaded sprites
            var allSprites = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Sprite>();

            foreach (var s in allSprites)
            {
                if (s == null || s.texture == null) continue;

                string texName = s.texture.name.ToLower();
                string spriteName = s.name.ToLower();

                if (!string.IsNullOrEmpty(textureName) && texName.Contains(textureName.ToLower()) &&
                    (spriteName == cleanFrameName.ToLower() || spriteName == frameName.ToLower()))
                {
                    return s;
                }
            }

            // Fallback atlases
            string[] fallbackAtlases = { "arcanas", "cards", "items", "ui", "randomazzo" };
            foreach (var atlas in fallbackAtlases)
            {
                var sprite = LoadSpriteFromAtlas(cleanFrameName, atlas);
                if (sprite != null) return sprite;
                sprite = LoadSpriteFromAtlas(frameName, atlas);
                if (sprite != null) return sprite;
            }

            return null;
        }

        private static int GetArcanaTypeInt(object arcanaType)
        {
            if (arcanaType == null) return -1;

            // Try IL2CPP pointer decode first
            try
            {
                var il2cppObj = arcanaType as Il2CppSystem.Object;
                if (il2cppObj != null)
                {
                    unsafe
                    {
                        IntPtr ptr = il2cppObj.Pointer;
                        int* valuePtr = (int*)((byte*)ptr.ToPointer() + 16);
                        return *valuePtr;
                    }
                }
            }
            catch { }

            // Try .NET enum/int conversion
            try
            {
                return Convert.ToInt32(arcanaType);
            }
            catch { }

            // Try unboxing via Unbox on Il2CppSystem.Object
            try
            {
                var unboxMethod = arcanaType.GetType().GetMethod("Unbox", BindingFlags.Public | BindingFlags.Instance);
                if (unboxMethod != null)
                {
                    var unboxed = unboxMethod.Invoke(arcanaType, null);
                    return Convert.ToInt32(unboxed);
                }
            }
            catch { }

            MelonLogger.Warning($"[Arcana] GetArcanaTypeInt failed for type {arcanaType.GetType().FullName}, value: {arcanaType}");
            return -1;
        }

        private static void ScanArcanaUI(int arcanaTypeInt, string arcanaName)
        {
            if (arcanaUICache.ContainsKey(arcanaTypeInt)) return;
            if (!lookupTablesBuilt || spriteToWeaponType == null || spriteToItemType == null) return;

            var weapons = new HashSet<WeaponType>();
            var items = new HashSet<ItemType>();

            try
            {

                // Find TextMeshProUGUI components with the arcana's name
                var allTmps = UnityEngine.Object.FindObjectsOfType<Il2CppTMPro.TextMeshProUGUI>();
                UnityEngine.Transform cardContainer = null;

                // Strip rich text for matching - extract plain text name
                string searchName = arcanaName.Trim();
                // Also try just the name part after the numeral (e.g., "Gemini" from "I - Gemini")
                string shortName = searchName.Contains(" - ") ? searchName.Substring(searchName.IndexOf(" - ") + 3).Trim() : searchName;

                int tmpCount = 0;
                foreach (var tmp in allTmps)
                {
                    if (tmp == null || tmp.text == null) continue;
                    tmpCount++;

                    string tmpText = tmp.text.Trim();
                    // Strip common rich text tags for comparison
                    string cleanText = System.Text.RegularExpressions.Regex.Replace(tmpText, "<[^>]+>", "").Trim();

                    // Check for exact match, clean match, or contains match
                    bool isMatch = tmpText == searchName ||
                                   cleanText == searchName ||
                                   tmpText.Contains(searchName) ||
                                   cleanText.Contains(searchName) ||
                                   tmpText.Contains(shortName) ||
                                   cleanText.Contains(shortName);

                    // Log texts that partially match for debugging
                    if (tmpText.Contains("Gemini") || cleanText.Contains("Gemini") ||
                        tmpText.Contains("gemini") || cleanText.Contains("gemini") ||
                        tmpText.Contains("arcana") || tmpText.Contains("Arcana"))
                    {
                    }

                    if (isMatch)
                    {

                        // Walk up to find the card container (try several levels)
                        var candidate = tmp.transform.parent;
                        for (int depth = 0; depth < 8 && candidate != null; depth++)
                        {
                            // Check if this container has many Image children (the icon grid)
                            var childImages = candidate.GetComponentsInChildren<UnityEngine.UI.Image>();
                            if (childImages.Length >= 10)
                            {
                                cardContainer = candidate;
                                break;
                            }
                            candidate = candidate.parent;
                        }
                        if (cardContainer != null) break;
                    }
                }


                if (cardContainer == null)
                {
                    return;
                }

                // Scan all Image components under the card container
                var images = cardContainer.GetComponentsInChildren<UnityEngine.UI.Image>();
                foreach (var img in images)
                {
                    if (img == null || img.sprite == null) continue;

                    string spriteName = img.sprite.name;
                    if (string.IsNullOrEmpty(spriteName)) continue;

                    // Clean up sprite name (remove .png if present)
                    string cleanName = spriteName;
                    if (cleanName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                        cleanName = cleanName.Substring(0, cleanName.Length - 4);

                    // Check both original and cleaned names against lookup tables
                    if (spriteToWeaponType.TryGetValue(spriteName, out var wt))
                        weapons.Add(wt);
                    else if (spriteToWeaponType.TryGetValue(cleanName, out var wt2))
                        weapons.Add(wt2);
                    else if (spriteToItemType.TryGetValue(spriteName, out var it))
                        items.Add(it);
                    else if (spriteToItemType.TryGetValue(cleanName, out var it2))
                        items.Add(it2);
                }


                if (weapons.Count > 0 || items.Count > 0)
                {
                    arcanaUICache[arcanaTypeInt] = (weapons, items);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Arcana] Error scanning arcana UI: {ex.Message}");
            }
        }

        private static List<WeaponType> GetAllArcanaAffectedWeaponTypes(object arcanaData, int arcanaTypeInt = -1, string arcanaName = null)
        {
            var result = new HashSet<WeaponType>();

            // Static data
            foreach (var wt in GetArcanaAffectedWeaponTypes(arcanaData))
                result.Add(wt);

            // Resolve arcanaTypeInt from name cache if not provided
            if (arcanaTypeInt < 0 && arcanaName == null)
            {
                var nameProp = arcanaData.GetType().GetProperty("name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                arcanaName = nameProp?.GetValue(arcanaData)?.ToString() ?? "";
                if (arcanaNameToInt.TryGetValue(arcanaName, out var cached))
                    arcanaTypeInt = cached;
            }

            // Panel captured data
            foreach (var wt in panelCapturedWeapons)
                result.Add(wt);

            // UI scan data
            if (arcanaTypeInt >= 0)
            {
                if (!arcanaUICache.ContainsKey(arcanaTypeInt))
                    ScanArcanaUI(arcanaTypeInt, arcanaName ?? "");

                if (arcanaUICache.TryGetValue(arcanaTypeInt, out var uiCached))
                {
                    foreach (var wt in uiCached.weapons)
                        result.Add(wt);
                }
            }

            return result.ToList();
        }

        private static List<ItemType> GetAllArcanaAffectedItemTypes(object arcanaData, int arcanaTypeInt = -1, string arcanaName = null)
        {
            var result = new HashSet<ItemType>();

            // Static data
            foreach (var it in GetArcanaAffectedItemTypes(arcanaData))
                result.Add(it);

            // Resolve arcanaTypeInt from name cache if not provided
            if (arcanaTypeInt < 0 && arcanaName == null)
            {
                var nameProp = arcanaData.GetType().GetProperty("name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                arcanaName = nameProp?.GetValue(arcanaData)?.ToString() ?? "";
                if (arcanaNameToInt.TryGetValue(arcanaName, out var cached))
                    arcanaTypeInt = cached;
            }

            // Panel captured data
            foreach (var it in panelCapturedItems)
                result.Add(it);

            // UI scan data
            if (arcanaTypeInt >= 0)
            {
                if (!arcanaUICache.ContainsKey(arcanaTypeInt))
                    ScanArcanaUI(arcanaTypeInt, arcanaName ?? "");

                if (arcanaUICache.TryGetValue(arcanaTypeInt, out var uiCached))
                {
                    foreach (var it in uiCached.items)
                        result.Add(it);
                }
            }

            return result.ToList();
        }

        // Global sets of weapons/items seen in ArcanaInfoPanel patches
        private static HashSet<WeaponType> panelCapturedWeapons = new HashSet<WeaponType>();
        private static HashSet<ItemType> panelCapturedItems = new HashSet<ItemType>();

        public static void CaptureArcanaAffectedWeapon(object arcanaInfoPanel, WeaponType weaponType)
        {
            try
            {
                panelCapturedWeapons.Add(weaponType);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Arcana] Error capturing weapon from patch: {ex.Message}");
            }
        }

        public static void CaptureArcanaAffectedItem(object arcanaInfoPanel, ItemType itemType)
        {
            try
            {
                panelCapturedItems.Add(itemType);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Arcana] Error capturing item from patch: {ex.Message}");
            }
        }

        private struct ArcanaInfo
        {
            public string Name;
            public string Description;
            public UnityEngine.Sprite Sprite;
            public object ArcanaData;
            public object ArcanaType;
        }

        private static List<ArcanaInfo> GetActiveArcanasForWeapon(WeaponType weaponType)
        {
            var result = new List<ArcanaInfo>();
            try
            {
                var activeArcanas = GetAllActiveArcanaTypes();
                foreach (var arcanaType in activeArcanas)
                {
                    var arcanaData = GetArcanaData(arcanaType);
                    if (arcanaData == null)
                    {
                        continue;
                    }

                    // Extract arcana type int and name
                    int arcanaTypeInt = GetArcanaTypeInt(arcanaType);
                    var arcanaNameProp = arcanaData.GetType().GetProperty("name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    string arcanaName = arcanaNameProp?.GetValue(arcanaData)?.ToString() ?? "?";

                    // Cache the name → int mapping for CreateArcanaPopup
                    if (arcanaTypeInt >= 0 && !string.IsNullOrEmpty(arcanaName))
                        arcanaNameToInt[arcanaName] = arcanaTypeInt;


                    // Check static data first, then panel capture, then UI scan
                    bool affectedStatic = IsWeaponAffectedByArcana(weaponType, arcanaData);
                    bool affectedPanel = !affectedStatic && panelCapturedWeapons.Contains(weaponType);
                    bool affectedUI = false;
                    if (!affectedStatic && !affectedPanel && arcanaTypeInt >= 0)
                    {
                        if (!arcanaUICache.ContainsKey(arcanaTypeInt))
                            ScanArcanaUI(arcanaTypeInt, arcanaName);
                        if (arcanaUICache.TryGetValue(arcanaTypeInt, out var cached))
                            affectedUI = cached.weapons.Contains(weaponType);
                    }

                    if (!affectedStatic && !affectedPanel && !affectedUI)
                    {
                        continue;
                    }

                    var nameProp = arcanaData.GetType().GetProperty("name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var descProp = arcanaData.GetType().GetProperty("description", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var frameProp = arcanaData.GetType().GetProperty("frameName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var textureProp = arcanaData.GetType().GetProperty("texture", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    string name = nameProp?.GetValue(arcanaData)?.ToString() ?? "";
                    string desc = descProp?.GetValue(arcanaData)?.ToString() ?? "";
                    string frameN = frameProp?.GetValue(arcanaData)?.ToString() ?? "";
                    string textureN = textureProp?.GetValue(arcanaData)?.ToString() ?? "";

                    var sprite = LoadArcanaSprite(textureN, frameN);

                    result.Add(new ArcanaInfo
                    {
                        Name = name,
                        Description = desc,
                        Sprite = sprite,
                        ArcanaData = arcanaData,
                        ArcanaType = arcanaType
                    });

                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Arcana] Error getting arcanas for weapon {weaponType}: {ex.Message}");
            }
            return result;
        }

        private static List<ArcanaInfo> GetActiveArcanasForItem(ItemType itemType)
        {
            var result = new List<ArcanaInfo>();
            try
            {
                var activeArcanas = GetAllActiveArcanaTypes();
                foreach (var arcanaType in activeArcanas)
                {
                    var arcanaData = GetArcanaData(arcanaType);
                    if (arcanaData == null) continue;

                    int arcanaTypeInt = GetArcanaTypeInt(arcanaType);
                    var arcanaNameProp = arcanaData.GetType().GetProperty("name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    string arcanaName = arcanaNameProp?.GetValue(arcanaData)?.ToString() ?? "?";

                    if (arcanaTypeInt >= 0 && !string.IsNullOrEmpty(arcanaName))
                        arcanaNameToInt[arcanaName] = arcanaTypeInt;

                    // Check static data first, then panel capture, then UI scan
                    bool affectedStatic = IsItemAffectedByArcana(itemType, arcanaData);
                    bool affectedPanel = !affectedStatic && panelCapturedItems.Contains(itemType);
                    bool affectedUI = false;
                    if (!affectedStatic && !affectedPanel && arcanaTypeInt >= 0)
                    {
                        if (!arcanaUICache.ContainsKey(arcanaTypeInt))
                            ScanArcanaUI(arcanaTypeInt, arcanaName);
                        if (arcanaUICache.TryGetValue(arcanaTypeInt, out var cached))
                            affectedUI = cached.items.Contains(itemType);
                    }

                    if (!affectedStatic && !affectedPanel && !affectedUI)
                    {
                        continue;
                    }

                    var nameProp = arcanaData.GetType().GetProperty("name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var descProp = arcanaData.GetType().GetProperty("description", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var frameProp = arcanaData.GetType().GetProperty("frameName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var textureProp = arcanaData.GetType().GetProperty("texture", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    string name = nameProp?.GetValue(arcanaData)?.ToString() ?? "";
                    string desc = descProp?.GetValue(arcanaData)?.ToString() ?? "";
                    string frameN = frameProp?.GetValue(arcanaData)?.ToString() ?? "";
                    string textureN = textureProp?.GetValue(arcanaData)?.ToString() ?? "";

                    var sprite = LoadArcanaSprite(textureN, frameN);

                    result.Add(new ArcanaInfo
                    {
                        Name = name,
                        Description = desc,
                        Sprite = sprite,
                        ArcanaData = arcanaData,
                        ArcanaType = arcanaType
                    });

                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Arcana] Error getting arcanas for item {itemType}: {ex.Message}");
            }
            return result;
        }

        #endregion
    }

    #region Harmony Patches

    public static class LevelUpPagePatches
    {
        public static void Show_Postfix(LevelUpPage __instance)
        {
            try
            {
                var dataManager = __instance.Data;
                if (dataManager != null)
                {
                    ItemTooltipsMod.CacheDataManager(dataManager);
                }

                // Try to find and cache the game session
                TryCacheGameSessionFromLevelUpPage(__instance);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in LevelUpPage.Show patch: {ex}");
            }
        }

        private static void TryCacheGameSessionFromLevelUpPage(LevelUpPage page)
        {
            if (page == null) return;

            var pageType = page.GetType();

            // Try various property/field names for game session
            string[] sessionNames = { "_gameSession", "GameSession", "gameSession", "_session", "Session" };

            foreach (var name in sessionNames)
            {
                // Try property
                var prop = pageType.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                {
                    try
                    {
                        var session = prop.GetValue(page);
                        if (session != null && ValidateGameSession(session))
                        {
                            ItemTooltipsMod.CacheGameSession(session);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"Error accessing {name} property: {ex.Message}");
                    }
                }

                // Try field
                var field = pageType.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    try
                    {
                        var session = field.GetValue(page);
                        if (session != null && ValidateGameSession(session))
                        {
                            ItemTooltipsMod.CacheGameSession(session);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"Error accessing {name} field: {ex.Message}");
                    }
                }
            }

            // Try to find any property that returns something with ActiveCharacter
            foreach (var prop in pageType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (prop.Name.ToLower().Contains("session") || prop.Name.ToLower().Contains("game"))
                {
                    try
                    {
                        var value = prop.GetValue(page);
                        if (value != null && ValidateGameSession(value))
                        {
                            ItemTooltipsMod.CacheGameSession(value);
                            return;
                        }
                    }
                    catch { }
                }
            }

            MelonLogger.Warning("Could not find GameSession in LevelUpPage!");
        }

        private static bool ValidateGameSession(object session)
        {
            if (session == null) return false;

            var charProp = session.GetType().GetProperty("ActiveCharacter", BindingFlags.Public | BindingFlags.Instance);
            return charProp != null;
        }
    }

    public static class GenericPagePatches
    {
        public static void Show_Postfix(object __instance)
        {
            try
            {
                var dataManager = GetDataManager(__instance);
                if (dataManager != null)
                {
                    ItemTooltipsMod.CacheDataManager(dataManager);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in page Show patch: {ex.Message}");
            }
        }

        private static object GetDataManager(object page)
        {
            var type = page.GetType();

            // Try "Data" property
            var dataProp = type.GetProperty("Data", BindingFlags.Public | BindingFlags.Instance);
            if (dataProp != null)
            {
                var result = dataProp.GetValue(page);
                if (result != null) return result;
            }

            // Try "_data" field
            var dataField = type.GetField("_data", BindingFlags.NonPublic | BindingFlags.Instance);
            if (dataField != null)
            {
                var result = dataField.GetValue(page);
                if (result != null) return result;
            }

            // Try base class
            var baseType = type.BaseType;
            while (baseType != null)
            {
                dataProp = baseType.GetProperty("Data", BindingFlags.Public | BindingFlags.Instance);
                if (dataProp != null)
                {
                    var result = dataProp.GetValue(page);
                    if (result != null) return result;
                }
                baseType = baseType.BaseType;
            }

            return null;
        }
    }

    /// <summary>
    /// Generic patches for ANY component that takes WeaponType or ItemType.
    /// This enables automatic tooltip support on all icon types.
    /// </summary>
    public static class GenericIconPatches
    {
        public static void SetWeapon_Postfix(object __instance, WeaponType __0, MethodBase __originalMethod)
        {
            try
            {
                // Capture arcana-affected weapons from ArcanaInfoPanel
                if (__originalMethod?.DeclaringType?.Name == "ArcanaInfoPanel")
                {
                    ItemTooltipsMod.CaptureArcanaAffectedWeapon(__instance, __0);
                }

                var go = GetGameObject(__instance);
                if (go != null)
                {
                    // For "Add" methods (like AddAffectedWeapon), the icon is a newly created child
                    bool isAddMethod = __originalMethod?.Name?.Contains("Add") ?? false;
                    ItemTooltipsMod.RegisterWeaponUI(go.GetInstanceID(), go, __0, isAddMethod);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in generic weapon patch: {ex.Message}");
            }
        }

        public static void SetItem_Postfix(object __instance, ItemType __0, MethodBase __originalMethod)
        {
            try
            {
                // Capture arcana-affected items from ArcanaInfoPanel
                if (__originalMethod?.DeclaringType?.Name == "ArcanaInfoPanel")
                {
                    ItemTooltipsMod.CaptureArcanaAffectedItem(__instance, __0);
                }

                var go = GetGameObject(__instance);
                if (go != null)
                {
                    bool isAddMethod = __originalMethod?.Name?.Contains("Add") ?? false;
                    ItemTooltipsMod.RegisterItemUI(go.GetInstanceID(), go, __0, isAddMethod);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in generic item patch: {ex.Message}");
            }
        }

        // For methods where WeaponType is the second parameter (index 1)
        // Also tries to cache game session from other params (like page at __2)
        public static void SetWeapon_Postfix_Arg1(object __instance, object __0, WeaponType __1, object[] __args, MethodBase __originalMethod)
        {
            try
            {
                // Try to cache game session from any page-like arguments
                if (__args != null)
                {
                    foreach (var arg in __args)
                    {
                        if (arg != null && !(arg is WeaponType))
                        {
                            TryCacheSessionFromArg(arg);
                        }
                    }
                }

                var go = GetGameObject(__instance);
                if (go != null)
                {
                    bool isAddMethod = __originalMethod?.Name?.Contains("Add") ?? false;
                    ItemTooltipsMod.RegisterWeaponUI(go.GetInstanceID(), go, __1, isAddMethod);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in generic weapon patch (arg1): {ex.Message}");
            }
        }

        // For methods where ItemType is the second parameter (index 1)
        // Also tries to cache game session from other params (like page at __2)
        public static void SetItem_Postfix_Arg1(object __instance, object __0, ItemType __1, object[] __args, MethodBase __originalMethod)
        {
            try
            {
                // Try to cache game session from any page-like arguments
                if (__args != null)
                {
                    foreach (var arg in __args)
                    {
                        if (arg != null && !(arg is ItemType))
                        {
                            TryCacheSessionFromArg(arg);
                        }
                    }
                }

                var go = GetGameObject(__instance);
                if (go != null)
                {
                    bool isAddMethod = __originalMethod?.Name?.Contains("Add") ?? false;
                    ItemTooltipsMod.RegisterItemUI(go.GetInstanceID(), go, __1, isAddMethod);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in generic item patch (arg1): {ex.Message}");
            }
        }

        // For methods where WeaponType is at position 2 or later - use reflection to find it
        public static void SetWeapon_Postfix_ArgN(object __instance, object[] __args, MethodBase __originalMethod)
        {
            try
            {
                var go = GetGameObject(__instance);
                if (go == null) return;

                bool isAddMethod = __originalMethod?.Name?.Contains("Add") ?? false;
                WeaponType? foundType = null;

                // Search all arguments for WeaponType and page objects
                foreach (var arg in __args)
                {
                    if (arg == null) continue;

                    if (arg is WeaponType wt)
                    {
                        foundType = wt;
                    }
                    else
                    {
                        // Try to cache game session from page-like arguments
                        TryCacheSessionFromArg(arg);
                    }
                }

                if (foundType.HasValue)
                {
                    ItemTooltipsMod.RegisterWeaponUI(go.GetInstanceID(), go, foundType.Value, isAddMethod);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in generic weapon patch (argN): {ex.Message}");
            }
        }

        // For methods where ItemType is at position 2 or later
        public static void SetItem_Postfix_ArgN(object __instance, object[] __args, MethodBase __originalMethod)
        {
            try
            {
                var go = GetGameObject(__instance);
                if (go == null) return;

                bool isAddMethod = __originalMethod?.Name?.Contains("Add") ?? false;
                ItemType? foundType = null;

                // Search all arguments for ItemType and page objects
                foreach (var arg in __args)
                {
                    if (arg == null) continue;

                    if (arg is ItemType it)
                    {
                        foundType = it;
                    }
                    else
                    {
                        // Try to cache game session from page-like arguments
                        TryCacheSessionFromArg(arg);
                    }
                }

                if (foundType.HasValue)
                {
                    ItemTooltipsMod.RegisterItemUI(go.GetInstanceID(), go, foundType.Value, isAddMethod);
                    return;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in generic item patch (argN): {ex.Message}");
            }
        }

        // For methods that take ArcanaType (runtime IL2CPP enum) - searches __args for it
        public static void SetArcana_Postfix_ArgN(object __instance, object[] __args, MethodBase __originalMethod)
        {
            try
            {
                var arcanaEnum = ItemTooltipsMod.GetCachedArcanaTypeEnum();
                if (arcanaEnum == null) return;

                var go = GetGameObject(__instance);
                if (go == null) return;

                foreach (var arg in __args)
                {
                    if (arg != null && arg.GetType() == arcanaEnum)
                    {
                        ItemTooltipsMod.RegisterArcanaUI(go.GetInstanceID(), go, arg);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in generic arcana patch (argN): {ex.Message}");
            }
        }

        /// <summary>
        /// Tries to cache the game session from an argument that might be a page object or CharacterController.
        /// </summary>
        private static void TryCacheSessionFromArg(object arg)
        {
            if (arg == null) return;

            var argType = arg.GetType();
            var typeName = argType.Name.ToLower();

            // Check if this arg is a DataManager (has GetConvertedWeapons method)
            if (!ItemTooltipsMod.HasCachedDataManager() && argType.GetMethod("GetConvertedWeapons") != null)
            {
                ItemTooltipsMod.CacheDataManager(arg);
                return;
            }

            // Check if this is a CharacterController - if so, try to get session from it
            if (typeName.Contains("character") || typeName.Contains("controller"))
            {
                TryCacheSessionFromCharacter(arg);
                return;
            }

            // Only try for types that look like pages
            if (!typeName.Contains("page") && !typeName.Contains("view") && !typeName.Contains("window"))
                return;

            // Try common property names for game session
            string[] sessionNames = { "_gameSession", "GameSession", "gameSession", "_session" };

            foreach (var name in sessionNames)
            {
                var prop = argType.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                {
                    try
                    {
                        var session = prop.GetValue(arg);
                        if (session != null)
                        {
                            // Verify it has ActiveCharacter
                            var charProp = session.GetType().GetProperty("ActiveCharacter", BindingFlags.Public | BindingFlags.Instance);
                            if (charProp != null)
                            {
                                ItemTooltipsMod.CacheGameSession(session);
                                return;
                            }
                        }
                    }
                    catch { }
                }

                var field = argType.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    try
                    {
                        var session = field.GetValue(arg);
                        if (session != null)
                        {
                            var charProp = session.GetType().GetProperty("ActiveCharacter", BindingFlags.Public | BindingFlags.Instance);
                            if (charProp != null)
                            {
                                ItemTooltipsMod.CacheGameSession(session);
                                return;
                            }
                        }
                    }
                    catch { }
                }
            }

            // Also search for DataManager reference on page-like objects
            if (!ItemTooltipsMod.HasCachedDataManager())
            {
                var allProps = argType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var prop in allProps)
                {
                    if (prop.PropertyType.Name.Contains("DataManager"))
                    {
                        try
                        {
                            var dm = prop.GetValue(arg);
                            if (dm != null)
                            {
                                ItemTooltipsMod.CacheDataManager(dm);
                                return;
                            }
                        }
                        catch { }
                    }
                }

                var allFields = argType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var field in allFields)
                {
                    if (field.FieldType.Name.Contains("DataManager"))
                    {
                        try
                        {
                            var dm = field.GetValue(arg);
                            if (dm != null)
                            {
                                ItemTooltipsMod.CacheDataManager(dm);
                                return;
                            }
                        }
                        catch { }
                    }
                }
            }
        }

        /// <summary>
        /// Tries to cache the game session from a CharacterController object.
        /// Path: CharacterController → _gameManager → _stage → GameSession
        /// </summary>
        private static void TryCacheSessionFromCharacter(object character)
        {
            if (character == null) return;

            var charType = character.GetType();

            try
            {
                // CharacterController has _gameManager
                var gameManagerProp = charType.GetProperty("_gameManager", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (gameManagerProp == null) return;

                var gameManager = gameManagerProp.GetValue(character);
                if (gameManager == null) return;


                // Try to get GameSession directly from GameManager first
                if (TryGetSessionFromObject(gameManager, "GameManager"))
                    return;

                // Try GameManager._stage
                var stageProp = gameManager.GetType().GetProperty("_stage", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (stageProp != null)
                {
                    var stage = stageProp.GetValue(gameManager);
                    if (stage != null)
                    {
                        if (TryGetSessionFromObject(stage, "Stage"))
                            return;

                        // Log stage properties
                        var stageProps = stage.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                            .Select(p => p.Name)
                            .Take(20)
                            .ToList();
                    }
                }

                // Try GameManager._adventureManager
                var advProp = gameManager.GetType().GetProperty("_adventureManager", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (advProp != null)
                {
                    var advManager = advProp.GetValue(gameManager);
                    if (advManager != null)
                    {
                        if (TryGetSessionFromObject(advManager, "AdventureManager"))
                            return;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TryCacheSessionFromCharacter] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Tries to find GameSession property on an object. Also tries to get DataManager.
        /// </summary>
        private static bool TryGetSessionFromObject(object obj, string objName)
        {
            if (obj == null) return false;

            // Also try to cache DataManager directly from this object (e.g., GameManager might have it)
            TryCacheDataManagerFromObject(obj, objName);

            // GameManager uses "GameSessionData" as the property name!
            string[] sessionNames = { "GameSessionData", "_gameSession", "GameSession", "gameSession", "_session", "Session", "CurrentSession", "_currentSession" };
            foreach (var name in sessionNames)
            {
                try
                {
                    var prop = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (prop != null)
                    {
                        var session = prop.GetValue(obj);
                        if (session != null)
                        {
                            var activeCharProp = session.GetType().GetProperty("ActiveCharacter", BindingFlags.Public | BindingFlags.Instance);
                            if (activeCharProp != null)
                            {
                                ItemTooltipsMod.CacheGameSession(session);
                                return true;
                            }
                        }
                    }
                }
                catch { }
            }
            return false;
        }

        /// <summary>
        /// Tries to cache DataManager from a GameManager component.
        /// </summary>
        public static void TryCacheDataManagerFromGameManager(object gameManager)
        {
            if (gameManager == null) return;
            if (ItemTooltipsMod.HasCachedDataManager()) return;

            var gmType = gameManager.GetType();

            // Try "Data" property first (what UnityExplorer shows)
            string[] dataNames = { "Data", "_data", "DataManager", "_dataManager" };
            foreach (var name in dataNames)
            {
                try
                {
                    // Try property
                    var prop = gmType.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (prop != null)
                    {
                        var dm = prop.GetValue(gameManager);
                        if (dm != null)
                        {
                            if (dm.GetType().Name.Contains("DataManager"))
                            {
                                ItemTooltipsMod.CacheDataManager(dm);
                                return;
                            }
                        }
                    }

                    // Try IL2CPP getter method (get_PropertyName)
                    var getMethod = gmType.GetMethod($"get_{name}", BindingFlags.Public | BindingFlags.Instance);
                    if (getMethod != null)
                    {
                        var dm = getMethod.Invoke(gameManager, null);
                        if (dm != null)
                        {
                            if (dm.GetType().Name.Contains("DataManager"))
                            {
                                ItemTooltipsMod.CacheDataManager(dm);
                                return;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                }
            }

            // List available properties for debugging
            var props = gmType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name).Take(25).ToList();
        }

        /// <summary>
        /// Tries to cache DataManager directly from an object like GameManager.
        /// </summary>
        private static void TryCacheDataManagerFromObject(object obj, string objName)
        {
            if (obj == null) return;

            string[] dataNames = { "Data", "_data", "DataManager", "_dataManager", "data" };
            foreach (var name in dataNames)
            {
                try
                {
                    var prop = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (prop != null)
                    {
                        var dm = prop.GetValue(obj);
                        if (dm != null && dm.GetType().Name.Contains("DataManager"))
                        {
                            ItemTooltipsMod.CacheDataManager(dm);
                            return;
                        }
                    }
                }
                catch { }
            }
        }

        private static UnityEngine.GameObject GetGameObject(object instance)
        {
            var gameObjectProp = instance.GetType().GetProperty("gameObject", BindingFlags.Public | BindingFlags.Instance);
            return gameObjectProp?.GetValue(instance) as UnityEngine.GameObject;
        }
    }

    public static class EquipmentIconPatches
    {
        public static void SetData_Postfix(object __instance)
        {
            try
            {
                var instanceType = __instance.GetType();
                var gameObjectProp = instanceType.GetProperty("gameObject", BindingFlags.Public | BindingFlags.Instance);

                if (gameObjectProp == null) return;

                var go = gameObjectProp.GetValue(__instance) as UnityEngine.GameObject;
                if (go == null) return;

                // Try to get Type property
                var typeProp = instanceType.GetProperty("Type", BindingFlags.Public | BindingFlags.Instance);
                if (typeProp != null)
                {
                    var typeVal = typeProp.GetValue(__instance);
                    if (typeVal is WeaponType wt)
                    {
                        ItemTooltipsMod.RegisterWeaponUI(go.GetInstanceID(), go, wt);
                    }
                    else if (typeVal is ItemType it)
                    {
                        ItemTooltipsMod.RegisterItemUI(go.GetInstanceID(), go, it);
                    }
                }

                // Also try _weaponType or _itemType fields
                var weaponField = instanceType.GetField("_weaponType", BindingFlags.NonPublic | BindingFlags.Instance);
                if (weaponField != null)
                {
                    var val = weaponField.GetValue(__instance);
                    if (val is WeaponType wt)
                    {
                        ItemTooltipsMod.RegisterWeaponUI(go.GetInstanceID(), go, wt);
                    }
                }

                var itemField = instanceType.GetField("_itemType", BindingFlags.NonPublic | BindingFlags.Instance);
                if (itemField != null)
                {
                    var val = itemField.GetValue(__instance);
                    if (val is ItemType it)
                    {
                        ItemTooltipsMod.RegisterItemUI(go.GetInstanceID(), go, it);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in EquipmentIcon patch: {ex.Message}");
            }
        }
    }

    public static class LevelUpItemUIPatches
    {
        // SetWeaponData signature: SetWeaponData(LevelUpPage page, WeaponType type, ...)
        // __0 = page, __1 = type (but we use named param for type)
        public static void SetWeaponData_Postfix(object __instance, object __0, WeaponType type)
        {
            try
            {
                // Try to cache game session from the page parameter (__0 = LevelUpPage)
                TryCacheGameSessionFromPage(__0);

                // Get the GameObject from the instance using reflection
                var instanceType = __instance.GetType();
                var gameObjectProp = instanceType.GetProperty("gameObject", BindingFlags.Public | BindingFlags.Instance);
                var getInstanceIdMethod = instanceType.GetMethod("GetInstanceID", BindingFlags.Public | BindingFlags.Instance);

                if (gameObjectProp != null && getInstanceIdMethod != null)
                {
                    var go = gameObjectProp.GetValue(__instance) as UnityEngine.GameObject;
                    int instanceId = (int)getInstanceIdMethod.Invoke(__instance, null);

                    if (go != null)
                    {
                        ItemTooltipsMod.RegisterWeaponUI(instanceId, go, type);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetWeaponData patch: {ex.Message}");
            }
        }

        // SetItemData signature: SetItemData(ItemType type, ItemData data, LevelUpPage page, ...)
        // __0 = type, __2 = page
        public static void SetItemData_Postfix(object __instance, object __2, ItemType type)
        {
            try
            {
                // Try to cache game session from the page parameter (__2 = LevelUpPage)
                TryCacheGameSessionFromPage(__2);

                var instanceType = __instance.GetType();
                var gameObjectProp = instanceType.GetProperty("gameObject", BindingFlags.Public | BindingFlags.Instance);
                var getInstanceIdMethod = instanceType.GetMethod("GetInstanceID", BindingFlags.Public | BindingFlags.Instance);

                if (gameObjectProp != null && getInstanceIdMethod != null)
                {
                    var go = gameObjectProp.GetValue(__instance) as UnityEngine.GameObject;
                    int instanceId = (int)getInstanceIdMethod.Invoke(__instance, null);

                    if (go != null)
                    {
                        ItemTooltipsMod.RegisterItemUI(instanceId, go, type);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetItemData patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Tries to extract and cache the game session from a LevelUpPage or similar page object.
        /// </summary>
        private static void TryCacheGameSessionFromPage(object page)
        {
            if (page == null) return;

            try
            {
                var pageType = page.GetType();

                // List of property/field names that might hold the game session
                string[] sessionNames = { "_gameSession", "GameSession", "gameSession", "_session", "Session" };

                foreach (var name in sessionNames)
                {
                    // Try property first
                    var prop = pageType.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (prop != null)
                    {
                        var session = prop.GetValue(page);
                        if (session != null)
                        {
                            // Verify it looks like a game session (has ActiveCharacter)
                            var charProp = session.GetType().GetProperty("ActiveCharacter", BindingFlags.Public | BindingFlags.Instance);
                            if (charProp != null)
                            {
                                ItemTooltipsMod.CacheGameSession(session);
                                return;
                            }
                        }
                    }

                    // Try field
                    var field = pageType.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                    {
                        var session = field.GetValue(page);
                        if (session != null)
                        {
                            var charProp = session.GetType().GetProperty("ActiveCharacter", BindingFlags.Public | BindingFlags.Instance);
                            if (charProp != null)
                            {
                                ItemTooltipsMod.CacheGameSession(session);
                                return;
                            }
                        }
                    }
                }

                // If no direct session property, check if the page itself has ActiveCharacter
                // (in case the page IS the session or has it directly)
                var directCharProp = pageType.GetProperty("ActiveCharacter", BindingFlags.Public | BindingFlags.Instance);
                if (directCharProp != null)
                {
                    ItemTooltipsMod.CacheGameSession(page);
                    return;
                }

                // Log what properties we found for debugging
                var allProps = pageType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Select(p => p.Name)
                    .Take(10)
                    .ToList();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error caching session from page: {ex.Message}");
            }
        }
    }

    #endregion
}
