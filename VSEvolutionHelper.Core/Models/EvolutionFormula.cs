using System.Collections.Generic;

namespace VSItemTooltips.Core.Models
{
    /// <summary>
    /// Represents a complete evolution formula (base → passives → evolved).
    /// Pure C# model for testing and business logic.
    /// </summary>
    public class EvolutionFormula
    {
        /// <summary>Base weapon ID (e.g., "WHIP")</summary>
        public string BaseWeaponId { get; set; }

        /// <summary>Base weapon display name</summary>
        public string BaseWeaponName { get; set; }

        /// <summary>Evolved weapon ID (e.g., "BLOODY_TEAR")</summary>
        public string EvolvedWeaponId { get; set; }

        /// <summary>Evolved weapon display name</summary>
        public string EvolvedWeaponName { get; set; }

        /// <summary>Base weapon that evolves (full info, optional)</summary>
        public WeaponInfo BaseWeapon { get; set; }

        /// <summary>Evolved weapon (result, full info, optional)</summary>
        public WeaponInfo EvolvedWeapon { get; set; }

        /// <summary>Required passives (weapons or items)</summary>
        public List<PassiveRequirement> RequiredPassives { get; set; } = new List<PassiveRequirement>();

        /// <summary>Is this formula complete (player has all requirements)?</summary>
        public bool IsComplete { get; set; }

        /// <summary>Missing requirements (if not complete)</summary>
        public List<PassiveRequirement> MissingRequirements { get; set; } = new List<PassiveRequirement>();

        /// <summary>Are any of the required items banned?</summary>
        public bool HasBannedRequirements { get; set; }
    }

    /// <summary>
    /// Result of evolution validation.
    /// </summary>
    public class EvolutionValidationResult
    {
        /// <summary>Can the weapon evolve with current setup?</summary>
        public bool CanEvolve { get; set; }
        
        /// <summary>Missing passives (empty if can evolve)</summary>
        public List<string> MissingPassives { get; set; } = new List<string>();
        
        /// <summary>Which missing passives require max level?</summary>
        public List<string> RequiresMaxLevel { get; set; } = new List<string>();
        
        /// <summary>Reason why evolution is blocked (if CanEvolve is false)</summary>
        public string BlockReason { get; set; }
    }
}
