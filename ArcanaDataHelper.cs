using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2CppVampireSurvivors.Data;
using Il2CppVampireSurvivors.Data.Weapons;
using MelonLoader;

namespace VSItemTooltips
{
    /// <summary>
    /// Provides centralized arcana data access, including active arcana detection,
    /// affection checks, UI scanning, and comprehensive queries combining multiple data sources.
    /// All methods are static for easy access throughout the mod.
    /// </summary>
    public static class ArcanaDataHelper
    {
        #region State Management (UI-specific caches)

        // These caches are maintained here but populated by ItemTooltips and patches
        private static Dictionary<int, (HashSet<WeaponType> weapons, HashSet<ItemType> items)> arcanaUICache =
            new Dictionary<int, (HashSet<WeaponType>, HashSet<ItemType>)>();
        private static Dictionary<string, int> arcanaNameToInt = new Dictionary<string, int>();
        private static HashSet<WeaponType> panelCapturedWeapons = new HashSet<WeaponType>();
        private static HashSet<ItemType> panelCapturedItems = new HashSet<ItemType>();
        private static bool arcanaDebugLogged = false;
        private static HashSet<WeaponType> arcanaWeaponDebugLogged = new HashSet<WeaponType>();

        /// <summary>
        /// Clears all per-run cached arcana data (called when scene loads).
        /// </summary>
        public static void ClearPerRunCaches()
        {
            panelCapturedWeapons.Clear();
            panelCapturedItems.Clear();
            arcanaDebugLogged = false;
            arcanaWeaponDebugLogged.Clear();
            // Note: arcanaUICache and arcanaNameToInt persist across runs (UI structure doesn't change)
        }

        #endregion

        #region GameManager Access

        /// <summary>
        /// Gets the game's GameManager singleton instance via reflection.
        /// Uses cached value from GameDataCache if available.
        /// </summary>
        public static object GetGameManager()
        {
            if (GameDataCache.GameManager != null) return GameDataCache.GameManager;

            try
            {
                var assembly = typeof(WeaponData).Assembly;
                var gameManagerType = assembly.GetTypes()
                    .FirstOrDefault(t => t.Name == "GameManager" && !t.IsInterface && typeof(UnityEngine.Component).IsAssignableFrom(t));
                if (gameManagerType == null) return null;

                var findMethod = typeof(UnityEngine.Object).GetMethods()
                    .FirstOrDefault(m => m.Name == "FindObjectOfType" && m.IsGenericMethod && m.GetParameters().Length == 0);
                if (findMethod == null) return null;

                var genericFind = findMethod.MakeGenericMethod(gameManagerType);
                GameDataCache.SetGameManager(genericFind.Invoke(null, null));
                return GameDataCache.GameManager;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Active Arcana Queries

        /// <summary>
        /// Gets all currently active/selected arcana types in the game.
        /// Tries multiple strategies: ActiveArcanas collection, then SelectedArcana from config.
        /// </summary>
        public static List<object> GetAllActiveArcanaTypes()
        {
            var result = new List<object>();
            try
            {
                var assembly = typeof(WeaponData).Assembly;

                if (GameDataCache.ArcanaTypeEnum == null)
                {
                    GameDataCache.SetArcanaTypeEnum(assembly.GetTypes().FirstOrDefault(t => t.Name == "ArcanaType"));
                }
                if (GameDataCache.ArcanaTypeEnum == null) return result;

                var gameMgr = GetGameManager();
                if (gameMgr == null) return result;

                var amProp = gameMgr.GetType().GetProperty("_arcanaManager", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (amProp == null) return result;

                var arcanaMgr = amProp.GetValue(gameMgr);
                if (arcanaMgr == null) return result;

                // One-time debug logging of arcana data structure
                if (!arcanaDebugLogged)
                {
                    arcanaDebugLogged = true;
                    LogArcanaDataStructure(arcanaMgr);
                }

                // Try to find active arcanas collection
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
                        if (activeArcanasList != null) break;
                    }
                    var field = arcanaMgr.GetType().GetField(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                    {
                        activeArcanasList = field.GetValue(arcanaMgr);
                        if (activeArcanasList != null) break;
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
                            if (arcana != null) result.Add(arcana);
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

                var arcanaValues = System.Enum.GetValues(GameDataCache.ArcanaTypeEnum);
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

        /// <summary>
        /// Internal helper to get arcanas from ActiveArcanas property (used for debug logging).
        /// </summary>
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

        /// <summary>
        /// Debug logging of arcana data structure (weapons/items lists with raw int values).
        /// Called once per scene to help understand arcana data format.
        /// </summary>
        private static void LogArcanaDataStructure(object arcanaMgr)
        {
            try
            {
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
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Arcana] Error in debug logging: {ex.Message}");
            }
        }

        #endregion

        #region Arcana Data Lookups

        /// <summary>
        /// Gets the arcana data (name, description, weapons, items) for a given arcana type enum value.
        /// Uses GameDataCache.GetAllArcanas() dictionary.
        /// </summary>
        public static object GetArcanaData(object arcanaType)
        {
            try
            {
                if (GameDataCache.DataManager == null || arcanaType == null) return null;

                var allArcanas = GameDataCache.GetAllArcanas();
                if (allArcanas == null) return null;

                var indexer = allArcanas.GetType().GetProperty("Item");
                if (indexer == null) return null;

                return indexer.GetValue(allArcanas, new object[] { arcanaType });
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Affection Checks

        /// <summary>
        /// Checks if a weapon type is affected by an arcana (appears in the arcana's weapons list).
        /// </summary>
        public static bool IsWeaponAffectedByArcana(WeaponType weaponType, object arcanaData)
        {
            try
            {
                var affected = GetArcanaAffectedWeaponTypes(arcanaData);
                return affected.Contains(weaponType);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if an item type is affected by an arcana (appears in the arcana's items list).
        /// </summary>
        public static bool IsItemAffectedByArcana(ItemType itemType, object arcanaData)
        {
            try
            {
                var affected = GetArcanaAffectedItemTypes(arcanaData);
                return affected.Contains(itemType);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the list of weapon types from an arcana's "weapons" property.
        /// Parses IL2CPP enum list and deduplicates by name.
        /// </summary>
        public static List<WeaponType> GetArcanaAffectedWeaponTypes(object arcanaData)
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

        /// <summary>
        /// Gets the list of item types from an arcana's "items" property.
        /// Parses IL2CPP enum list and deduplicates by name.
        /// </summary>
        public static List<ItemType> GetArcanaAffectedItemTypes(object arcanaData)
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

        #endregion

        #region Type Conversion

        /// <summary>
        /// Converts an IL2CPP arcana type enum value to its integer representation.
        /// Tries multiple strategies: IL2CPP pointer decode, .NET conversion, Unbox method.
        /// Returns -1 if conversion fails.
        /// </summary>
        public static int GetArcanaTypeInt(object arcanaType)
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

        #endregion

        #region UI Scanning & Caching

        /// <summary>
        /// Scans the Collection screen UI to find which weapons/items are displayed for a specific arcana.
        /// Finds the arcana's card by matching the name in TextMeshProUGUI components,
        /// then scans all Image sprites under that card and matches them to the lookup tables.
        /// Results are cached in arcanaUICache for fast subsequent lookups.
        /// </summary>
        public static void ScanArcanaUI(int arcanaTypeInt, string arcanaName)
        {
            if (arcanaUICache.ContainsKey(arcanaTypeInt)) return;
            if (!GameDataCache.LookupTablesBuilt || GameDataCache.SpriteToWeaponType == null || GameDataCache.SpriteToItemType == null) return;

            var weapons = new HashSet<WeaponType>();
            var items = new HashSet<ItemType>();

            try
            {
                // Find TextMeshProUGUI components with the arcana's name
                var allTmps = UnityEngine.Object.FindObjectsOfType<Il2CppTMPro.TextMeshProUGUI>();
                UnityEngine.Transform cardContainer = null;

                // Strip rich text for matching
                string searchName = arcanaName.Trim();
                string shortName = searchName.Contains(" - ") ? searchName.Substring(searchName.IndexOf(" - ") + 3).Trim() : searchName;

                int tmpCount = 0;
                foreach (var tmp in allTmps)
                {
                    if (tmp == null || tmp.text == null) continue;
                    tmpCount++;

                    string tmpText = tmp.text.Trim();
                    string cleanText = System.Text.RegularExpressions.Regex.Replace(tmpText, "<[^>]+>", "").Trim();

                    bool isMatch = tmpText == searchName ||
                                   cleanText == searchName ||
                                   tmpText.Contains(searchName) ||
                                   cleanText.Contains(searchName) ||
                                   tmpText.Contains(shortName) ||
                                   cleanText.Contains(shortName);

                    if (isMatch)
                    {
                        // Walk up to find the card container (try several levels)
                        var candidate = tmp.transform.parent;
                        for (int depth = 0; depth < 8 && candidate != null; depth++)
                        {
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

                if (cardContainer == null) return;

                // Scan all Image components under the card container
                var images = cardContainer.GetComponentsInChildren<UnityEngine.UI.Image>();
                foreach (var img in images)
                {
                    if (img == null || img.sprite == null) continue;

                    string spriteName = img.sprite.name;
                    if (string.IsNullOrEmpty(spriteName)) continue;

                    string cleanName = spriteName;
                    if (cleanName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                        cleanName = cleanName.Substring(0, cleanName.Length - 4);

                    if (GameDataCache.SpriteToWeaponType.TryGetValue(spriteName, out var wt))
                        weapons.Add(wt);
                    else if (GameDataCache.SpriteToWeaponType.TryGetValue(cleanName, out var wt2))
                        weapons.Add(wt2);
                    else if (GameDataCache.SpriteToItemType.TryGetValue(spriteName, out var it))
                        items.Add(it);
                    else if (GameDataCache.SpriteToItemType.TryGetValue(cleanName, out var it2))
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

        #endregion

        #region Comprehensive Queries

        /// <summary>
        /// Gets all weapons affected by an arcana, combining data from:
        /// 1. Static arcana data (arcanaData.weapons property)
        /// 2. Panel-captured data (from Harmony patches on ArcanaInfoPanel)
        /// 3. UI scan data (from Collection screen icon scanning)
        /// </summary>
        public static List<WeaponType> GetAllArcanaAffectedWeaponTypes(object arcanaData, int arcanaTypeInt = -1, string arcanaName = null)
        {
            var result = new HashSet<WeaponType>();

            // 1. Static data from arcana property
            foreach (var wt in GetArcanaAffectedWeaponTypes(arcanaData))
                result.Add(wt);

            // 2. Resolve arcanaTypeInt from name cache if not provided
            if (arcanaTypeInt < 0 && arcanaName == null)
            {
                var nameProp = arcanaData.GetType().GetProperty("name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                arcanaName = nameProp?.GetValue(arcanaData)?.ToString() ?? "";
                if (arcanaNameToInt.TryGetValue(arcanaName, out var cached))
                    arcanaTypeInt = cached;
            }

            // 3. Panel captured data (from Harmony patches)
            foreach (var wt in panelCapturedWeapons)
                result.Add(wt);

            // 4. UI scan data (from Collection screen)
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

        /// <summary>
        /// Gets all items affected by an arcana, combining data from:
        /// 1. Static arcana data (arcanaData.items property)
        /// 2. Panel-captured data (from Harmony patches on ArcanaInfoPanel)
        /// 3. UI scan data (from Collection screen icon scanning)
        /// </summary>
        public static List<ItemType> GetAllArcanaAffectedItemTypes(object arcanaData, int arcanaTypeInt = -1, string arcanaName = null)
        {
            var result = new HashSet<ItemType>();

            // 1. Static data
            foreach (var it in GetArcanaAffectedItemTypes(arcanaData))
                result.Add(it);

            // 2. Resolve arcanaTypeInt
            if (arcanaTypeInt < 0 && arcanaName == null)
            {
                var nameProp = arcanaData.GetType().GetProperty("name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                arcanaName = nameProp?.GetValue(arcanaData)?.ToString() ?? "";
                if (arcanaNameToInt.TryGetValue(arcanaName, out var cached))
                    arcanaTypeInt = cached;
            }

            // 3. Panel captured data
            foreach (var it in panelCapturedItems)
                result.Add(it);

            // 4. UI scan data
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

        #endregion

        #region Capture Methods (Called by Harmony Patches)

        /// <summary>
        /// Called by Harmony patches on ArcanaInfoPanel.AddAffectedWeapon to capture
        /// weapons that are affected by the current arcana being viewed.
        /// Adds to the global panelCapturedWeapons set.
        /// </summary>
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

        /// <summary>
        /// Called by Harmony patches on ArcanaInfoPanel.AddAffectedItem to capture
        /// items that are affected by the current arcana being viewed.
        /// Adds to the global panelCapturedItems set.
        /// </summary>
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

        /// <summary>
        /// Caches the arcana name to int mapping for quick lookups later.
        /// Called when GetArcanaTypeInt is used successfully.
        /// </summary>
        public static void CacheArcanaNameToInt(string arcanaName, int arcanaTypeInt)
        {
            if (arcanaTypeInt >= 0 && !string.IsNullOrEmpty(arcanaName))
                arcanaNameToInt[arcanaName] = arcanaTypeInt;
        }

        #endregion

        #region Active Arcana Info (for UI Display)

        /// <summary>
        /// Structure holding displayable arcana info for UI popups.
        /// </summary>
        public struct ArcanaInfo
        {
            public string Name;
            public string Description;
            public UnityEngine.Sprite Sprite;
            public object ArcanaData;
            public object ArcanaType;
        }

        /// <summary>
        /// Gets all active arcanas that affect a specific weapon, with full display info.
        /// Checks static data, panel captures, and UI scans.
        /// Returns list of ArcanaInfo structs ready for UI display.
        /// </summary>
        public static List<ArcanaInfo> GetActiveArcanasForWeapon(WeaponType weaponType)
        {
            var result = new List<ArcanaInfo>();
            try
            {
                var activeArcanas = GetAllActiveArcanaTypes();
                foreach (var arcanaType in activeArcanas)
                {
                    var arcanaData = GetArcanaData(arcanaType);
                    if (arcanaData == null) continue;

                    // Extract arcana type int and name
                    int arcanaTypeInt = GetArcanaTypeInt(arcanaType);
                    var arcanaNameProp = arcanaData.GetType().GetProperty("name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    string arcanaName = arcanaNameProp?.GetValue(arcanaData)?.ToString() ?? "?";

                    // Cache the name → int mapping
                    CacheArcanaNameToInt(arcanaName, arcanaTypeInt);

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

                    if (!affectedStatic && !affectedPanel && !affectedUI) continue;

                    // Extract display info
                    var nameProp = arcanaData.GetType().GetProperty("name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var descProp = arcanaData.GetType().GetProperty("description", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var frameProp = arcanaData.GetType().GetProperty("frameName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var textureProp = arcanaData.GetType().GetProperty("texture", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    string name = nameProp?.GetValue(arcanaData)?.ToString() ?? "";
                    string desc = descProp?.GetValue(arcanaData)?.ToString() ?? "";
                    string frameN = frameProp?.GetValue(arcanaData)?.ToString() ?? "";
                    string textureN = textureProp?.GetValue(arcanaData)?.ToString() ?? "";

                    var sprite = DataAccessHelper.LoadArcanaSprite(textureN, frameN);

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

        /// <summary>
        /// Gets all active arcanas that affect a specific item, with full display info.
        /// Checks static data, panel captures, and UI scans.
        /// Returns list of ArcanaInfo structs ready for UI display.
        /// </summary>
        public static List<ArcanaInfo> GetActiveArcanasForItem(ItemType itemType)
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

                    CacheArcanaNameToInt(arcanaName, arcanaTypeInt);

                    // Check static, panel, then UI
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

                    if (!affectedStatic && !affectedPanel && !affectedUI) continue;

                    // Extract display info
                    var nameProp = arcanaData.GetType().GetProperty("name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var descProp = arcanaData.GetType().GetProperty("description", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var frameProp = arcanaData.GetType().GetProperty("frameName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var textureProp = arcanaData.GetType().GetProperty("texture", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    string name = nameProp?.GetValue(arcanaData)?.ToString() ?? "";
                    string desc = descProp?.GetValue(arcanaData)?.ToString() ?? "";
                    string frameN = frameProp?.GetValue(arcanaData)?.ToString() ?? "";
                    string textureN = textureProp?.GetValue(arcanaData)?.ToString() ?? "";

                    var sprite = DataAccessHelper.LoadArcanaSprite(textureN, frameN);

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
}
