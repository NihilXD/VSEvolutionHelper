#nullable disable
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

[assembly: MelonInfo(typeof(VSItemTooltips.ItemTooltipsMod), "VS Item Tooltips", "1.2.0", "NihilXD")]
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
        private new static HarmonyLib.Harmony harmonyInstance;

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

        // NOTE: All data caching (DataManager, GameSession, lookup tables, etc.) is now in GameDataCache.cs
        // Arcana state is managed by ArcanaDataHelper
        // Local tracking flag only
        private static bool loggedLookupTables = false;

        // Collection screen hover tracking (no EventTriggers - uses per-frame raycast)
        private static Dictionary<int, (UnityEngine.GameObject go, WeaponType? weapon, ItemType? item, object arcanaType)> collectionIcons =
            new Dictionary<int, (UnityEngine.GameObject, WeaponType?, ItemType?, object)>();
        private static int currentCollectionHoverId = -1;
        private static UnityEngine.GameObject collectionPopup = null;

        // Controller/keyboard support
        private static bool usingController = false;
        private static UnityEngine.Vector3 lastMousePosition = UnityEngine.Vector3.zero;
        private static UnityEngine.GameObject lastSelectedObject = null;
        private static float dwellStartTime = 0f;
        private static readonly float DwellDelay = 0.5f;
        private static UnityEngine.GameObject dwellTarget = null;
        private static bool passivePopupShown = false;
        private static bool interactiveMode = false;
        private static List<UnityEngine.GameObject> formulaIcons = new List<UnityEngine.GameObject>();
        private static int currentFormulaIndex = -1;
        private static UnityEngine.GameObject interactiveHighlight = null;
        private static UnityEngine.GameObject preDwellSelection = null; // what was selected before we showed popup
        private static Dictionary<int, (WeaponType? weapon, ItemType? item)> formulaIconData =
            new Dictionary<int, (WeaponType?, ItemType?)>();
        private static UnityEngine.GameObject interactivePopup = null; // which popup interactive mode is on
        private static UnityEngine.GameObject cachedNavigatorArrows = null; // game's ButtonNavigator arrows to hide
        // Collection popup back-stack (tracks history for Backspace navigation)
        private static List<(WeaponType? weapon, ItemType? item, object arcana)> collectionPopupBackStack =
            new List<(WeaponType?, ItemType?, object)>();
        private static (WeaponType? weapon, ItemType? item, object arcana) currentCollectionPopupData;
        // Equipment navigation mode (pause screen)
        private static bool equipmentNavMode = false;
        private static List<UnityEngine.GameObject> equipmentIcons = new List<UnityEngine.GameObject>();
        private static int currentEquipmentIndex = -1;
        private static UnityEngine.GameObject equipmentHighlight = null;

        // Styling
        private static readonly UnityEngine.Color PopupBgColor = new UnityEngine.Color(0.08f, 0.08f, 0.12f, 0.98f);
        private static readonly UnityEngine.Color PopupBorderColor = new UnityEngine.Color(0.5f, 0.5f, 0.7f, 1f);
        private static readonly float IconSize = 48f;
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
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"Error searching assembly for LevelUpItemUI: {ex.Message}");
                    }
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
                        GameDataCache.SetArcanaTypeEnum(arcanaTypeEnum);
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Error discovering ArcanaType enum: {ex.Message}");
                }

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
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"Error searching assembly '{assembly.FullName}' for icon types: {ex.Message}");
                    }
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
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"Error searching assembly for MerchantPage: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not patch MerchantPage: {ex.Message}");
            }
        }

        private static bool triedEarlyCaching = false;

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            // Reset caching state when a new scene loads
            triedEarlyCaching = false;

            // Clear per-run state (handled by GameDataCache)
            GameDataCache.ClearPerRunCaches();

            // Clear local per-run state
            cachedSafeArea = null;

            // Clear arcana-related per-run state (managed by ArcanaDataHelper)
            ArcanaDataHelper.ClearPerRunCaches();
        }

        private void TryEarlyCaching()
        {
            if (triedEarlyCaching) return;
            if (GameDataCache.DataManager != null && GameDataCache.WeaponsDict != null) return;

            triedEarlyCaching = true;

            try
            {
                // Method 1: Try FindObjectOfType for DataManager directly
                try
                {
                    System.Type dataManagerType = null;
                    foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            foreach (var t in assembly.GetTypes())
                            {
                                if (t.Name == "DataManager" && t.Namespace != null && t.Namespace.Contains("VampireSurvivors"))
                                {
                                    dataManagerType = t;
                                    break;
                                }
                            }
                            if (dataManagerType != null) break;
                        }
                        catch (Exception ex)
                        {
                            MelonLogger.Warning($"Error searching assembly for DataManager type: {ex.Message}");
                        }
                    }

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
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Error in FindObjectOfType for DataManager: {ex.Message}");
                }

                // Method 2: Try GameManager.Instance.Data
                System.Type gameManagerType = null;
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        foreach (var t in assembly.GetTypes())
                        {
                            if (t.Name == "GameManager")
                            {
                                gameManagerType = t;
                                break;
                            }
                        }
                        if (gameManagerType != null) break;
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"Error searching assembly for GameManager type in TryEarlyCaching: {ex.Message}");
                    }
                }

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
                        catch (Exception ex)
                        {
                            MelonLogger.Warning($"Error accessing GameManager static member '{member.Name}': {ex.Message}");
                        }
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
                                catch (Exception ex)
                                {
                                    MelonLogger.Warning($"Error accessing property '{prop.Name}' on GameManager instance: {ex.Message}");
                                }
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
            if (!triedEarlyCaching && GameDataCache.DataManager == null)
            {
                TryEarlyCaching();
            }

            // Detect input mode (mouse vs controller/keyboard)
            DetectInputMode();

            // Detect ESC key press to find pause menu
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Escape))
            {
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
                if (GameDataCache.GameSession == null)
                {
                    TryFindGameSession();
                }

                // Setup HUD hovers when paused
                if (hudInventory != null && GameDataCache.GameSession != null)
                {
                    SetupHUDHovers();
                }
            }

            // State change: became unpaused
            if (!isGamePaused && wasGamePaused)
            {
                HidePopup();
                ClearTrackedIcons();
                ResetControllerState();
                loggedScanStatus = false; // Reset so we can warn again if needed
                loggedScanResults = false;
                scannedPauseView = false; // Rescan pause view next time
                scannedWeaponSelection = false;
            }

            // Reset view caches if we've returned to main menu (views destroyed)
            if (!isGamePaused && levelUpView == null && merchantView == null && pauseView == null && itemFoundView == null)
            {
                if (inGameUIFound)
                {
                    inGameUIFound = false;
                    hudSearched = false;
                    hudInventory = null;
                    cachedSafeArea = null;
                }
            }

            wasGamePaused = isGamePaused;

            // Collection screen hover detection (runs even when not in-game)
            if (!isGamePaused && collectionIcons.Count > 0)
            {
                if (usingController)
                    UpdateControllerCollectionDwell();
                else
                    UpdateCollectionHover();
            }

            // Controller/keyboard support: back button and interactive mode (works in both paused and collection)
            if (usingController)
            {
                HandleBackButton();
                UpdateInteractiveMode();

                if (equipmentNavMode)
                    UpdateEquipmentNavMode();

                // Y/Tab button logic:
                // 1. Already interactive → acts as back
                // 2. Not in equipment nav, popup showing → enter interactive mode
                // 3. Pause screen, no equipment nav, no popup → enter equipment nav
                if (IsInteractButtonPressed())
                {
                    if (interactiveMode)
                    {
                        HandleBackButton(true);
                    }
                    else if (!equipmentNavMode && passivePopupShown)
                    {
                        EnterInteractiveMode();
                    }
                    else if (!equipmentNavMode && !passivePopupShown && isGamePaused
                             && pauseView != null && pauseView.activeInHierarchy)
                    {
                        EnterEquipmentNavMode();
                    }
                }

                // A/Space button: enter interactive mode from equipment nav tooltip
                if (IsSubmitButtonPressed() && equipmentNavMode && passivePopupShown && !interactiveMode)
                {
                    EnterInteractiveMode();
                }
            }

            // Only process when game is paused
            if (!isGamePaused) return;

            // Controller/keyboard dwell detection for paused screens (skip if in equipment nav mode)
            if (usingController && !equipmentNavMode)
            {
                UpdateControllerDwell();
            }

            // Throttle scanning
            float currentTime = UnityEngine.Time.unscaledTime;
            if (currentTime - lastScanTime >= scanInterval)
            {
                lastScanTime = currentTime;
                ScanForIcons();

                // Scan weapon selection view (Arma Dio pickup) - checked continuously
                // because it may activate after another view (like ItemFound) already triggered pause
                if (!scannedWeaponSelection && weaponSelectionView != null && weaponSelectionView.activeInHierarchy)
                {
                    ScanWeaponSelectionView(weaponSelectionView);
                }
            }
        }

        #region Pause Detection

        // Cached UI view references (popup/menu views)
        private static UnityEngine.GameObject levelUpView = null;
        private static UnityEngine.GameObject merchantView = null;
        private static UnityEngine.GameObject pauseView = null;
        private static UnityEngine.GameObject itemFoundView = null;
        private static UnityEngine.GameObject arcanaView = null;
        private static UnityEngine.GameObject weaponSelectionView = null;

        // Cached HUD elements (always visible, but only hoverable when paused)
        private static UnityEngine.GameObject hudInventory = null;
        private static bool hudSearched = false;
        private static bool inGameUIFound = false; // True once we've confirmed game UI exists

        // Currently active views (for targeted scanning)
        private static List<UnityEngine.Transform> activeUIContainers = new List<UnityEngine.Transform>();

        // Cached Safe Area transform (parent of all views)
        private static UnityEngine.Transform cachedSafeArea = null;

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
            if (weaponSelectionView == null)
                weaponSelectionView = UnityEngine.GameObject.Find("GAME UI/Canvas - Game UI/Safe Area/View - WeaponSelection");

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
            bool anyViewFound = levelUpView != null || merchantView != null || pauseView != null || itemFoundView != null || weaponSelectionView != null;

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

            if (weaponSelectionView != null && weaponSelectionView.activeInHierarchy)
            {
                activeUIContainers.Add(weaponSelectionView.transform);
                isPaused = true;
            }

            // Also check time scale as fallback, but ONLY if we're in an actual game run
            // (prevents activating on main menu / Collection screen where timeScale may also be 0)
            if (!isPaused && inGameUIFound && UnityEngine.Time.timeScale == 0f)
            {
                // Cache Safe Area transform for efficient repeated access
                if (cachedSafeArea == null)
                {
                    var safeAreaGo = UnityEngine.GameObject.Find("GAME UI/Canvas - Game UI/Safe Area");
                    if (safeAreaGo != null)
                        cachedSafeArea = safeAreaGo.transform;
                }

                // Scan for any active views under Safe Area every frame
                // (handles Arma Dio, and any other views not explicitly tracked)
                if (cachedSafeArea != null)
                {
                    for (int i = 0; i < cachedSafeArea.childCount; i++)
                    {
                        var child = cachedSafeArea.GetChild(i);
                        if (child.gameObject.activeInHierarchy)
                        {
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
                else if (!wasGamePaused)
                {
                    MelonLogger.Warning("Safe Area not found!");
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
            if (!GameDataCache.LookupTablesBuilt)
            {
                BuildLookupTables();
                if (!GameDataCache.LookupTablesBuilt && !loggedScanStatus)
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

                    if (GameDataCache.SpriteToWeaponType != null && GameDataCache.SpriteToWeaponType.TryGetValue(spriteName, out var wt))
                    {
                        weaponType = wt;
                    }
                    else if (GameDataCache.SpriteToItemType != null && GameDataCache.SpriteToItemType.TryGetValue(spriteName, out var it))
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
        private static bool scannedWeaponSelection = false;

        /// <summary>
        /// Scans the Arma Dio weapon selection view for items with WeaponType/ItemType properties.
        /// Walks all children recursively and adds hover tracking to any that have type info.
        /// </summary>
        private static System.Type cachedWeaponSelectionItemType = null;
        private static bool triedFindingWSIType = false;

        private static void ScanWeaponSelectionView(UnityEngine.GameObject viewGo)
        {
            if (scannedWeaponSelection) return;
            scannedWeaponSelection = true;

            // Find the WeaponSelectionItemUI type once via assembly reflection
            if (!triedFindingWSIType)
            {
                triedFindingWSIType = true;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!assembly.FullName.Contains("Il2Cpp")) continue;
                    try
                    {
                        var wsiType = assembly.GetTypes().FirstOrDefault(t => t.Name == "WeaponSelectionItemUI");
                        if (wsiType != null)
                        {
                            cachedWeaponSelectionItemType = wsiType;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"Error searching assembly for WeaponSelectionItemUI: {ex.Message}");
                    }
                }
            }

            if (cachedWeaponSelectionItemType == null)
            {
                MelonLogger.Warning("WeaponSelectionItemUI type not found in assemblies");
                return;
            }

            // Find Content container: Panel → ScrollViewWithSlider → Viewport → Content
            var panel = FindChildRecursive(viewGo.transform, "Panel");
            if (panel == null) { MelonLogger.Warning("Panel not found in WeaponSelection"); return; }

            var scrollView = panel.Find("ScrollViewWithSlider");
            if (scrollView == null) { MelonLogger.Warning("ScrollViewWithSlider not found"); return; }

            var viewport = scrollView.Find("Viewport");
            if (viewport == null) { MelonLogger.Warning("Viewport not found"); return; }

            UnityEngine.Transform content = null;
            for (int i = 0; i < viewport.childCount; i++)
            {
                var child = viewport.GetChild(i);
                if (child.name == "Content") { content = child; break; }
            }
            if (content == null) { MelonLogger.Warning("Content not found"); return; }

            // Cache the get__type method (getter for _type property)
            var getTypeMethod = cachedWeaponSelectionItemType.GetMethod("get__type", BindingFlags.Public | BindingFlags.Instance);
            // Also try GetWeaponType as fallback
            var getWeaponTypeMethod = cachedWeaponSelectionItemType.GetMethod("GetWeaponType", BindingFlags.Public | BindingFlags.Instance);

            int count = 0;
            for (int i = 0; i < content.childCount; i++)
            {
                var item = content.GetChild(i);
                if (!item.gameObject.activeInHierarchy) continue;

                WeaponType? weaponType = null;

                try
                {
                    // Use string-based GetComponent which works in IL2CPP
                    var baseComp = item.gameObject.GetComponent("WeaponSelectionItemUI");
                    if (baseComp != null)
                    {
                        // Cast to the actual type via IL2CPP pointer
                        // IL2CPP proxy objects are constructed with IntPtr to the underlying object
                        var typedComp = System.Activator.CreateInstance(cachedWeaponSelectionItemType, new object[] { baseComp.Pointer });

                        // Try get__type() method
                        if (getTypeMethod != null)
                        {
                            var val = getTypeMethod.Invoke(typedComp, null);
                            if (val is WeaponType wt) weaponType = wt;
                        }

                        // Fallback to GetWeaponType()
                        if (weaponType == null && getWeaponTypeMethod != null)
                        {
                            var val = getWeaponTypeMethod.Invoke(typedComp, null);
                            if (val is WeaponType wt) weaponType = wt;
                        }

                    }
                }
                catch (Exception ex)
                {
                    if (i == 0) MelonLogger.Warning($"Error reading WSI component: {ex.Message}");
                }

                if (weaponType.HasValue)
                {
                    // Add hover to WeaponFrame icon, not the whole card
                    var weaponFrame = item.Find("WeaponFrame");
                    var hoverTarget = weaponFrame != null ? weaponFrame.gameObject : item.gameObject;
                    AddHoverToGameObject(hoverTarget, weaponType, null);
                    count++;
                }
            }

            MelonLogger.Msg($"WeaponSelection: set up hovers on {count}/{content.childCount} items");
        }

        /// <summary>
        /// Recursively scans children for components with WeaponType/ItemType properties and adds hovers.
        /// </summary>
        private static int ScanChildrenForTypes(UnityEngine.Transform parent, int depth, int maxDepth)
        {
            if (depth > maxDepth) return 0;
            int count = 0;

            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (!child.gameObject.activeInHierarchy) continue;

                WeaponType? weaponType = null;
                ItemType? itemType = null;

                var components = child.GetComponents<UnityEngine.Component>();
                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    var compType = comp.GetType();
                    if (compType.Namespace != null && compType.Namespace.StartsWith("UnityEngine")) continue;

                    // Check Type property
                    var typeProp = compType.GetProperty("Type", BindingFlags.Public | BindingFlags.Instance);
                    if (typeProp != null)
                    {
                        try
                        {
                            var typeVal = typeProp.GetValue(comp);
                            if (typeVal is WeaponType wt) weaponType = wt;
                            else if (typeVal is ItemType it) itemType = it;
                        }
                        catch (Exception ex)
                        {
                            MelonLogger.Warning($"Error accessing Type property on component '{compType.Name}': {ex.Message}");
                        }
                    }

                    // Check _type field
                    if (weaponType == null && itemType == null)
                    {
                        var typeField = compType.GetField("_type", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (typeField != null)
                        {
                            try
                            {
                                var typeVal = typeField.GetValue(comp);
                                if (typeVal is WeaponType wt) weaponType = wt;
                                else if (typeVal is ItemType it) itemType = it;
                            }
                            catch (Exception ex)
                            {
                                MelonLogger.Warning($"Error accessing _type field on component '{compType.Name}': {ex.Message}");
                            }
                        }
                    }

                    // Check WeaponType / ItemType properties by name
                    if (weaponType == null && itemType == null)
                    {
                        var weaponProp = compType.GetProperty("WeaponType", BindingFlags.Public | BindingFlags.Instance);
                        if (weaponProp != null)
                        {
                            try
                            {
                                var val = weaponProp.GetValue(comp);
                                if (val is WeaponType wt) weaponType = wt;
                            }
                            catch (Exception ex)
                            {
                                MelonLogger.Warning($"Error accessing WeaponType property on component '{compType.Name}': {ex.Message}");
                            }
                        }

                        var itemProp = compType.GetProperty("ItemType", BindingFlags.Public | BindingFlags.Instance);
                        if (itemProp != null)
                        {
                            try
                            {
                                var val = itemProp.GetValue(comp);
                                if (val is ItemType it) itemType = it;
                            }
                            catch (Exception ex)
                            {
                                MelonLogger.Warning($"Error accessing ItemType property on component '{compType.Name}': {ex.Message}");
                            }
                        }
                    }

                    if (weaponType.HasValue || itemType.HasValue) break;
                }

                if (weaponType.HasValue || itemType.HasValue)
                {
                    AddHoverToGameObject(child.gameObject, weaponType, itemType);
                    count++;
                }

                // Recurse into children
                count += ScanChildrenForTypes(child, depth + 1, maxDepth);
            }

            return count;
        }

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


            for (int i = 0; i < t.childCount && i < 10; i++)
            {
                LogHierarchy(t.GetChild(i), depth + 1, maxDepth);
            }

            if (t.childCount > 10)
            {
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

        // Wrapper method - delegates to GameDataCache
        private static void BuildLookupTables()
        {
            GameDataCache.BuildLookupTables();

            // Log if tables were built (local flag only)
            if (GameDataCache.LookupTablesBuilt && !loggedLookupTables)
            {
                loggedLookupTables = true;
                MelonLogger.Msg($"Built lookup tables: {GameDataCache.SpriteToWeaponType.Count} weapons, {GameDataCache.SpriteToItemType.Count} items");
            }
        }

        public static void CacheGameSession(object gameSession)
        {
            GameDataCache.CacheGameSession(gameSession);
            // Also cache DataManager from the session if we don't have it yet
            if (GameDataCache.DataManager == null && gameSession != null)
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
                                        GameDataCache.CacheGameSession(session);
                                        return;
                                    }
                                }
                            }
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"Error in FindObjectOfType for GameSessionData: {ex.Message}");
                    }
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
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Error finding GameManager from Game GameObject: {ex.Message}");
                }

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
                                            GameDataCache.CacheGameSession(instance);
                                            return;
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    MelonLogger.Warning($"Error accessing Instance property on session type: {ex.Message}");
                                }
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
                                            GameDataCache.CacheGameSession(instance);
                                            return;
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    MelonLogger.Warning($"Error accessing instance field on session type: {ex.Message}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"Error searching for GameSessionData type with Instance: {ex.Message}");
                    }
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
                                GameDataCache.CacheGameSession(session);
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
                                GameDataCache.CacheGameSession(session);
                                return true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Error accessing session property/field '{name}' in TryGetSessionFromComponent: {ex.Message}");
                }
            }

            return false;
        }

        /// <summary>
        /// Scans the HUD inventory and adds hovers to weapon/item icons.
        /// Called when the game is paused and HUD is found.
        /// </summary>
        public static void SetupHUDHovers()
        {
            if (GameDataCache.GameSession == null || hudInventory == null) return;

            try
            {
                // Get ActiveCharacter from game session
                var activeCharProp = GameDataCache.GameSession.GetType().GetProperty("ActiveCharacter", BindingFlags.Public | BindingFlags.Instance);
                if (activeCharProp == null) return;

                var activeChar = activeCharProp.GetValue(GameDataCache.GameSession);
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
            return GameDataCache.DataManager != null;
        }

        // Static version of data manager caching for use outside OnUpdate
        private static void TryCacheDataManagerStatic()
        {
            if (GameDataCache.DataManager != null) return;

            try
            {
                // Try GameManager.Instance.Data
                System.Type gameManagerType = null;
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        foreach (var t in assembly.GetTypes())
                        {
                            if (t.Name == "GameManager")
                            {
                                gameManagerType = t;
                                break;
                            }
                        }
                        if (gameManagerType != null) break;
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"Error searching assembly for GameManager in TryCacheDataManagerStatic: {ex.Message}");
                    }
                }

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
                        catch (Exception ex)
                        {
                            MelonLogger.Warning($"Error accessing static member '{member.Name}' in TryCacheDataManagerStatic: {ex.Message}");
                        }
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
                                catch (Exception ex)
                                {
                                    MelonLogger.Warning($"Error accessing property '{prop.Name}' on GameManager instance in TryCacheDataManagerStatic: {ex.Message}");
                                }
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

        // Wrapper method - delegates to GameDataCache
        public static void CacheDataManager(object dataManager)
        {
            GameDataCache.CacheDataManager(dataManager);
        }

        #endregion

        #region Controller/Keyboard Support

        /// <summary>
        /// Detects whether the player is using mouse or controller/keyboard by tracking
        /// mouse movement and EventSystem selection changes.
        /// </summary>
        private static void DetectInputMode()
        {
            var mousePos = UnityEngine.Input.mousePosition;
            bool mouseMoved = (mousePos - lastMousePosition).sqrMagnitude > 1f;
            lastMousePosition = mousePos;

            if (mouseMoved)
            {
                if (usingController)
                {
                    if (equipmentNavMode) ExitEquipmentNavMode();
                    usingController = false;
                    ExitInteractiveMode();
                    dwellTarget = null;
                    passivePopupShown = false;
                }
                return;
            }

            // Check if EventSystem selection changed (indicates controller/keyboard navigation)
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            if (eventSystem == null) return;

            var currentSelected = eventSystem.currentSelectedGameObject;
            if (currentSelected != lastSelectedObject && currentSelected != null)
            {
                // Selection changed without mouse movement = controller/keyboard input
                if (!usingController)
                {
                    usingController = true;
                }
            }
            lastSelectedObject = currentSelected;
        }

        /// <summary>
        /// Handles controller/keyboard dwell detection for paused screens.
        /// When a tracked icon is focused for DwellDelay seconds, shows a passive popup.
        /// </summary>
        private static void UpdateControllerDwell()
        {
            // Don't process dwell changes while in interactive mode
            if (interactiveMode) return;

            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            if (eventSystem == null) return;

            var selected = eventSystem.currentSelectedGameObject;
            if (selected == null)
            {
                dwellTarget = null;
                return;
            }

            // If selection changed
            if (selected != dwellTarget)
            {
                dwellTarget = selected;
                dwellStartTime = UnityEngine.Time.unscaledTime;

                // If we had a passive popup and selection moved outside of it, close it
                if (passivePopupShown && !interactiveMode)
                {
                    bool insidePopup = false;
                    foreach (var popup in popupStack)
                    {
                        if (popup != null && selected.transform.IsChildOf(popup.transform))
                        {
                            insidePopup = true;
                            break;
                        }
                    }
                    if (!insidePopup)
                    {
                        HideAllPopups();
                        passivePopupShown = false;
                    }
                }
                return;
            }

            // Same selection - check if dwell time elapsed
            if (passivePopupShown) return; // Already showing a popup for this dwell

            float elapsed = UnityEngine.Time.unscaledTime - dwellStartTime;
            if (elapsed < DwellDelay) return;

            // Dwell time reached - try to find a tracked icon for this selection
            var icon = FindTrackedIconForObject(selected);
            if (icon != null)
            {
                preDwellSelection = selected;
                ShowItemPopup(selected.transform, icon.Value.weapon, icon.Value.item);
                passivePopupShown = true;
            }
        }

        /// <summary>
        /// Handles dwell detection while in equipment navigation mode on the pause screen.
        /// Shows tooltip popup after dwelling on an equipment icon.
        /// </summary>
        private static void UpdateEquipmentNavMode()
        {
            if (!equipmentNavMode) return;
            if (interactiveMode) return; // Interactive mode handles its own updates

            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            if (eventSystem == null) return;

            var selected = eventSystem.currentSelectedGameObject;
            if (selected == null)
            {
                dwellTarget = null;
                return;
            }

            // Track which equipment icon is selected and move highlight
            for (int i = 0; i < equipmentIcons.Count; i++)
            {
                if (equipmentIcons[i] != null && (selected == equipmentIcons[i] || selected.transform.IsChildOf(equipmentIcons[i].transform)))
                {
                    if (i != currentEquipmentIndex)
                    {
                        currentEquipmentIndex = i;
                        SetEquipmentHighlight(i);
                    }
                    break;
                }
            }

            // Selection changed
            if (selected != dwellTarget)
            {
                dwellTarget = selected;
                dwellStartTime = UnityEngine.Time.unscaledTime;

                // Close existing popup if selection moved
                if (passivePopupShown)
                {
                    HideAllPopups();
                    passivePopupShown = false;
                }
                return;
            }

            // Same selection — check dwell
            if (passivePopupShown) return;

            float elapsed = UnityEngine.Time.unscaledTime - dwellStartTime;
            if (elapsed < DwellDelay) return;

            // Dwell time reached — show tooltip
            var icon = FindTrackedIconForObject(selected);
            if (icon != null)
            {
                ShowItemPopup(selected.transform, icon.Value.weapon, icon.Value.item);
                passivePopupShown = true;
            }
        }

        /// <summary>
        /// Handles controller/keyboard dwell for the collection screen (not in-game).
        /// </summary>
        private static void UpdateControllerCollectionDwell()
        {
            // Don't process dwell changes while in interactive mode
            if (interactiveMode) return;

            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            if (eventSystem == null) return;

            var selected = eventSystem.currentSelectedGameObject;
            if (selected == null)
            {
                dwellTarget = null;
                return;
            }

            if (selected != dwellTarget)
            {
                dwellTarget = selected;
                dwellStartTime = UnityEngine.Time.unscaledTime;
                // Selection changed — allow new dwell popup
                passivePopupShown = false;
                return;
            }

            // Check dwell
            float elapsed = UnityEngine.Time.unscaledTime - dwellStartTime;
            if (elapsed < DwellDelay) return;
            if (passivePopupShown) return;

            // Look up in collection icons
            int selectedId = selected.GetInstanceID();
            if (collectionIcons.TryGetValue(selectedId, out var iconData))
            {
                preDwellSelection = selected;
                ShowCollectionPopup(iconData.weapon, iconData.item, iconData.arcanaType);
                passivePopupShown = true;
            }
            else
            {
                // Walk parents to find a match (icon might be a child of the tracked object)
                var parent = selected.transform.parent;
                while (parent != null)
                {
                    int parentId = parent.gameObject.GetInstanceID();
                    if (collectionIcons.TryGetValue(parentId, out var parentData))
                    {
                        preDwellSelection = selected;
                        ShowCollectionPopup(parentData.weapon, parentData.item, parentData.arcanaType);
                        passivePopupShown = true;
                        break;
                    }
                    parent = parent.parent;
                }
            }
        }

        /// <summary>
        /// Finds a tracked icon (weapon/item) for the given GameObject, checking the object itself
        /// and walking up its parents.
        /// </summary>
        private static (WeaponType? weapon, ItemType? item)? FindTrackedIconForObject(UnityEngine.GameObject go)
        {
            if (go == null) return null;

            // Check direct match in trackedIcons
            int id = go.GetInstanceID();
            if (trackedIcons.TryGetValue(id, out var tracked))
            {
                return (tracked.WeaponType, tracked.ItemType);
            }

            // Check uiToWeaponType / uiToItemType
            if (uiToWeaponType.TryGetValue(id, out var wt))
                return (wt, null);
            if (uiToItemType.TryGetValue(id, out var it))
                return (null, it);

            // Walk parents
            var parent = go.transform.parent;
            while (parent != null)
            {
                int parentId = parent.gameObject.GetInstanceID();
                if (trackedIcons.TryGetValue(parentId, out var parentTracked))
                    return (parentTracked.WeaponType, parentTracked.ItemType);
                if (uiToWeaponType.TryGetValue(parentId, out var pwt))
                    return (pwt, null);
                if (uiToItemType.TryGetValue(parentId, out var pit))
                    return (null, pit);
                parent = parent.parent;
            }

            return null;
        }

        /// <summary>
        /// Checks if the interact button was pressed (Tab or controller Y/Triangle).
        /// When not in interactive mode this enters it; when already interactive it acts as back.
        /// </summary>
        private static bool IsInteractButtonPressed()
        {
            return UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Tab) ||
                   UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.JoystickButton3); // Y/Triangle
        }

        /// <summary>
        /// Checks if the back button was pressed (Backspace or controller B/Circle).
        /// Note: Escape is handled separately since the game already uses it.
        /// </summary>
        private static bool IsBackButtonPressed()
        {
            return UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Backspace) ||
                   UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.JoystickButton1);
        }

        /// <summary>
        /// Checks if the submit/confirm button was pressed (Space, Enter, or controller A/Cross).
        /// </summary>
        private static bool IsSubmitButtonPressed()
        {
            return UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Space) ||
                   UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Return) ||
                   UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.JoystickButton0); // A/Cross
        }

        /// <summary>
        /// Sets up explicit Navigation links between formula icons, using Y position to
        /// determine rows. Left/right navigates within a row, up/down between rows.
        /// </summary>
        private static void SetupFormulaNavigation()
        {
            if (formulaIcons.Count == 0) return;

            // Group icons into rows by Y position (icons at same Y are in same row)
            // Use a tolerance of 5 units for floating point imprecision
            var rows = new List<List<int>>(); // each row is a list of indices into formulaIcons
            var rowYValues = new List<float>();

            for (int i = 0; i < formulaIcons.Count; i++)
            {
                if (formulaIcons[i] == null) continue;
                float y = formulaIcons[i].transform.localPosition.y;

                int rowIndex = -1;
                for (int r = 0; r < rowYValues.Count; r++)
                {
                    if (UnityEngine.Mathf.Abs(y - rowYValues[r]) < 5f)
                    {
                        rowIndex = r;
                        break;
                    }
                }

                if (rowIndex < 0)
                {
                    rowIndex = rows.Count;
                    rows.Add(new List<int>());
                    rowYValues.Add(y);
                }
                rows[rowIndex].Add(i);
            }

            // Sort rows top-to-bottom (highest Y first in Unity UI)
            var sortedRows = new List<(float y, List<int> indices)>();
            for (int r = 0; r < rows.Count; r++)
                sortedRows.Add((rowYValues[r], rows[r]));
            sortedRows.Sort((a, b) => b.y.CompareTo(a.y));

            // Sort icons within each row left-to-right (lowest X first)
            foreach (var row in sortedRows)
            {
                row.indices.Sort((a, b) =>
                    formulaIcons[a].transform.localPosition.x.CompareTo(
                    formulaIcons[b].transform.localPosition.x));
            }

            // Build a lookup: for each icon index, which row and column is it in?
            var iconRowCol = new Dictionary<int, (int row, int col)>();
            for (int r = 0; r < sortedRows.Count; r++)
            {
                for (int c = 0; c < sortedRows[r].indices.Count; c++)
                {
                    iconRowCol[sortedRows[r].indices[c]] = (r, c);
                }
            }

            // Set up navigation for each icon
            for (int i = 0; i < formulaIcons.Count; i++)
            {
                var btn = formulaIcons[i].GetComponent<UnityEngine.UI.Button>();
                if (btn == null || !iconRowCol.ContainsKey(i)) continue;

                var (row, col) = iconRowCol[i];
                var nav = new UnityEngine.UI.Navigation();
                nav.mode = UnityEngine.UI.Navigation.Mode.Explicit;

                // Left: previous in same row
                if (col > 0)
                {
                    var leftIdx = sortedRows[row].indices[col - 1];
                    var leftBtn = formulaIcons[leftIdx].GetComponent<UnityEngine.UI.Selectable>();
                    if (leftBtn != null) nav.selectOnLeft = leftBtn;
                }

                // Right: next in same row
                if (col < sortedRows[row].indices.Count - 1)
                {
                    var rightIdx = sortedRows[row].indices[col + 1];
                    var rightBtn = formulaIcons[rightIdx].GetComponent<UnityEngine.UI.Selectable>();
                    if (rightBtn != null) nav.selectOnRight = rightBtn;
                }

                // Up: same column (or closest) in row above
                if (row > 0)
                {
                    var aboveRow = sortedRows[row - 1].indices;
                    int aboveCol = System.Math.Min(col, aboveRow.Count - 1);
                    var upBtn = formulaIcons[aboveRow[aboveCol]].GetComponent<UnityEngine.UI.Selectable>();
                    if (upBtn != null) nav.selectOnUp = upBtn;
                }

                // Down: same column (or closest) in row below
                if (row < sortedRows.Count - 1)
                {
                    var belowRow = sortedRows[row + 1].indices;
                    int belowCol = System.Math.Min(col, belowRow.Count - 1);
                    var downBtn = formulaIcons[belowRow[belowCol]].GetComponent<UnityEngine.UI.Selectable>();
                    if (downBtn != null) nav.selectOnDown = downBtn;
                }

                btn.navigation = nav;
            }

        }

        /// <summary>
        /// Enters interactive mode on the current top popup, allowing navigation of formula icons.
        /// </summary>
        private static void EnterInteractiveMode()
        {
            // Use popup stack if available, otherwise try collection popup
            UnityEngine.GameObject topPopup = null;
            if (popupStack.Count > 0)
                topPopup = popupStack[popupStack.Count - 1];
            else if (collectionPopup != null)
                topPopup = collectionPopup;

            if (topPopup == null) return;

            interactiveMode = true;
            interactivePopup = topPopup;
            CollectFormulaIcons(topPopup);

            if (formulaIcons.Count > 0)
            {
                SetupFormulaNavigation();

                // Focus the first formula icon — this steals focus from the game's UI
                currentFormulaIndex = 0;
                var eventSystem = UnityEngine.EventSystems.EventSystem.current;
                if (eventSystem != null)
                {
                    eventSystem.SetSelectedGameObject(formulaIcons[0]);
                }
                SetFormulaHighlight(0);

                // Hide the game's navigator arrows while in interactive mode
                HideNavigatorArrows();
            }
            else
            {
                interactiveMode = false;
                interactivePopup = null;
            }
        }

        /// <summary>
        /// Exits interactive mode, restoring focus to the underlying UI element.
        /// </summary>
        private static void ExitInteractiveMode()
        {
            // Reset navigation on formula icons before clearing
            foreach (var icon in formulaIcons)
            {
                if (icon == null) continue;
                var btn = icon.GetComponent<UnityEngine.UI.Button>();
                if (btn != null)
                {
                    var nav = btn.navigation;
                    nav.mode = UnityEngine.UI.Navigation.Mode.None;
                    btn.navigation = nav;
                }
            }

            interactiveMode = false;
            interactivePopup = null;
            currentFormulaIndex = -1;
            formulaIcons.Clear();

            if (interactiveHighlight != null)
            {
                UnityEngine.Object.Destroy(interactiveHighlight);
                interactiveHighlight = null;
            }

            // Restore the game's navigator arrows
            ShowNavigatorArrows();

            // Restore focus to the element that was selected before dwell
            if (preDwellSelection != null)
            {
                var eventSystem = UnityEngine.EventSystems.EventSystem.current;
                if (eventSystem != null)
                {
                    eventSystem.SetSelectedGameObject(preDwellSelection);
                }
            }
        }

        /// <summary>
        /// Enters equipment navigation mode on the pause screen.
        /// Collects equipment icons, adds Selectable components, sets up navigation, and focuses the first icon.
        /// </summary>
        private static void EnterEquipmentNavMode()
        {
            if (pauseView == null || !pauseView.activeInHierarchy) return;

            var equipmentPanel = FindChildRecursive(pauseView.transform, "EquipmentPanel");
            if (equipmentPanel == null) return;

            var weaponsPanel = equipmentPanel.Find("WeaponsPanel");
            var accessoryPanel = equipmentPanel.Find("AccessoryPanel");

            equipmentIcons.Clear();

            // Collect equipment icons from both panels, tracking counts per panel
            CollectEquipmentIcons(weaponsPanel);
            int weaponIconCount = equipmentIcons.Count;
            CollectEquipmentIcons(accessoryPanel);

            if (equipmentIcons.Count == 0) return;

            // Add Button components for navigation (if not already present)
            foreach (var icon in equipmentIcons)
            {
                if (icon.GetComponent<UnityEngine.UI.Button>() == null)
                {
                    var btn = icon.AddComponent<UnityEngine.UI.Button>();
                    var colors = btn.colors;
                    colors.normalColor = new UnityEngine.Color(1f, 1f, 1f, 0f);
                    colors.highlightedColor = new UnityEngine.Color(1f, 1f, 1f, 0f);
                    colors.pressedColor = new UnityEngine.Color(1f, 1f, 1f, 0f);
                    colors.selectedColor = new UnityEngine.Color(1f, 1f, 1f, 0f);
                    btn.colors = colors;
                }
            }

            // Set up navigation — weapons row then accessories row
            SetupEquipmentNavigation(weaponIconCount);

            // Save current selection and enter equipment nav mode
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            if (eventSystem != null)
                preDwellSelection = eventSystem.currentSelectedGameObject;

            equipmentNavMode = true;
            currentEquipmentIndex = 0;
            dwellTarget = null;
            dwellStartTime = 0f;
            passivePopupShown = false;

            // Focus first icon and highlight it
            if (eventSystem != null)
                eventSystem.SetSelectedGameObject(equipmentIcons[0]);
            SetEquipmentHighlight(0);

            HideNavigatorArrows();
        }

        /// <summary>
        /// Collects EquipmentIconPause children that have a tracked weapon/item type.
        /// </summary>
        private static void CollectEquipmentIcons(UnityEngine.Transform panel)
        {
            if (panel == null) return;

            for (int i = 0; i < panel.childCount; i++)
            {
                var child = panel.GetChild(i);
                if (!child.name.Contains("EquipmentIconPause")) continue;
                if (!child.gameObject.activeInHierarchy) continue;

                // Only include icons that have a tracked type
                var iconData = FindTrackedIconForObject(child.gameObject);
                if (iconData != null)
                    equipmentIcons.Add(child.gameObject);
            }
        }

        /// <summary>
        /// Sets up explicit navigation links between equipment icons.
        /// Groups by Y position (weapons row vs accessories row), left/right within row, up/down between rows.
        /// </summary>
        private static void SetupEquipmentNavigation(int weaponCount)
        {
            if (equipmentIcons.Count == 0) return;

            // Row 0: weapons (indices 0 to weaponCount-1)
            // Row 1: accessories (indices weaponCount to end)
            int accessoryStart = weaponCount;
            int accessoryCount = equipmentIcons.Count - weaponCount;

            for (int i = 0; i < equipmentIcons.Count; i++)
            {
                var btn = equipmentIcons[i].GetComponent<UnityEngine.UI.Button>();
                if (btn == null) continue;

                var nav = new UnityEngine.UI.Navigation();
                nav.mode = UnityEngine.UI.Navigation.Mode.Explicit;

                bool isWeapon = i < weaponCount;
                int rowStart = isWeapon ? 0 : accessoryStart;
                int rowEnd = isWeapon ? weaponCount : equipmentIcons.Count;
                int col = i - rowStart;
                int rowSize = rowEnd - rowStart;

                // Left: previous in same row
                if (col > 0)
                {
                    var leftBtn = equipmentIcons[i - 1].GetComponent<UnityEngine.UI.Selectable>();
                    if (leftBtn != null) nav.selectOnLeft = leftBtn;
                }

                // Right: next in same row
                if (col < rowSize - 1)
                {
                    var rightBtn = equipmentIcons[i + 1].GetComponent<UnityEngine.UI.Selectable>();
                    if (rightBtn != null) nav.selectOnRight = rightBtn;
                }

                // Up/Down between rows
                if (isWeapon && accessoryCount > 0)
                {
                    // Down to accessories — same column or closest
                    int downIdx = accessoryStart + System.Math.Min(col, accessoryCount - 1);
                    var downBtn = equipmentIcons[downIdx].GetComponent<UnityEngine.UI.Selectable>();
                    if (downBtn != null) nav.selectOnDown = downBtn;
                }
                else if (!isWeapon && weaponCount > 0)
                {
                    // Up to weapons — same column or closest
                    int upIdx = System.Math.Min(col, weaponCount - 1);
                    var upBtn = equipmentIcons[upIdx].GetComponent<UnityEngine.UI.Selectable>();
                    if (upBtn != null) nav.selectOnUp = upBtn;
                }

                btn.navigation = nav;
            }

        }

        /// <summary>
        /// Exits equipment navigation mode, restoring focus to the pause menu buttons.
        /// </summary>
        private static void ExitEquipmentNavMode()
        {
            equipmentNavMode = false;
            currentEquipmentIndex = -1;
            dwellTarget = null;

            if (equipmentHighlight != null)
            {
                UnityEngine.Object.Destroy(equipmentHighlight);
                equipmentHighlight = null;
            }

            // Reset navigation on equipment icons
            foreach (var icon in equipmentIcons)
            {
                if (icon == null) continue;
                var btn = icon.GetComponent<UnityEngine.UI.Button>();
                if (btn != null)
                {
                    var nav = btn.navigation;
                    nav.mode = UnityEngine.UI.Navigation.Mode.None;
                    btn.navigation = nav;
                }
            }
            equipmentIcons.Clear();

            // Close any open popups
            if (passivePopupShown)
            {
                HideAllPopups();
                passivePopupShown = false;
            }

            ShowNavigatorArrows();

            // Restore focus to previous selection
            if (preDwellSelection != null)
            {
                var eventSystem = UnityEngine.EventSystems.EventSystem.current;
                if (eventSystem != null)
                    eventSystem.SetSelectedGameObject(preDwellSelection);
            }
        }

        /// <summary>
        /// Collects all formula icons from a popup that have associated weapon/item data.
        /// </summary>
        private static void CollectFormulaIcons(UnityEngine.GameObject popup)
        {
            formulaIcons.Clear();

            // Walk all children with EventTrigger (formula icons have them for click handling)
            var triggers = popup.GetComponentsInChildren<UnityEngine.EventSystems.EventTrigger>(false);
            foreach (var trigger in triggers)
            {
                if (trigger.gameObject == popup) continue; // Skip the popup itself

                int iconId = trigger.gameObject.GetInstanceID();
                if (formulaIconData.ContainsKey(iconId))
                {
                    formulaIcons.Add(trigger.gameObject);
                }
            }
        }

        /// <summary>
        /// Sets the visual highlight on the formula icon at the given index.
        /// </summary>
        private static void SetFormulaHighlight(int index)
        {
            // Remove old highlight
            if (interactiveHighlight != null)
            {
                UnityEngine.Object.Destroy(interactiveHighlight);
                interactiveHighlight = null;
            }

            if (index < 0 || index >= formulaIcons.Count) return;

            var target = formulaIcons[index];
            if (target == null) return;

            // Create a highlight frame around the icon
            interactiveHighlight = new UnityEngine.GameObject("ControllerHighlight");
            interactiveHighlight.transform.SetParent(target.transform, false);

            var highlightRect = interactiveHighlight.AddComponent<UnityEngine.RectTransform>();
            highlightRect.anchorMin = UnityEngine.Vector2.zero;
            highlightRect.anchorMax = UnityEngine.Vector2.one;
            highlightRect.offsetMin = new UnityEngine.Vector2(-3f, -3f);
            highlightRect.offsetMax = new UnityEngine.Vector2(3f, 3f);

            var highlightImage = interactiveHighlight.AddComponent<UnityEngine.UI.Image>();
            highlightImage.color = new UnityEngine.Color(0f, 0.9f, 1f, 0.25f); // Semi-transparent cyan fill
            highlightImage.raycastTarget = false;

            var outline = interactiveHighlight.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = new UnityEngine.Color(0f, 0.9f, 1f, 1f); // Cyan border
            outline.effectDistance = new UnityEngine.Vector2(2f, 2f);
        }

        /// <summary>
        /// Sets the visual highlight on the equipment icon at the given index.
        /// </summary>
        private static void SetEquipmentHighlight(int index)
        {
            if (equipmentHighlight != null)
            {
                UnityEngine.Object.Destroy(equipmentHighlight);
                equipmentHighlight = null;
            }

            if (index < 0 || index >= equipmentIcons.Count) return;

            var target = equipmentIcons[index];
            if (target == null) return;

            // Find the largest child Image to parent the highlight to (the actual visible icon)
            UnityEngine.Transform highlightParent = target.transform;
            float highlightSize = 48f;
            var images = target.GetComponentsInChildren<UnityEngine.UI.Image>(false);
            foreach (var img in images)
            {
                if (img.gameObject == target) continue;
                var imgRect = img.GetComponent<UnityEngine.RectTransform>();
                if (imgRect != null)
                {
                    float size = UnityEngine.Mathf.Max(imgRect.rect.width, imgRect.rect.height);
                    if (size > highlightSize)
                    {
                        highlightSize = size;
                        highlightParent = img.transform;
                    }
                }
            }

            equipmentHighlight = new UnityEngine.GameObject("EquipmentHighlight");
            equipmentHighlight.transform.SetParent(highlightParent, false);

            var highlightRect = equipmentHighlight.AddComponent<UnityEngine.RectTransform>();
            highlightRect.anchorMin = new UnityEngine.Vector2(0.5f, 0.5f);
            highlightRect.anchorMax = new UnityEngine.Vector2(0.5f, 0.5f);
            highlightRect.pivot = new UnityEngine.Vector2(0.5f, 0.5f);
            highlightRect.sizeDelta = new UnityEngine.Vector2(highlightSize + 6f, highlightSize + 6f);

            var highlightImage = equipmentHighlight.AddComponent<UnityEngine.UI.Image>();
            highlightImage.color = new UnityEngine.Color(0f, 0.9f, 1f, 0.25f);
            highlightImage.raycastTarget = false;

            var outline = equipmentHighlight.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = new UnityEngine.Color(0f, 0.9f, 1f, 1f);
            outline.effectDistance = new UnityEngine.Vector2(2f, 2f);
        }

        /// <summary>
        /// Monitors interactive mode state. Navigation and activation are handled by
        /// Button components + EventSystem natively. This tracks which formula icon
        /// is selected and moves the visual highlight. Also handles popup replacement
        /// (when a formula icon is activated, the popup is replaced with a new one).
        /// </summary>
        private static void UpdateInteractiveMode()
        {
            if (!interactiveMode) return;

            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            if (eventSystem == null) return;

            var selected = eventSystem.currentSelectedGameObject;

            // Check if formulaIcons are stale (popup was replaced by Button.onClick)
            // This must happen BEFORE the null-selected check, because when the old popup is
            // destroyed, the selected object (which was on that popup) becomes null too.
            bool stale = formulaIcons.Count == 0;
            if (!stale && formulaIcons[0] == null)
                stale = true;

            if (stale)
            {
                // Popup was replaced — try to re-enter interactive mode on the new popup
                UnityEngine.GameObject newPopup = null;
                if (popupStack.Count > 0)
                    newPopup = popupStack[popupStack.Count - 1];
                else if (collectionPopup != null)
                    newPopup = collectionPopup;

                if (newPopup != null)
                {
                    CollectFormulaIcons(newPopup);
                    if (formulaIcons.Count > 0)
                    {
                        SetupFormulaNavigation();
                        currentFormulaIndex = 0;
                        eventSystem.SetSelectedGameObject(formulaIcons[0]);
                        SetFormulaHighlight(0);
                        return;
                    }
                }
                // Couldn't re-enter — exit
                ExitInteractiveMode();
                return;
            }

            // Check if a new popup was pushed on top (popupStack case — old popup still exists)
            UnityEngine.GameObject currentTop = null;
            if (popupStack.Count > 0)
                currentTop = popupStack[popupStack.Count - 1];
            else if (collectionPopup != null)
                currentTop = collectionPopup;

            if (currentTop != null && currentTop != interactivePopup)
            {
                // Clean up old navigation
                foreach (var icon in formulaIcons)
                {
                    if (icon == null) continue;
                    var btn = icon.GetComponent<UnityEngine.UI.Button>();
                    if (btn != null)
                    {
                        var nav = btn.navigation;
                        nav.mode = UnityEngine.UI.Navigation.Mode.None;
                        btn.navigation = nav;
                    }
                }
                if (interactiveHighlight != null)
                {
                    UnityEngine.Object.Destroy(interactiveHighlight);
                    interactiveHighlight = null;
                }

                interactivePopup = currentTop;
                CollectFormulaIcons(currentTop);
                if (formulaIcons.Count > 0)
                {
                    SetupFormulaNavigation();
                    currentFormulaIndex = 0;
                    eventSystem.SetSelectedGameObject(formulaIcons[0]);
                    SetFormulaHighlight(0);
                    return;
                }
                else
                {
                    // New popup has no formula icons — exit interactive mode
                    ExitInteractiveMode();
                    return;
                }
            }

            // If nothing is selected and icons aren't stale, something else happened (e.g., Escape)
            if (selected == null)
            {
                ExitInteractiveMode();
                return;
            }

            // Track which formula icon is selected and move highlight
            for (int i = 0; i < formulaIcons.Count; i++)
            {
                if (formulaIcons[i] != null && selected == formulaIcons[i])
                {
                    if (i != currentFormulaIndex)
                    {
                        currentFormulaIndex = i;
                        SetFormulaHighlight(i);
                    }
                    return; // Selection is on a formula icon — all good
                }
                // Also check if selected is a child of a formula icon
                if (formulaIcons[i] != null && selected.transform.IsChildOf(formulaIcons[i].transform))
                {
                    if (i != currentFormulaIndex)
                    {
                        currentFormulaIndex = i;
                        SetFormulaHighlight(i);
                    }
                    return;
                }
            }

            // Selection is not on any formula icon — check if it's inside the popup
            bool insidePopup = false;
            foreach (var popup in popupStack)
            {
                if (popup != null && selected.transform.IsChildOf(popup.transform))
                {
                    insidePopup = true;
                    break;
                }
            }
            if (!insidePopup && collectionPopup != null && selected.transform.IsChildOf(collectionPopup.transform))
            {
                insidePopup = true;
            }

            if (!insidePopup)
            {
                ExitInteractiveMode();
            }
        }

        /// <summary>
        /// Handles back button press for closing popup layers.
        /// </summary>
        private static void HandleBackButton(bool force = false)
        {
            if (!force && !IsBackButtonPressed()) return;

            // --- Collection popups: simple close, no back-stack navigation ---
            if (collectionPopup != null && popupStack.Count == 0)
            {
                if (interactiveMode)
                {
                    ExitInteractiveMode();
                }
                HideCollectionPopup();
                passivePopupShown = false;
                collectionPopupBackStack.Clear();
                // Restore focus to the collection grid
                if (preDwellSelection != null)
                {
                    var eventSystem = UnityEngine.EventSystems.EventSystem.current;
                    if (eventSystem != null)
                        eventSystem.SetSelectedGameObject(preDwellSelection);
                }
                return;
            }

            // --- Equipment nav mode on pause screen ---
            if (equipmentNavMode)
            {
                if (interactiveMode)
                {
                    if (popupStack.Count > 1)
                    {
                        ExitInteractiveMode();
                        HideTopPopup();
                        EnterInteractiveMode();
                    }
                    else
                    {
                        // Exit interactive mode, keep popup as passive, stay in equipment nav
                        ExitInteractiveMode();
                        passivePopupShown = true;
                        // Restore focus to the equipment icon (not the pause menu button)
                        if (dwellTarget != null)
                        {
                            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
                            if (eventSystem != null)
                                eventSystem.SetSelectedGameObject(dwellTarget);
                        }
                    }
                }
                else if (passivePopupShown)
                {
                    // Close popup, stay in equipment nav
                    HideAllPopups();
                    passivePopupShown = false;
                }
                else
                {
                    // No popup — exit equipment nav entirely
                    ExitEquipmentNavMode();
                }
                return;
            }

            // --- In-game popupStack: navigate back through layers ---
            if (interactiveMode)
            {
                if (popupStack.Count > 1)
                {
                    // Close top popup, re-enter interactive mode on the one below
                    ExitInteractiveMode();
                    HideTopPopup();
                    EnterInteractiveMode();
                }
                else
                {
                    // Only one popup left — exit interactive mode, keep popup as passive
                    ExitInteractiveMode();
                    passivePopupShown = true;
                }
            }
            else if (passivePopupShown && popupStack.Count > 0)
            {
                // Close the passive popup entirely
                HideAllPopups();
                passivePopupShown = false;
                // Restore focus
                if (preDwellSelection != null)
                {
                    var eventSystem = UnityEngine.EventSystems.EventSystem.current;
                    if (eventSystem != null)
                        eventSystem.SetSelectedGameObject(preDwellSelection);
                }
            }
        }

        /// <summary>
        /// Resets all controller/keyboard state.
        /// </summary>
        private static void ResetControllerState()
        {
            usingController = false;
            dwellTarget = null;
            passivePopupShown = false;
            interactiveMode = false;
            interactivePopup = null;
            formulaIcons.Clear();
            currentFormulaIndex = -1;
            preDwellSelection = null;
            lastSelectedObject = null;
            collectionPopupBackStack.Clear();
            equipmentNavMode = false;
            equipmentIcons.Clear();
            currentEquipmentIndex = -1;
            ShowNavigatorArrows();
            if (interactiveHighlight != null)
            {
                UnityEngine.Object.Destroy(interactiveHighlight);
                interactiveHighlight = null;
            }
            if (equipmentHighlight != null)
            {
                UnityEngine.Object.Destroy(equipmentHighlight);
                equipmentHighlight = null;
            }
        }

        /// <summary>
        /// Hides the game's floating navigator arrows (Safe Area/Navigators/ButtonNavigator).
        /// </summary>
        private static void HideNavigatorArrows()
        {
            if (cachedNavigatorArrows == null)
            {
                cachedNavigatorArrows = UnityEngine.GameObject.Find("GAME UI/Canvas - Game UI/Safe Area/Navigators/ButtonNavigator");
            }
            if (cachedNavigatorArrows != null)
            {
                cachedNavigatorArrows.SetActive(false);
            }
        }

        /// <summary>
        /// Restores the game's floating navigator arrows.
        /// </summary>
        private static void ShowNavigatorArrows()
        {
            if (cachedNavigatorArrows != null)
            {
                cachedNavigatorArrows.SetActive(true);
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

            if (weaponType.HasValue && GameDataCache.WeaponsDict != null)
            {
                var data = GetWeaponData(weaponType.Value);
                if (data != null)
                {
                    itemName = GetLocalizedWeaponName(data, weaponType.Value);
                    description = GetLocalizedWeaponDescription(data, weaponType.Value);
                    itemSprite = GetSpriteForWeapon(weaponType.Value);
                }
            }
            else if (itemType.HasValue && GameDataCache.PowerUpsDict != null)
            {
                var data = GetPowerUpData(itemType.Value);
                if (data != null)
                {
                    itemName = GetLocalizedPowerUpName(data, itemType.Value);
                    description = GetLocalizedPowerUpDescription(data, itemType.Value);
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
                    // Passive items exist as both ItemType and WeaponType. Try to find the
                    // matching WeaponType so we can use AddWeaponEvolutionSection which has
                    // the full passive evolution logic (finds ALL recipes using this item).
                    string itemEnumName = itemType.Value.ToString();
                    bool handledAsWeapon = false;
                    if (System.Enum.TryParse<WeaponType>(itemEnumName, out var matchingWeapon) &&
                        GetWeaponData(matchingWeapon) != null)
                    {
                        yOffset = AddWeaponEvolutionSection(popup.transform, font, matchingWeapon, yOffset, maxWidth);
                        handledAsWeapon = true;
                    }
                    else
                    {
                    }

                    if (!handledAsWeapon)
                    {
                        yOffset = AddItemEvolutionSection(popup.transform, font, itemType.Value, yOffset, maxWidth);
                    }
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

        /// <summary>
        /// Reads the 'requiresMax' list from a WeaponData object and returns a set of WeaponType int values.
        /// </summary>
        /// <summary>
        /// Reads 'requiresMax' from the evolved weapon's data and returns a set of WeaponType int values
        /// indicating which synergy passives need to be at max level.
        /// </summary>
        public static HashSet<int> GetRequiresMaxFromEvolved(string evoInto)
        {
            if (string.IsNullOrEmpty(evoInto)) return null;
            if (!System.Enum.TryParse<WeaponType>(evoInto, out var evoType)) return null;

            var evoData = GetWeaponData(evoType);
            if (evoData == null) return null;

            try
            {
                var prop = evoData.GetType().GetProperty("requiresMax", BindingFlags.Public | BindingFlags.Instance);
                if (prop == null) return null;

                var list = prop.GetValue(evoData);
                if (list == null) return null;

                var countProp = list.GetType().GetProperty("Count");
                if (countProp == null) return null;
                int count = (int)countProp.GetValue(list);
                if (count == 0) return null;

                var indexer = list.GetType().GetProperty("Item");
                if (indexer == null) return null;

                var result = new System.Collections.Generic.HashSet<int>();
                for (int i = 0; i < count; i++)
                {
                    var item = indexer.GetValue(list, new object[] { i });
                    if (item is WeaponType wt)
                        result.Add((int)wt);
                    else if (item != null)
                        result.Add((int)item);
                }
                return result.Count > 0 ? result : null;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading requiresMax from weapon data: {ex.Message}");
            }
            return null;
        }

        // Check if player owns a weapon
        private static bool PlayerOwnsWeapon(WeaponType weaponType) => DataAccessHelper.PlayerOwnsWeapon(weaponType);

        // Check if player owns a passive item
        private static bool PlayerOwnsItem(ItemType itemType) => DataAccessHelper.PlayerOwnsItem(itemType);

        // Check if a WeaponType is equipped as an accessory (passive item).
        // Passive items like Wings have WeaponType entries but are in the accessories slot.
        private static bool PlayerOwnsAccessory(WeaponType weaponType)
        {
            if (GameDataCache.GameSession == null) return false;

            try
            {
                var activeCharProp = GameDataCache.GameSession.GetType().GetProperty("ActiveCharacter", BindingFlags.Public | BindingFlags.Instance);
                if (activeCharProp == null) return false;

                var activeChar = activeCharProp.GetValue(GameDataCache.GameSession);
                if (activeChar == null) return false;

                var accessoriesManagerProp = activeChar.GetType().GetProperty("AccessoriesManager", BindingFlags.Public | BindingFlags.Instance);
                if (accessoriesManagerProp == null) return false;

                var accessoriesManager = accessoriesManagerProp.GetValue(activeChar);
                if (accessoriesManager == null) return false;

                var activeEquipProp = accessoriesManager.GetType().GetProperty("ActiveEquipment", BindingFlags.Public | BindingFlags.Instance);
                if (activeEquipProp == null) return false;

                var equipment = activeEquipProp.GetValue(accessoriesManager);
                if (equipment == null) return false;

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
                        var equipType = typeProp.GetValue(item);
                        if (equipType != null && equipType.ToString() == searchStr)
                            return true;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error checking if player owns accessory '{weaponType}': {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Checks if a weapon type has been banished by the player.
        /// </summary>
        public static bool IsWeaponBanned(WeaponType weaponType) => DataAccessHelper.IsWeaponBanned(weaponType);

        /// <summary>
        /// Checks if an item type has been banished by the player.
        /// </summary>
        public static bool IsItemBanned(ItemType itemType) => DataAccessHelper.IsItemBanned(itemType);

        // Old implementations removed - now in DataAccessHelper
        // Create an icon with optional yellow circle background for owned items and red X for banned
        private static UnityEngine.GameObject CreateFormulaIcon(UnityEngine.Transform parent, string name,
            UnityEngine.Sprite sprite, bool isOwned, bool isBanned, float size, float x, float y)
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

            // Red X overlay for banned items
            if (isBanned)
            {
                // Two crossing red bars rotated ±45°
                for (int barIdx = 0; barIdx < 2; barIdx++)
                {
                    var barObj = new UnityEngine.GameObject(barIdx == 0 ? "BannedBar1" : "BannedBar2");
                    barObj.transform.SetParent(container.transform, false);
                    var barImage = barObj.AddComponent<UnityEngine.UI.Image>();
                    barImage.color = new UnityEngine.Color(1f, 0.15f, 0.15f, 0.9f);
                    barImage.raycastTarget = false;
                    var barRect = barObj.GetComponent<UnityEngine.RectTransform>();
                    barRect.anchorMin = new UnityEngine.Vector2(0.5f, 0.5f);
                    barRect.anchorMax = new UnityEngine.Vector2(0.5f, 0.5f);
                    barRect.pivot = new UnityEngine.Vector2(0.5f, 0.5f);
                    barRect.sizeDelta = new UnityEngine.Vector2(size * 1.2f, size * 0.15f);
                    barRect.localRotation = UnityEngine.Quaternion.Euler(0f, 0f, barIdx == 0 ? 45f : -45f);
                }
            }

            return container;
        }

        /// <summary>
        /// Counts how many weapons use this type as a passive requirement in their evoSynergy.
        /// Optionally filters out dual-weapon partners (weapons that produce the same evolved
        /// weapon as ownEvoInto, which are just the other side of a combo evolution).
        /// Now uses EvolutionFormulaCache for O(1) lookup instead of O(n) iteration.
        /// </summary>
        private static int CountPassiveUses(WeaponType passiveType, string ownEvoInto = null)
        {
            // Use cache if available (fast O(1) lookup)
            if (GameDataCache.EvolutionCache != null)
            {
                return GameDataCache.EvolutionCache.CountPassiveUsages(passiveType, ownEvoInto);
            }

            // Fallback: return 0 if cache not built yet
            return 0;
        }

        // EVOLUTION UI RENDERING - Delegates to EvolutionUIBuilder
        private static float AddWeaponEvolutionSection(UnityEngine.Transform parent, Il2CppTMPro.TMP_FontAsset font, WeaponType weaponType, float yOffset, float maxWidth) =>
            EvolutionUIBuilder.AddWeaponEvolutionSection(parent, font, weaponType, yOffset, maxWidth);

        /// <summary>
        /// For evolved weapons, shows what base weapon + passives created this evolution.
        /// Now uses EvolutionFormulaCache for O(1) lookup instead of O(n) iteration.
        /// </summary>
        private static float AddEvolvedFromSection(UnityEngine.Transform parent, Il2CppTMPro.TMP_FontAsset font, WeaponType evolvedType, float yOffset, float maxWidth) =>
            EvolutionUIBuilder.AddEvolvedFromSection(parent, font, evolvedType, yOffset, maxWidth);

        /// <summary>
        /// Shows evolutions for passive items (items that don't evolve themselves but enable other weapons to evolve).
        /// This handles WeaponType values like DURATION (Spellbinder), MAGNET (Attractorb), etc.
        /// </summary>
        private static float AddPassiveEvolutionSection(UnityEngine.Transform parent, Il2CppTMPro.TMP_FontAsset font, WeaponType passiveType, float yOffset, float maxWidth) =>
            EvolutionUIBuilder.AddPassiveEvolutionSection(parent, font, passiveType, yOffset, maxWidth);

        private static float AddItemEvolutionSection(UnityEngine.Transform parent, Il2CppTMPro.TMP_FontAsset font, ItemType itemType, float yOffset, float maxWidth) =>
            EvolutionUIBuilder.AddItemEvolutionSection(parent, font, itemType, yOffset, maxWidth);

        private static float AddArcanaSection(UnityEngine.Transform parent, Il2CppTMPro.TMP_FontAsset font,
            List<ArcanaInfo> arcanas, float yOffset, float maxWidth)
        {
            // Convert to tuple format for EvolutionUIBuilder
            var arcanaData = arcanas.Select(a => (a.Name, a.Sprite, a.ArcanaData)).ToList();
            return EvolutionUIBuilder.AddArcanaSection(parent, font, arcanaData, yOffset, maxWidth);
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

            var arcanaSprite = DataAccessHelper.LoadArcanaSprite(textureName, frameName);
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

                    // Grid layout using tested IconGridLayout service
                    float iconSize = 38f;
                    float iconSpacing = 6f;
                    float availableWidth = maxWidth - Padding * 2;

                    var gridLayout = new VSItemTooltips.Core.Services.IconGridLayout(iconSize, iconSpacing);
                    var (rows, cols) = gridLayout.CalculateGrid(totalAffected, availableWidth);

                    int itemIndex = 0;

                    // Weapons first
                    foreach (var wt in affectedWeapons)
                    {
                        var (x, y) = gridLayout.GetIconPosition(itemIndex, cols);
                        bool owned = PlayerOwnsWeapon(wt);
                        var sprite = GetSpriteForWeapon(wt);
                        var icon = CreateFormulaIcon(popup.transform, $"AffectedWeapon{itemIndex}", sprite, owned, IsWeaponBanned(wt), iconSize, Padding + x, yOffset + y);
                        AddHoverToGameObject(icon, wt, null, useClick: true);
                        itemIndex++;
                    }

                    // Then passive items
                    foreach (var it in affectedItems)
                    {
                        var (x, y) = gridLayout.GetIconPosition(itemIndex, cols);
                        bool owned = PlayerOwnsItem(it);
                        var sprite = GetSpriteForItem(it);
                        var icon = CreateFormulaIcon(popup.transform, $"AffectedItem{itemIndex}", sprite, owned, IsItemBanned(it), iconSize, Padding + x, yOffset + y);
                        AddHoverToGameObject(icon, null, it, useClick: true);
                        itemIndex++;
                    }

                    // Update yOffset for the total grid height
                    yOffset -= gridLayout.CalculateGridHeight(rows);
                }
            }

            // Set final size
            yOffset -= Padding;
            rect.sizeDelta = new UnityEngine.Vector2(maxWidth, -yOffset);

            return popup;
        }

        internal static void AddArcanaHoverToGameObject(UnityEngine.GameObject go, object arcanaData)
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

        private static void PositionPopup(UnityEngine.GameObject popup, UnityEngine.Transform anchor)
        {
            var popupRect = popup.GetComponent<UnityEngine.RectTransform>();
            if (popupRect == null) return;

            var popupParent = popup.transform.parent;
            if (popupParent == null) return;

            // Convert anchor position to local space
            var localPos = popupParent.InverseTransformPoint(anchor.position);

            // Convert from pivot-relative to anchor-relative
            var parentRect = popupParent.GetComponent<UnityEngine.RectTransform>();
            if (parentRect != null)
            {
                var anchorOffset = parentRect.rect.center;
                localPos.x -= anchorOffset.x;
                localPos.y -= anchorOffset.y;
            }

            // Use PopupPositionCalculator from Core (tested!)
            float parentWidth = parentRect != null ? parentRect.rect.width : 1920f;
            float parentHeight = parentRect != null ? parentRect.rect.height : 1080f;

            var calculator = new VSItemTooltips.Core.Models.PopupPositionCalculator(parentWidth, parentHeight);

            var (posX, posY) = calculator.CalculatePosition(
                localPos.x,
                localPos.y,
                popupRect.sizeDelta.x,
                popupRect.sizeDelta.y,
                usingController
            );

            popupRect.anchoredPosition = new UnityEngine.Vector2(posX, posY);
        }

        /// <summary>
        /// Finds an appropriate parent for the popup by walking up the hierarchy.
        /// </summary>
        private static UnityEngine.Transform FindPopupParent(UnityEngine.Transform anchor)
        {
            // Walk up the hierarchy and attach to any "View -" parent or Safe Area.
            // This handles known views (Level Up, Merchant, etc.) as well as
            // dynamically discovered ones (Arma Dio pickup selection, etc.)
            var current = anchor;
            while (current != null)
            {
                if (current.name.StartsWith("View - ") || current.name == "Safe Area")
                    return current;
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
            // Reset controller/interactive state
            interactiveMode = false;
            passivePopupShown = false;
            formulaIcons.Clear();
            currentFormulaIndex = -1;
            formulaIconData.Clear();
            if (interactiveHighlight != null)
            {
                UnityEngine.Object.Destroy(interactiveHighlight);
                interactiveHighlight = null;
            }

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
                    // Clean up formulaIconData for icons in this popup
                    var triggers = topPopup.GetComponentsInChildren<UnityEngine.EventSystems.EventTrigger>(false);
                    foreach (var trigger in triggers)
                    {
                        formulaIconData.Remove(trigger.gameObject.GetInstanceID());
                    }
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
            // If in interactive mode, push current popup data onto back stack before replacing
            if (interactiveMode && collectionPopup != null)
            {
                collectionPopupBackStack.Add(currentCollectionPopupData);
            }

            HideCollectionPopup();

            // Track what this popup is showing (for back-stack navigation)
            currentCollectionPopupData = (weaponType, itemType, arcanaType);

            // Try to cache data if not cached yet (needed for popup content)
            if (GameDataCache.DataManager == null)
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
                // Clean up formulaIconData for icons in collection popup
                var triggers = collectionPopup.GetComponentsInChildren<UnityEngine.EventSystems.EventTrigger>(false);
                foreach (var trigger in triggers)
                {
                    formulaIconData.Remove(trigger.gameObject.GetInstanceID());
                }
                UnityEngine.Object.Destroy(collectionPopup);
                collectionPopup = null;
            }
            passivePopupShown = false;
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
            return GameDataCache.ArcanaTypeEnum;
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

        internal static void AddHoverToGameObject(UnityEngine.GameObject go, WeaponType? weaponType, ItemType? itemType, bool useClick = false)
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
                // Register for controller/keyboard navigation
                formulaIconData[go.GetInstanceID()] = (weaponType, itemType);

                // Add Button component so this icon can be selected by the EventSystem
                // (enables keyboard/controller navigation and Submit handling)
                var button = go.AddComponent<UnityEngine.UI.Button>();
                var containerImage = go.GetComponent<UnityEngine.UI.Image>();
                if (containerImage != null)
                    button.targetGraphic = containerImage;

                // Color transitions for selection highlight
                var colors = button.colors;
                colors.normalColor = new UnityEngine.Color(0f, 0f, 0f, 0f);
                colors.highlightedColor = new UnityEngine.Color(0f, 0.9f, 1f, 0.2f);
                colors.selectedColor = new UnityEngine.Color(0f, 0.9f, 1f, 0.35f);
                colors.pressedColor = new UnityEngine.Color(0f, 0.9f, 1f, 0.5f);
                colors.fadeDuration = 0.1f;
                button.colors = colors;

                // Wire onClick for both mouse clicks and keyboard Submit (Enter/Space)
                button.onClick.AddListener((UnityEngine.Events.UnityAction)(() =>
                {
                    ShowItemPopup(go.transform, weaponType, itemType);
                }));

                // Disable navigation by default — EnterInteractiveMode sets up explicit links
                var nav = button.navigation;
                nav.mode = UnityEngine.UI.Navigation.Mode.None;
                button.navigation = nav;
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

        #region Data Access Helpers - Delegates to DataAccessHelper

        // All methods in this region are thin wrappers that delegate to DataAccessHelper
        // for clean separation of concerns and reusability.

        public static List<WeaponType> GetOwnedWeaponTypes() => DataAccessHelper.GetOwnedWeaponTypes();
        public static List<ItemType> GetOwnedItemTypes() => DataAccessHelper.GetOwnedItemTypes();
        public static WeaponData GetWeaponData(WeaponType type) => DataAccessHelper.GetWeaponData(type);
        public static object GetPowerUpData(ItemType type) => DataAccessHelper.GetPowerUpData(type);
        private static Il2CppSystem.Collections.Generic.List<WeaponData> GetWeaponDataList(WeaponType type) => DataAccessHelper.GetWeaponDataList(type);
        private static UnityEngine.Sprite GetCircleSprite() => DataAccessHelper.GetCircleSprite();
        private static UnityEngine.Sprite LoadSpriteFromAtlas(string frameName, string atlasName) => DataAccessHelper.LoadSpriteFromAtlas(frameName, atlasName);
        private static UnityEngine.Sprite GetSpriteForWeapon(WeaponType weaponType) => DataAccessHelper.GetSpriteForWeapon(weaponType);
        private static UnityEngine.Sprite GetSpriteForItem(ItemType itemType) => DataAccessHelper.GetSpriteForItem(itemType);
        public static T GetPropertyValue<T>(object obj, string propertyName) => DataAccessHelper.GetPropertyValue<T>(obj, propertyName);
        public static string GetLocalizedWeaponDescription(WeaponData data, WeaponType type) => DataAccessHelper.GetLocalizedWeaponDescription(data, type);
        public static string GetLocalizedWeaponName(WeaponData data, WeaponType type) => DataAccessHelper.GetLocalizedWeaponName(data, type);
        public static string GetLocalizedPowerUpDescription(object data, ItemType type) => DataAccessHelper.GetLocalizedPowerUpDescription(data, type);
        public static string GetLocalizedPowerUpName(object data, ItemType type) => DataAccessHelper.GetLocalizedPowerUpName(data, type);
        private static string GetI2Translation(string term) => DataAccessHelper.GetI2Translation(term);
        private static Il2CppTMPro.TMP_FontAsset GetFont() => DataAccessHelper.GetFont();

        #endregion

        #region Arcana Data Access - Delegates to ArcanaDataHelper

        // All arcana-related methods now delegate to ArcanaDataHelper for clean separation.
        // Local state (UI caches, captured sets) are managed by ArcanaDataHelper internally.

        private static object GetGameManager() => ArcanaDataHelper.GetGameManager();
        private static List<object> GetAllActiveArcanaTypes() => ArcanaDataHelper.GetAllActiveArcanaTypes();
        private static object GetArcanaData(object arcanaType) => ArcanaDataHelper.GetArcanaData(arcanaType);
        private static bool IsWeaponAffectedByArcana(WeaponType weaponType, object arcanaData) => ArcanaDataHelper.IsWeaponAffectedByArcana(weaponType, arcanaData);
        private static bool IsItemAffectedByArcana(ItemType itemType, object arcanaData) => ArcanaDataHelper.IsItemAffectedByArcana(itemType, arcanaData);
        private static List<WeaponType> GetArcanaAffectedWeaponTypes(object arcanaData) => ArcanaDataHelper.GetArcanaAffectedWeaponTypes(arcanaData);
        private static List<ItemType> GetArcanaAffectedItemTypes(object arcanaData) => ArcanaDataHelper.GetArcanaAffectedItemTypes(arcanaData);
        private static int GetArcanaTypeInt(object arcanaType) => ArcanaDataHelper.GetArcanaTypeInt(arcanaType);
        private static List<WeaponType> GetAllArcanaAffectedWeaponTypes(object arcanaData, int arcanaTypeInt = -1, string arcanaName = null) =>
            ArcanaDataHelper.GetAllArcanaAffectedWeaponTypes(arcanaData, arcanaTypeInt, arcanaName);
        private static List<ItemType> GetAllArcanaAffectedItemTypes(object arcanaData, int arcanaTypeInt = -1, string arcanaName = null) =>
            ArcanaDataHelper.GetAllArcanaAffectedItemTypes(arcanaData, arcanaTypeInt, arcanaName);

        // Capture methods (called by Harmony patches)
        public static void CaptureArcanaAffectedWeapon(object arcanaInfoPanel, WeaponType weaponType) =>
            ArcanaDataHelper.CaptureArcanaAffectedWeapon(arcanaInfoPanel, weaponType);
        public static void CaptureArcanaAffectedItem(object arcanaInfoPanel, ItemType itemType) =>
            ArcanaDataHelper.CaptureArcanaAffectedItem(arcanaInfoPanel, itemType);

        // Arcana info for UI display
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
            var helperArcanas = ArcanaDataHelper.GetActiveArcanasForWeapon(weaponType);
            var result = new List<ArcanaInfo>();
            foreach (var arcana in helperArcanas)
            {
                result.Add(new ArcanaInfo
                {
                    Name = arcana.Name,
                    Description = arcana.Description,
                    Sprite = arcana.Sprite,
                    ArcanaData = arcana.ArcanaData,
                    ArcanaType = arcana.ArcanaType
                });
            }
            return result;
        }

        private static List<ArcanaInfo> GetActiveArcanasForItem(ItemType itemType)
        {
            var helperArcanas = ArcanaDataHelper.GetActiveArcanasForItem(itemType);
            var result = new List<ArcanaInfo>();
            foreach (var arcana in helperArcanas)
            {
                result.Add(new ArcanaInfo
                {
                    Name = arcana.Name,
                    Description = arcana.Description,
                    Sprite = arcana.Sprite,
                    ArcanaData = arcana.ArcanaData,
                    ArcanaType = arcana.ArcanaType
                });
            }
            return result;
        }

        // OLD IMPLEMENTATIONS REMOVED - Now in ArcanaDataHelper

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
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"Error accessing property '{prop.Name}' in TryCacheGameSessionFromLevelUpPage: {ex.Message}");
                    }
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
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"Error accessing session property '{name}' in TryCacheSessionFromArg: {ex.Message}");
                    }
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
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"Error accessing session field '{name}' in TryCacheSessionFromArg: {ex.Message}");
                    }
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
                        catch (Exception ex)
                        {
                            MelonLogger.Warning($"Error accessing DataManager property '{prop.Name}' in TryCacheSessionFromArg: {ex.Message}");
                        }
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
                        catch (Exception ex)
                        {
                            MelonLogger.Warning($"Error accessing DataManager field '{field.Name}' in TryCacheSessionFromArg: {ex.Message}");
                        }
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
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Error accessing session property '{name}' on object '{objName}': {ex.Message}");
                }
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
                catch
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
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Error accessing DataManager property '{name}' on object: {ex.Message}");
                }
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