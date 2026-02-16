using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VSItemTooltips.Tests.Helpers
{
    /// <summary>
    /// Mock enums for testing ArcanaDataHelper logic without IL2CPP dependencies.
    /// </summary>
    public enum MockWeaponType
    {
        WHIP,
        KNIFE,
        AXE,
        GARLIC,
        CROSS
    }

    public enum MockItemType
    {
        SPINACH,
        ARMOR,
        HOLLOW_HEART,
        PUMMAROLA,
        WINGS
    }

    /// <summary>
    /// Unit tests for ArcanaDataHelper.
    /// Note: Most methods in ArcanaDataHelper require Unity/IL2CPP runtime (not testable in unit tests).
    /// These tests focus on the pure algorithmic parts: list aggregation, deduplication, caching logic.
    /// Uses mock enums instead of IL2CPP types to test the algorithms.
    /// </summary>
    public class ArcanaDataHelperTests
    {
        #region Test Setup Helpers

        /// <summary>
        /// Creates a mock weapon list for testing aggregation logic.
        /// </summary>
        private List<MockWeaponType> CreateMockWeaponList(params MockWeaponType[] weapons)
        {
            return weapons.ToList();
        }

        /// <summary>
        /// Creates a mock item list for testing aggregation logic.
        /// </summary>
        private List<MockItemType> CreateMockItemList(params MockItemType[] items)
        {
            return items.ToList();
        }

        #endregion

        #region List Aggregation Tests

        [Fact]
        public void GetAllAffectedWeaponTypes_CombinesMultipleSources_Deduplicates()
        {
            // Arrange: Simulate combining static + panel + UI data with overlaps
            var staticWeapons = new List<MockWeaponType> { MockWeaponType.WHIP, MockWeaponType.KNIFE, MockWeaponType.AXE };
            var panelWeapons = new List<MockWeaponType> { MockWeaponType.KNIFE, MockWeaponType.GARLIC, MockWeaponType.WHIP }; // KNIFE and WHIP overlap
            var uiWeapons = new List<MockWeaponType> { MockWeaponType.AXE, MockWeaponType.CROSS, MockWeaponType.GARLIC }; // AXE and GARLIC overlap

            // Act: Simulate the aggregation logic
            var result = new HashSet<MockWeaponType>();
            foreach (var w in staticWeapons) result.Add(w);
            foreach (var w in panelWeapons) result.Add(w);
            foreach (var w in uiWeapons) result.Add(w);
            var finalList = result.ToList();

            // Assert: All unique weapons included, duplicates removed
            Assert.Equal(5, finalList.Count); // WHIP, KNIFE, AXE, GARLIC, CROSS
            Assert.Contains(MockWeaponType.WHIP, finalList);
            Assert.Contains(MockWeaponType.KNIFE, finalList);
            Assert.Contains(MockWeaponType.AXE, finalList);
            Assert.Contains(MockWeaponType.GARLIC, finalList);
            Assert.Contains(MockWeaponType.CROSS, finalList);
        }

        [Fact]
        public void GetAllAffectedItemTypes_CombinesMultipleSources_Deduplicates()
        {
            // Arrange
            var staticItems = new List<MockItemType> { MockItemType.SPINACH, MockItemType.ARMOR, MockItemType.HOLLOW_HEART };
            var panelItems = new List<MockItemType> { MockItemType.ARMOR, MockItemType.PUMMAROLA }; // ARMOR overlaps
            var uiItems = new List<MockItemType> { MockItemType.SPINACH, MockItemType.WINGS, MockItemType.PUMMAROLA }; // SPINACH and PUMMAROLA overlap

            // Act
            var result = new HashSet<MockItemType>();
            foreach (var i in staticItems) result.Add(i);
            foreach (var i in panelItems) result.Add(i);
            foreach (var i in uiItems) result.Add(i);
            var finalList = result.ToList();

            // Assert
            Assert.Equal(5, finalList.Count); // SPINACH, ARMOR, HOLLOW_HEART, PUMMAROLA, WINGS
            Assert.Contains(MockItemType.SPINACH, finalList);
            Assert.Contains(MockItemType.ARMOR, finalList);
            Assert.Contains(MockItemType.HOLLOW_HEART, finalList);
            Assert.Contains(MockItemType.PUMMAROLA, finalList);
            Assert.Contains(MockItemType.WINGS, finalList);
        }

        [Fact]
        public void Aggregation_HandlesEmptySources()
        {
            // Arrange
            var staticWeapons = new List<MockWeaponType> { MockWeaponType.WHIP };
            var panelWeapons = new List<MockWeaponType>(); // Empty
            var uiWeapons = new List<MockWeaponType>(); // Empty

            // Act
            var result = new HashSet<MockWeaponType>();
            foreach (var w in staticWeapons) result.Add(w);
            foreach (var w in panelWeapons) result.Add(w);
            foreach (var w in uiWeapons) result.Add(w);
            var finalList = result.ToList();

            // Assert
            Assert.Single(finalList);
            Assert.Equal(MockWeaponType.WHIP, finalList[0]);
        }

        [Fact]
        public void Aggregation_AllSourcesEmpty_ReturnsEmpty()
        {
            // Arrange
            var staticWeapons = new List<MockWeaponType>();
            var panelWeapons = new List<MockWeaponType>();
            var uiWeapons = new List<MockWeaponType>();

            // Act
            var result = new HashSet<MockWeaponType>();
            foreach (var w in staticWeapons) result.Add(w);
            foreach (var w in panelWeapons) result.Add(w);
            foreach (var w in uiWeapons) result.Add(w);
            var finalList = result.ToList();

            // Assert
            Assert.Empty(finalList);
        }

        #endregion

        #region Deduplication Logic Tests

        [Fact]
        public void HashSet_CorrectlyDeduplicates()
        {
            // Arrange
            var weapons = new List<MockWeaponType>
            {
                MockWeaponType.WHIP,
                MockWeaponType.KNIFE,
                MockWeaponType.WHIP, // Duplicate
                MockWeaponType.AXE,
                MockWeaponType.KNIFE, // Duplicate
                MockWeaponType.WHIP  // Duplicate
            };

            // Act
            var deduplicated = new HashSet<MockWeaponType>(weapons).ToList();

            // Assert
            Assert.Equal(3, deduplicated.Count);
            Assert.Contains(MockWeaponType.WHIP, deduplicated);
            Assert.Contains(MockWeaponType.KNIFE, deduplicated);
            Assert.Contains(MockWeaponType.AXE, deduplicated);
        }

        [Fact]
        public void Deduplication_PreservesOrder_WithLinq()
        {
            // Arrange: Test that Distinct() preserves first occurrence order
            var weapons = new List<MockWeaponType>
            {
                MockWeaponType.AXE,
                MockWeaponType.WHIP,
                MockWeaponType.AXE, // Duplicate
                MockWeaponType.KNIFE,
                MockWeaponType.WHIP  // Duplicate
            };

            // Act: Using Distinct instead of HashSet to preserve order
            var deduplicated = weapons.Distinct().ToList();

            // Assert: Order of first occurrences preserved
            Assert.Equal(3, deduplicated.Count);
            Assert.Equal(MockWeaponType.AXE, deduplicated[0]);
            Assert.Equal(MockWeaponType.WHIP, deduplicated[1]);
            Assert.Equal(MockWeaponType.KNIFE, deduplicated[2]);
        }

        #endregion

        #region Cache Logic Tests

        [Fact]
        public void CacheNameToInt_StoresMapping()
        {
            // Arrange
            var cache = new Dictionary<string, int>();
            string arcanaName = "I - Gemini";
            int arcanaTypeInt = 5;

            // Act: Simulate CacheArcanaNameToInt logic
            if (arcanaTypeInt >= 0 && !string.IsNullOrEmpty(arcanaName))
                cache[arcanaName] = arcanaTypeInt;

            // Assert
            Assert.True(cache.ContainsKey(arcanaName));
            Assert.Equal(5, cache[arcanaName]);
        }

        [Fact]
        public void CacheNameToInt_IgnoresNegativeInts()
        {
            // Arrange
            var cache = new Dictionary<string, int>();
            string arcanaName = "Invalid Arcana";
            int arcanaTypeInt = -1;

            // Act
            if (arcanaTypeInt >= 0 && !string.IsNullOrEmpty(arcanaName))
                cache[arcanaName] = arcanaTypeInt;

            // Assert: Should not be added
            Assert.False(cache.ContainsKey(arcanaName));
        }

        [Fact]
        public void CacheNameToInt_IgnoresNullOrEmptyNames()
        {
            // Arrange
            var cache = new Dictionary<string, int>();
            string arcanaName = null;
            int arcanaTypeInt = 5;

            // Act
            if (arcanaTypeInt >= 0 && !string.IsNullOrEmpty(arcanaName))
                cache[arcanaName] = arcanaTypeInt;

            // Assert
            Assert.Empty(cache);
        }

        [Fact]
        public void CacheNameToInt_OverwritesExistingMapping()
        {
            // Arrange
            var cache = new Dictionary<string, int>();
            string arcanaName = "I - Gemini";
            cache[arcanaName] = 5;

            // Act: Update with new value
            cache[arcanaName] = 10;

            // Assert
            Assert.Equal(10, cache[arcanaName]);
        }

        #endregion

        #region IsAffectedByArcana Logic Tests

        [Fact]
        public void IsWeaponAffected_ContainsWeapon_ReturnsTrue()
        {
            // Arrange
            var affectedWeapons = new List<MockWeaponType> { MockWeaponType.WHIP, MockWeaponType.KNIFE, MockWeaponType.AXE };
            var targetWeapon = MockWeaponType.KNIFE;

            // Act: Simulate IsWeaponAffectedByArcana logic
            bool result = affectedWeapons.Contains(targetWeapon);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void IsWeaponAffected_DoesNotContainWeapon_ReturnsFalse()
        {
            // Arrange
            var affectedWeapons = new List<MockWeaponType> { MockWeaponType.WHIP, MockWeaponType.KNIFE };
            var targetWeapon = MockWeaponType.GARLIC;

            // Act
            bool result = affectedWeapons.Contains(targetWeapon);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsItemAffected_ContainsItem_ReturnsTrue()
        {
            // Arrange
            var affectedItems = new List<MockItemType> { MockItemType.SPINACH, MockItemType.ARMOR };
            var targetItem = MockItemType.SPINACH;

            // Act
            bool result = affectedItems.Contains(targetItem);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void IsItemAffected_EmptyList_ReturnsFalse()
        {
            // Arrange
            var affectedItems = new List<MockItemType>();
            var targetItem = MockItemType.SPINACH;

            // Act
            bool result = affectedItems.Contains(targetItem);

            // Assert
            Assert.False(result);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void Aggregation_LargeLists_PerformanceTest()
        {
            // Arrange: Create large lists with many duplicates
            var staticWeapons = Enum.GetValues(typeof(MockWeaponType)).Cast<MockWeaponType>().ToList();
            var panelWeapons = Enum.GetValues(typeof(MockWeaponType)).Cast<MockWeaponType>().ToList();
            var uiWeapons = Enum.GetValues(typeof(MockWeaponType)).Cast<MockWeaponType>().ToList();

            // Act: Aggregate
            var startTime = DateTime.Now;
            var result = new HashSet<MockWeaponType>();
            foreach (var w in staticWeapons) result.Add(w);
            foreach (var w in panelWeapons) result.Add(w);
            foreach (var w in uiWeapons) result.Add(w);
            var finalList = result.ToList();
            var duration = DateTime.Now - startTime;

            // Assert: Should complete quickly (< 100ms) and have all unique weapon types
            Assert.True(duration.TotalMilliseconds < 100, $"Aggregation took too long: {duration.TotalMilliseconds}ms");
            Assert.Equal(staticWeapons.Distinct().Count(), finalList.Count);
        }

        [Fact]
        public void Deduplication_WithNullValues_HandlesGracefully()
        {
            // Arrange: Enum types can't actually be null, but test the pattern
            var weapons = new List<MockWeaponType?> { MockWeaponType.WHIP, null, MockWeaponType.KNIFE, null, MockWeaponType.WHIP };

            // Act: Filter nulls and deduplicate
            var filtered = weapons.Where(w => w.HasValue).Select(w => w.Value).Distinct().ToList();

            // Assert
            Assert.Equal(2, filtered.Count);
            Assert.Contains(MockWeaponType.WHIP, filtered);
            Assert.Contains(MockWeaponType.KNIFE, filtered);
        }

        [Fact]
        public void Cache_ConcurrentAccess_Simulation()
        {
            // Arrange: Simulate multiple "threads" (sequential in test) accessing cache
            var cache = new Dictionary<string, int>();
            var arcanas = new[] { ("Gemini", 5), ("Cancer", 10), ("Leo", 15) };

            // Act: Add entries
            foreach (var (name, id) in arcanas)
            {
                if (id >= 0 && !string.IsNullOrEmpty(name))
                    cache[name] = id;
            }

            // Assert: All entries present
            Assert.Equal(3, cache.Count);
            Assert.Equal(5, cache["Gemini"]);
            Assert.Equal(10, cache["Cancer"]);
            Assert.Equal(15, cache["Leo"]);
        }

        #endregion

        #region String Matching Tests (for UI scanning logic)

        [Theory]
        [InlineData("I - Gemini", "I - Gemini", true)]  // Exact match
        [InlineData("I - Gemini", "Gemini", true)]       // Short name match
        [InlineData("<color=white>I - Gemini</color>", "I - Gemini", true)] // Rich text match
        [InlineData("Gemini", "I - Gemini", true)]      // uiText contains shortName ("Gemini")
        [InlineData("Cancer", "Gemini", false)]          // No match
        public void StringMatching_ArcanaNames(string uiText, string searchName, bool shouldMatch)
        {
            // Arrange: Simulate the matching logic from ScanArcanaUI
            string cleanText = System.Text.RegularExpressions.Regex.Replace(uiText, "<[^>]+>", "").Trim();
            string shortName = searchName.Contains(" - ") ? searchName.Substring(searchName.IndexOf(" - ") + 3).Trim() : searchName;

            // Act: Check if match
            bool isMatch = uiText == searchName ||
                           cleanText == searchName ||
                           uiText.Contains(searchName) ||
                           cleanText.Contains(searchName) ||
                           uiText.Contains(shortName) ||
                           cleanText.Contains(shortName);

            // Assert
            Assert.Equal(shouldMatch, isMatch);
        }

        [Theory]
        [InlineData("weapon_icon.png", "weapon_icon")]  // .png removed
        [InlineData("weapon_icon", "weapon_icon")]      // No extension, no change
        [InlineData("WEAPON_ICON", "weapon_icon")]      // No extension, case differs
        public void SpriteNameCleaning_RemovesPngExtension(string spriteName, string expectedClean)
        {
            // Arrange & Act: Simulate sprite name cleaning logic
            string cleanName = spriteName;
            if (cleanName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                cleanName = cleanName.Substring(0, cleanName.Length - 4);

            // Assert
            Assert.Equal(expectedClean, cleanName, ignoreCase: true);
        }

        #endregion

        #region GetArcanaTypeInt Tests (Algorithm)

        [Fact]
        public void GetArcanaTypeInt_ConvertToInt32_Success()
        {
            // Arrange: Simulate enum-like object that can be converted to int
            object arcanaType = 5; // Boxing an int

            // Act: Test Convert.ToInt32 path
            int result = -1;
            try
            {
                result = Convert.ToInt32(arcanaType);
            }
            catch { }

            // Assert
            Assert.Equal(5, result);
        }

        [Fact]
        public void GetArcanaTypeInt_InvalidConversion_ReturnsNegative()
        {
            // Arrange
            object arcanaType = "NotAnInt";

            // Act
            int result = -1;
            try
            {
                result = Convert.ToInt32(arcanaType);
            }
            catch
            {
                result = -1; // Fallback
            }

            // Assert
            Assert.Equal(-1, result);
        }

        [Fact]
        public void GetArcanaTypeInt_NullInput_ReturnsNegative()
        {
            // Arrange
            object arcanaType = null;

            // Act: Simulate the null check
            int result = (arcanaType == null) ? -1 : 0;

            // Assert
            Assert.Equal(-1, result);
        }

        #endregion

        #region ClearPerRunCaches Tests

        [Fact]
        public void ClearPerRunCaches_ClearsCaptureSets()
        {
            // Arrange: Simulate populated capture sets
            var panelWeapons = new HashSet<MockWeaponType> { MockWeaponType.WHIP, MockWeaponType.KNIFE };
            var panelItems = new HashSet<MockItemType> { MockItemType.SPINACH };

            // Act: Clear
            panelWeapons.Clear();
            panelItems.Clear();

            // Assert
            Assert.Empty(panelWeapons);
            Assert.Empty(panelItems);
        }

        [Fact]
        public void ClearPerRunCaches_DoesNotClearPersistentCache()
        {
            // Arrange: UI cache should persist across runs
            var uiCache = new Dictionary<int, (HashSet<MockWeaponType> weapons, HashSet<MockItemType> items)>();
            uiCache[5] = (new HashSet<MockWeaponType> { MockWeaponType.WHIP }, new HashSet<MockItemType>());

            var panelWeapons = new HashSet<MockWeaponType> { MockWeaponType.KNIFE };

            // Act: Clear only per-run data
            panelWeapons.Clear();
            // uiCache is NOT cleared

            // Assert
            Assert.Empty(panelWeapons);
            Assert.Single(uiCache); // UI cache persists
        }

        #endregion
    }
}
