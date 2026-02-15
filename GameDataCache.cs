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
    /// Manages all cached game data including DataManager, GameSession, evolution formulas, and lookup tables.
    /// Centralized cache management for clean separation of data concerns.
    /// </summary>
    public static class GameDataCache
    {
        #region Cached Data

        // Core game data
        private static object cachedDataManager = null;
        private static object cachedWeaponsDict = null;
        private static object cachedPowerUpsDict = null;

        // Evolution formula cache (built once from game data)
        private static VSItemTooltips.Adapters.EvolutionFormulaCache evolutionCache = null;

        // Game session cache (for accessing player inventory)
        private static object cachedGameSession = null;

        // Arcana cache
        private static System.Type cachedArcanaTypeEnum = null;
        private static object cachedAllArcanas = null;
        private static object cachedGameManager = null;

        // Ban status cache
        private static object cachedLevelUpFactory = null;

        // Sprite name to item type lookup (built on first use)
        private static Dictionary<string, WeaponType> spriteToWeaponType = null;
        private static Dictionary<string, ItemType> spriteToItemType = null;
        private static bool lookupTablesBuilt = false;
        private static bool loggedLookupTables = false;

        #endregion

        #region Public Accessors

        public static object DataManager => cachedDataManager;
        public static object WeaponsDict => cachedWeaponsDict;
        public static object PowerUpsDict => cachedPowerUpsDict;
        public static VSItemTooltips.Adapters.EvolutionFormulaCache EvolutionCache => evolutionCache;
        public static object GameSession => cachedGameSession;
        public static System.Type ArcanaTypeEnum => cachedArcanaTypeEnum;
        public static object GameManager => cachedGameManager;
        public static object LevelUpFactory => cachedLevelUpFactory;

        public static bool HasDataManager => cachedDataManager != null;
        public static bool HasGameSession => cachedGameSession != null;
        public static bool LookupTablesBuilt => lookupTablesBuilt;

        public static Dictionary<string, WeaponType> SpriteToWeaponType => spriteToWeaponType;
        public static Dictionary<string, ItemType> SpriteToItemType => spriteToItemType;

        #endregion

        #region DataManager Caching

        /// <summary>
        /// Caches the DataManager and builds lookup tables and evolution cache.
        /// </summary>
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

                // Build evolution formula cache (this enables EvolutionFormulaService usage)
                try
                {
                    var adapter = new VSItemTooltips.Adapters.GameDataAdapter(null);
                    evolutionCache = new VSItemTooltips.Adapters.EvolutionFormulaCache(adapter, null);
                    evolutionCache.BuildCache();
                    MelonLogger.Msg($"Evolution formula cache built: {evolutionCache.Count} formulas");
                }
                catch (Exception cacheEx)
                {
                    MelonLogger.Warning($"Failed to build evolution cache: {cacheEx.Message}");
                    evolutionCache = null;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error caching data manager: {ex}");
            }
        }

        /// <summary>
        /// Tries to cache DataManager from various static sources.
        /// Called when DataManager is needed but not yet cached (e.g., Collection screen).
        /// </summary>
        public static void TryCacheDataManagerStatic()
        {
            if (cachedDataManager != null) return;

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
                    catch { }
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

        #endregion

        #region GameSession Caching

        /// <summary>
        /// Caches the game session and extracts DataManager from it if not already cached.
        /// </summary>
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
        public static void TryFindGameSession()
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
                try
                {
                    var gameObj = UnityEngine.GameObject.Find("Game");
                    if (gameObj != null)
                    {
                        var components = gameObj.GetComponents<UnityEngine.Component>();
                        foreach (var comp in components)
                        {
                            if (comp == null) continue;
                            var compType = comp.GetType();

                            if (compType.Name.Contains("GameManager"))
                            {
                                var sessionProp = compType.GetProperty("GameSessionData", BindingFlags.Public | BindingFlags.Instance);
                                if (sessionProp != null)
                                {
                                    var session = sessionProp.GetValue(comp);
                                    if (session != null)
                                    {
                                        CacheGameSession(session);
                                        return;
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }

                // Additional methods omitted for brevity - can be added if needed
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error finding game session: {ex.Message}");
            }
        }

        /// <summary>
        /// Tries to get game session from a Unity component. Returns true if found.
        /// </summary>
        public static bool TryGetSessionFromComponent(UnityEngine.Component component)
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

        #endregion

        #region Lookup Tables

        /// <summary>
        /// Builds sprite name → WeaponType/ItemType lookup tables from cached dictionaries.
        /// </summary>
        public static void BuildLookupTables()
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
                            var dataList = indexer.GetValue(weaponsDict, new object[] { key });
                            if (dataList != null)
                            {
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
                                var dataType = dataOrList.GetType();
                                var countProp = dataType.GetProperty("Count");
                                if (countProp != null)
                                {
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

            var frameNameProp = data.GetType().GetProperty("frameName", BindingFlags.Public | BindingFlags.Instance);
            if (frameNameProp != null)
            {
                frameName = frameNameProp.GetValue(data) as string;
            }

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

        #endregion

        #region Special Caches

        /// <summary>
        /// Sets the cached ArcanaType enum (discovered during patching).
        /// </summary>
        public static void SetArcanaTypeEnum(System.Type arcanaTypeEnum)
        {
            cachedArcanaTypeEnum = arcanaTypeEnum;
        }

        /// <summary>
        /// Sets the cached GameManager reference.
        /// </summary>
        public static void SetGameManager(object gameManager)
        {
            cachedGameManager = gameManager;
        }

        /// <summary>
        /// Sets the cached AllArcanas collection.
        /// </summary>
        public static void SetAllArcanas(object allArcanas)
        {
            cachedAllArcanas = allArcanas;
        }

        /// <summary>
        /// Gets AllArcanas from DataManager if not already cached.
        /// </summary>
        public static object GetAllArcanas()
        {
            if (cachedAllArcanas != null) return cachedAllArcanas;

            if (cachedDataManager != null)
            {
                try
                {
                    var allArcanasProp = cachedDataManager.GetType().GetProperty("AllArcanas", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (allArcanasProp != null)
                    {
                        cachedAllArcanas = allArcanasProp.GetValue(cachedDataManager);
                    }
                }
                catch { }
            }

            return cachedAllArcanas;
        }

        /// <summary>
        /// Gets or caches the LevelUpFactory from GameManager.
        /// </summary>
        public static object GetLevelUpFactory()
        {
            if (cachedLevelUpFactory != null) return cachedLevelUpFactory;

            try
            {
                // Get GameManager if not cached
                if (cachedGameManager == null)
                {
                    // Try to find it
                    var assembly = typeof(WeaponData).Assembly;
                    var gameManagerType = assembly.GetTypes().FirstOrDefault(t => t.Name == "GameManager" && !t.IsInterface && typeof(UnityEngine.Component).IsAssignableFrom(t));
                    if (gameManagerType != null)
                    {
                        var findMethod = typeof(UnityEngine.Object).GetMethods()
                            .FirstOrDefault(m => m.Name == "FindObjectOfType" && m.IsGenericMethod && m.GetParameters().Length == 0);
                        if (findMethod != null)
                        {
                            var genericFind = findMethod.MakeGenericMethod(gameManagerType);
                            cachedGameManager = genericFind.Invoke(null, null);
                        }
                    }
                }

                if (cachedGameManager != null)
                {
                    var factoryProp = cachedGameManager.GetType().GetProperty("LevelUpFactory",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (factoryProp != null)
                    {
                        cachedLevelUpFactory = factoryProp.GetValue(cachedGameManager);
                    }
                }
            }
            catch { }

            return cachedLevelUpFactory;
        }

        #endregion

        #region Clear/Reset

        /// <summary>
        /// Clears per-run caches (session, arcanas, etc.) but keeps DataManager.
        /// Called when a new scene loads.
        /// </summary>
        public static void ClearPerRunCaches()
        {
            cachedGameSession = null;
            cachedGameManager = null;
            cachedAllArcanas = null;
            cachedLevelUpFactory = null;
        }

        #endregion
    }
}
