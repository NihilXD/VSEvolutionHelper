using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppVampireSurvivors.Data;
using Il2CppVampireSurvivors.Data.Weapons;
using VSItemTooltips.Core.Models;

namespace VSItemTooltips.Adapters
{
    /// <summary>
    /// Adapts IL2CPP game data types to Core DTOs for use in business logic.
    /// This keeps IL2CPP dependencies isolated to the main mod project.
    /// </summary>
    public class GameDataAdapter
    {
        private readonly ItemTooltipsMod _mod; // Currently unused - uses static methods

        public GameDataAdapter(ItemTooltipsMod mod = null)
        {
            _mod = mod;
        }

        /// <summary>
        /// Converts IL2CPP WeaponData to Core WeaponInfo DTO.
        /// </summary>
        public WeaponInfo ConvertToWeaponInfo(WeaponData weaponData, WeaponType weaponType)
        {
            if (weaponData == null) return null;

            var weaponInfo = new WeaponInfo
            {
                Id = weaponType.ToString(),
                Name = ItemTooltipsMod.GetLocalizedWeaponName(weaponData, weaponType),
                Description = ItemTooltipsMod.GetLocalizedWeaponDescription(weaponData, weaponType)
            };

            // Get evolution data
            try
            {
                var evoInto = ItemTooltipsMod.GetPropertyValue<string>(weaponData, "evoInto");
                weaponInfo.EvolvesInto = evoInto;

                // Get synergy requirements
                var synergyProp = weaponData.GetType().GetProperty("evoSynergy");
                if (synergyProp != null)
                {
                    var synergy = synergyProp.GetValue(weaponData) as Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<WeaponType>;
                    if (synergy != null && synergy.Length > 0)
                    {
                        var requiresMaxTypes = ItemTooltipsMod.GetRequiresMaxFromEvolved(evoInto);
                        weaponInfo.RequiredPassives = ConvertPassiveRequirements(synergy, requiresMaxTypes);
                    }
                }

                // Determine if this is primarily a passive or active weapon
                weaponInfo.IsPrimaryPassive = false; // Will be determined by service
                weaponInfo.IsEvolved = false; // Will be set if this is an evolution result
            }
            catch { }

            return weaponInfo;
        }

        /// <summary>
        /// Converts IL2CPP evoSynergy array to Core PassiveRequirement list.
        /// </summary>
        private List<PassiveRequirement> ConvertPassiveRequirements(
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<WeaponType> evoSynergy,
            HashSet<int> requiresMaxTypes)
        {
            var requirements = new List<PassiveRequirement>();
            if (evoSynergy == null) return requirements;

            for (int i = 0; i < evoSynergy.Length; i++)
            {
                var reqType = evoSynergy[i];
                var requirement = new PassiveRequirement
                {
                    RequiresMaxLevel = requiresMaxTypes != null && requiresMaxTypes.Contains((int)reqType)
                };

                // Check if this is a weapon or item
                var reqData = ItemTooltipsMod.GetWeaponData(reqType);
                if (reqData != null)
                {
                    requirement.WeaponId = reqType.ToString();
                    requirement.Name = ItemTooltipsMod.GetLocalizedWeaponName(reqData, reqType);
                    requirement.Type = PassiveType.Weapon;
                }
                else
                {
                    // Try as item
                    int enumValue = (int)reqType;
                    if (Enum.IsDefined(typeof(ItemType), enumValue))
                    {
                        var itemType = (ItemType)enumValue;
                        var itemData = ItemTooltipsMod.GetPowerUpData(itemType);
                        if (itemData != null)
                        {
                            requirement.ItemId = itemType.ToString();
                            requirement.Name = ItemTooltipsMod.GetLocalizedPowerUpName(itemData, itemType);
                            requirement.Type = PassiveType.Item;
                        }
                    }
                }

                requirements.Add(requirement);
            }

            return requirements;
        }

        /// <summary>
        /// Gets owned passive IDs from game session.
        /// </summary>
        public List<string> GetOwnedPassiveIds()
        {
            var owned = new List<string>();

            try
            {
                // Get owned weapons
                var ownedWeapons = ItemTooltipsMod.GetOwnedWeaponTypes();
                foreach (var wt in ownedWeapons)
                {
                    owned.Add(wt.ToString());
                }

                // Get owned items
                var ownedItems = ItemTooltipsMod.GetOwnedItemTypes();
                foreach (var it in ownedItems)
                {
                    owned.Add(it.ToString());
                }
            }
            catch { }

            return owned;
        }

        /// <summary>
        /// Gets banned item IDs from game state.
        /// </summary>
        public List<string> GetBannedItemIds()
        {
            var banned = new List<string>();

            try
            {
                // Check all weapon types
                foreach (WeaponType wt in Enum.GetValues(typeof(WeaponType)))
                {
                    if (ItemTooltipsMod.IsWeaponBanned(wt))
                        banned.Add(wt.ToString());
                }

                // Check all item types
                foreach (ItemType it in Enum.GetValues(typeof(ItemType)))
                {
                    if (ItemTooltipsMod.IsItemBanned(it))
                        banned.Add(it.ToString());
                }
            }
            catch { }

            return banned;
        }
    }
}
