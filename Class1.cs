using System;
using System.Linq;
using System.Reflection;
using MelonLoader;
using HarmonyLib;
using Il2CppVampireSurvivors.UI;
using Il2CppVampireSurvivors.Data.Weapons;
using Il2CppVampireSurvivors.Data;
using Il2CppVampireSurvivors;
using Il2CppSystem.Collections.Generic;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using Il2CppInterop.Runtime;

[assembly: MelonInfo(typeof(VSEvolutionHelper.VSEvolutionHelper), "VSEvolutionHelper", "1.0.0", "Nihil")]
[assembly: MelonGame("poncle", "Vampire Survivors")]

namespace VSEvolutionHelper
{
    public class VSEvolutionHelper : MelonMod
    {
        private static HarmonyLib.Harmony harmonyInstance;

        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("VSEvolutionHelper initialized!");


            harmonyInstance = new HarmonyLib.Harmony("com.nihil.vsevolutionhelper");
            harmonyInstance.PatchAll(typeof(VSEvolutionHelper).Assembly);
        }
    }

    [HarmonyPatch(typeof(LevelUpItemUI), "SetWeaponData")]
    public class LevelUpItemUI_SetWeaponData_Patch
    {
        private static System.Type spriteManagerType = null;

        public static void Postfix(LevelUpItemUI __instance, LevelUpPage page, WeaponType type,
            WeaponData baseData, WeaponData levelData, int index, int newLevel, bool isNew, bool showEvo,
            Il2CppSystem.Collections.Generic.List<UnityEngine.Sprite> evoIcons, UnityEngine.Sprite characterOwner)
        {
            try
            {
                // Debug: log when this is called
                MelonLogger.Msg($"[SetWeaponData] Called: type={type}, page={page != null}, baseData={baseData?.name ?? "null"}");

                if (baseData == null) return;

                // Find SpriteManager type if not already cached (needed for special cases too)
                if (spriteManagerType == null)
                {
                    var assembly = typeof(WeaponData).Assembly;
                    spriteManagerType = assembly.GetTypes().FirstOrDefault(t => t.Name == "SpriteManager");
                }

                // Get data manager early (needed for special cases)
                var dataManager = page.Data;
                if (dataManager == null) return;

                var weaponsDict = dataManager.GetConvertedWeapons();
                var powerUpsDict = dataManager.GetConvertedPowerUpData();
                if (weaponsDict == null || powerUpsDict == null) return;

                // Check for special hardcoded evolutions (Clock Lancet, Laurel)
                // These have no evoInto/evoSynergy because their requirements are pickup-only items
                if (TryBuildSpecialFormula(__instance, page, type, baseData, weaponsDict, powerUpsDict))
                {
                    return; // Handled by special case
                }

                // Only process items that can evolve
                if (string.IsNullOrEmpty(baseData.evoInto))
                {
                    return;
                }

                // Skip if no evolution requirements
                if (baseData.evoSynergy == null || baseData.evoSynergy.Length == 0)
                {
                    return;
                }

                // Check if this item is actually a PowerUp by seeing if it exists in the PowerUps dictionary
                // Real PowerUps (Spinach, Candelabrador) are in powerUpsDict
                // Weapons (even those used by other weapons) are NOT in powerUpsDict
                bool isActualPowerUp = IsInPowerUpsDict(type, powerUpsDict);

                if (isActualPowerUp)
                {
                    // This is a PowerUp card - find ALL weapons that use this PowerUp
                    BuildPowerUpFormulas(__instance, page, type, baseData, weaponsDict, powerUpsDict);
                }
                else
                {
                    // This is a regular weapon (or weapon combo) - show single evolution formula
                    BuildWeaponFormula(__instance, page, type, baseData, weaponsDict, powerUpsDict);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in SetWeaponData patch: {ex}");
            }
        }

        // Count how many weapons use this item in their evoSynergy
        // PowerUps like Spinach are used by many weapons (Fire Wand, Magic Wand, etc.)
        // Weapon combos like Peachone/Ebony Wings only reference each other (1-2 weapons)
        private static int CountWeaponsUsingItem(WeaponType itemType, object weaponsDict)
        {
            int count = 0;
            var dictType = weaponsDict.GetType();
            var keysProperty = dictType.GetProperty("Keys");
            var tryGetMethod = dictType.GetMethod("TryGetValue");

            if (keysProperty == null || tryGetMethod == null) return 0;

            var keysCollection = keysProperty.GetValue(weaponsDict);
            if (keysCollection == null) return 0;

            var getEnumeratorMethod = keysCollection.GetType().GetMethod("GetEnumerator");
            if (getEnumeratorMethod == null) return 0;

            var enumerator = getEnumeratorMethod.Invoke(keysCollection, null);
            var enumeratorType = enumerator.GetType();
            var moveNextMethod = enumeratorType.GetMethod("MoveNext");
            var currentProperty = enumeratorType.GetProperty("Current");

            while ((bool)moveNextMethod.Invoke(enumerator, null))
            {
                var weaponTypeKey = currentProperty.GetValue(enumerator);
                if (weaponTypeKey == null) continue;

                // Skip self
                if (weaponTypeKey.ToString() == itemType.ToString()) continue;

                var weaponParams = new object[] { weaponTypeKey, null };
                var found = (bool)tryGetMethod.Invoke(weaponsDict, weaponParams);
                var weaponList = weaponParams[1];

                if (!found || weaponList == null) continue;

                var countProp = weaponList.GetType().GetProperty("Count");
                var itemProp = weaponList.GetType().GetProperty("Item");
                if (countProp == null || itemProp == null) continue;

                int itemCount = (int)countProp.GetValue(weaponList);
                if (itemCount == 0) continue;

                var weaponData = itemProp.GetValue(weaponList, new object[] { 0 }) as WeaponData;
                if (weaponData == null || weaponData.evoSynergy == null || string.IsNullOrEmpty(weaponData.evoInto))
                    continue;

                // Check if this weapon requires our item
                foreach (var reqType in weaponData.evoSynergy)
                {
                    if (reqType.ToString() == itemType.ToString())
                    {
                        count++;
                        break;
                    }
                }
            }
            return count;
        }

        // Check if a WeaponType actually exists in the PowerUps dictionary
        // This distinguishes real PowerUps (Spinach, Hollow Heart) from weapons
        public static bool IsInPowerUpsDict(WeaponType weaponType, object powerUpsDict)
        {
            try
            {
                // Convert WeaponType to PowerUpType enum
                var gameAssembly = typeof(WeaponData).Assembly;
                var powerUpTypeEnum = gameAssembly.GetType("Il2CppVampireSurvivors.Data.PowerUpType");
                if (powerUpTypeEnum == null) return false;

                // Try to parse as PowerUpType - if the enum value doesn't exist, it's not a PowerUp
                var weaponTypeStr = weaponType.ToString();
                if (!System.Enum.IsDefined(powerUpTypeEnum, weaponTypeStr))
                    return false;

                var powerUpValue = System.Enum.Parse(powerUpTypeEnum, weaponTypeStr);

                // Check if it exists in the dictionary
                var tryGetMethod = powerUpsDict.GetType().GetMethod("TryGetValue");
                if (tryGetMethod == null) return false;

                var powerUpParams = new object[] { powerUpValue, null };
                var found = (bool)tryGetMethod.Invoke(powerUpsDict, powerUpParams);
                return found && powerUpParams[1] != null;
            }
            catch
            {
                return false;
            }
        }

        // Check if a weapon is available (not DLC that user doesn't own)
        public static bool IsWeaponAvailable(WeaponData weaponData)
        {
            if (weaponData == null) return false;

            try
            {
                // Check if the weapon is hidden - DLC content the user doesn't own is marked as hidden
                // (isUnlocked is too restrictive as it also filters base game weapons not yet unlocked)
                var hiddenProp = weaponData.GetType().GetProperty("hidden",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (hiddenProp != null)
                {
                    var isHidden = (bool)hiddenProp.GetValue(weaponData);
                    return !isHidden; // Available if NOT hidden
                }

                // Fallback: assume available if we can't check
                return true;
            }
            catch
            {
                return true;
            }
        }

        // Handle special evolutions that have pickup-only requirements (not in normal data)
        // Clock Lancet + Gold Ring + Silver Ring → Infinite Corridor
        // Laurel + Metaglio Left + Metaglio Right → Crimson Shroud
        private static bool TryBuildSpecialFormula(LevelUpItemUI instance, LevelUpPage page, WeaponType type,
            WeaponData baseData, object weaponsDict, object powerUpsDict)
        {
            string typeStr = type.ToString();
            string weaponName = baseData.name ?? "";

            // Check for Clock Lancet
            if (typeStr.Contains("CLOCK") || weaponName.Contains("Clock Lancet"))
            {
                return BuildClockLancetFormula(instance, page, type, weaponsDict, powerUpsDict);
            }

            // Check for Laurel
            if (typeStr.Contains("LAUREL") || weaponName.Contains("Laurel"))
            {
                return BuildLaurelFormula(instance, page, type, weaponsDict, powerUpsDict);
            }

            return false; // Not a special case
        }

        // Clock Lancet + Gold Ring + Silver Ring → Infinite Corridor
        private static bool BuildClockLancetFormula(LevelUpItemUI instance, LevelUpPage page, WeaponType clockType,
            object weaponsDict, object powerUpsDict)
        {
            try
            {
                var formula = new EvolutionFormula();
                formula.ResultName = "CORRIDOR"; // Internal name for Infinite Corridor

                // 1. Add Clock Lancet
                var clockSprite = TryLoadFromWeapons(clockType, weaponsDict);
                if (clockSprite != null)
                {
                    bool ownsClock = PlayerOwnsRequirement(page, clockType);
                    formula.Ingredients.Add((clockSprite, ownsClock));
                }

                // 2. Add Gold Ring
                if (System.Enum.TryParse<WeaponType>("GOLD", out var goldRingType))
                {
                    var goldSprite = TryLoadFromWeapons(goldRingType, weaponsDict);
                    if (goldSprite != null)
                    {
                        bool ownsGold = PlayerOwnsRequirement(page, goldRingType);
                        formula.Ingredients.Add((goldSprite, ownsGold));
                    }
                }

                // 3. Add Silver Ring
                if (System.Enum.TryParse<WeaponType>("SILVER", out var silverRingType))
                {
                    var silverSprite = TryLoadFromWeapons(silverRingType, weaponsDict);
                    if (silverSprite != null)
                    {
                        bool ownsSilver = PlayerOwnsRequirement(page, silverRingType);
                        formula.Ingredients.Add((silverSprite, ownsSilver));
                    }
                }

                // 4. Load result sprite (Infinite Corridor)
                if (System.Enum.TryParse<WeaponType>("CORRIDOR", out var resultType))
                {
                    formula.ResultSprite = TryLoadFromWeapons(resultType, weaponsDict);
                }

                // Display if we have at least the weapon
                if (formula.Ingredients.Count > 0)
                {
                    var formulas = new System.Collections.Generic.List<EvolutionFormula> { formula };
                    DisplayEvolutionFormulas(instance, formulas);
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error building Clock Lancet formula: {ex.Message}");
            }

            return false;
        }

        // Laurel + Metaglio Left + Metaglio Right → Crimson Shroud
        private static bool BuildLaurelFormula(LevelUpItemUI instance, LevelUpPage page, WeaponType laurelType,
            object weaponsDict, object powerUpsDict)
        {
            try
            {
                var formula = new EvolutionFormula();
                formula.ResultName = "SHROUD"; // Internal name for Crimson Shroud

                // 1. Add Laurel
                var laurelSprite = TryLoadFromWeapons(laurelType, weaponsDict);
                if (laurelSprite != null)
                {
                    bool ownsLaurel = PlayerOwnsRequirement(page, laurelType);
                    formula.Ingredients.Add((laurelSprite, ownsLaurel));
                }

                // 2. Add Metaglio Left
                if (System.Enum.TryParse<WeaponType>("LEFT", out var metaLeftType))
                {
                    var leftSprite = TryLoadFromWeapons(metaLeftType, weaponsDict);
                    if (leftSprite != null)
                    {
                        bool ownsLeft = PlayerOwnsRequirement(page, metaLeftType);
                        formula.Ingredients.Add((leftSprite, ownsLeft));
                    }
                }

                // 3. Add Metaglio Right
                if (System.Enum.TryParse<WeaponType>("RIGHT", out var metaRightType))
                {
                    var rightSprite = TryLoadFromWeapons(metaRightType, weaponsDict);
                    if (rightSprite != null)
                    {
                        bool ownsRight = PlayerOwnsRequirement(page, metaRightType);
                        formula.Ingredients.Add((rightSprite, ownsRight));
                    }
                }

                // 4. Load result sprite (Crimson Shroud)
                if (System.Enum.TryParse<WeaponType>("SHROUD", out var resultType))
                {
                    formula.ResultSprite = TryLoadFromWeapons(resultType, weaponsDict);
                }

                // Display if we have at least the weapon
                if (formula.Ingredients.Count > 0)
                {
                    var formulas = new System.Collections.Generic.List<EvolutionFormula> { formula };
                    DisplayEvolutionFormulas(instance, formulas);
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error building Laurel formula: {ex.Message}");
            }

            return false;
        }

        // Build formula for a regular weapon card
        private static void BuildWeaponFormula(LevelUpItemUI instance, LevelUpPage page, WeaponType type,
            WeaponData baseData, object weaponsDict, object powerUpsDict)
        {
            var formula = new EvolutionFormula();
            formula.ResultName = baseData.evoInto;

            // 1. Add the weapon itself as first ingredient
            var weaponSprite = TryLoadFromWeapons(type, weaponsDict);
            if (weaponSprite != null)
            {
                bool ownsWeapon = PlayerOwnsRequirement(page, type);
                formula.Ingredients.Add((weaponSprite, ownsWeapon));
            }

            // 2. Add all requirements as additional ingredients
            foreach (var reqType in baseData.evoSynergy)
            {
                var sprite = LoadSpriteForRequirement(reqType, weaponsDict, powerUpsDict);
                if (sprite != null)
                {
                    bool playerOwns = PlayerOwnsRequirement(page, reqType);
                    formula.Ingredients.Add((sprite, playerOwns));
                }
            }

            // 3. Load the evolution result sprite
            formula.ResultSprite = LoadEvoResultSprite(baseData.evoInto, weaponsDict);

            // Display the formula
            if (formula.Ingredients.Count > 0)
            {
                var formulas = new System.Collections.Generic.List<EvolutionFormula> { formula };
                DisplayEvolutionFormulas(instance, formulas);
            }
        }

        // Build formulas for a PowerUp card - find ALL weapons that use this PowerUp
        private static void BuildPowerUpFormulas(LevelUpItemUI instance, LevelUpPage page, WeaponType powerUpType,
            WeaponData powerUpData, object weaponsDict, object powerUpsDict)
        {
            var formulas = new System.Collections.Generic.List<EvolutionFormula>();

            // Iterate through all weapons to find ones that require this PowerUp
            var dictType = weaponsDict.GetType();
            var keysProperty = dictType.GetProperty("Keys");
            var tryGetMethod = dictType.GetMethod("TryGetValue");

            if (keysProperty == null || tryGetMethod == null) return;

            var keysCollection = keysProperty.GetValue(weaponsDict);
            if (keysCollection == null) return;

            var getEnumeratorMethod = keysCollection.GetType().GetMethod("GetEnumerator");
            if (getEnumeratorMethod == null) return;

            var enumerator = getEnumeratorMethod.Invoke(keysCollection, null);
            var enumeratorType = enumerator.GetType();
            var moveNextMethod = enumeratorType.GetMethod("MoveNext");
            var currentProperty = enumeratorType.GetProperty("Current");

            while ((bool)moveNextMethod.Invoke(enumerator, null))
            {
                var weaponTypeKey = currentProperty.GetValue(enumerator);
                if (weaponTypeKey == null) continue;

                var weaponParams = new object[] { weaponTypeKey, null };
                var found = (bool)tryGetMethod.Invoke(weaponsDict, weaponParams);
                var weaponList = weaponParams[1];

                if (!found || weaponList == null) continue;

                var countProp = weaponList.GetType().GetProperty("Count");
                var itemProp = weaponList.GetType().GetProperty("Item");
                if (countProp == null || itemProp == null) continue;

                int itemCount = (int)countProp.GetValue(weaponList);
                if (itemCount == 0) continue;

                var weaponData = itemProp.GetValue(weaponList, new object[] { 0 }) as WeaponData;
                if (weaponData == null || weaponData.evoSynergy == null || string.IsNullOrEmpty(weaponData.evoInto))
                    continue;

                // Check if this weapon requires our PowerUp
                bool weaponRequiresThisPowerUp = false;
                foreach (var reqType in weaponData.evoSynergy)
                {
                    if (reqType.ToString() == powerUpType.ToString())
                    {
                        weaponRequiresThisPowerUp = true;
                        break;
                    }
                }

                if (weaponRequiresThisPowerUp)
                {
                    // 1. Get weapon type and check for DLC content
                    WeaponType wType = (WeaponType)weaponTypeKey;

                    // Skip weapons that aren't available (DLC not owned)
                    if (!IsWeaponAvailable(weaponData))
                    {
                        continue;
                    }

                    // Build formula for this weapon
                    var formula = new EvolutionFormula();
                    formula.ResultName = weaponData.evoInto;

                    // 2. Add the weapon as first ingredient
                    var weaponSprite = TryLoadFromWeapons(wType, weaponsDict);

                    // Skip if weapon sprite doesn't load
                    if (weaponSprite == null)
                    {
                        continue;
                    }

                    bool ownsWeapon = PlayerOwnsRequirement(page, wType);
                    formula.PrimaryWeaponOwned = ownsWeapon;
                    formula.Ingredients.Add((weaponSprite, ownsWeapon));

                    // 2. Add all requirements (including this PowerUp)
                    bool missingRequirement = false;
                    foreach (var reqType in weaponData.evoSynergy)
                    {
                        var reqSprite = LoadSpriteForRequirement(reqType, weaponsDict, powerUpsDict);
                        if (reqSprite != null)
                        {
                            bool ownsReq = PlayerOwnsRequirement(page, reqType);
                            formula.Ingredients.Add((reqSprite, ownsReq));
                        }
                        else
                        {
                            // Skip formulas with missing requirement sprites (DLC)
                            missingRequirement = true;
                            break;
                        }
                    }

                    if (missingRequirement) continue;

                    // 3. Load the evolution result sprite
                    formula.ResultSprite = LoadEvoResultSprite(weaponData.evoInto, weaponsDict);

                    // Skip if result sprite doesn't load (DLC not installed)
                    if (formula.ResultSprite == null)
                    {
                        continue;
                    }

                    // Only add if we don't already have this formula (avoid duplicates from weapon variants)
                    bool alreadyExists = formulas.Exists(f => f.ResultName == formula.ResultName);
                    if (formula.Ingredients.Count > 0 && !alreadyExists)
                    {
                        formulas.Add(formula);
                    }
                }
            }

            // Sort formulas: owned weapons first
            formulas.Sort((a, b) => b.PrimaryWeaponOwned.CompareTo(a.PrimaryWeaponOwned));

            // Display all formulas
            if (formulas.Count > 0)
            {
                DisplayEvolutionFormulas(instance, formulas);
            }
        }

        public static UnityEngine.Sprite LoadSpriteForRequirement(WeaponType reqType, object weaponsDict, object powerUpsDict, bool debug = false)
        {
            // Try loading from weapons dictionary first
            var weaponSprite = TryLoadFromWeapons(reqType, weaponsDict, debug);
            if (weaponSprite != null)
            {
                if (debug) MelonLogger.Msg($"[LoadSpriteForRequirement] {reqType}: found in weapons");
                return weaponSprite;
            }

            // Try loading from PowerUps dictionary
            if (debug) MelonLogger.Msg($"[LoadSpriteForRequirement] {reqType}: not in weapons, trying powerups");
            var powerUpSprite = TryLoadFromPowerUps(reqType, powerUpsDict, debug);
            if (powerUpSprite != null)
            {
                if (debug) MelonLogger.Msg($"[LoadSpriteForRequirement] {reqType}: found in powerups");
                return powerUpSprite;
            }

            if (debug) MelonLogger.Msg($"[LoadSpriteForRequirement] {reqType}: not found anywhere");
            return null;
        }

        public static UnityEngine.Sprite TryLoadFromWeapons(WeaponType reqType, object weaponsDict, bool debug = false)
        {
            try
            {
                var tryGetMethod = weaponsDict.GetType().GetMethod("TryGetValue");
                if (tryGetMethod == null)
                {
                    if (debug) MelonLogger.Msg($"[TryLoadFromWeapons] TryGetValue method not found on {weaponsDict.GetType().Name}");
                    return null;
                }

                var weaponParams = new object[] { reqType, null };
                var found = (bool)tryGetMethod.Invoke(weaponsDict, weaponParams);
                var weaponCollection = weaponParams[1];

                if (debug) MelonLogger.Msg($"[TryLoadFromWeapons] {reqType}: found={found}, collection={(weaponCollection != null ? "not null" : "null")}");

                if (found && weaponCollection != null)
                {
                    var collectionType = weaponCollection.GetType();
                    if (debug) MelonLogger.Msg($"[TryLoadFromWeapons] {reqType}: collectionType={collectionType.Name}");

                    var countProperty = collectionType.GetProperty("Count");
                    if (countProperty != null)
                    {
                        int itemCount = (int)countProperty.GetValue(weaponCollection);
                        if (debug) MelonLogger.Msg($"[TryLoadFromWeapons] {reqType}: itemCount={itemCount}");
                        if (itemCount > 0)
                        {
                            var itemProperty = collectionType.GetProperty("Item");
                            if (itemProperty != null)
                            {
                                var firstItem = itemProperty.GetValue(weaponCollection, new object[] { 0 });
                                if (debug) MelonLogger.Msg($"[TryLoadFromWeapons] {reqType}: firstItem={(firstItem != null ? firstItem.GetType().Name : "null")}");
                                if (firstItem != null)
                                {
                                    var weaponData = firstItem as WeaponData;
                                    if (debug) MelonLogger.Msg($"[TryLoadFromWeapons] {reqType}: cast to WeaponData={(weaponData != null ? "success" : "FAILED")}");
                                    if (weaponData != null)
                                    {
                                        var sprite = LoadWeaponSprite(weaponData, debug);
                                        if (debug) MelonLogger.Msg($"[TryLoadFromWeapons] {reqType}: LoadWeaponSprite returned {(sprite != null ? "sprite" : "null")}");
                                        return sprite;
                                    }
                                }
                            }
                            else
                            {
                                if (debug) MelonLogger.Msg($"[TryLoadFromWeapons] {reqType}: Item property not found");
                            }
                        }
                    }
                    else
                    {
                        if (debug) MelonLogger.Msg($"[TryLoadFromWeapons] {reqType}: No Count property, trying direct cast");
                        var weaponData = weaponCollection as WeaponData;
                        if (weaponData != null)
                        {
                            return LoadWeaponSprite(weaponData, debug);
                        }
                        else
                        {
                            if (debug) MelonLogger.Msg($"[TryLoadFromWeapons] {reqType}: Direct cast to WeaponData failed");
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                if (debug) MelonLogger.Error($"[TryLoadFromWeapons] Exception: {ex}");
            }

            return null;
        }

        private static UnityEngine.Sprite LoadWeaponSprite(WeaponData weaponData, bool debug = false)
        {
            try
            {
                if (debug) MelonLogger.Msg($"[LoadWeaponSprite] Checking WeaponData type: {weaponData.GetType().Name}");

                // First, check for direct Sprite property
                var spriteProps = weaponData.GetType().GetProperties()
                    .Where(p => p.PropertyType == typeof(UnityEngine.Sprite))
                    .ToArray();

                if (debug) MelonLogger.Msg($"[LoadWeaponSprite] Found {spriteProps.Length} Sprite properties");

                foreach (var prop in spriteProps)
                {
                    try
                    {
                        var sprite = prop.GetValue(weaponData) as UnityEngine.Sprite;
                        if (debug) MelonLogger.Msg($"[LoadWeaponSprite] Property {prop.Name}: {(sprite != null ? "has sprite" : "null")}");
                        if (sprite != null) return sprite;
                    }
                    catch { }
                }

                // Check for texture and frameName properties (like PowerUpData)
                var textureProp = weaponData.GetType().GetProperty("texture");
                var frameNameProp = weaponData.GetType().GetProperty("frameName");

                if (debug) MelonLogger.Msg($"[LoadWeaponSprite] texture prop: {(textureProp != null ? "found" : "null")}, frameName prop: {(frameNameProp != null ? "found" : "null")}");

                if (textureProp != null && frameNameProp != null)
                {
                    var atlasName = textureProp.GetValue(weaponData) as string;
                    var frameName = frameNameProp.GetValue(weaponData) as string;

                    if (debug) MelonLogger.Msg($"[LoadWeaponSprite] atlas={atlasName}, frame={frameName}");

                    if (!string.IsNullOrEmpty(atlasName) && !string.IsNullOrEmpty(frameName))
                    {
                        return LoadSpriteFromAtlas(frameName, atlasName);
                    }
                }
            }
            catch (System.Exception ex)
            {
                if (debug) MelonLogger.Error($"[LoadWeaponSprite] Exception: {ex}");
            }

            return null;
        }

        private static UnityEngine.Sprite TryLoadFromPowerUps(WeaponType reqType, object powerUpsDict, bool debug = false)
        {
            try
            {
                // Convert WeaponType to PowerUpType
                var gameAssembly = typeof(WeaponData).Assembly;
                var powerUpTypeEnum = gameAssembly.GetType("Il2CppVampireSurvivors.Data.PowerUpType");

                if (powerUpTypeEnum == null)
                {
                    if (debug) MelonLogger.Msg($"[TryLoadFromPowerUps] PowerUpType enum not found");
                    return null;
                }

                var powerUpTypeStr = reqType.ToString();
                object powerUpValue;
                try
                {
                    powerUpValue = System.Enum.Parse(powerUpTypeEnum, powerUpTypeStr);
                }
                catch
                {
                    if (debug) MelonLogger.Msg($"[TryLoadFromPowerUps] {reqType} not a valid PowerUpType");
                    return null;
                }

                var tryGetMethod = powerUpsDict.GetType().GetMethod("TryGetValue");
                if (tryGetMethod == null)
                {
                    if (debug) MelonLogger.Msg($"[TryLoadFromPowerUps] TryGetValue not found");
                    return null;
                }

                var powerUpParams = new object[] { powerUpValue, null };
                var found = (bool)tryGetMethod.Invoke(powerUpsDict, powerUpParams);
                var powerUpList = powerUpParams[1];

                if (debug) MelonLogger.Msg($"[TryLoadFromPowerUps] {reqType}: found={found}, list={(powerUpList != null ? "not null" : "null")}");

                if (found && powerUpList != null)
                {
                    // PowerUp dictionary returns a List - get first item
                    var countProperty = powerUpList.GetType().GetProperty("Count");
                    if (countProperty != null)
                    {
                        int itemCount = (int)countProperty.GetValue(powerUpList);
                        if (itemCount > 0)
                        {
                            var itemProperty = powerUpList.GetType().GetProperty("Item");
                            if (itemProperty != null)
                            {
                                var firstItem = itemProperty.GetValue(powerUpList, new object[] { 0 });
                                if (firstItem != null)
                                {
                                    var textureProp = firstItem.GetType().GetProperty("_texture_k__BackingField");
                                    var frameNameProp = firstItem.GetType().GetProperty("_frameName_k__BackingField");

                                    if (textureProp != null && frameNameProp != null)
                                    {
                                        var atlasName = textureProp.GetValue(firstItem) as string;
                                        var frameName = frameNameProp.GetValue(firstItem) as string;

                                        if (!string.IsNullOrEmpty(atlasName) && !string.IsNullOrEmpty(frameName))
                                        {
                                            return LoadSpriteFromAtlas(frameName, atlasName);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                // Expected for non-PowerUp requirements, don't log
            }

            return null;
        }

        private static UnityEngine.Sprite LoadSpriteFromAtlas(string frameName, string atlasName)
        {
            try
            {
                // Initialize spriteManagerType if needed (may not be set if no level-up has happened yet)
                if (spriteManagerType == null)
                {
                    var assembly = typeof(WeaponData).Assembly;
                    spriteManagerType = assembly.GetTypes().FirstOrDefault(t => t.Name == "SpriteManager");
                    MelonLogger.Msg($"[LoadSpriteFromAtlas] Initialized spriteManagerType: {(spriteManagerType != null ? "success" : "FAILED")}");
                }

                if (spriteManagerType == null) return null;

                var getSpriteFastMethod = spriteManagerType.GetMethod("GetSpriteFast",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static,
                    null,
                    new System.Type[] { typeof(string), typeof(string) },
                    null);

                if (getSpriteFastMethod != null)
                {
                    var result = getSpriteFastMethod.Invoke(null, new object[] { frameName, atlasName }) as UnityEngine.Sprite;

                    if (result == null)
                    {
                        // Try without .png extension
                        var nameWithoutExt = frameName.Replace(".png", "");
                        result = getSpriteFastMethod.Invoke(null, new object[] { nameWithoutExt, atlasName }) as UnityEngine.Sprite;
                    }

                    return result;
                }
            }
            catch { }

            return null;
        }

        // Helper method to check an ActiveEquipment list for a specific type
        private static bool CheckEquipmentList(object manager, WeaponType reqType)
        {
            try
            {
                // Get ActiveEquipment list from the manager (works for both WeaponsManager and AccessoriesManager)
                var activeEquipmentProp = manager.GetType().GetProperty("ActiveEquipment",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (activeEquipmentProp == null) return false;

                var activeEquipment = activeEquipmentProp.GetValue(manager);
                if (activeEquipment == null) return false;

                // Get count
                var countProp = activeEquipment.GetType().GetProperty("Count");
                if (countProp == null) return false;

                int count = (int)countProp.GetValue(activeEquipment);

                // Get indexer
                var itemProp = activeEquipment.GetType().GetProperty("Item");
                if (itemProp == null) return false;

                // Loop through each Equipment and check Type
                for (int i = 0; i < count; i++)
                {
                    var equipment = itemProp.GetValue(activeEquipment, new object[] { i });
                    if (equipment == null) continue;

                    var typeProp = equipment.GetType().GetProperty("Type",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);

                    if (typeProp != null)
                    {
                        var equipmentType = typeProp.GetValue(equipment);
                        if (equipmentType != null && equipmentType.ToString() == reqType.ToString())
                        {
                            return true; // Found it!
                        }
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        // Helper method to check if player owns a specific weapon or item
        public static bool PlayerOwnsRequirement(LevelUpPage page, WeaponType reqType)
        {
            try
            {
                // Navigate to real player inventory: _gameSession → ActiveCharacter → WeaponsManager → ActiveEquipment
                var gameSessionProp = page.GetType().GetProperty("_gameSession",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (gameSessionProp == null) return false;

                var gameSession = gameSessionProp.GetValue(page);
                if (gameSession == null) return false;

                var activeCharProp = gameSession.GetType().GetProperty("ActiveCharacter",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (activeCharProp == null) return false;

                var activeChar = activeCharProp.GetValue(gameSession);
                if (activeChar == null) return false;

                // Check BOTH WeaponsManager (for weapons) AND AccessoriesManager (for PowerUps)

                // 1. Check WeaponsManager.ActiveEquipment
                var weaponsManagerProp = activeChar.GetType().GetProperty("WeaponsManager",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (weaponsManagerProp != null)
                {
                    var weaponsManager = weaponsManagerProp.GetValue(activeChar);
                    if (weaponsManager != null)
                    {
                        if (CheckEquipmentList(weaponsManager, reqType))
                            return true;
                    }
                }

                // 2. Check AccessoriesManager.ActiveEquipment (for PowerUps)
                var accessoriesManagerProp = activeChar.GetType().GetProperty("AccessoriesManager",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (accessoriesManagerProp != null)
                {
                    var accessoriesManager = accessoriesManagerProp.GetValue(activeChar);
                    if (accessoriesManager != null)
                    {
                        if (CheckEquipmentList(accessoriesManager, reqType))
                            return true;
                    }
                }

                // Not found in either inventory
                return false;
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"Error checking inventory for {reqType}: {ex.Message}");
                return false; // Default to false if we can't check (don't highlight)
            }
        }

        // Helper to get full GameObject path for debugging
        private static string GetGameObjectPath(UnityEngine.Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }

        // Data structure for a single evolution formula
        public class EvolutionFormula
        {
            public System.Collections.Generic.List<(UnityEngine.Sprite sprite, bool owned)> Ingredients = new System.Collections.Generic.List<(UnityEngine.Sprite, bool)>();
            public UnityEngine.Sprite ResultSprite;
            public string ResultName;
            public bool PrimaryWeaponOwned; // True if player owns the main weapon for this formula
        }

        // Cache for TMP font
        private static Il2CppTMPro.TMP_FontAsset cachedFont = null;
        private static UnityEngine.Material cachedFontMaterial = null;

        // Popup state (public so ItemFoundPage patch can use same popup system)
        public static GameObject hoverPopup = null;
        public static System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<EvolutionFormula>> buttonFormulas = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<EvolutionFormula>>();
        public static System.Collections.Generic.Dictionary<int, float> buttonIconSizes = new System.Collections.Generic.Dictionary<int, float>();
        public static System.Collections.Generic.Dictionary<int, Transform> buttonContainers = new System.Collections.Generic.Dictionary<int, Transform>();
        public static int lastClickedButtonId = 0;

        // Display evolution formulas with full [A] + [B] → [C] format
        public static void DisplayEvolutionFormulas(LevelUpItemUI __instance, System.Collections.Generic.List<EvolutionFormula> formulas)
        {
            try
            {
                // Get the evo container (parent of _EvoIcons)
                var evoIconsProperty = __instance.GetType().GetProperty("_EvoIcons",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (evoIconsProperty == null) return;

                var evoIconsArray = evoIconsProperty.GetValue(__instance) as Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.UI.Image>;
                if (evoIconsArray == null || evoIconsArray.Length == 0) return;

                // Get the "evo" container (parent of the icon slots)
                var evoContainer = evoIconsArray[0].transform.parent;
                if (evoContainer == null) return;

                // Activate the container
                evoContainer.gameObject.SetActive(true);

                // Hide all original evo icons
                for (int i = 0; i < evoIconsArray.Length; i++)
                {
                    if (evoIconsArray[i] != null)
                    {
                        evoIconsArray[i].gameObject.SetActive(false);
                    }
                }

                // Hide the "evo" text label if it exists (check for TMP or Text components on the container)
                var evoContainerTMP = evoContainer.GetComponent<Il2CppTMPro.TextMeshProUGUI>();
                if (evoContainerTMP != null)
                {
                    evoContainerTMP.enabled = false;
                }
                var evoContainerText = evoContainer.GetComponent<UnityEngine.UI.Text>();
                if (evoContainerText != null)
                {
                    evoContainerText.enabled = false;
                }
                // Also check children for text
                var childTexts = evoContainer.GetComponentsInChildren<Il2CppTMPro.TextMeshProUGUI>(true);
                foreach (var txt in childTexts)
                {
                    if (txt.gameObject.name == "evo" || txt.text == "evo")
                    {
                        txt.enabled = false;
                    }
                }

                // Get or cache TMP font from existing UI
                if (cachedFont == null)
                {
                    var existingTMPs = __instance.GetComponentsInChildren<Il2CppTMPro.TextMeshProUGUI>(true);
                    if (existingTMPs.Length > 0)
                    {
                        cachedFont = existingTMPs[0].font;
                        cachedFontMaterial = existingTMPs[0].fontSharedMaterial;
                    }
                }

                // Clean up any previous formula rows, buttons and popups we created
                var existingRows = new System.Collections.Generic.List<UnityEngine.Transform>();
                for (int i = 0; i < evoContainer.childCount; i++)
                {
                    var child = evoContainer.GetChild(i);
                    if (child.name.StartsWith("EvoFormula_") || child.name == "MoreIndicator" || child.name == "MoreButton")
                    {
                        existingRows.Add(child);
                    }
                }
                foreach (var row in existingRows)
                {
                    UnityEngine.Object.Destroy(row.gameObject);
                }

                // Clean up any existing hover popup
                if (hoverPopup != null)
                {
                    UnityEngine.Object.Destroy(hoverPopup);
                    hoverPopup = null;
                }
                lastClickedButtonId = 0;

                // Get icon size from original icons - keep them compact to avoid overlapping text
                float iconSize = 20f;
                var originalRect = evoIconsArray[0].GetComponent<UnityEngine.RectTransform>();
                if (originalRect != null)
                {
                    iconSize = originalRect.sizeDelta.x * 0.9f; // Slightly smaller than original
                }

                float rowHeight = iconSize + 2f;
                float xSpacing = 2f;
                float textWidth = 14f;

                // Limit formulas to avoid overflow onto description text
                int maxFormulas = 2;
                int displayCount = System.Math.Min(formulas.Count, maxFormulas);

                // Create formula rows
                for (int rowIndex = 0; rowIndex < displayCount; rowIndex++)
                {
                    var formula = formulas[rowIndex];
                    float yOffset = -rowIndex * rowHeight;
                    float xPos = 0f;

                    // Create row container
                    var rowObj = new UnityEngine.GameObject($"EvoFormula_{rowIndex}");
                    rowObj.transform.SetParent(evoContainer, false);
                    var rowRect = rowObj.AddComponent<UnityEngine.RectTransform>();
                    rowRect.anchorMin = new UnityEngine.Vector2(0f, 1f);
                    rowRect.anchorMax = new UnityEngine.Vector2(0f, 1f);
                    rowRect.pivot = new UnityEngine.Vector2(0f, 1f);
                    rowRect.anchoredPosition = new UnityEngine.Vector2(0f, yOffset);
                    rowRect.sizeDelta = new UnityEngine.Vector2(300f, rowHeight);

                    // Add ingredients
                    for (int ingIndex = 0; ingIndex < formula.Ingredients.Count; ingIndex++)
                    {
                        var (sprite, owned) = formula.Ingredients[ingIndex];

                        // Add "+" before all but first ingredient
                        if (ingIndex > 0)
                        {
                            CreateTextElement(rowObj.transform, "+", xPos, iconSize, textWidth);
                            xPos += textWidth;
                        }

                        // Add ingredient icon
                        CreateIconElement(rowObj.transform, sprite, owned, xPos, iconSize, $"Ing_{ingIndex}");
                        xPos += iconSize + xSpacing;
                    }

                    // Add arrow
                    CreateTextElement(rowObj.transform, "→", xPos, iconSize, textWidth);
                    xPos += textWidth;

                    // Add result icon (never has checkmark - it's what you're making)
                    if (formula.ResultSprite != null)
                    {
                        CreateIconElement(rowObj.transform, formula.ResultSprite, false, xPos, iconSize, "Result");
                    }
                }

                // Show "[+X]" button to the right if there are additional formulas
                if (formulas.Count > maxFormulas && cachedFont != null)
                {
                    int remaining = formulas.Count - maxFormulas;

                    // Create button container positioned to the right
                    var moreObj = new UnityEngine.GameObject("MoreButton");
                    moreObj.transform.SetParent(evoContainer, false);

                    var moreRect = moreObj.AddComponent<UnityEngine.RectTransform>();
                    moreRect.anchorMin = new UnityEngine.Vector2(1f, 0.5f);
                    moreRect.anchorMax = new UnityEngine.Vector2(1f, 0.5f);
                    moreRect.pivot = new UnityEngine.Vector2(0f, 0.5f);
                    moreRect.anchoredPosition = new UnityEngine.Vector2(5f, 0f);
                    moreRect.sizeDelta = new UnityEngine.Vector2(40f, displayCount * rowHeight);

                    // Add Button component for click interaction
                    var button = moreObj.AddComponent<UnityEngine.UI.Button>();

                    // Add background image for button appearance
                    var bgImage = moreObj.AddComponent<UnityEngine.UI.Image>();
                    bgImage.color = new Color(0.3f, 0.3f, 0.3f, 0.8f);
                    bgImage.raycastTarget = true;
                    button.targetGraphic = bgImage;

                    // Store formulas for this specific button using its instance ID
                    int buttonId = moreObj.GetInstanceID();
                    buttonFormulas[buttonId] = formulas.GetRange(maxFormulas, formulas.Count - maxFormulas);
                    buttonIconSizes[buttonId] = iconSize;
                    buttonContainers[buttonId] = evoContainer;

                    // Add click handler that captures the button ID
                    button.onClick.AddListener((UnityEngine.Events.UnityAction)(() => OnMoreButtonClick(buttonId)));

                    // Add text
                    var textObj = new UnityEngine.GameObject("Text");
                    textObj.transform.SetParent(moreObj.transform, false);

                    var moreTMP = textObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                    moreTMP.font = cachedFont;
                    moreTMP.fontSharedMaterial = cachedFontMaterial;
                    moreTMP.text = $"+{remaining}";
                    moreTMP.fontSize = iconSize * 0.7f;
                    moreTMP.color = new Color(1f, 1f, 1f, 1f);
                    moreTMP.alignment = Il2CppTMPro.TextAlignmentOptions.Center;
                    moreTMP.raycastTarget = false; // Don't block clicks

                    var textRect = moreTMP.GetComponent<UnityEngine.RectTransform>();
                    textRect.anchorMin = new UnityEngine.Vector2(0f, 0f);
                    textRect.anchorMax = new UnityEngine.Vector2(1f, 1f);
                    textRect.offsetMin = Vector2.zero;
                    textRect.offsetMax = Vector2.zero;
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error displaying evolution formulas: {ex}");
            }
        }

        public static void OnMoreButtonClick(int buttonId)
        {
            // Toggle popup - if showing for this button, hide it
            if (hoverPopup != null && lastClickedButtonId == buttonId)
            {
                UnityEngine.Object.Destroy(hoverPopup);
                hoverPopup = null;
                lastClickedButtonId = 0;
                return;
            }

            // If showing for different button, hide it first
            if (hoverPopup != null)
            {
                UnityEngine.Object.Destroy(hoverPopup);
                hoverPopup = null;
            }

            // Get formulas for this button
            if (!buttonFormulas.ContainsKey(buttonId) || buttonFormulas[buttonId].Count == 0)
            {
                return;
            }
            if (!buttonContainers.ContainsKey(buttonId) || buttonContainers[buttonId] == null)
            {
                return;
            }

            var pendingFormulas = buttonFormulas[buttonId];
            var evoContainer = buttonContainers[buttonId];
            float iconSize = buttonIconSizes.ContainsKey(buttonId) ? buttonIconSizes[buttonId] : 20f;

            lastClickedButtonId = buttonId;
            float rowHeight = iconSize + 2f;
            float xSpacing = 2f;
            float textWidth = 14f;

            // Calculate popup size based on content
            // Each formula: icons + plus signs + arrow + result
            float maxFormulaWidth = 0f;
            foreach (var formula in pendingFormulas)
            {
                float formulaWidth = formula.Ingredients.Count * iconSize +
                                    (formula.Ingredients.Count - 1) * textWidth + // plus signs
                                    textWidth + // arrow
                                    (formula.ResultSprite != null ? iconSize : 0);
                if (formulaWidth > maxFormulaWidth) maxFormulaWidth = formulaWidth;
            }
            float popupWidth = maxFormulaWidth + 16f; // Add padding
            float popupHeight = pendingFormulas.Count * rowHeight + 10f;

            // Create popup panel - parent to evoContainer directly for better positioning
            hoverPopup = new UnityEngine.GameObject("HoverPopup");
            hoverPopup.transform.SetParent(evoContainer, false);

            var popupRect = hoverPopup.AddComponent<UnityEngine.RectTransform>();

            // Position popup in upper-middle area of the card, using fixed size for content fit
            popupRect.anchorMin = new UnityEngine.Vector2(0.30f, 0.58f);
            popupRect.anchorMax = new UnityEngine.Vector2(0.30f, 0.58f);
            popupRect.pivot = new UnityEngine.Vector2(0f, 0f);
            popupRect.sizeDelta = new UnityEngine.Vector2(popupWidth, popupHeight);
            popupRect.anchoredPosition = UnityEngine.Vector2.zero;

            // Add background
            var bgImage = hoverPopup.AddComponent<UnityEngine.UI.Image>();
            bgImage.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);

            // Add formulas to popup
            for (int rowIndex = 0; rowIndex < pendingFormulas.Count; rowIndex++)
            {
                var formula = pendingFormulas[rowIndex];
                float yOffset = -rowIndex * rowHeight - 5f;
                float xPos = 5f;

                var rowObj = new UnityEngine.GameObject($"PopupRow_{rowIndex}");
                rowObj.transform.SetParent(hoverPopup.transform, false);
                var rowRect = rowObj.AddComponent<UnityEngine.RectTransform>();
                rowRect.anchorMin = new UnityEngine.Vector2(0f, 1f);
                rowRect.anchorMax = new UnityEngine.Vector2(0f, 1f);
                rowRect.pivot = new UnityEngine.Vector2(0f, 1f);
                rowRect.anchoredPosition = new UnityEngine.Vector2(0f, yOffset);
                rowRect.sizeDelta = new UnityEngine.Vector2(popupWidth, rowHeight);

                // Add ingredients
                for (int ingIndex = 0; ingIndex < formula.Ingredients.Count; ingIndex++)
                {
                    var (sprite, owned) = formula.Ingredients[ingIndex];

                    if (ingIndex > 0)
                    {
                        CreateTextElement(rowObj.transform, "+", xPos, iconSize, textWidth);
                        xPos += textWidth;
                    }

                    CreateIconElement(rowObj.transform, sprite, owned, xPos, iconSize, $"Ing_{ingIndex}");
                    xPos += iconSize + xSpacing;
                }

                // Add arrow
                CreateTextElement(rowObj.transform, "→", xPos, iconSize, textWidth);
                xPos += textWidth;

                // Add result
                if (formula.ResultSprite != null)
                {
                    CreateIconElement(rowObj.transform, formula.ResultSprite, false, xPos, iconSize, "Result");
                }
            }

            // Ensure popup is on top
            hoverPopup.transform.SetAsLastSibling();
        }

        private static void CreateIconElement(UnityEngine.Transform parent, UnityEngine.Sprite sprite, bool owned, float xPos, float size, string name)
        {
            var iconObj = new UnityEngine.GameObject($"Icon_{name}");
            iconObj.transform.SetParent(parent, false);

            var iconRect = iconObj.AddComponent<UnityEngine.RectTransform>();
            iconRect.anchorMin = new UnityEngine.Vector2(0f, 0.5f);
            iconRect.anchorMax = new UnityEngine.Vector2(0f, 0.5f);
            iconRect.pivot = new UnityEngine.Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new UnityEngine.Vector2(xPos, 0f);
            iconRect.sizeDelta = new UnityEngine.Vector2(size, size);

            var iconImage = iconObj.AddComponent<UnityEngine.UI.Image>();
            iconImage.sprite = sprite;
            iconImage.color = new Color(1f, 1f, 1f, 1f);

            // Add checkmark if owned
            if (owned && cachedFont != null)
            {
                var checkObj = new UnityEngine.GameObject("Check");
                checkObj.transform.SetParent(iconObj.transform, false);

                var checkTMP = checkObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                checkTMP.font = cachedFont;
                checkTMP.fontSharedMaterial = cachedFontMaterial;
                checkTMP.text = "✓";
                checkTMP.fontSize = size * 0.6f;
                checkTMP.color = new Color(0.2f, 1f, 0.2f, 1f);
                checkTMP.alignment = Il2CppTMPro.TextAlignmentOptions.Center;
                checkTMP.outlineWidth = 0.3f;
                checkTMP.outlineColor = new Color32(0, 0, 0, 255);

                var checkRect = checkTMP.GetComponent<UnityEngine.RectTransform>();
                checkRect.anchorMin = new UnityEngine.Vector2(1f, 0f);
                checkRect.anchorMax = new UnityEngine.Vector2(1f, 0f);
                checkRect.pivot = new UnityEngine.Vector2(1f, 0f);
                checkRect.anchoredPosition = new UnityEngine.Vector2(2f, -2f);
                checkRect.sizeDelta = new UnityEngine.Vector2(size * 0.6f, size * 0.6f);
            }
        }

        private static void CreateTextElement(UnityEngine.Transform parent, string text, float xPos, float height, float width)
        {
            if (cachedFont == null) return;

            var textObj = new UnityEngine.GameObject($"Text_{text}");
            textObj.transform.SetParent(parent, false);

            var textTMP = textObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
            textTMP.font = cachedFont;
            textTMP.fontSharedMaterial = cachedFontMaterial;
            textTMP.text = text;
            textTMP.fontSize = height * 0.6f;
            textTMP.color = new Color(1f, 1f, 1f, 1f);
            textTMP.alignment = Il2CppTMPro.TextAlignmentOptions.Center;

            var textRect = textTMP.GetComponent<UnityEngine.RectTransform>();
            textRect.anchorMin = new UnityEngine.Vector2(0f, 0.5f);
            textRect.anchorMax = new UnityEngine.Vector2(0f, 0.5f);
            textRect.pivot = new UnityEngine.Vector2(0f, 0.5f);
            textRect.anchoredPosition = new UnityEngine.Vector2(xPos, 0f);
            textRect.sizeDelta = new UnityEngine.Vector2(width, height);
        }

        // Load sprite for evolution result by name (e.g., "HELLFIRE")
        public static UnityEngine.Sprite LoadEvoResultSprite(string evoInto, object weaponsDict)
        {
            try
            {
                var evoWeaponType = (WeaponType)System.Enum.Parse(typeof(WeaponType), evoInto);
                return TryLoadFromWeapons(evoWeaponType, weaponsDict);
            }
            catch { }
            return null;
        }
    }

    // Patch for PowerUp/Item cards to show which weapons they evolve
    [HarmonyPatch(typeof(LevelUpItemUI), "SetItemData")]
    public class LevelUpItemUI_SetItemData_Patch
    {
        public static void Postfix(LevelUpItemUI __instance, ItemType type, object data, LevelUpPage page, int index, object affectedCharacters)
        {
            try
            {
                // Debug: log when this is called
                MelonLogger.Msg($"[SetItemData] Called: type={type}, page={page != null}, data={data != null}");

                if (data == null) return;

                var dataManager = page.Data;
                if (dataManager == null) return;

                var weaponsDict = dataManager.GetConvertedWeapons();
                var powerUpsDict = dataManager.GetConvertedPowerUpData();
                if (weaponsDict == null) return;

                // Collect all evolution formulas that use this PowerUp
                var formulas = new System.Collections.Generic.List<LevelUpItemUI_SetWeaponData_Patch.EvolutionFormula>();

                // Iterate through all weapons to find ones that require this PowerUp
                var dictType = weaponsDict.GetType();
                var keysProperty = dictType.GetProperty("Keys");
                var tryGetMethod = dictType.GetMethod("TryGetValue");

                if (keysProperty == null || tryGetMethod == null) return;

                var keysCollection = keysProperty.GetValue(weaponsDict);
                if (keysCollection == null) return;

                var getEnumeratorMethod = keysCollection.GetType().GetMethod("GetEnumerator");
                if (getEnumeratorMethod == null) return;

                var enumerator = getEnumeratorMethod.Invoke(keysCollection, null);
                var enumeratorType = enumerator.GetType();
                var moveNextMethod = enumeratorType.GetMethod("MoveNext");
                var currentProperty = enumeratorType.GetProperty("Current");

                while ((bool)moveNextMethod.Invoke(enumerator, null))
                {
                    var weaponTypeKey = currentProperty.GetValue(enumerator);
                    if (weaponTypeKey == null) continue;

                    var weaponParams = new object[] { weaponTypeKey, null };
                    var found = (bool)tryGetMethod.Invoke(weaponsDict, weaponParams);
                    var weaponList = weaponParams[1];

                    if (!found || weaponList == null) continue;

                    var countProp = weaponList.GetType().GetProperty("Count");
                    var itemProp = weaponList.GetType().GetProperty("Item");
                    if (countProp == null || itemProp == null) continue;

                    int itemCount = (int)countProp.GetValue(weaponList);
                    if (itemCount == 0) continue;

                    var weaponData = itemProp.GetValue(weaponList, new object[] { 0 }) as WeaponData;
                    if (weaponData == null || weaponData.evoSynergy == null || string.IsNullOrEmpty(weaponData.evoInto))
                        continue;

                    // Check if this weapon requires our PowerUp
                    bool weaponRequiresThisPowerUp = false;
                    foreach (var reqType in weaponData.evoSynergy)
                    {
                        if (reqType.ToString() == type.ToString())
                        {
                            weaponRequiresThisPowerUp = true;
                            break;
                        }
                    }

                    if (weaponRequiresThisPowerUp)
                    {
                        // 1. Get weapon type and check for DLC content
                        WeaponType wType = (WeaponType)weaponTypeKey;

                        // Skip weapons that aren't available (DLC not owned)
                        if (!LevelUpItemUI_SetWeaponData_Patch.IsWeaponAvailable(weaponData))
                        {
                            continue;
                        }

                        // Build formula for this weapon
                        var formula = new LevelUpItemUI_SetWeaponData_Patch.EvolutionFormula();
                        formula.ResultName = weaponData.evoInto;

                        // 2. Add the weapon as first ingredient
                        var weaponSprite = LevelUpItemUI_SetWeaponData_Patch.TryLoadFromWeapons(wType, weaponsDict);

                        // Skip if weapon sprite doesn't load
                        if (weaponSprite == null)
                        {
                            continue;
                        }

                        bool ownsWeapon = LevelUpItemUI_SetWeaponData_Patch.PlayerOwnsRequirement(page, wType);
                        formula.Ingredients.Add((weaponSprite, ownsWeapon));

                        // 2. Add all requirements (including this PowerUp)
                        bool missingRequirement = false;
                        foreach (var reqType in weaponData.evoSynergy)
                        {
                            var reqSprite = LevelUpItemUI_SetWeaponData_Patch.LoadSpriteForRequirement(reqType, weaponsDict, powerUpsDict);
                            if (reqSprite != null)
                            {
                                bool ownsReq = LevelUpItemUI_SetWeaponData_Patch.PlayerOwnsRequirement(page, reqType);
                                formula.Ingredients.Add((reqSprite, ownsReq));
                            }
                            else
                            {
                                // Skip formulas with missing requirement sprites (DLC)
                                missingRequirement = true;
                                break;
                            }
                        }

                        if (missingRequirement) continue;

                        // 3. Load the evolution result sprite
                        formula.ResultSprite = LevelUpItemUI_SetWeaponData_Patch.LoadEvoResultSprite(weaponData.evoInto, weaponsDict);

                        // Skip if result sprite doesn't load (DLC not installed)
                        if (formula.ResultSprite == null)
                        {
                            continue;
                        }

                        if (formula.Ingredients.Count > 0)
                        {
                            formulas.Add(formula);
                        }
                    }
                }

                // Display all formulas
                if (formulas.Count > 0)
                {
                    LevelUpItemUI_SetWeaponData_Patch.DisplayEvolutionFormulas(__instance, formulas);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in SetItemData patch: {ex}");
            }
        }
    }

    // Patch for ItemFoundPage (ground pickup UI) to show evolution formulas
    [HarmonyPatch(typeof(Il2CppVampireSurvivors.UI.ItemFoundPage), "SetWeaponDisplay")]
    public class ItemFoundPage_SetWeaponDisplay_Patch
    {
        public static void Postfix(Il2CppVampireSurvivors.UI.ItemFoundPage __instance, int level)
        {
            try
            {
                MelonLogger.Msg($"[ItemFoundPage.SetWeaponDisplay] Called with level={level}");

                // Get weapon data from instance properties
                var weaponType = __instance._weapon;
                var baseWeaponData = __instance._baseWeaponData;
                var dataManager = __instance._dataManager;

                if (baseWeaponData == null || dataManager == null)
                {
                    MelonLogger.Msg("[ItemFoundPage] Missing data");
                    return;
                }

                // Debug: log evoSynergy contents
                string synergyInfo = "null";
                if (baseWeaponData.evoSynergy != null)
                {
                    synergyInfo = $"Length={baseWeaponData.evoSynergy.Length}";
                    if (baseWeaponData.evoSynergy.Length > 0)
                    {
                        var synergyTypes = new System.Collections.Generic.List<string>();
                        foreach (var s in baseWeaponData.evoSynergy)
                        {
                            synergyTypes.Add(s.ToString());
                        }
                        synergyInfo += $" [{string.Join(", ", synergyTypes)}]";
                    }
                }
                MelonLogger.Msg($"[ItemFoundPage] Weapon: {baseWeaponData.name}, evoInto: {baseWeaponData.evoInto}, evoSynergy: {synergyInfo}");

                // Check for special cases (Clock Lancet, Laurel)
                string typeStr = weaponType.ToString();
                string weaponName = baseWeaponData.name ?? "";
                bool isSpecialCase = typeStr.Contains("CLOCK") || weaponName.Contains("Clock Lancet") ||
                                     typeStr.Contains("LAUREL") || weaponName.Contains("Laurel");

                // Skip if no evolution and not a special case
                if (string.IsNullOrEmpty(baseWeaponData.evoInto) && !isSpecialCase)
                {
                    return;
                }

                var weaponsDict = dataManager.GetConvertedWeapons();
                var powerUpsDict = dataManager.GetConvertedPowerUpData();

                if (weaponsDict == null || powerUpsDict == null)
                {
                    return;
                }

                // Check if this is a powerup using the same logic as level-up UI
                // This properly detects items like Spellbinder, Spinach, etc.
                bool isActualPowerUp = LevelUpItemUI_SetWeaponData_Patch.IsInPowerUpsDict(weaponType, powerUpsDict);
                MelonLogger.Msg($"[ItemFoundPage] isActualPowerUp={isActualPowerUp}, isSpecialCase={isSpecialCase}");

                if (isActualPowerUp && !isSpecialCase)
                {
                    MelonLogger.Msg($"[ItemFoundPage] Treating as PowerUp - finding weapons that use it");
                    // This is a powerup - find weapons that use it for evolution
                    DisplayFormulasForPowerUpPickup(__instance, weaponType, weaponsDict, powerUpsDict);
                    return;
                }

                // Build the formula for a weapon
                var formula = new LevelUpItemUI_SetWeaponData_Patch.EvolutionFormula();

                // Handle special cases
                if (typeStr.Contains("CLOCK") || weaponName.Contains("Clock Lancet"))
                {
                    BuildClockLancetFormulaForPickup(__instance, formula, weaponType, weaponsDict);
                }
                else if (typeStr.Contains("LAUREL") || weaponName.Contains("Laurel"))
                {
                    BuildLaurelFormulaForPickup(__instance, formula, weaponType, weaponsDict);
                }
                else
                {
                    // Regular weapon formula
                    formula.ResultName = baseWeaponData.evoInto;
                    MelonLogger.Msg($"[ItemFoundPage] Building formula for result: {formula.ResultName}");

                    // Add the weapon itself (not marked as owned - player is just picking it up)
                    var weaponSprite = LevelUpItemUI_SetWeaponData_Patch.TryLoadFromWeapons(weaponType, weaponsDict, true);
                    MelonLogger.Msg($"[ItemFoundPage] Weapon sprite for {weaponType}: {(weaponSprite != null ? "loaded" : "NULL")}");
                    if (weaponSprite != null)
                    {
                        formula.Ingredients.Add((weaponSprite, false)); // Not marked - player is just picking it up
                    }

                    // Add requirements
                    foreach (var reqType in baseWeaponData.evoSynergy)
                    {
                        var sprite = LevelUpItemUI_SetWeaponData_Patch.LoadSpriteForRequirement(reqType, weaponsDict, powerUpsDict, true);
                        MelonLogger.Msg($"[ItemFoundPage] Req sprite for {reqType}: {(sprite != null ? "loaded" : "NULL")}");
                        if (sprite != null)
                        {
                            // For pickups, we don't have easy access to player inventory, so just show unchecked
                            formula.Ingredients.Add((sprite, false));
                        }
                    }

                    // Load result sprite
                    formula.ResultSprite = LevelUpItemUI_SetWeaponData_Patch.LoadEvoResultSprite(baseWeaponData.evoInto, weaponsDict);
                    MelonLogger.Msg($"[ItemFoundPage] Result sprite: {(formula.ResultSprite != null ? "loaded" : "NULL")}");
                }

                MelonLogger.Msg($"[ItemFoundPage] Formula has {formula.Ingredients.Count} ingredients");

                // Display the formula on the ItemFoundPage
                if (formula.Ingredients.Count > 0)
                {
                    MelonLogger.Msg($"[ItemFoundPage] Calling DisplayFormulaOnItemFoundPage");
                    DisplayFormulaOnItemFoundPage(__instance, formula);
                }
                else
                {
                    MelonLogger.Msg($"[ItemFoundPage] No ingredients, skipping display");
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in ItemFoundPage patch: {ex}");
            }
        }

        private static void BuildClockLancetFormulaForPickup(Il2CppVampireSurvivors.UI.ItemFoundPage instance,
            LevelUpItemUI_SetWeaponData_Patch.EvolutionFormula formula, WeaponType clockType, object weaponsDict)
        {
            formula.ResultName = "CORRIDOR";

            var clockSprite = LevelUpItemUI_SetWeaponData_Patch.TryLoadFromWeapons(clockType, weaponsDict);
            if (clockSprite != null) formula.Ingredients.Add((clockSprite, true));

            if (System.Enum.TryParse<WeaponType>("GOLD", out var goldType))
            {
                var goldSprite = LevelUpItemUI_SetWeaponData_Patch.TryLoadFromWeapons(goldType, weaponsDict);
                if (goldSprite != null) formula.Ingredients.Add((goldSprite, false));
            }

            if (System.Enum.TryParse<WeaponType>("SILVER", out var silverType))
            {
                var silverSprite = LevelUpItemUI_SetWeaponData_Patch.TryLoadFromWeapons(silverType, weaponsDict);
                if (silverSprite != null) formula.Ingredients.Add((silverSprite, false));
            }

            if (System.Enum.TryParse<WeaponType>("CORRIDOR", out var resultType))
            {
                formula.ResultSprite = LevelUpItemUI_SetWeaponData_Patch.TryLoadFromWeapons(resultType, weaponsDict);
            }
        }

        private static void BuildLaurelFormulaForPickup(Il2CppVampireSurvivors.UI.ItemFoundPage instance,
            LevelUpItemUI_SetWeaponData_Patch.EvolutionFormula formula, WeaponType laurelType, object weaponsDict)
        {
            formula.ResultName = "SHROUD";

            var laurelSprite = LevelUpItemUI_SetWeaponData_Patch.TryLoadFromWeapons(laurelType, weaponsDict);
            if (laurelSprite != null) formula.Ingredients.Add((laurelSprite, true));

            if (System.Enum.TryParse<WeaponType>("LEFT", out var leftType))
            {
                var leftSprite = LevelUpItemUI_SetWeaponData_Patch.TryLoadFromWeapons(leftType, weaponsDict);
                if (leftSprite != null) formula.Ingredients.Add((leftSprite, false));
            }

            if (System.Enum.TryParse<WeaponType>("RIGHT", out var rightType))
            {
                var rightSprite = LevelUpItemUI_SetWeaponData_Patch.TryLoadFromWeapons(rightType, weaponsDict);
                if (rightSprite != null) formula.Ingredients.Add((rightSprite, false));
            }

            if (System.Enum.TryParse<WeaponType>("SHROUD", out var resultType))
            {
                formula.ResultSprite = LevelUpItemUI_SetWeaponData_Patch.TryLoadFromWeapons(resultType, weaponsDict);
            }
        }

        private static void DisplayFormulaOnItemFoundPage(Il2CppVampireSurvivors.UI.ItemFoundPage instance,
            LevelUpItemUI_SetWeaponData_Patch.EvolutionFormula formula)
        {
            try
            {
                // Find the Container child and explore its structure
                UnityEngine.Transform container = instance.transform.Find("Container");
                UnityEngine.Transform parent = instance.transform;

                if (container != null)
                {
                    MelonLogger.Msg($"[ItemFoundPage] Container children:");
                    for (int c = 0; c < container.childCount; c++)
                    {
                        var ch = container.GetChild(c);
                        MelonLogger.Msg($"[ItemFoundPage]   Container/{ch.name}");
                        // Also log grandchildren
                        for (int gc = 0; gc < ch.childCount; gc++)
                        {
                            var gch = ch.GetChild(gc);
                            MelonLogger.Msg($"[ItemFoundPage]     {ch.name}/{gch.name}");
                        }
                    }

                    // The Scroll View contains the actual item card
                    var scrollView = container.Find("Scroll View");
                    if (scrollView != null)
                    {
                        var viewport = scrollView.Find("Viewport");
                        if (viewport != null)
                        {
                            MelonLogger.Msg($"[ItemFoundPage] Viewport children:");
                            for (int vc = 0; vc < viewport.childCount; vc++)
                            {
                                var vch = viewport.GetChild(vc);
                                MelonLogger.Msg($"[ItemFoundPage]   Viewport/{vch.name}");
                                for (int vgc = 0; vgc < vch.childCount; vgc++)
                                {
                                    var vgch = vch.GetChild(vgc);
                                    MelonLogger.Msg($"[ItemFoundPage]     {vch.name}/{vgch.name}");
                                }
                            }

                            if (viewport.childCount > 0)
                            {
                                var content = viewport.GetChild(0);
                                // Try to find the item card inside the content
                                if (content.childCount > 0)
                                {
                                    var levelUpItem = content.GetChild(0); // The actual item UI element (LevelUpItem)
                                    MelonLogger.Msg($"[ItemFoundPage] Found LevelUpItem: {levelUpItem.name}");

                                    // Find and activate the "evo" element - this is where evolution icons belong
                                    var evoElement = levelUpItem.Find("evo");
                                    if (evoElement != null)
                                    {
                                        // Activate the evo element if it's hidden
                                        if (!evoElement.gameObject.activeInHierarchy)
                                        {
                                            evoElement.gameObject.SetActive(true);
                                            MelonLogger.Msg($"[ItemFoundPage] Activated 'evo' element");
                                        }

                                        // Hide the "evo" text label that shows up
                                        var evoTexts = evoElement.GetComponentsInChildren<Il2CppTMPro.TextMeshProUGUI>(true);
                                        foreach (var txt in evoTexts)
                                        {
                                            if (txt.text == "evo" || txt.gameObject.name == "evo")
                                            {
                                                txt.gameObject.SetActive(false);
                                                MelonLogger.Msg($"[ItemFoundPage] Hid 'evo' text label");
                                            }
                                        }
                                        // Also check regular UI Text
                                        var regularTexts = evoElement.GetComponentsInChildren<UnityEngine.UI.Text>(true);
                                        foreach (var txt in regularTexts)
                                        {
                                            if (txt.text == "evo" || txt.gameObject.name == "evo")
                                            {
                                                txt.gameObject.SetActive(false);
                                                MelonLogger.Msg($"[ItemFoundPage] Hid 'evo' UI.Text label");
                                            }
                                        }

                                        parent = evoElement;
                                        MelonLogger.Msg($"[ItemFoundPage] Using 'evo' as parent");
                                    }
                                    else
                                    {
                                        parent = levelUpItem;
                                        MelonLogger.Msg($"[ItemFoundPage] 'evo' not found, using LevelUpItem");
                                    }
                                }
                                else
                                {
                                    parent = content;
                                    MelonLogger.Msg($"[ItemFoundPage] Using Content: {parent.name}");
                                }
                            }
                            else
                            {
                                parent = viewport;
                                MelonLogger.Msg($"[ItemFoundPage] Using Viewport (no children)");
                            }
                        }
                        else
                        {
                            parent = scrollView;
                            MelonLogger.Msg($"[ItemFoundPage] Using Scroll View");
                        }
                    }
                    else
                    {
                        parent = container;
                        MelonLogger.Msg($"[ItemFoundPage] Using Container (fallback)");
                    }
                }

                // Comprehensive cleanup: search parent AND its parent (levelUpItem) for formulas
                var toDestroySingle = new System.Collections.Generic.List<UnityEngine.GameObject>();

                // Clean from current parent
                MelonLogger.Msg($"[ItemFoundPage-Single] Parent '{parent.name}' has {parent.childCount} children:");
                for (int ci = 0; ci < parent.childCount; ci++)
                {
                    var child = parent.GetChild(ci);
                    MelonLogger.Msg($"[ItemFoundPage-Single]   Child {ci}: {child.name}");
                    if (child != null && (child.name.StartsWith("EvoFormula") || child.name == "MoreButton"))
                    {
                        toDestroySingle.Add(child.gameObject);
                    }
                }

                // If parent is evo element, also clean from levelUpItem (where multi-formula puts formulas)
                if (parent.name == "evo" && parent.parent != null)
                {
                    var levelUpItem = parent.parent;
                    MelonLogger.Msg($"[ItemFoundPage-Single] Also checking levelUpItem '{levelUpItem.name}' with {levelUpItem.childCount} children:");
                    for (int ci = 0; ci < levelUpItem.childCount; ci++)
                    {
                        var child = levelUpItem.GetChild(ci);
                        MelonLogger.Msg($"[ItemFoundPage-Single]   levelUpItem child {ci}: {child.name}");
                        if (child != null && (child.name.StartsWith("EvoFormula") || child.name == "MoreButton"))
                        {
                            toDestroySingle.Add(child.gameObject);
                        }
                    }
                }

                foreach (var go in toDestroySingle)
                {
                    if (go != null)
                    {
                        MelonLogger.Msg($"[ItemFoundPage-Single] Destroying: {go.name}");
                        UnityEngine.Object.DestroyImmediate(go);
                    }
                }
                MelonLogger.Msg($"[ItemFoundPage-Single] Cleaned up {toDestroySingle.Count} old formula objects total");

                // Create formula container
                var formulaObj = new UnityEngine.GameObject("EvoFormula_Pickup");
                formulaObj.transform.SetParent(parent, false);

                var formulaRect = formulaObj.AddComponent<UnityEngine.RectTransform>();

                // Position at center of parent (evo element or LevelUpItem)
                formulaRect.anchorMin = new UnityEngine.Vector2(0.5f, 0.5f);
                formulaRect.anchorMax = new UnityEngine.Vector2(0.5f, 0.5f);
                formulaRect.pivot = new UnityEngine.Vector2(0.5f, 0.5f);
                formulaRect.anchoredPosition = UnityEngine.Vector2.zero;
                MelonLogger.Msg($"[ItemFoundPage] Using parent: {parent.name}, Formula at center");

                float iconSize = 24f;
                float xSpacing = 4f;
                float textWidth = 16f;

                // Calculate total width
                float totalWidth = formula.Ingredients.Count * iconSize +
                                   (formula.Ingredients.Count - 1) * xSpacing +
                                   textWidth + // arrow
                                   (formula.ResultSprite != null ? iconSize : 0);

                formulaRect.sizeDelta = new UnityEngine.Vector2(totalWidth, iconSize);

                float xPos = -totalWidth / 2f;

                // Get font from existing UI
                Il2CppTMPro.TMP_FontAsset font = null;
                var existingTMPs = instance.GetComponentsInChildren<Il2CppTMPro.TextMeshProUGUI>(true);
                if (existingTMPs.Length > 0) font = existingTMPs[0].font;

                // Add ingredients
                for (int i = 0; i < formula.Ingredients.Count; i++)
                {
                    var (sprite, owned) = formula.Ingredients[i];

                    if (i > 0 && font != null)
                    {
                        // Add "+"
                        var plusObj = new UnityEngine.GameObject("Plus");
                        plusObj.transform.SetParent(formulaObj.transform, false);
                        var plusTMP = plusObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                        plusTMP.font = font;
                        plusTMP.text = "+";
                        plusTMP.fontSize = iconSize * 0.6f;
                        plusTMP.color = UnityEngine.Color.white;
                        plusTMP.alignment = Il2CppTMPro.TextAlignmentOptions.Center;

                        var plusRect = plusTMP.GetComponent<UnityEngine.RectTransform>();
                        plusRect.anchorMin = new UnityEngine.Vector2(0.5f, 0.5f);
                        plusRect.anchorMax = new UnityEngine.Vector2(0.5f, 0.5f);
                        plusRect.pivot = new UnityEngine.Vector2(0f, 0.5f);
                        plusRect.anchoredPosition = new UnityEngine.Vector2(xPos, 0f);
                        plusRect.sizeDelta = new UnityEngine.Vector2(textWidth, iconSize);
                        xPos += textWidth;
                    }

                    // Add icon
                    var iconObj = new UnityEngine.GameObject($"Icon_{i}");
                    iconObj.transform.SetParent(formulaObj.transform, false);

                    var iconRect = iconObj.AddComponent<UnityEngine.RectTransform>();
                    iconRect.anchorMin = new UnityEngine.Vector2(0.5f, 0.5f);
                    iconRect.anchorMax = new UnityEngine.Vector2(0.5f, 0.5f);
                    iconRect.pivot = new UnityEngine.Vector2(0f, 0.5f);
                    iconRect.anchoredPosition = new UnityEngine.Vector2(xPos, 0f);
                    iconRect.sizeDelta = new UnityEngine.Vector2(iconSize, iconSize);

                    var iconImage = iconObj.AddComponent<UnityEngine.UI.Image>();
                    iconImage.sprite = sprite;

                    // Add checkmark if owned
                    if (owned && font != null)
                    {
                        var checkObj = new UnityEngine.GameObject("Check");
                        checkObj.transform.SetParent(iconObj.transform, false);
                        var checkTMP = checkObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                        checkTMP.font = font;
                        checkTMP.text = "✓";
                        checkTMP.fontSize = iconSize * 0.5f;
                        checkTMP.color = new UnityEngine.Color(0.2f, 1f, 0.2f, 1f);
                        checkTMP.alignment = Il2CppTMPro.TextAlignmentOptions.Center;

                        var checkRect = checkTMP.GetComponent<UnityEngine.RectTransform>();
                        checkRect.anchorMin = new UnityEngine.Vector2(1f, 0f);
                        checkRect.anchorMax = new UnityEngine.Vector2(1f, 0f);
                        checkRect.pivot = new UnityEngine.Vector2(1f, 0f);
                        checkRect.anchoredPosition = new UnityEngine.Vector2(2f, -2f);
                        checkRect.sizeDelta = new UnityEngine.Vector2(iconSize * 0.5f, iconSize * 0.5f);
                    }

                    xPos += iconSize + xSpacing;
                }

                // Add arrow
                if (font != null)
                {
                    var arrowObj = new UnityEngine.GameObject("Arrow");
                    arrowObj.transform.SetParent(formulaObj.transform, false);
                    var arrowTMP = arrowObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                    arrowTMP.font = font;
                    arrowTMP.text = "→";
                    arrowTMP.fontSize = iconSize * 0.6f;
                    arrowTMP.color = UnityEngine.Color.white;
                    arrowTMP.alignment = Il2CppTMPro.TextAlignmentOptions.Center;

                    var arrowRect = arrowTMP.GetComponent<UnityEngine.RectTransform>();
                    arrowRect.anchorMin = new UnityEngine.Vector2(0.5f, 0.5f);
                    arrowRect.anchorMax = new UnityEngine.Vector2(0.5f, 0.5f);
                    arrowRect.pivot = new UnityEngine.Vector2(0f, 0.5f);
                    arrowRect.anchoredPosition = new UnityEngine.Vector2(xPos, 0f);
                    arrowRect.sizeDelta = new UnityEngine.Vector2(textWidth, iconSize);
                    xPos += textWidth;
                }

                // Add result icon
                if (formula.ResultSprite != null)
                {
                    var resultObj = new UnityEngine.GameObject("Result");
                    resultObj.transform.SetParent(formulaObj.transform, false);

                    var resultRect = resultObj.AddComponent<UnityEngine.RectTransform>();
                    resultRect.anchorMin = new UnityEngine.Vector2(0.5f, 0.5f);
                    resultRect.anchorMax = new UnityEngine.Vector2(0.5f, 0.5f);
                    resultRect.pivot = new UnityEngine.Vector2(0f, 0.5f);
                    resultRect.anchoredPosition = new UnityEngine.Vector2(xPos, 0f);
                    resultRect.sizeDelta = new UnityEngine.Vector2(iconSize, iconSize);

                    var resultImage = resultObj.AddComponent<UnityEngine.UI.Image>();
                    resultImage.sprite = formula.ResultSprite;
                }

                MelonLogger.Msg("[ItemFoundPage] Formula displayed");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error displaying formula on ItemFoundPage: {ex}");
            }
        }

        private static void DisplayFormulasForPowerUpPickup(Il2CppVampireSurvivors.UI.ItemFoundPage instance,
            WeaponType powerUpType, object weaponsDict, object powerUpsDict)
        {
            try
            {
                var formulas = new System.Collections.Generic.List<LevelUpItemUI_SetWeaponData_Patch.EvolutionFormula>();
                var seenResults = new System.Collections.Generic.HashSet<string>();

                var dictType = weaponsDict.GetType();
                var keysProperty = dictType.GetProperty("Keys");
                var tryGetMethod = dictType.GetMethod("TryGetValue");

                if (keysProperty == null || tryGetMethod == null) return;

                var keys = keysProperty.GetValue(weaponsDict);
                var enumerator = keys.GetType().GetMethod("GetEnumerator").Invoke(keys, null);
                var moveNextMethod = enumerator.GetType().GetMethod("MoveNext");
                var currentProperty = enumerator.GetType().GetProperty("Current");

                string powerUpTypeStr = powerUpType.ToString();

                while ((bool)moveNextMethod.Invoke(enumerator, null))
                {
                    var weaponKey = currentProperty.GetValue(enumerator);
                    var weaponTypeKey = (WeaponType)weaponKey;

                    var parameters = new object[] { weaponTypeKey, null };
                    bool found = (bool)tryGetMethod.Invoke(weaponsDict, parameters);
                    if (!found || parameters[1] == null) continue;

                    // The dictionary returns a List of WeaponData, get the first item
                    var weaponList = parameters[1];
                    var countProp = weaponList.GetType().GetProperty("Count");
                    var itemProp = weaponList.GetType().GetProperty("Item");
                    if (countProp == null || itemProp == null) continue;

                    int itemCount = (int)countProp.GetValue(weaponList);
                    if (itemCount == 0) continue;

                    var weaponData = itemProp.GetValue(weaponList, new object[] { 0 }) as WeaponData;
                    if (weaponData == null) continue;
                    if (string.IsNullOrEmpty(weaponData.evoInto)) continue;
                    if (!LevelUpItemUI_SetWeaponData_Patch.IsWeaponAvailable(weaponData)) continue;
                    if (seenResults.Contains(weaponData.evoInto)) continue;
                    if (weaponData.evoSynergy == null || weaponData.evoSynergy.Length == 0) continue;

                    // Check if this weapon uses our powerup for evolution
                    bool usesThisPowerUp = false;
                    foreach (var reqType in weaponData.evoSynergy)
                    {
                        if (reqType.ToString() == powerUpTypeStr)
                        {
                            usesThisPowerUp = true;
                            break;
                        }
                    }

                    if (!usesThisPowerUp) continue;

                    seenResults.Add(weaponData.evoInto);

                    var formula = new LevelUpItemUI_SetWeaponData_Patch.EvolutionFormula();
                    formula.ResultName = weaponData.evoInto;

                    // Add the weapon sprite
                    var weaponSprite = LevelUpItemUI_SetWeaponData_Patch.TryLoadFromWeapons(weaponTypeKey, weaponsDict);
                    if (weaponSprite != null) formula.Ingredients.Add((weaponSprite, false));

                    // Add the powerup sprite (NOT owned - player is just picking it up)
                    var powerUpSprite = LevelUpItemUI_SetWeaponData_Patch.TryLoadFromWeapons(powerUpType, weaponsDict);
                    if (powerUpSprite != null) formula.Ingredients.Add((powerUpSprite, false));

                    // Add result sprite
                    formula.ResultSprite = LevelUpItemUI_SetWeaponData_Patch.LoadEvoResultSprite(weaponData.evoInto, weaponsDict);

                    if (formula.Ingredients.Count > 0)
                    {
                        formulas.Add(formula);
                    }
                }

                MelonLogger.Msg($"[ItemFoundPage] Found {formulas.Count} formulas for powerup {powerUpTypeStr}");

                if (formulas.Count > 0)
                {
                    DisplayFormulasOnItemFoundPage(instance, formulas);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in DisplayFormulasForPowerUpPickup: {ex}");
            }
        }

        private static void DisplayFormulasOnItemFoundPage(Il2CppVampireSurvivors.UI.ItemFoundPage instance,
            System.Collections.Generic.List<LevelUpItemUI_SetWeaponData_Patch.EvolutionFormula> formulas)
        {
            try
            {
                MelonLogger.Msg($"[ItemFoundPage-Multi] Instance ID: {instance.GetInstanceID()}, formulas to display: {formulas.Count}");

                // Find the proper parent - navigate to the evo element like the single formula version
                Transform parent = null;

                // Navigate: instance -> Container -> Scroll View -> Viewport -> Content -> LevelUpItem -> evo
                // OR fallback: search for LevelUpItem directly in the hierarchy
                var container = instance.transform.Find("Container");
                MelonLogger.Msg($"[ItemFoundPage-Multi] Container: {(container != null ? "found" : "NULL")}");
                if (container != null)
                {
                    var scrollView = container.Find("Scroll View");
                    MelonLogger.Msg($"[ItemFoundPage-Multi] Scroll View: {(scrollView != null ? "found" : "NULL")}");
                    if (scrollView != null)
                    {
                        var viewport = scrollView.Find("Viewport");
                        MelonLogger.Msg($"[ItemFoundPage-Multi] Viewport: {(viewport != null ? "found" : "NULL")}, childCount={viewport?.childCount ?? 0}");
                        if (viewport != null && viewport.childCount > 0)
                        {
                            var content = viewport.GetChild(0);
                            MelonLogger.Msg($"[ItemFoundPage-Multi] Content: {content.name}, childCount={content.childCount}");
                            if (content.childCount > 0)
                            {
                                var levelUpItem = content.GetChild(0);
                                MelonLogger.Msg($"[ItemFoundPage-Multi] LevelUpItem: {levelUpItem.name}");
                                // Use LevelUpItem as parent - evo element is too small for ItemFoundPage
                                parent = levelUpItem;
                                MelonLogger.Msg($"[ItemFoundPage-Multi] Using LevelUpItem as parent");
                            }
                        }
                    }
                    else
                    {
                        // Fallback: Search container's children for LevelUpItem or any child with "evo" element
                        MelonLogger.Msg($"[ItemFoundPage-Multi] Scroll View not found, searching container children ({container.childCount}):");
                        for (int i = 0; i < container.childCount; i++)
                        {
                            var child = container.GetChild(i);
                            MelonLogger.Msg($"[ItemFoundPage-Multi]   Container child {i}: {child.name}");
                            // Check if this child has an "evo" element (indicates it's the item card)
                            var evoCheck = child.Find("evo");
                            if (evoCheck != null || child.name == "LevelUpItem")
                            {
                                parent = child;
                                MelonLogger.Msg($"[ItemFoundPage-Multi] Using '{child.name}' as parent (has evo element)");
                                break;
                            }
                        }

                        // If still not found, search recursively for LevelUpItem
                        if (parent == null)
                        {
                            var allTransforms = instance.GetComponentsInChildren<UnityEngine.Transform>(true);
                            foreach (var t in allTransforms)
                            {
                                if (t.name == "LevelUpItem")
                                {
                                    parent = t;
                                    MelonLogger.Msg($"[ItemFoundPage-Multi] Found LevelUpItem via recursive search");
                                    break;
                                }
                            }
                        }
                    }
                }

                if (parent == null)
                {
                    MelonLogger.Msg("[ItemFoundPage-Multi] Could not find proper parent");
                    return;
                }

                // Comprehensive cleanup: search parent AND evo element (single-formula uses evo, multi uses parent)
                var toDestroy = new System.Collections.Generic.List<UnityEngine.GameObject>();

                // Clean from parent (levelUpItem)
                MelonLogger.Msg($"[ItemFoundPage-Multi] Parent '{parent.name}' has {parent.childCount} children:");
                for (int i = 0; i < parent.childCount; i++)
                {
                    var child = parent.GetChild(i);
                    MelonLogger.Msg($"[ItemFoundPage-Multi]   Child {i}: {child.name}");
                    if (child != null && (child.name.StartsWith("EvoFormula") || child.name == "MoreButton"))
                    {
                        toDestroy.Add(child.gameObject);
                    }
                }

                // Also clean from evo element if it exists (single-formula might have put formulas there)
                var evoElement = parent.Find("evo");
                if (evoElement != null)
                {
                    MelonLogger.Msg($"[ItemFoundPage-Multi] 'evo' element has {evoElement.childCount} children:");
                    for (int i = 0; i < evoElement.childCount; i++)
                    {
                        var child = evoElement.GetChild(i);
                        MelonLogger.Msg($"[ItemFoundPage-Multi]   evo child {i}: {child.name}");
                        if (child != null && (child.name.StartsWith("EvoFormula") || child.name == "MoreButton"))
                        {
                            toDestroy.Add(child.gameObject);
                        }
                    }
                }

                foreach (var go in toDestroy)
                {
                    if (go != null)
                    {
                        MelonLogger.Msg($"[ItemFoundPage-Multi] Destroying: {go.name}");
                        UnityEngine.Object.DestroyImmediate(go);
                    }
                }
                MelonLogger.Msg($"[ItemFoundPage-Multi] Cleaned up {toDestroy.Count} old formula objects total");

                // Get font from existing UI
                Il2CppTMPro.TMP_FontAsset font = null;
                UnityEngine.Material fontMaterial = null;
                var existingTMPs = instance.GetComponentsInChildren<Il2CppTMPro.TextMeshProUGUI>(true);
                if (existingTMPs.Length > 0)
                {
                    font = existingTMPs[0].font;
                    fontMaterial = existingTMPs[0].fontSharedMaterial;
                }

                float iconSize = 24f;
                float xSpacing = 4f;
                float textWidth = 16f;
                float rowHeight = iconSize + 4f;

                // Limit displayed formulas to 2, show "+X" button for rest
                int maxFormulas = 2;
                int displayCount = System.Math.Min(formulas.Count, maxFormulas);

                for (int f = 0; f < displayCount; f++)
                {
                    var formula = formulas[f];

                    var formulaObj = new UnityEngine.GameObject($"EvoFormula_Pickup_{f}");
                    formulaObj.transform.SetParent(parent, false);

                    var formulaRect = formulaObj.AddComponent<UnityEngine.RectTransform>();
                    // Use percentage-based positioning for resolution independence
                    // Position in upper-right, under the "New!" text, stacking downward
                    float anchorY = 0.78f - (f * 0.13f); // Start at 78%, stack downward
                    formulaRect.anchorMin = new UnityEngine.Vector2(0.5f, anchorY - 0.11f);
                    formulaRect.anchorMax = new UnityEngine.Vector2(0.98f, anchorY);
                    formulaRect.pivot = new UnityEngine.Vector2(0f, 0.5f); // Left-aligned within right area
                    formulaRect.anchoredPosition = UnityEngine.Vector2.zero;
                    formulaRect.offsetMin = UnityEngine.Vector2.zero;
                    formulaRect.offsetMax = UnityEngine.Vector2.zero;

                    float totalWidth = formula.Ingredients.Count * iconSize +
                                       (formula.Ingredients.Count - 1) * xSpacing +
                                       textWidth + (formula.ResultSprite != null ? iconSize : 0);

                    formulaRect.sizeDelta = new UnityEngine.Vector2(totalWidth, rowHeight);

                    float xPos = 0f;

                    // Add ingredients
                    for (int i = 0; i < formula.Ingredients.Count; i++)
                    {
                        var (sprite, owned) = formula.Ingredients[i];

                        if (i > 0 && font != null)
                        {
                            var plusObj = new UnityEngine.GameObject("Plus");
                            plusObj.transform.SetParent(formulaObj.transform, false);
                            var plusTMP = plusObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                            plusTMP.font = font;
                            plusTMP.text = "+";
                            plusTMP.fontSize = iconSize * 0.6f;
                            plusTMP.color = UnityEngine.Color.white;
                            plusTMP.alignment = Il2CppTMPro.TextAlignmentOptions.Center;

                            var plusRect = plusTMP.GetComponent<UnityEngine.RectTransform>();
                            plusRect.anchorMin = new UnityEngine.Vector2(0f, 0.5f);
                            plusRect.anchorMax = new UnityEngine.Vector2(0f, 0.5f);
                            plusRect.pivot = new UnityEngine.Vector2(0f, 0.5f);
                            plusRect.anchoredPosition = new UnityEngine.Vector2(xPos, 0f);
                            plusRect.sizeDelta = new UnityEngine.Vector2(textWidth, iconSize);
                            xPos += textWidth;
                        }

                        var iconObj = new UnityEngine.GameObject($"Icon_{i}");
                        iconObj.transform.SetParent(formulaObj.transform, false);

                        var iconRect = iconObj.AddComponent<UnityEngine.RectTransform>();
                        iconRect.anchorMin = new UnityEngine.Vector2(0f, 0.5f);
                        iconRect.anchorMax = new UnityEngine.Vector2(0f, 0.5f);
                        iconRect.pivot = new UnityEngine.Vector2(0f, 0.5f);
                        iconRect.anchoredPosition = new UnityEngine.Vector2(xPos, 0f);
                        iconRect.sizeDelta = new UnityEngine.Vector2(iconSize, iconSize);

                        var iconImage = iconObj.AddComponent<UnityEngine.UI.Image>();
                        iconImage.sprite = sprite;

                        if (owned && font != null)
                        {
                            var checkObj = new UnityEngine.GameObject("Check");
                            checkObj.transform.SetParent(iconObj.transform, false);
                            var checkTMP = checkObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                            checkTMP.font = font;
                            checkTMP.text = "✓";
                            checkTMP.fontSize = iconSize * 0.5f;
                            checkTMP.color = new UnityEngine.Color(0.2f, 1f, 0.2f, 1f);
                            checkTMP.alignment = Il2CppTMPro.TextAlignmentOptions.Center;

                            var checkRect = checkTMP.GetComponent<UnityEngine.RectTransform>();
                            checkRect.anchorMin = new UnityEngine.Vector2(1f, 0f);
                            checkRect.anchorMax = new UnityEngine.Vector2(1f, 0f);
                            checkRect.pivot = new UnityEngine.Vector2(1f, 0f);
                            checkRect.anchoredPosition = new UnityEngine.Vector2(2f, -2f);
                            checkRect.sizeDelta = new UnityEngine.Vector2(iconSize * 0.5f, iconSize * 0.5f);
                        }

                        xPos += iconSize + xSpacing;
                    }

                    // Add arrow
                    if (font != null)
                    {
                        var arrowObj = new UnityEngine.GameObject("Arrow");
                        arrowObj.transform.SetParent(formulaObj.transform, false);
                        var arrowTMP = arrowObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                        arrowTMP.font = font;
                        arrowTMP.text = "→";
                        arrowTMP.fontSize = iconSize * 0.6f;
                        arrowTMP.color = UnityEngine.Color.white;
                        arrowTMP.alignment = Il2CppTMPro.TextAlignmentOptions.Center;

                        var arrowRect = arrowTMP.GetComponent<UnityEngine.RectTransform>();
                        arrowRect.anchorMin = new UnityEngine.Vector2(0f, 0.5f);
                        arrowRect.anchorMax = new UnityEngine.Vector2(0f, 0.5f);
                        arrowRect.pivot = new UnityEngine.Vector2(0f, 0.5f);
                        arrowRect.anchoredPosition = new UnityEngine.Vector2(xPos, 0f);
                        arrowRect.sizeDelta = new UnityEngine.Vector2(textWidth, iconSize);
                        xPos += textWidth;
                    }

                    // Add result icon
                    if (formula.ResultSprite != null)
                    {
                        var resultObj = new UnityEngine.GameObject("Result");
                        resultObj.transform.SetParent(formulaObj.transform, false);

                        var resultRect = resultObj.AddComponent<UnityEngine.RectTransform>();
                        resultRect.anchorMin = new UnityEngine.Vector2(0f, 0.5f);
                        resultRect.anchorMax = new UnityEngine.Vector2(0f, 0.5f);
                        resultRect.pivot = new UnityEngine.Vector2(0f, 0.5f);
                        resultRect.anchoredPosition = new UnityEngine.Vector2(xPos, 0f);
                        resultRect.sizeDelta = new UnityEngine.Vector2(iconSize, iconSize);

                        var resultImage = resultObj.AddComponent<UnityEngine.UI.Image>();
                        resultImage.sprite = formula.ResultSprite;
                    }
                }

                // Show "[+X]" button if there are additional formulas
                if (formulas.Count > maxFormulas && font != null)
                {
                    int remaining = formulas.Count - maxFormulas;

                    // Create button container positioned below the formulas
                    var moreObj = new UnityEngine.GameObject("MoreButton");
                    moreObj.transform.SetParent(parent, false);

                    var moreRect = moreObj.AddComponent<UnityEngine.RectTransform>();
                    // Position to the right of formulas (like level-up UI)
                    moreRect.anchorMin = new UnityEngine.Vector2(0.88f, 0.53f);
                    moreRect.anchorMax = new UnityEngine.Vector2(0.98f, 0.78f);
                    moreRect.pivot = new UnityEngine.Vector2(0.5f, 0.5f);
                    moreRect.anchoredPosition = UnityEngine.Vector2.zero;
                    moreRect.offsetMin = UnityEngine.Vector2.zero;
                    moreRect.offsetMax = UnityEngine.Vector2.zero;

                    // Add Button component for click interaction
                    var button = moreObj.AddComponent<UnityEngine.UI.Button>();

                    // Add background image for button appearance
                    var bgImage = moreObj.AddComponent<UnityEngine.UI.Image>();
                    bgImage.color = new UnityEngine.Color(0.3f, 0.3f, 0.3f, 0.8f);
                    bgImage.raycastTarget = true;
                    button.targetGraphic = bgImage;

                    // Store formulas for this specific button using its instance ID
                    int buttonId = moreObj.GetInstanceID();
                    LevelUpItemUI_SetWeaponData_Patch.buttonFormulas[buttonId] = formulas.GetRange(maxFormulas, formulas.Count - maxFormulas);
                    LevelUpItemUI_SetWeaponData_Patch.buttonIconSizes[buttonId] = iconSize;
                    LevelUpItemUI_SetWeaponData_Patch.buttonContainers[buttonId] = parent;

                    // Add click handler that captures the button ID
                    button.onClick.AddListener((UnityEngine.Events.UnityAction)(() => LevelUpItemUI_SetWeaponData_Patch.OnMoreButtonClick(buttonId)));

                    // Add text
                    var textObj = new UnityEngine.GameObject("Text");
                    textObj.transform.SetParent(moreObj.transform, false);

                    var moreTMP = textObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                    moreTMP.font = font;
                    if (fontMaterial != null) moreTMP.fontSharedMaterial = fontMaterial;
                    moreTMP.text = $"+{remaining}";
                    moreTMP.fontSize = iconSize * 0.7f;
                    moreTMP.color = new UnityEngine.Color(1f, 1f, 1f, 1f);
                    moreTMP.alignment = Il2CppTMPro.TextAlignmentOptions.Center;
                    moreTMP.raycastTarget = false;

                    var textRect = moreTMP.GetComponent<UnityEngine.RectTransform>();
                    textRect.anchorMin = new UnityEngine.Vector2(0f, 0f);
                    textRect.anchorMax = new UnityEngine.Vector2(1f, 1f);
                    textRect.offsetMin = UnityEngine.Vector2.zero;
                    textRect.offsetMax = UnityEngine.Vector2.zero;

                    MelonLogger.Msg($"[ItemFoundPage] Added +{remaining} button");
                }

                MelonLogger.Msg($"[ItemFoundPage] Displayed {displayCount} of {formulas.Count} formulas");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error displaying formulas on ItemFoundPage: {ex}");
            }
        }
    }

    // Patch for ItemFoundPage PowerUp/Item pickups
    [HarmonyPatch(typeof(Il2CppVampireSurvivors.UI.ItemFoundPage), "SetItemDisplay")]
    public class ItemFoundPage_SetItemDisplay_Patch
    {
        public static void Postfix(Il2CppVampireSurvivors.UI.ItemFoundPage __instance)
        {
            try
            {
                MelonLogger.Msg($"[ItemFoundPage.SetItemDisplay] Called");

                // Get item data from instance properties
                var itemType = __instance._item;
                var itemData = __instance._itemData;
                var dataManager = __instance._dataManager;

                if (itemData == null || dataManager == null)
                {
                    MelonLogger.Msg("[ItemFoundPage] Missing item data");
                    return;
                }

                MelonLogger.Msg($"[ItemFoundPage] Item: {itemType}");

                var weaponsDict = dataManager.GetConvertedWeapons();
                var powerUpsDict = dataManager.GetConvertedPowerUpData();

                if (weaponsDict == null) return;

                // Find all weapons that use this PowerUp for evolution
                var formulas = new System.Collections.Generic.List<LevelUpItemUI_SetWeaponData_Patch.EvolutionFormula>();

                var dictType = weaponsDict.GetType();
                var keysProperty = dictType.GetProperty("Keys");
                var tryGetMethod = dictType.GetMethod("TryGetValue");

                if (keysProperty == null || tryGetMethod == null) return;

                var keysCollection = keysProperty.GetValue(weaponsDict);
                if (keysCollection == null) return;

                var getEnumeratorMethod = keysCollection.GetType().GetMethod("GetEnumerator");
                if (getEnumeratorMethod == null) return;

                var enumerator = getEnumeratorMethod.Invoke(keysCollection, null);
                var enumeratorType = enumerator.GetType();
                var moveNextMethod = enumeratorType.GetMethod("MoveNext");
                var currentProperty = enumeratorType.GetProperty("Current");

                while ((bool)moveNextMethod.Invoke(enumerator, null))
                {
                    var weaponTypeKey = currentProperty.GetValue(enumerator);
                    if (weaponTypeKey == null) continue;

                    var weaponParams = new object[] { weaponTypeKey, null };
                    var found = (bool)tryGetMethod.Invoke(weaponsDict, weaponParams);
                    var weaponList = weaponParams[1];

                    if (!found || weaponList == null) continue;

                    var countProp = weaponList.GetType().GetProperty("Count");
                    var itemProp = weaponList.GetType().GetProperty("Item");
                    if (countProp == null || itemProp == null) continue;

                    int itemCount = (int)countProp.GetValue(weaponList);
                    if (itemCount == 0) continue;

                    var weaponData = itemProp.GetValue(weaponList, new object[] { 0 }) as WeaponData;
                    if (weaponData == null || weaponData.evoSynergy == null || string.IsNullOrEmpty(weaponData.evoInto))
                        continue;

                    // Check if weapon is available (not hidden DLC)
                    if (!LevelUpItemUI_SetWeaponData_Patch.IsWeaponAvailable(weaponData))
                        continue;

                    // Check if this weapon requires our PowerUp
                    bool weaponRequiresThisPowerUp = false;
                    foreach (var reqType in weaponData.evoSynergy)
                    {
                        if (reqType.ToString() == itemType.ToString())
                        {
                            weaponRequiresThisPowerUp = true;
                            break;
                        }
                    }

                    if (weaponRequiresThisPowerUp)
                    {
                        WeaponType wType = (WeaponType)weaponTypeKey;

                        var formula = new LevelUpItemUI_SetWeaponData_Patch.EvolutionFormula();
                        formula.ResultName = weaponData.evoInto;

                        // Add the weapon
                        var weaponSprite = LevelUpItemUI_SetWeaponData_Patch.TryLoadFromWeapons(wType, weaponsDict);
                        if (weaponSprite == null) continue;
                        formula.Ingredients.Add((weaponSprite, false)); // Don't know if player owns it

                        // Add requirements
                        bool missingReq = false;
                        foreach (var reqType in weaponData.evoSynergy)
                        {
                            var reqSprite = LevelUpItemUI_SetWeaponData_Patch.LoadSpriteForRequirement(reqType, weaponsDict, powerUpsDict);
                            if (reqSprite != null)
                            {
                                // Mark this powerup as owned (player just picked it up)
                                bool owned = reqType.ToString() == itemType.ToString();
                                formula.Ingredients.Add((reqSprite, owned));
                            }
                            else
                            {
                                missingReq = true;
                                break;
                            }
                        }

                        if (missingReq) continue;

                        // Load result sprite
                        formula.ResultSprite = LevelUpItemUI_SetWeaponData_Patch.LoadEvoResultSprite(weaponData.evoInto, weaponsDict);
                        if (formula.ResultSprite == null) continue;

                        // Check for duplicates
                        bool alreadyExists = formulas.Exists(f => f.ResultName == formula.ResultName);
                        if (!alreadyExists && formula.Ingredients.Count > 0)
                        {
                            formulas.Add(formula);
                        }
                    }
                }

                // Display formulas (limit to first 2 for space)
                if (formulas.Count > 0)
                {
                    DisplayFormulasOnItemFoundPage(__instance, formulas);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in ItemFoundPage SetItemDisplay patch: {ex}");
            }
        }

        private static void DisplayFormulasOnItemFoundPage(Il2CppVampireSurvivors.UI.ItemFoundPage instance,
            System.Collections.Generic.List<LevelUpItemUI_SetWeaponData_Patch.EvolutionFormula> formulas)
        {
            try
            {
                var contentPanel = instance._ContentPanel;
                if (contentPanel == null) return;

                // Clean up existing
                for (int i = contentPanel.childCount - 1; i >= 0; i--)
                {
                    var child = contentPanel.GetChild(i);
                    if (child.name.StartsWith("EvoFormula"))
                    {
                        UnityEngine.Object.Destroy(child.gameObject);
                    }
                }

                // Get font
                Il2CppTMPro.TMP_FontAsset font = null;
                var existingTMPs = instance.GetComponentsInChildren<Il2CppTMPro.TextMeshProUGUI>(true);
                if (existingTMPs.Length > 0) font = existingTMPs[0].font;

                float iconSize = 20f;
                float xSpacing = 2f;
                float textWidth = 14f;
                float rowHeight = iconSize + 4f;

                int maxFormulas = System.Math.Min(formulas.Count, 2);

                for (int rowIndex = 0; rowIndex < maxFormulas; rowIndex++)
                {
                    var formula = formulas[rowIndex];

                    var rowObj = new UnityEngine.GameObject($"EvoFormula_{rowIndex}");
                    rowObj.transform.SetParent(contentPanel, false);

                    var rowRect = rowObj.AddComponent<UnityEngine.RectTransform>();
                    rowRect.anchorMin = new UnityEngine.Vector2(0.5f, 0f);
                    rowRect.anchorMax = new UnityEngine.Vector2(0.5f, 0f);
                    rowRect.pivot = new UnityEngine.Vector2(0.5f, 0f);
                    rowRect.anchoredPosition = new UnityEngine.Vector2(0f, 10f + rowIndex * rowHeight);

                    float totalWidth = formula.Ingredients.Count * iconSize +
                                       (formula.Ingredients.Count - 1) * textWidth +
                                       textWidth +
                                       (formula.ResultSprite != null ? iconSize : 0);

                    rowRect.sizeDelta = new UnityEngine.Vector2(totalWidth, iconSize);

                    float xPos = -totalWidth / 2f;

                    // Add ingredients
                    for (int i = 0; i < formula.Ingredients.Count; i++)
                    {
                        var (sprite, owned) = formula.Ingredients[i];

                        if (i > 0 && font != null)
                        {
                            var plusObj = new UnityEngine.GameObject("Plus");
                            plusObj.transform.SetParent(rowObj.transform, false);
                            var plusTMP = plusObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                            plusTMP.font = font;
                            plusTMP.text = "+";
                            plusTMP.fontSize = iconSize * 0.6f;
                            plusTMP.color = UnityEngine.Color.white;
                            plusTMP.alignment = Il2CppTMPro.TextAlignmentOptions.Center;

                            var plusRect = plusTMP.GetComponent<UnityEngine.RectTransform>();
                            plusRect.anchorMin = new UnityEngine.Vector2(0.5f, 0.5f);
                            plusRect.anchorMax = new UnityEngine.Vector2(0.5f, 0.5f);
                            plusRect.pivot = new UnityEngine.Vector2(0f, 0.5f);
                            plusRect.anchoredPosition = new UnityEngine.Vector2(xPos, 0f);
                            plusRect.sizeDelta = new UnityEngine.Vector2(textWidth, iconSize);
                            xPos += textWidth;
                        }

                        var iconObj = new UnityEngine.GameObject($"Icon_{i}");
                        iconObj.transform.SetParent(rowObj.transform, false);

                        var iconRect = iconObj.AddComponent<UnityEngine.RectTransform>();
                        iconRect.anchorMin = new UnityEngine.Vector2(0.5f, 0.5f);
                        iconRect.anchorMax = new UnityEngine.Vector2(0.5f, 0.5f);
                        iconRect.pivot = new UnityEngine.Vector2(0f, 0.5f);
                        iconRect.anchoredPosition = new UnityEngine.Vector2(xPos, 0f);
                        iconRect.sizeDelta = new UnityEngine.Vector2(iconSize, iconSize);

                        var iconImage = iconObj.AddComponent<UnityEngine.UI.Image>();
                        iconImage.sprite = sprite;

                        if (owned && font != null)
                        {
                            var checkObj = new UnityEngine.GameObject("Check");
                            checkObj.transform.SetParent(iconObj.transform, false);
                            var checkTMP = checkObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                            checkTMP.font = font;
                            checkTMP.text = "✓";
                            checkTMP.fontSize = iconSize * 0.5f;
                            checkTMP.color = new UnityEngine.Color(0.2f, 1f, 0.2f, 1f);
                            checkTMP.alignment = Il2CppTMPro.TextAlignmentOptions.Center;

                            var checkRect = checkTMP.GetComponent<UnityEngine.RectTransform>();
                            checkRect.anchorMin = new UnityEngine.Vector2(1f, 0f);
                            checkRect.anchorMax = new UnityEngine.Vector2(1f, 0f);
                            checkRect.pivot = new UnityEngine.Vector2(1f, 0f);
                            checkRect.anchoredPosition = new UnityEngine.Vector2(2f, -2f);
                            checkRect.sizeDelta = new UnityEngine.Vector2(iconSize * 0.5f, iconSize * 0.5f);
                        }

                        xPos += iconSize + xSpacing;
                    }

                    // Arrow
                    if (font != null)
                    {
                        var arrowObj = new UnityEngine.GameObject("Arrow");
                        arrowObj.transform.SetParent(rowObj.transform, false);
                        var arrowTMP = arrowObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                        arrowTMP.font = font;
                        arrowTMP.text = "→";
                        arrowTMP.fontSize = iconSize * 0.6f;
                        arrowTMP.color = UnityEngine.Color.white;
                        arrowTMP.alignment = Il2CppTMPro.TextAlignmentOptions.Center;

                        var arrowRect = arrowTMP.GetComponent<UnityEngine.RectTransform>();
                        arrowRect.anchorMin = new UnityEngine.Vector2(0.5f, 0.5f);
                        arrowRect.anchorMax = new UnityEngine.Vector2(0.5f, 0.5f);
                        arrowRect.pivot = new UnityEngine.Vector2(0f, 0.5f);
                        arrowRect.anchoredPosition = new UnityEngine.Vector2(xPos, 0f);
                        arrowRect.sizeDelta = new UnityEngine.Vector2(textWidth, iconSize);
                        xPos += textWidth;
                    }

                    // Result
                    if (formula.ResultSprite != null)
                    {
                        var resultObj = new UnityEngine.GameObject("Result");
                        resultObj.transform.SetParent(rowObj.transform, false);

                        var resultRect = resultObj.AddComponent<UnityEngine.RectTransform>();
                        resultRect.anchorMin = new UnityEngine.Vector2(0.5f, 0.5f);
                        resultRect.anchorMax = new UnityEngine.Vector2(0.5f, 0.5f);
                        resultRect.pivot = new UnityEngine.Vector2(0f, 0.5f);
                        resultRect.anchoredPosition = new UnityEngine.Vector2(xPos, 0f);
                        resultRect.sizeDelta = new UnityEngine.Vector2(iconSize, iconSize);

                        var resultImage = resultObj.AddComponent<UnityEngine.UI.Image>();
                        resultImage.sprite = formula.ResultSprite;
                    }
                }

                MelonLogger.Msg($"[ItemFoundPage] Displayed {maxFormulas} formula(s) for powerup");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error displaying formulas on ItemFoundPage: {ex}");
            }
        }
    }
}
