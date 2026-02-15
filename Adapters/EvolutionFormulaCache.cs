using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppVampireSurvivors.Data;
using Il2CppVampireSurvivors.Data.Weapons;
using VSItemTooltips.Core.Models;

namespace VSItemTooltips.Adapters
{
    /// <summary>
    /// Caches all evolution formulas for the current game session.
    /// Builds the cache once from IL2CPP game data, then serves it multiple times
    /// to avoid repeated expensive reflection and iteration.
    /// </summary>
    public class EvolutionFormulaCache
    {
        private List<EvolutionFormula> _cachedFormulas;
        private Dictionary<string, List<EvolutionFormula>> _formulasByBaseWeapon;
        private Dictionary<string, List<EvolutionFormula>> _formulasByPassive;
        private Dictionary<string, EvolutionFormula> _formulasByEvolvedWeapon;
        private readonly GameDataAdapter _adapter;
        private readonly ItemTooltipsMod _mod; // Currently unused

        public EvolutionFormulaCache(GameDataAdapter adapter, ItemTooltipsMod mod = null)
        {
            _adapter = adapter;
            _mod = mod;
        }

        /// <summary>
        /// Builds the formula cache from all weapons in the game data.
        /// Should be called once when DataManager is cached.
        /// </summary>
        public void BuildCache()
        {
            _cachedFormulas = new List<EvolutionFormula>();
            _formulasByBaseWeapon = new Dictionary<string, List<EvolutionFormula>>();
            _formulasByPassive = new Dictionary<string, List<EvolutionFormula>>();
            _formulasByEvolvedWeapon = new Dictionary<string, EvolutionFormula>();

            // Get all weapon types from the enum
            foreach (WeaponType weaponType in Enum.GetValues(typeof(WeaponType)))
            {
                var weaponData = ItemTooltipsMod.GetWeaponData(weaponType);
                if (weaponData == null) continue;

                // Check if this weapon has an evolution
                string evoInto = ItemTooltipsMod.GetPropertyValue<string>(weaponData, "evoInto");
                if (string.IsNullOrEmpty(evoInto)) continue;

                // Get synergy requirements
                var synergyProp = weaponData.GetType().GetProperty("evoSynergy");
                if (synergyProp == null) continue;

                var synergy = synergyProp.GetValue(weaponData) as Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<WeaponType>;
                if (synergy == null || synergy.Length == 0) continue;

                // Convert to Core DTO
                var weaponInfo = _adapter.ConvertToWeaponInfo(weaponData, weaponType);
                if (weaponInfo == null || weaponInfo.RequiredPassives == null) continue;

                // Parse evolved weapon
                if (!Enum.TryParse<WeaponType>(evoInto, out var evolvedType)) continue;

                // Create formula
                var formula = new EvolutionFormula
                {
                    BaseWeaponId = weaponType.ToString(),
                    BaseWeaponName = weaponInfo.Name,
                    EvolvedWeaponId = evoInto,
                    EvolvedWeaponName = GetWeaponName(evoInto),
                    RequiredPassives = weaponInfo.RequiredPassives
                };

                // Add to cache
                _cachedFormulas.Add(formula);

                // Index by base weapon
                if (!_formulasByBaseWeapon.ContainsKey(formula.BaseWeaponId))
                    _formulasByBaseWeapon[formula.BaseWeaponId] = new List<EvolutionFormula>();
                _formulasByBaseWeapon[formula.BaseWeaponId].Add(formula);

                // Index by evolved weapon
                _formulasByEvolvedWeapon[formula.EvolvedWeaponId] = formula;

                // Index by each passive requirement
                foreach (var passive in formula.RequiredPassives)
                {
                    string passiveId = passive.WeaponId ?? passive.ItemId;
                    if (string.IsNullOrEmpty(passiveId)) continue;

                    if (!_formulasByPassive.ContainsKey(passiveId))
                        _formulasByPassive[passiveId] = new List<EvolutionFormula>();
                    _formulasByPassive[passiveId].Add(formula);
                }
            }
        }

        /// <summary>
        /// Gets all cached formulas.
        /// </summary>
        public List<EvolutionFormula> GetAll()
        {
            return _cachedFormulas ?? new List<EvolutionFormula>();
        }

        /// <summary>
        /// Gets the evolution formula for a specific base weapon.
        /// Returns null if the weapon doesn't evolve.
        /// </summary>
        public EvolutionFormula GetForWeapon(WeaponType weaponType)
        {
            string weaponId = weaponType.ToString();
            if (_formulasByBaseWeapon != null && _formulasByBaseWeapon.TryGetValue(weaponId, out var formulas))
            {
                return formulas.FirstOrDefault();
            }
            return null;
        }

        /// <summary>
        /// Gets the formula that produces the given evolved weapon.
        /// Useful for "Evolved From" sections.
        /// </summary>
        public EvolutionFormula GetForEvolvedWeapon(WeaponType evolvedType)
        {
            string evolvedId = evolvedType.ToString();
            if (_formulasByEvolvedWeapon != null && _formulasByEvolvedWeapon.TryGetValue(evolvedId, out var formula))
            {
                return formula;
            }
            return null;
        }

        /// <summary>
        /// Gets all formulas that use a specific weapon as a passive requirement.
        /// Excludes dual-weapon partners (weapons that produce the same evolution).
        /// </summary>
        public List<EvolutionFormula> GetFormulasUsingWeaponAsPassive(WeaponType passiveType, string excludeEvoInto = null)
        {
            string passiveId = passiveType.ToString();
            if (_formulasByPassive == null || !_formulasByPassive.TryGetValue(passiveId, out var formulas))
            {
                return new List<EvolutionFormula>();
            }

            var result = formulas.ToList();

            // Filter out dual-weapon partners if requested
            if (!string.IsNullOrEmpty(excludeEvoInto))
            {
                result = result.Where(f => f.EvolvedWeaponId != excludeEvoInto).ToList();
            }

            // Also filter out self-reference (weapon using itself as passive)
            result = result.Where(f => f.BaseWeaponId != passiveId).ToList();

            return result;
        }

        /// <summary>
        /// Gets all formulas that use a specific item as a passive requirement.
        /// </summary>
        public List<EvolutionFormula> GetFormulasUsingItemAsPassive(ItemType itemType)
        {
            string itemId = itemType.ToString();
            if (_formulasByPassive == null || !_formulasByPassive.TryGetValue(itemId, out var formulas))
            {
                return new List<EvolutionFormula>();
            }

            return formulas.ToList();
        }

        /// <summary>
        /// Counts how many formulas use this weapon as a passive (excluding self-reference and dual partners).
        /// Used to determine if a weapon is primarily a passive item.
        /// </summary>
        public int CountPassiveUsages(WeaponType passiveType, string ownEvoInto = null)
        {
            return GetFormulasUsingWeaponAsPassive(passiveType, ownEvoInto).Count;
        }

        /// <summary>
        /// Determines if a weapon is primarily used as a passive in evolution formulas.
        /// A weapon is considered "primary passive" if it's used in 2+ other evolutions.
        /// </summary>
        public bool IsPrimaryPassive(WeaponType weaponType)
        {
            // Get own evolution (if any) to exclude dual partners
            var ownFormula = GetForWeapon(weaponType);
            string ownEvoInto = ownFormula?.EvolvedWeaponId;

            int usageCount = CountPassiveUsages(weaponType, ownEvoInto);
            return usageCount >= 2;
        }

        /// <summary>
        /// Helper to get weapon name from weapon type string.
        /// </summary>
        private string GetWeaponName(string weaponTypeStr)
        {
            if (Enum.TryParse<WeaponType>(weaponTypeStr, out var weaponType))
            {
                var data = ItemTooltipsMod.GetWeaponData(weaponType);
                if (data != null)
                {
                    return ItemTooltipsMod.GetLocalizedWeaponName(data, weaponType);
                }
            }
            return weaponTypeStr;
        }

        /// <summary>
        /// Gets the count of cached formulas (for diagnostics).
        /// </summary>
        public int Count => _cachedFormulas?.Count ?? 0;
    }
}
