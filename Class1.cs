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

    #region Shared UI Utilities

    /// <summary>
    /// Shared UI helper utilities for creating icons, labels, popups, etc.
    /// Used by EvolutionDisplay and ArcanaDisplay for consistent UI across all windows.
    /// </summary>
    public static class UIHelper
    {
        // Cached font reference
        private static Il2CppTMPro.TMP_FontAsset cachedFont = null;
        private static UnityEngine.Material cachedFontMaterial = null;

        /// <summary>
        /// Gets or caches the TMP font from existing UI elements.
        /// </summary>
        public static (Il2CppTMPro.TMP_FontAsset font, UnityEngine.Material material) GetFont(UnityEngine.Transform searchRoot)
        {
            if (cachedFont != null)
                return (cachedFont, cachedFontMaterial);

            var existingTMPs = searchRoot.root.GetComponentsInChildren<Il2CppTMPro.TextMeshProUGUI>(true);
            if (existingTMPs.Length > 0)
            {
                cachedFont = existingTMPs[0].font;
                cachedFontMaterial = existingTMPs[0].fontSharedMaterial;
            }
            return (cachedFont, cachedFontMaterial);
        }

        /// <summary>
        /// Creates a UI icon with sprite.
        /// </summary>
        public static UnityEngine.GameObject CreateIcon(
            UnityEngine.Transform parent,
            UnityEngine.Sprite sprite,
            UnityEngine.Vector2 position,
            float size,
            string name = "Icon",
            bool preserveAspect = true)
        {
            var iconObj = new UnityEngine.GameObject(name);
            iconObj.transform.SetParent(parent, false);

            var rect = iconObj.AddComponent<UnityEngine.RectTransform>();
            rect.anchorMin = new UnityEngine.Vector2(0f, 0.5f);
            rect.anchorMax = new UnityEngine.Vector2(0f, 0.5f);
            rect.pivot = new UnityEngine.Vector2(0f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new UnityEngine.Vector2(size, size);

            var image = iconObj.AddComponent<UnityEngine.UI.Image>();
            image.sprite = sprite;
            image.preserveAspect = preserveAspect;

            return iconObj;
        }

        /// <summary>
        /// Creates a UI icon with custom anchoring.
        /// </summary>
        public static UnityEngine.GameObject CreateIcon(
            UnityEngine.Transform parent,
            UnityEngine.Sprite sprite,
            UnityEngine.Vector2 anchorMin,
            UnityEngine.Vector2 anchorMax,
            UnityEngine.Vector2 pivot,
            UnityEngine.Vector2 position,
            UnityEngine.Vector2 size,
            string name = "Icon")
        {
            var iconObj = new UnityEngine.GameObject(name);
            iconObj.transform.SetParent(parent, false);

            var rect = iconObj.AddComponent<UnityEngine.RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var image = iconObj.AddComponent<UnityEngine.UI.Image>();
            image.sprite = sprite;
            image.preserveAspect = true;

            return iconObj;
        }

        /// <summary>
        /// Creates a TMP text label.
        /// </summary>
        public static UnityEngine.GameObject CreateLabel(
            UnityEngine.Transform parent,
            string text,
            UnityEngine.Vector2 position,
            UnityEngine.Vector2 size,
            float fontSize,
            UnityEngine.Color color,
            Il2CppTMPro.TextAlignmentOptions alignment = Il2CppTMPro.TextAlignmentOptions.Left,
            string name = "Label")
        {
            var (font, material) = GetFont(parent);
            if (font == null) return null;

            var labelObj = new UnityEngine.GameObject(name);
            labelObj.transform.SetParent(parent, false);

            var rect = labelObj.AddComponent<UnityEngine.RectTransform>();
            rect.anchorMin = new UnityEngine.Vector2(0f, 0.5f);
            rect.anchorMax = new UnityEngine.Vector2(0f, 0.5f);
            rect.pivot = new UnityEngine.Vector2(0f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var tmp = labelObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
            tmp.font = font;
            if (material != null) tmp.fontSharedMaterial = material;
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.overflowMode = Il2CppTMPro.TextOverflowModes.Overflow;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;

            return labelObj;
        }

        /// <summary>
        /// Adds hover event triggers to a GameObject.
        /// </summary>
        public static void AddHoverTrigger(
            UnityEngine.GameObject obj,
            System.Action onEnter,
            System.Action onExit)
        {
            var eventTrigger = obj.AddComponent<UnityEngine.EventSystems.EventTrigger>();

            var enterEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
            enterEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter;
            enterEntry.callback.AddListener((UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData>)((data) => onEnter?.Invoke()));
            eventTrigger.triggers.Add(enterEntry);

            var exitEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
            exitEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit;
            exitEntry.callback.AddListener((UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData>)((data) => onExit?.Invoke()));
            eventTrigger.triggers.Add(exitEntry);
        }

        /// <summary>
        /// Creates a popup background with outline.
        /// </summary>
        public static UnityEngine.GameObject CreatePopupBase(
            UnityEngine.Transform parent,
            string name,
            UnityEngine.Color bgColor,
            UnityEngine.Color outlineColor,
            float outlineWidth = 2f)
        {
            var popupObj = new UnityEngine.GameObject(name);
            popupObj.transform.SetParent(parent, false);

            var rect = popupObj.AddComponent<UnityEngine.RectTransform>();
            rect.anchorMin = new UnityEngine.Vector2(0.5f, 0.5f);
            rect.anchorMax = new UnityEngine.Vector2(0.5f, 0.5f);
            rect.pivot = new UnityEngine.Vector2(0.5f, 0.5f);

            var bgImage = popupObj.AddComponent<UnityEngine.UI.Image>();
            bgImage.color = bgColor;
            bgImage.raycastTarget = true;

            var outline = popupObj.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = outlineColor;
            outline.effectDistance = new UnityEngine.Vector2(outlineWidth, outlineWidth);

            return popupObj;
        }

        /// <summary>
        /// Adds a vertical layout group with content size fitter.
        /// </summary>
        public static void AddVerticalLayout(
            UnityEngine.GameObject obj,
            float padding,
            float spacing)
        {
            var layout = obj.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.padding = new UnityEngine.RectOffset((int)padding, (int)padding, (int)padding, (int)padding);
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = obj.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        }

        /// <summary>
        /// Adds a horizontal layout group.
        /// </summary>
        public static void AddHorizontalLayout(
            UnityEngine.GameObject obj,
            float spacing)
        {
            var layout = obj.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
        }

        /// <summary>
        /// Destroys an existing child by name if it exists.
        /// </summary>
        public static void CleanupChild(UnityEngine.Transform parent, string childName)
        {
            var existing = parent.Find(childName);
            if (existing != null)
                UnityEngine.Object.Destroy(existing.gameObject);
        }

        /// <summary>
        /// Finds a parent transform by name, walking up the hierarchy.
        /// </summary>
        public static UnityEngine.Transform FindParentByName(UnityEngine.Transform start, string name)
        {
            var current = start;
            while (current != null && current.name != name)
            {
                current = current.parent;
            }
            return current ?? start.root;
        }

        /// <summary>
        /// Adds a layout element for size control in layout groups.
        /// </summary>
        public static void AddLayoutElement(UnityEngine.GameObject obj, float preferredWidth, float preferredHeight)
        {
            var layoutElement = obj.AddComponent<UnityEngine.UI.LayoutElement>();
            layoutElement.preferredWidth = preferredWidth;
            layoutElement.preferredHeight = preferredHeight;
        }
    }

    /// <summary>
    /// Data class representing an evolution formula (weapon + requirements -> evolved weapon).
    /// </summary>
    public class EvolutionFormula
    {
        public System.Collections.Generic.List<(UnityEngine.Sprite sprite, bool owned, string name)> Ingredients = new System.Collections.Generic.List<(UnityEngine.Sprite sprite, bool owned, string name)>();
        public UnityEngine.Sprite ResultSprite;
        public string ResultName;
        public bool CanEvolve; // True if all requirements are met
    }

    /// <summary>
    /// Data class representing arcana information.
    /// </summary>
    public class ArcanaInfo
    {
        public string Name;
        public string Description;
        public UnityEngine.Sprite Sprite;
        public object ArcanaData; // Raw game data for further queries
        public System.Collections.Generic.List<WeaponType> AffectedWeaponTypes = new System.Collections.Generic.List<WeaponType>();
    }

    /// <summary>
    /// Context enum for different UI locations.
    /// </summary>
    public enum UIContext
    {
        LevelUp,
        Merchant,
        Pickup
    }

    /// <summary>
    /// Unified evolution display system - one implementation for all windows.
    /// </summary>
    public static class EvolutionDisplay
    {
        // Single popup instance shared across all contexts
        private static UnityEngine.GameObject currentPopup = null;

        // Popup styling constants
        private static readonly UnityEngine.Color PopupBgColor = new UnityEngine.Color(0.1f, 0.1f, 0.15f, 0.95f);
        private static readonly UnityEngine.Color PopupOutlineColor = new UnityEngine.Color(0.4f, 0.6f, 0.9f, 1f);
        private static readonly UnityEngine.Color OwnedHighlightColor = new UnityEngine.Color(0.2f, 0.8f, 0.2f, 1f);
        private static readonly float IconSize = 24f;
        private static readonly float Spacing = 4f;
        private static readonly float Padding = 10f;

        /// <summary>
        /// Displays the evolution indicator ("evo: [N]") on a UI element.
        /// Returns the created container for positioning by caller.
        /// </summary>
        public static UnityEngine.GameObject ShowIndicator(
            UnityEngine.Transform parent,
            System.Collections.Generic.List<EvolutionFormula> formulas,
            UIContext context,
            System.Action<UnityEngine.Transform> onShowPopup)
        {
            string containerName = "EvoIndicator_Unified";
            UIHelper.CleanupChild(parent, containerName);

            if (formulas == null || formulas.Count == 0) return null;

            // Create container
            var containerObj = new UnityEngine.GameObject(containerName);
            containerObj.transform.SetParent(parent, false);

            var containerRect = containerObj.AddComponent<UnityEngine.RectTransform>();

            // Position based on context
            switch (context)
            {
                case UIContext.LevelUp:
                    containerRect.anchorMin = new UnityEngine.Vector2(0f, 0f);
                    containerRect.anchorMax = new UnityEngine.Vector2(0f, 0f);
                    containerRect.pivot = new UnityEngine.Vector2(0f, 0f);
                    containerRect.anchoredPosition = new UnityEngine.Vector2(10f, 10f);
                    break;
                case UIContext.Merchant:
                    containerRect.anchorMin = new UnityEngine.Vector2(0.5f, 1f);
                    containerRect.anchorMax = new UnityEngine.Vector2(0.5f, 1f);
                    containerRect.pivot = new UnityEngine.Vector2(0f, 1f);
                    containerRect.anchoredPosition = new UnityEngine.Vector2(0f, -35f);
                    break;
                case UIContext.Pickup:
                    containerRect.anchorMin = new UnityEngine.Vector2(0f, 0f);
                    containerRect.anchorMax = new UnityEngine.Vector2(0f, 0f);
                    containerRect.pivot = new UnityEngine.Vector2(0f, 0f);
                    containerRect.anchoredPosition = new UnityEngine.Vector2(5f, 5f);
                    break;
            }
            containerRect.sizeDelta = new UnityEngine.Vector2(300f, 28f);

            float xPos = 0f;

            // Create "evo:" label
            var labelObj = UIHelper.CreateLabel(
                containerObj.transform,
                "evo:",
                new UnityEngine.Vector2(xPos, 0f),
                new UnityEngine.Vector2(50f, 24f),
                18f,
                UnityEngine.Color.white,
                Il2CppTMPro.TextAlignmentOptions.Left,
                "EvoLabel");
            xPos += 60f;

            // Create hover icon with formula count
            var iconObj = new UnityEngine.GameObject("EvoIcon");
            iconObj.transform.SetParent(containerObj.transform, false);

            var iconRect = iconObj.AddComponent<UnityEngine.RectTransform>();
            iconRect.anchorMin = new UnityEngine.Vector2(0f, 0.5f);
            iconRect.anchorMax = new UnityEngine.Vector2(0f, 0.5f);
            iconRect.pivot = new UnityEngine.Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new UnityEngine.Vector2(xPos, 0f);
            iconRect.sizeDelta = new UnityEngine.Vector2(24f, 24f);

            var bgImage = iconObj.AddComponent<UnityEngine.UI.Image>();
            bgImage.color = new UnityEngine.Color(0.2f, 0.5f, 0.9f, 0.9f);
            bgImage.raycastTarget = true;

            // Add count text
            var (font, material) = UIHelper.GetFont(parent);
            if (font != null)
            {
                var textObj = new UnityEngine.GameObject("Count");
                textObj.transform.SetParent(iconObj.transform, false);
                var textRect = textObj.AddComponent<UnityEngine.RectTransform>();
                textRect.anchorMin = UnityEngine.Vector2.zero;
                textRect.anchorMax = UnityEngine.Vector2.one;
                textRect.offsetMin = UnityEngine.Vector2.zero;
                textRect.offsetMax = UnityEngine.Vector2.zero;

                var tmp = textObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                tmp.font = font;
                tmp.text = formulas.Count.ToString();
                tmp.fontSize = 18f;
                tmp.color = UnityEngine.Color.white;
                tmp.alignment = Il2CppTMPro.TextAlignmentOptions.Center;
                tmp.raycastTarget = false;
            }

            // Add hover trigger
            UIHelper.AddHoverTrigger(iconObj,
                () => onShowPopup?.Invoke(iconObj.transform),
                () => HidePopup());

            return containerObj;
        }

        /// <summary>
        /// Shows the evolution popup with all formulas.
        /// </summary>
        public static void ShowPopup(
            UnityEngine.Transform anchor,
            System.Collections.Generic.List<EvolutionFormula> formulas,
            UIContext context)
        {
            HidePopup();

            if (formulas == null || formulas.Count == 0) return;

            // Find appropriate parent for popup (to avoid clipping)
            string parentName = context == UIContext.Merchant ? "View - Merchant" : "View - Level Up";
            var popupParent = UIHelper.FindParentByName(anchor, parentName);

            // Create popup
            currentPopup = UIHelper.CreatePopupBase(popupParent, "EvoPopup_Unified", PopupBgColor, PopupOutlineColor);
            UIHelper.AddVerticalLayout(currentPopup, Padding, Spacing);

            // Add title
            var titleRow = new UnityEngine.GameObject("TitleRow");
            titleRow.transform.SetParent(currentPopup.transform, false);
            UIHelper.AddLayoutElement(titleRow, 200f, 20f);

            var (font, material) = UIHelper.GetFont(anchor);
            if (font != null)
            {
                var titleTmp = titleRow.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                titleTmp.font = font;
                titleTmp.text = "Evolution Paths";
                titleTmp.fontSize = 16f;
                titleTmp.color = new UnityEngine.Color(1f, 0.85f, 0.4f, 1f); // Gold
                titleTmp.alignment = Il2CppTMPro.TextAlignmentOptions.Center;
            }

            // Add each formula row (limit to 4, show "+N more" if needed)
            int maxShow = System.Math.Min(formulas.Count, 4);
            for (int i = 0; i < maxShow; i++)
            {
                CreateFormulaRow(currentPopup.transform, formulas[i], font, material);
            }

            if (formulas.Count > 4)
            {
                var moreRow = new UnityEngine.GameObject("MoreRow");
                moreRow.transform.SetParent(currentPopup.transform, false);
                UIHelper.AddLayoutElement(moreRow, 200f, 18f);

                if (font != null)
                {
                    var moreTmp = moreRow.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                    moreTmp.font = font;
                    moreTmp.text = $"+{formulas.Count - 4} more...";
                    moreTmp.fontSize = 14f;
                    moreTmp.color = new UnityEngine.Color(0.7f, 0.7f, 0.7f, 1f);
                    moreTmp.alignment = Il2CppTMPro.TextAlignmentOptions.Center;
                }
            }

            // Position popup near anchor
            PositionPopup(currentPopup, anchor, context);
        }

        /// <summary>
        /// Creates a single formula row: [icon] + [icon] → [result]
        /// </summary>
        private static void CreateFormulaRow(
            UnityEngine.Transform parent,
            EvolutionFormula formula,
            Il2CppTMPro.TMP_FontAsset font,
            UnityEngine.Material fontMaterial)
        {
            var rowObj = new UnityEngine.GameObject("FormulaRow");
            rowObj.transform.SetParent(parent, false);
            UIHelper.AddHorizontalLayout(rowObj, 3f);

            // Calculate row width
            float rowWidth = 0f;
            foreach (var ing in formula.Ingredients)
            {
                rowWidth += IconSize + 3f; // icon + spacing
                if (formula.Ingredients.IndexOf(ing) < formula.Ingredients.Count - 1)
                    rowWidth += 15f; // "+" text width
            }
            rowWidth += 20f + IconSize; // arrow + result icon

            UIHelper.AddLayoutElement(rowObj, rowWidth, IconSize);

            // Add ingredients with "+" between them
            for (int i = 0; i < formula.Ingredients.Count; i++)
            {
                var ing = formula.Ingredients[i];

                // Create icon
                var iconObj = new UnityEngine.GameObject($"Ingredient_{i}");
                iconObj.transform.SetParent(rowObj.transform, false);

                var iconImage = iconObj.AddComponent<UnityEngine.UI.Image>();
                iconImage.sprite = ing.sprite;
                iconImage.preserveAspect = true;

                UIHelper.AddLayoutElement(iconObj, IconSize, IconSize);

                // Add green highlight if owned
                if (ing.owned)
                {
                    var outline = iconObj.AddComponent<UnityEngine.UI.Outline>();
                    outline.effectColor = OwnedHighlightColor;
                    outline.effectDistance = new UnityEngine.Vector2(2f, 2f);
                }

                // Add "+" if not last ingredient
                if (i < formula.Ingredients.Count - 1 && font != null)
                {
                    var plusObj = new UnityEngine.GameObject("Plus");
                    plusObj.transform.SetParent(rowObj.transform, false);
                    UIHelper.AddLayoutElement(plusObj, 15f, IconSize);

                    var plusTmp = plusObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                    plusTmp.font = font;
                    plusTmp.text = "+";
                    plusTmp.fontSize = 16f;
                    plusTmp.color = UnityEngine.Color.white;
                    plusTmp.alignment = Il2CppTMPro.TextAlignmentOptions.Center;
                }
            }

            // Add arrow
            if (font != null)
            {
                var arrowObj = new UnityEngine.GameObject("Arrow");
                arrowObj.transform.SetParent(rowObj.transform, false);
                UIHelper.AddLayoutElement(arrowObj, 20f, IconSize);

                var arrowTmp = arrowObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                arrowTmp.font = font;
                arrowTmp.text = "→";
                arrowTmp.fontSize = 16f;
                arrowTmp.color = UnityEngine.Color.white;
                arrowTmp.alignment = Il2CppTMPro.TextAlignmentOptions.Center;
            }

            // Add result icon
            var resultObj = new UnityEngine.GameObject("Result");
            resultObj.transform.SetParent(rowObj.transform, false);

            var resultImage = resultObj.AddComponent<UnityEngine.UI.Image>();
            resultImage.sprite = formula.ResultSprite;
            resultImage.preserveAspect = true;

            UIHelper.AddLayoutElement(resultObj, IconSize, IconSize);
        }

        /// <summary>
        /// Positions the popup relative to anchor based on context.
        /// </summary>
        private static void PositionPopup(UnityEngine.GameObject popup, UnityEngine.Transform anchor, UIContext context)
        {
            var popupRect = popup.GetComponent<UnityEngine.RectTransform>();
            var popupParent = popup.transform.parent;

            if (popupParent != null)
            {
                // Convert anchor world position to popup parent's local space
                var localPos = popupParent.InverseTransformPoint(anchor.position);

                // Offset based on context
                float xOffset = 30f;
                float yOffset = 0f;
                switch (context)
                {
                    case UIContext.Merchant:
                        xOffset = 60f;
                        break;
                    default:
                        xOffset = 30f;
                        break;
                }

                popupRect.anchoredPosition = new UnityEngine.Vector2(localPos.x + xOffset, localPos.y + yOffset);
            }
        }

        /// <summary>
        /// Hides the current popup.
        /// </summary>
        public static void HidePopup()
        {
            if (currentPopup != null)
            {
                UnityEngine.Object.Destroy(currentPopup);
                currentPopup = null;
            }
        }
    }

    /// <summary>
    /// Unified arcana display system - one implementation for all windows.
    /// </summary>
    public static class ArcanaDisplay
    {
        // Single popup instance
        private static UnityEngine.GameObject currentPopup = null;

        // Styling constants
        private static readonly UnityEngine.Color PopupBgColor = new UnityEngine.Color(0.15f, 0.1f, 0.2f, 0.95f);
        private static readonly UnityEngine.Color PopupOutlineColor = new UnityEngine.Color(0.8f, 0.5f, 0.9f, 1f);
        private static readonly float IconSize = 20f;

        /// <summary>
        /// Displays the arcana indicator ("arcana:" + icon) on a UI element.
        /// Call after EvolutionDisplay.ShowIndicator to position correctly.
        /// </summary>
        public static UnityEngine.GameObject ShowIndicator(
            UnityEngine.Transform parent,
            ArcanaInfo arcana,
            UIContext context,
            float xOffset, // Where to start (after evo indicator)
            System.Action<UnityEngine.Transform> onShowPopup)
        {
            string containerName = "ArcanaIndicator_Unified";
            UIHelper.CleanupChild(parent, containerName);

            if (arcana == null || arcana.Sprite == null) return null;

            // For merchant, add to the evo container if it exists
            var evoContainer = parent.Find("EvoIndicator_Unified");
            var targetParent = evoContainer != null ? evoContainer : parent;

            // Create "arcana:" label
            var labelObj = UIHelper.CreateLabel(
                targetParent,
                "arcana:",
                new UnityEngine.Vector2(xOffset, 0f),
                new UnityEngine.Vector2(60f, 24f),
                18f,
                UnityEngine.Color.white,
                Il2CppTMPro.TextAlignmentOptions.Left,
                "ArcanaLabel");

            xOffset += 65f;

            // Create arcana icon
            var iconObj = UIHelper.CreateIcon(
                targetParent,
                arcana.Sprite,
                new UnityEngine.Vector2(xOffset, 0f),
                IconSize,
                containerName);

            var iconImage = iconObj.GetComponent<UnityEngine.UI.Image>();
            iconImage.raycastTarget = true;

            // Add hover trigger
            UIHelper.AddHoverTrigger(iconObj,
                () => onShowPopup?.Invoke(iconObj.transform),
                () => HidePopup());

            return iconObj;
        }

        /// <summary>
        /// Shows the arcana popup with details.
        /// </summary>
        public static void ShowPopup(
            UnityEngine.Transform anchor,
            ArcanaInfo arcana,
            object weaponsDict,
            object powerUpsDict,
            UIContext context)
        {
            HidePopup();

            if (arcana == null) return;

            // Find appropriate parent
            string parentName = context == UIContext.Merchant ? "View - Merchant" : "View - Level Up";
            var popupParent = UIHelper.FindParentByName(anchor, parentName);

            // Create popup
            currentPopup = UIHelper.CreatePopupBase(popupParent, "ArcanaPopup_Unified", PopupBgColor, PopupOutlineColor);
            UIHelper.AddVerticalLayout(currentPopup, 10f, 5f);

            var (font, material) = UIHelper.GetFont(anchor);

            // Title row with icon and name
            var titleRow = new UnityEngine.GameObject("TitleRow");
            titleRow.transform.SetParent(currentPopup.transform, false);
            UIHelper.AddHorizontalLayout(titleRow, 8f);
            UIHelper.AddLayoutElement(titleRow, 250f, 28f);

            // Arcana icon in title
            var titleIcon = new UnityEngine.GameObject("TitleIcon");
            titleIcon.transform.SetParent(titleRow.transform, false);
            var titleIconImage = titleIcon.AddComponent<UnityEngine.UI.Image>();
            titleIconImage.sprite = arcana.Sprite;
            titleIconImage.preserveAspect = true;
            UIHelper.AddLayoutElement(titleIcon, 24f, 24f);

            // Arcana name
            if (font != null)
            {
                var nameObj = new UnityEngine.GameObject("Name");
                nameObj.transform.SetParent(titleRow.transform, false);
                UIHelper.AddLayoutElement(nameObj, 200f, 24f);

                var nameTmp = nameObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                nameTmp.font = font;
                nameTmp.text = arcana.Name;
                nameTmp.fontSize = 18f;
                nameTmp.color = new UnityEngine.Color(0.9f, 0.7f, 1f, 1f); // Purple tint
                nameTmp.alignment = Il2CppTMPro.TextAlignmentOptions.Left;
            }

            // Description
            if (font != null && !string.IsNullOrEmpty(arcana.Description))
            {
                var descObj = new UnityEngine.GameObject("Description");
                descObj.transform.SetParent(currentPopup.transform, false);
                UIHelper.AddLayoutElement(descObj, 250f, 40f);

                var descTmp = descObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                descTmp.font = font;
                descTmp.text = arcana.Description;
                descTmp.fontSize = 12f;
                descTmp.color = new UnityEngine.Color(0.8f, 0.8f, 0.8f, 1f);
                descTmp.alignment = Il2CppTMPro.TextAlignmentOptions.Left;
                descTmp.enableWordWrapping = true;
            }

            // Affected weapons section
            if (arcana.AffectedWeaponTypes.Count > 0)
            {
                // "Affects:" label
                if (font != null)
                {
                    var affectsLabel = new UnityEngine.GameObject("AffectsLabel");
                    affectsLabel.transform.SetParent(currentPopup.transform, false);
                    UIHelper.AddLayoutElement(affectsLabel, 250f, 18f);

                    var affectsTmp = affectsLabel.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                    affectsTmp.font = font;
                    affectsTmp.text = "Affects:";
                    affectsTmp.fontSize = 14f;
                    affectsTmp.color = new UnityEngine.Color(1f, 0.85f, 0.4f, 1f); // Gold
                    affectsTmp.alignment = Il2CppTMPro.TextAlignmentOptions.Left;
                }

                // Weapon icons row
                var iconsRow = new UnityEngine.GameObject("IconsRow");
                iconsRow.transform.SetParent(currentPopup.transform, false);
                UIHelper.AddHorizontalLayout(iconsRow, 4f);

                int iconsWidth = System.Math.Min(arcana.AffectedWeaponTypes.Count, 8) * ((int)IconSize + 4);
                UIHelper.AddLayoutElement(iconsRow, iconsWidth, IconSize);

                // Add weapon icons (max 8)
                int shown = 0;
                foreach (var weaponType in arcana.AffectedWeaponTypes)
                {
                    if (shown >= 8) break;

                    var sprite = LoadWeaponSprite(weaponType, weaponsDict, powerUpsDict);
                    if (sprite != null)
                    {
                        var wepIcon = new UnityEngine.GameObject($"Weapon_{shown}");
                        wepIcon.transform.SetParent(iconsRow.transform, false);

                        var wepImage = wepIcon.AddComponent<UnityEngine.UI.Image>();
                        wepImage.sprite = sprite;
                        wepImage.preserveAspect = true;

                        UIHelper.AddLayoutElement(wepIcon, IconSize, IconSize);
                        shown++;
                    }
                }
            }

            // Position popup
            PositionPopup(currentPopup, anchor, context);
        }

        private static void PositionPopup(UnityEngine.GameObject popup, UnityEngine.Transform anchor, UIContext context)
        {
            var popupRect = popup.GetComponent<UnityEngine.RectTransform>();
            var popupParent = popup.transform.parent;

            if (popupParent != null)
            {
                // Convert anchor world position to popup parent's local space
                var localPos = popupParent.InverseTransformPoint(anchor.position);
                popupRect.anchoredPosition = new UnityEngine.Vector2(localPos.x + 60f, localPos.y);
            }
        }

        /// <summary>
        /// Hides the current popup.
        /// </summary>
        public static void HidePopup()
        {
            if (currentPopup != null)
            {
                UnityEngine.Object.Destroy(currentPopup);
                currentPopup = null;
            }
        }

        // Helper to load weapon sprite (reuse existing logic)
        private static UnityEngine.Sprite LoadWeaponSprite(WeaponType weaponType, object weaponsDict, object powerUpsDict)
        {
            // This will be filled in by referencing existing sprite loading code
            // For now, return null - we'll wire this up when migrating
            return null;
        }
    }

    #endregion

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
                // Auto-close any existing arcana popup when level-up cards refresh
                if (currentPopup != null)
                {
                    UnityEngine.Object.Destroy(currentPopup);
                    currentPopup = null;
                }

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

                // Check if this weapon is affected by the player's selected arcana
                var arcanaInfo = GetActiveArcanaForWeapon(page, type);
                if (arcanaInfo.HasValue)
                {
                    var affectedWeaponTypes = GetArcanaAffectedWeaponTypes(arcanaInfo.Value.arcanaData);
                    DisplayArcanaIndicator(__instance, arcanaInfo.Value.name, arcanaInfo.Value.description, arcanaInfo.Value.sprite, affectedWeaponTypes, weaponsDict, powerUpsDict);
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

        // Arcana system helpers
        private static System.Type cachedArcanaTypeEnum = null;
        private static object cachedAllArcanas = null;

        /// <summary>
        /// Gets the ArcanaType enum value for the currently selected arcana.
        /// Returns null if no arcana is selected or arcanas are disabled.
        /// </summary>
        public static object GetSelectedArcanaType(LevelUpPage page)
        {
            try
            {
                // Note: page parameter is not actually used - we find GameManager directly
                var assembly = typeof(WeaponData).Assembly;

                // Cache the ArcanaType enum
                if (cachedArcanaTypeEnum == null)
                {
                    cachedArcanaTypeEnum = assembly.GetTypes().FirstOrDefault(t => t.Name == "ArcanaType");
                }
                if (cachedArcanaTypeEnum == null) return null;

                // Get GameManager -> ArcanaManager -> PlayerOptions -> Config -> SelectedArcana
                var gameManagerType = assembly.GetTypes().FirstOrDefault(t => t.Name == "GameManager" && !t.IsInterface && typeof(UnityEngine.Component).IsAssignableFrom(t));
                if (gameManagerType == null) return null;

                var findMethod = typeof(UnityEngine.Object).GetMethods()
                    .FirstOrDefault(m => m.Name == "FindObjectOfType" && m.IsGenericMethod && m.GetParameters().Length == 0);
                if (findMethod == null) return null;

                var genericFind = findMethod.MakeGenericMethod(gameManagerType);
                var gameMgr = genericFind.Invoke(null, null);
                if (gameMgr == null) return null;

                var amProp = gameMgr.GetType().GetProperty("_arcanaManager", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (amProp == null) return null;

                var arcanaMgr = amProp.GetValue(gameMgr);
                if (arcanaMgr == null) return null;

                var poProp = arcanaMgr.GetType().GetProperty("_playerOptions", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (poProp == null) return null;

                var playerOpts = poProp.GetValue(arcanaMgr);
                if (playerOpts == null) return null;

                var configProp = playerOpts.GetType().GetProperty("Config", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (configProp == null) return null;

                var config = configProp.GetValue(playerOpts);
                if (config == null) return null;

                var selectedArcanaProp = config.GetType().GetProperty("SelectedArcana", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (selectedArcanaProp == null) return null;

                var selectedArcanaInt = (int)selectedArcanaProp.GetValue(config);

                // Check if arcanas are enabled (SelectedMazzo = true)
                var selectedMazzoProp = config.GetType().GetProperty("SelectedMazzo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (selectedMazzoProp != null)
                {
                    var mazzoEnabled = (bool)selectedMazzoProp.GetValue(config);
                    if (!mazzoEnabled) return null; // Arcanas disabled
                }

                // Convert int to ArcanaType enum
                var arcanaValues = System.Enum.GetValues(cachedArcanaTypeEnum);
                foreach (var val in arcanaValues)
                {
                    if ((int)val == selectedArcanaInt)
                    {
                        return val;
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the ArcanaData for a given ArcanaType enum value.
        /// </summary>
        public static object GetArcanaData(LevelUpPage page, object arcanaType)
        {
            if (page == null || page.Data == null) return null;
            return GetArcanaDataFromDataManager(page.Data, arcanaType);
        }

        /// <summary>
        /// Gets the ArcanaData for a given ArcanaType enum value using a DataManager.
        /// </summary>
        public static object GetArcanaDataFromDataManager(object dataManager, object arcanaType)
        {
            try
            {
                if (dataManager == null || arcanaType == null) return null;

                // Cache AllArcanas dictionary
                if (cachedAllArcanas == null)
                {
                    var allArcanasProp = dataManager.GetType().GetProperty("AllArcanas", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (allArcanasProp != null)
                    {
                        cachedAllArcanas = allArcanasProp.GetValue(dataManager);
                    }
                }
                if (cachedAllArcanas == null) return null;

                // Get the arcana data from the dictionary
                var indexer = cachedAllArcanas.GetType().GetProperty("Item");
                if (indexer == null) return null;

                return indexer.GetValue(cachedAllArcanas, new object[] { arcanaType });
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Checks if a WeaponType is in the arcana's weapons list.
        /// </summary>
        public static bool IsWeaponAffectedByArcana(WeaponType weaponType, object arcanaData)
        {
            try
            {
                if (arcanaData == null) return false;

                var weaponsProp = arcanaData.GetType().GetProperty("weapons", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (weaponsProp == null) return false;

                var weapons = weaponsProp.GetValue(arcanaData);
                if (weapons == null) return false;

                var countProp = weapons.GetType().GetProperty("Count");
                if (countProp == null) return false;

                int count = (int)countProp.GetValue(weapons);
                if (count == 0) return false;

                var itemProp = weapons.GetType().GetProperty("Item");
                if (itemProp == null) return false;

                int targetValue = (int)weaponType;

                for (int i = 0; i < count; i++)
                {
                    var w = itemProp.GetValue(weapons, new object[] { i });
                    if (w == null) continue;

                    // Decode the boxed enum value using pointer offset 16
                    var il2cppObj = w as Il2CppSystem.Object;
                    if (il2cppObj != null)
                    {
                        try
                        {
                            unsafe
                            {
                                IntPtr ptr = il2cppObj.Pointer;
                                int* valuePtr = (int*)((byte*)ptr.ToPointer() + 16);
                                if (*valuePtr == targetValue)
                                {
                                    return true;
                                }
                            }
                        }
                        catch { }
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if an ItemType (powerup) is in the arcana's items list.
        /// </summary>
        public static bool IsItemAffectedByArcana(ItemType itemType, object arcanaData)
        {
            try
            {
                if (arcanaData == null) return false;

                var itemsProp = arcanaData.GetType().GetProperty("items", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (itemsProp == null) return false;

                var items = itemsProp.GetValue(arcanaData);
                if (items == null) return false;

                var countProp = items.GetType().GetProperty("Count");
                if (countProp == null) return false;

                int count = (int)countProp.GetValue(items);
                if (count == 0) return false;

                var itemProp = items.GetType().GetProperty("Item");
                if (itemProp == null) return false;

                int targetValue = (int)itemType;

                for (int i = 0; i < count; i++)
                {
                    var item = itemProp.GetValue(items, new object[] { i });
                    if (item == null) continue;

                    // Decode the boxed enum value using pointer offset 16
                    var il2cppObj = item as Il2CppSystem.Object;
                    if (il2cppObj != null)
                    {
                        try
                        {
                            unsafe
                            {
                                IntPtr ptr = il2cppObj.Pointer;
                                int* valuePtr = (int*)((byte*)ptr.ToPointer() + 16);
                                if (*valuePtr == targetValue)
                                {
                                    return true;
                                }
                            }
                        }
                        catch { }
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets arcana info for a powerup/item if it's affected by the currently selected arcana.
        /// </summary>
        public static (string name, string description, UnityEngine.Sprite sprite, object arcanaData)? GetActiveArcanaForItem(LevelUpPage page, ItemType itemType)
        {
            try
            {
                var selectedArcanaType = GetSelectedArcanaType(page);
                if (selectedArcanaType == null) return null;

                var arcanaData = GetArcanaData(page, selectedArcanaType);
                if (arcanaData == null) return null;

                if (!IsItemAffectedByArcana(itemType, arcanaData)) return null;

                // Get arcana info and sprite
                var nameProp = arcanaData.GetType().GetProperty("name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var descProp = arcanaData.GetType().GetProperty("description", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var frameProp = arcanaData.GetType().GetProperty("frameName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var textureProp = arcanaData.GetType().GetProperty("texture", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                string name = nameProp?.GetValue(arcanaData)?.ToString() ?? "";
                string description = descProp?.GetValue(arcanaData)?.ToString() ?? "";
                string frameName = frameProp?.GetValue(arcanaData)?.ToString() ?? "";
                string textureName = textureProp?.GetValue(arcanaData)?.ToString() ?? "";

                UnityEngine.Sprite sprite = LoadArcanaSprite(textureName, frameName, arcanaData);

                return (name, description, sprite, arcanaData);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets arcana info for a powerup/item using DataManager.
        /// </summary>
        public static (string name, string description, UnityEngine.Sprite sprite, object arcanaData)? GetActiveArcanaForItemFromDataManager(object dataManager, ItemType itemType)
        {
            try
            {
                var selectedArcanaType = GetSelectedArcanaType(null);
                if (selectedArcanaType == null) return null;

                var arcanaData = GetArcanaDataFromDataManager(dataManager, selectedArcanaType);
                if (arcanaData == null) return null;

                if (!IsItemAffectedByArcana(itemType, arcanaData)) return null;

                // Get arcana info and sprite
                var nameProp = arcanaData.GetType().GetProperty("name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var descProp = arcanaData.GetType().GetProperty("description", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var frameProp = arcanaData.GetType().GetProperty("frameName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var textureProp = arcanaData.GetType().GetProperty("texture", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                string name = nameProp?.GetValue(arcanaData)?.ToString() ?? "";
                string description = descProp?.GetValue(arcanaData)?.ToString() ?? "";
                string frameName = frameProp?.GetValue(arcanaData)?.ToString() ?? "";
                string textureName = textureProp?.GetValue(arcanaData)?.ToString() ?? "";

                UnityEngine.Sprite sprite = LoadArcanaSprite(textureName, frameName, arcanaData);

                return (name, description, sprite, arcanaData);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets arcana info for a weapon if it's affected by the currently selected arcana.
        /// Returns (arcanaName, arcanaDescription, sprite, arcanaData) or null if not affected.
        /// </summary>
        public static (string name, string description, UnityEngine.Sprite sprite, object arcanaData)? GetActiveArcanaForWeapon(LevelUpPage page, WeaponType weaponType)
        {
            try
            {
                var selectedArcanaType = GetSelectedArcanaType(page);
                if (selectedArcanaType == null) return null;

                var arcanaData = GetArcanaData(page, selectedArcanaType);
                if (arcanaData == null) return null;

                if (!IsWeaponAffectedByArcana(weaponType, arcanaData)) return null;

                // Get arcana info
                var nameProp = arcanaData.GetType().GetProperty("name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var descProp = arcanaData.GetType().GetProperty("description", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var frameProp = arcanaData.GetType().GetProperty("frameName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var textureProp = arcanaData.GetType().GetProperty("texture", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                string name = nameProp?.GetValue(arcanaData)?.ToString() ?? "";
                string description = descProp?.GetValue(arcanaData)?.ToString() ?? "";
                string frameName = frameProp?.GetValue(arcanaData)?.ToString() ?? "";
                string textureName = textureProp?.GetValue(arcanaData)?.ToString() ?? "";

                // Get arcana type for searching
                var arcanaTypeProp = arcanaData.GetType().GetProperty("arcanaType", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                string arcanaTypeStr = arcanaTypeProp?.GetValue(arcanaData)?.ToString() ?? "";

                MelonLogger.Msg($"Arcana: {name}, texture={textureName}, frameName={frameName}, arcanaType={arcanaTypeStr}");

                // Try to load the arcana sprite
                UnityEngine.Sprite sprite = null;

                // Clean up frameName (remove .png extension if present)
                string cleanFrameName = frameName;
                if (cleanFrameName.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
                {
                    cleanFrameName = cleanFrameName.Substring(0, cleanFrameName.Length - 4);
                }

                // Method 1: Try to load from the specific texture atlas first (most reliable)
                if (!string.IsNullOrEmpty(textureName))
                {
                    // Try various frame name formats
                    string[] frameNamesToTry = { frameName, cleanFrameName, $"{cleanFrameName}.png" };
                    foreach (var fn in frameNamesToTry)
                    {
                        sprite = LoadSpriteFromAtlas(fn, textureName);
                        if (sprite != null)
                        {
                            MelonLogger.Msg($"Found arcana sprite in atlas '{textureName}': {fn}");
                            break;
                        }
                    }
                }

                // Method 2: Search all sprites for one from the correct texture
                if (sprite == null)
                {
                    var allSprites = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Sprite>();

                    foreach (var s in allSprites)
                    {
                        if (s == null || s.texture == null) continue;

                        // Check if this sprite is from the right texture and has the right frame name
                        string texName = s.texture.name.ToLower();
                        string spriteName = s.name.ToLower();

                        if (texName.Contains(textureName.ToLower()) &&
                            (spriteName == cleanFrameName.ToLower() || spriteName == frameName.ToLower()))
                        {
                            sprite = s;
                            MelonLogger.Msg($"Found arcana sprite from texture '{s.texture.name}': {s.name}");
                            break;
                        }
                    }
                }

                // Method 3: Search for sprites that match the texture name pattern
                if (sprite == null && !string.IsNullOrEmpty(textureName))
                {
                    var allSprites = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Sprite>();

                    foreach (var s in allSprites)
                    {
                        if (s == null || s.texture == null) continue;

                        string texName = s.texture.name.ToLower();
                        string spriteName = s.name;

                        // Look for sprite from the randomazzo texture with matching number
                        if (texName.Contains(textureName.ToLower()) && spriteName == cleanFrameName)
                        {
                            sprite = s;
                            MelonLogger.Msg($"Found arcana sprite (texture match): {s.texture.name}/{s.name}");
                            break;
                        }
                    }
                }

                // Method 4: Fallback - try other atlases
                if (sprite == null)
                {
                    string[] atlasesToTry = { "arcanas", "cards", "items", "ui" };
                    string[] frameNamesToTry = { frameName, cleanFrameName, $"arcana_{cleanFrameName}" };

                    foreach (var atlas in atlasesToTry)
                    {
                        foreach (var fn in frameNamesToTry)
                        {
                            sprite = LoadSpriteFromAtlas(fn, atlas);
                            if (sprite != null)
                            {
                                MelonLogger.Msg($"Found arcana sprite in fallback atlas '{atlas}': {fn}");
                                break;
                            }
                        }
                        if (sprite != null) break;
                    }
                }

                if (sprite == null)
                {
                    MelonLogger.Warning($"Could not find sprite for arcana: {name} (texture={textureName}, frame={frameName})");
                }

                return (name, description, sprite, arcanaData);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets arcana info for a weapon if it's affected by the currently selected arcana.
        /// Version that works with DataManager (for ItemFoundPage, merchant, etc.)
        /// </summary>
        public static (string name, string description, UnityEngine.Sprite sprite, object arcanaData)? GetActiveArcanaForWeaponFromDataManager(object dataManager, WeaponType weaponType)
        {
            try
            {
                var selectedArcanaType = GetSelectedArcanaType(null);
                if (selectedArcanaType == null) return null;

                var arcanaData = GetArcanaDataFromDataManager(dataManager, selectedArcanaType);
                if (arcanaData == null) return null;

                if (!IsWeaponAffectedByArcana(weaponType, arcanaData)) return null;

                // Get arcana info
                var nameProp = arcanaData.GetType().GetProperty("name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var descProp = arcanaData.GetType().GetProperty("description", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var frameProp = arcanaData.GetType().GetProperty("frameName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var textureProp = arcanaData.GetType().GetProperty("texture", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                string name = nameProp?.GetValue(arcanaData)?.ToString() ?? "";
                string description = descProp?.GetValue(arcanaData)?.ToString() ?? "";
                string frameName = frameProp?.GetValue(arcanaData)?.ToString() ?? "";
                string textureName = textureProp?.GetValue(arcanaData)?.ToString() ?? "";

                // Load the arcana sprite (reuse the existing sprite loading logic)
                UnityEngine.Sprite sprite = LoadArcanaSprite(textureName, frameName, arcanaData);

                return (name, description, sprite, arcanaData);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Loads an arcana sprite given the texture and frame names.
        /// </summary>
        private static UnityEngine.Sprite LoadArcanaSprite(string textureName, string frameName, object arcanaData)
        {
            UnityEngine.Sprite sprite = null;

            // Clean up frameName (remove .png extension if present)
            string cleanFrameName = frameName;
            if (cleanFrameName.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
            {
                cleanFrameName = cleanFrameName.Substring(0, cleanFrameName.Length - 4);
            }

            // Method 1: Try to load from the specific texture atlas first
            if (!string.IsNullOrEmpty(textureName))
            {
                string[] frameNamesToTry = { frameName, cleanFrameName, $"{cleanFrameName}.png" };
                foreach (var fn in frameNamesToTry)
                {
                    sprite = LoadSpriteFromAtlas(fn, textureName);
                    if (sprite != null) return sprite;
                }
            }

            // Method 2: Search all sprites for one from the correct texture
            var allSprites = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Sprite>();

            foreach (var s in allSprites)
            {
                if (s == null || s.texture == null) continue;

                string texName = s.texture.name.ToLower();
                string spriteName = s.name.ToLower();

                if (texName.Contains(textureName.ToLower()) &&
                    (spriteName == cleanFrameName.ToLower() || spriteName == frameName.ToLower()))
                {
                    return s;
                }
            }

            // Method 3: Search for sprites that match the texture name pattern
            if (!string.IsNullOrEmpty(textureName))
            {
                foreach (var s in allSprites)
                {
                    if (s == null || s.texture == null) continue;

                    string texName = s.texture.name.ToLower();

                    if (texName.Contains(textureName.ToLower()) && s.name == cleanFrameName)
                    {
                        return s;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the list of affected weapon names from arcana data.
        /// </summary>
        public static System.Collections.Generic.List<string> GetArcanaAffectedWeaponNames(object arcanaData, object weaponsDict)
        {
            var weaponNames = new System.Collections.Generic.List<string>();
            try
            {
                if (arcanaData == null) return weaponNames;

                var weaponsProp = arcanaData.GetType().GetProperty("weapons", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (weaponsProp == null) return weaponNames;

                var weapons = weaponsProp.GetValue(arcanaData);
                if (weapons == null) return weaponNames;

                var countProp = weapons.GetType().GetProperty("Count");
                if (countProp == null) return weaponNames;

                int count = (int)countProp.GetValue(weapons);
                var itemProp = weapons.GetType().GetProperty("Item");
                if (itemProp == null) return weaponNames;

                for (int i = 0; i < Math.Min(count, 10); i++) // Limit to 10 for display
                {
                    var w = itemProp.GetValue(weapons, new object[] { i });
                    if (w == null) continue;

                    // Decode the boxed enum value using pointer offset 16
                    var il2cppObj = w as Il2CppSystem.Object;
                    if (il2cppObj != null)
                    {
                        try
                        {
                            unsafe
                            {
                                IntPtr ptr = il2cppObj.Pointer;
                                int* valuePtr = (int*)((byte*)ptr.ToPointer() + 16);
                                int weaponTypeInt = *valuePtr;

                                // Convert to WeaponType and get name from weapons dict if available
                                if (System.Enum.IsDefined(typeof(WeaponType), weaponTypeInt))
                                {
                                    var weaponType = (WeaponType)weaponTypeInt;
                                    string weaponName = weaponType.ToString();

                                    // Try to get friendly name from weapons dict
                                    if (weaponsDict != null)
                                    {
                                        try
                                        {
                                            var tryGetMethod = weaponsDict.GetType().GetMethod("TryGetValue");
                                            if (tryGetMethod != null)
                                            {
                                                var weaponParams = new object[] { weaponType, null };
                                                var found = (bool)tryGetMethod.Invoke(weaponsDict, weaponParams);
                                                if (found && weaponParams[1] != null)
                                                {
                                                    var weaponList = weaponParams[1];
                                                    var listCountProp = weaponList.GetType().GetProperty("Count");
                                                    var listItemProp = weaponList.GetType().GetProperty("Item");
                                                    if (listCountProp != null && listItemProp != null && (int)listCountProp.GetValue(weaponList) > 0)
                                                    {
                                                        var weaponData = listItemProp.GetValue(weaponList, new object[] { 0 }) as WeaponData;
                                                        if (weaponData != null && !string.IsNullOrEmpty(weaponData.name))
                                                        {
                                                            weaponName = weaponData.name;
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        catch { }
                                    }

                                    weaponNames.Add(weaponName);
                                }
                            }
                        }
                        catch { }
                    }
                }

                if (count > 10)
                {
                    weaponNames.Add($"...and {count - 10} more");
                }
            }
            catch { }
            return weaponNames;
        }

        /// <summary>
        /// Gets all unique weapon types affected by the arcana (for loading sprites).
        /// </summary>
        public static System.Collections.Generic.List<WeaponType> GetArcanaAffectedWeaponTypes(object arcanaData)
        {
            var weaponTypes = new System.Collections.Generic.List<WeaponType>();
            var seenTypes = new System.Collections.Generic.HashSet<int>(); // Track unique weapon types
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

                    // Decode the boxed enum value using pointer offset 16
                    var il2cppObj = w as Il2CppSystem.Object;
                    if (il2cppObj != null)
                    {
                        try
                        {
                            unsafe
                            {
                                IntPtr ptr = il2cppObj.Pointer;
                                int* valuePtr = (int*)((byte*)ptr.ToPointer() + 16);
                                int weaponTypeInt = *valuePtr;

                                // Only add if we haven't seen this weapon type before
                                if (System.Enum.IsDefined(typeof(WeaponType), weaponTypeInt) && seenTypes.Add(weaponTypeInt))
                                {
                                    weaponTypes.Add((WeaponType)weaponTypeInt);
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return weaponTypes;
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

        /// <summary>
        /// Displays an arcana indicator on the level-up card to show this weapon is affected by an active arcana.
        /// Shows arcana icon and name, clickable to show description and affected weapons.
        /// </summary>
        public static void DisplayArcanaIndicator(LevelUpItemUI instance, string arcanaName, string arcanaDescription, UnityEngine.Sprite arcanaSprite, System.Collections.Generic.List<WeaponType> affectedWeaponTypes, object weaponsDict, object powerUpsDict)
        {
            try
            {
                // Get the evo container (parent of _EvoIcons)
                var evoIconsProperty = instance.GetType().GetProperty("_EvoIcons",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (evoIconsProperty == null) return;

                var evoIconsArray = evoIconsProperty.GetValue(instance) as Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.UI.Image>;
                if (evoIconsArray == null || evoIconsArray.Length == 0) return;

                // Get the "evo" container (parent of the icon slots)
                var evoContainer = evoIconsArray[0].transform.parent;
                if (evoContainer == null) return;

                // Look for existing arcana indicator and remove it
                var existingIndicator = evoContainer.Find("ArcanaIndicator");
                if (existingIndicator != null)
                {
                    UnityEngine.Object.Destroy(existingIndicator.gameObject);
                }

                // Also remove any existing popup
                var existingPopup = evoContainer.Find("ArcanaPopup");
                if (existingPopup != null)
                {
                    UnityEngine.Object.Destroy(existingPopup.gameObject);
                }

                // Create arcana indicator - just the icon, right-aligned
                var indicatorObj = new UnityEngine.GameObject("ArcanaIndicator");
                indicatorObj.transform.SetParent(evoContainer, false);

                var indicatorRect = indicatorObj.AddComponent<UnityEngine.RectTransform>();

                // Position to the right of the evolution formula area
                indicatorRect.anchorMin = new UnityEngine.Vector2(1f, 0.5f); // Right side
                indicatorRect.anchorMax = new UnityEngine.Vector2(1f, 0.5f);
                indicatorRect.pivot = new UnityEngine.Vector2(0f, 0.5f); // Pivot on left edge
                indicatorRect.anchoredPosition = new UnityEngine.Vector2(5f, 0f); // Small gap from formula
                indicatorRect.sizeDelta = new UnityEngine.Vector2(40f, 40f); // Larger icon size

                // Add the arcana card image
                var iconImage = indicatorObj.AddComponent<UnityEngine.UI.Image>();
                if (arcanaSprite != null)
                {
                    iconImage.sprite = arcanaSprite;
                    iconImage.preserveAspect = true;
                }
                else
                {
                    iconImage.color = new UnityEngine.Color(0f, 0f, 0f, 0f); // Transparent if no sprite
                }

                // Store popup data for the hover handler
                var popupData = new ArcanaPopupData
                {
                    Name = arcanaName,
                    Description = arcanaDescription,
                    ArcanaSprite = arcanaSprite,
                    AffectedWeaponTypes = affectedWeaponTypes,
                    WeaponsDict = weaponsDict,
                    PowerUpsDict = powerUpsDict,
                    EvoContainer = evoContainer,
                    SourceCard = instance.GetComponent<UnityEngine.RectTransform>()
                };

                // Add EventTrigger for hover events
                var eventTrigger = indicatorObj.AddComponent<UnityEngine.EventSystems.EventTrigger>();

                // Pointer Enter - show popup
                var enterEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
                enterEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter;
                enterEntry.callback.AddListener((UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData>)((eventData) => ShowArcanaPopupOnHover(popupData)));
                eventTrigger.triggers.Add(enterEntry);

                // Pointer Exit - hide popup
                var exitEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
                exitEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit;
                exitEntry.callback.AddListener((UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData>)((eventData) => HideArcanaPopup()));
                eventTrigger.triggers.Add(exitEntry);

                MelonLogger.Msg($"Arcana indicator displayed: {arcanaName} (hover enabled)");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error displaying arcana indicator: {ex.Message}");
            }
        }

        /// <summary>
        /// Displays an arcana indicator on the pickup window (ItemFoundPage).
        /// Shows arcana icon to the right of the formula, with hover popup.
        /// </summary>
        public static void DisplayArcanaIndicatorOnPickup(UnityEngine.Transform formulaParent, string arcanaName, string arcanaDescription, UnityEngine.Sprite arcanaSprite, System.Collections.Generic.List<WeaponType> affectedWeaponTypes, object weaponsDict, object powerUpsDict)
        {
            try
            {
                if (formulaParent == null || arcanaSprite == null) return;

                // Look for existing arcana indicator and remove it
                var existingIndicator = formulaParent.Find("ArcanaIndicator_Pickup");
                if (existingIndicator != null)
                {
                    UnityEngine.Object.Destroy(existingIndicator.gameObject);
                }

                // Create arcana indicator - icon only, positioned to the right of the formula
                var indicatorObj = new UnityEngine.GameObject("ArcanaIndicator_Pickup");
                indicatorObj.transform.SetParent(formulaParent, false);

                var indicatorRect = indicatorObj.AddComponent<UnityEngine.RectTransform>();

                // Position to the right of the formula
                indicatorRect.anchorMin = new UnityEngine.Vector2(1f, 0.5f);
                indicatorRect.anchorMax = new UnityEngine.Vector2(1f, 0.5f);
                indicatorRect.pivot = new UnityEngine.Vector2(0f, 0.5f);
                indicatorRect.anchoredPosition = new UnityEngine.Vector2(8f, 0f); // Gap from formula
                indicatorRect.sizeDelta = new UnityEngine.Vector2(32f, 32f); // Icon size

                // Add the arcana card image
                var iconImage = indicatorObj.AddComponent<UnityEngine.UI.Image>();
                iconImage.sprite = arcanaSprite;
                iconImage.preserveAspect = true;

                // Store popup data for the hover handler
                var popupData = new ArcanaPopupData
                {
                    Name = arcanaName,
                    Description = arcanaDescription,
                    ArcanaSprite = arcanaSprite,
                    AffectedWeaponTypes = affectedWeaponTypes,
                    WeaponsDict = weaponsDict,
                    PowerUpsDict = powerUpsDict,
                    EvoContainer = formulaParent,
                    SourceCard = null
                };

                // Add EventTrigger for hover events
                var eventTrigger = indicatorObj.AddComponent<UnityEngine.EventSystems.EventTrigger>();

                // Pointer Enter - show popup
                var enterEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
                enterEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter;
                enterEntry.callback.AddListener((UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData>)((eventData) => ShowArcanaPopupOnHover(popupData)));
                eventTrigger.triggers.Add(enterEntry);

                // Pointer Exit - hide popup
                var exitEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
                exitEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit;
                exitEntry.callback.AddListener((UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData>)((eventData) => HideArcanaPopup()));
                eventTrigger.triggers.Add(exitEntry);

                MelonLogger.Msg($"Arcana indicator displayed on pickup: {arcanaName}");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error displaying arcana indicator on pickup: {ex.Message}");
            }
        }

        // Data class for popup information
        private class ArcanaPopupData
        {
            public string Name;
            public string Description;
            public UnityEngine.Sprite ArcanaSprite;
            public System.Collections.Generic.List<WeaponType> AffectedWeaponTypes;
            public object WeaponsDict;
            public object PowerUpsDict;
            public UnityEngine.Transform EvoContainer;
            public UnityEngine.RectTransform SourceCard; // The card that triggered this popup
        }

        // Static reference to current popup for toggling
        private static UnityEngine.GameObject currentPopup = null;

        /// <summary>
        /// Shows the arcana popup on hover (no toggle behavior).
        /// </summary>
        private static void ShowArcanaPopupOnHover(ArcanaPopupData data)
        {
            // If popup already exists, don't create another
            if (currentPopup != null) return;
            ShowArcanaPopupInternal(data);
        }

        /// <summary>
        /// Hides the arcana popup on hover exit.
        /// </summary>
        private static void HideArcanaPopup()
        {
            if (currentPopup != null)
            {
                UnityEngine.Object.Destroy(currentPopup);
                currentPopup = null;
            }
        }

        /// <summary>
        /// Shows/hides the arcana popup with description and affected weapons (toggle version).
        /// </summary>
        private static void ShowArcanaPopup(ArcanaPopupData data)
        {
            try
            {
                // Toggle: if popup exists, destroy it
                if (currentPopup != null)
                {
                    UnityEngine.Object.Destroy(currentPopup);
                    currentPopup = null;
                    return;
                }
                ShowArcanaPopupInternal(data);
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error showing arcana popup: {ex.Message}");
            }
        }

        /// <summary>
        /// Internal method to create the arcana popup.
        /// </summary>
        private static void ShowArcanaPopupInternal(ArcanaPopupData data)
        {
            try
            {
                // Find a high-level parent for proper z-ordering (renders on top of cards)
                UnityEngine.Transform popupParent = data.EvoContainer;
                var searchTransform = data.EvoContainer;
                while (searchTransform.parent != null)
                {
                    if (searchTransform.name.Contains("Content") ||
                        searchTransform.name.Contains("Viewport") ||
                        searchTransform.name.Contains("Scroll"))
                    {
                        popupParent = searchTransform;
                    }
                    searchTransform = searchTransform.parent;
                }

                // Get the world position of the evo container before reparenting
                var evoWorldPos = data.EvoContainer.position;

                // Create popup at high level
                var popupObj = new UnityEngine.GameObject("ArcanaPopup");
                popupObj.transform.SetParent(popupParent, false);
                currentPopup = popupObj;

                var popupRect = popupObj.AddComponent<UnityEngine.RectTransform>();

                // Position at the evo container's world position, offset down
                popupRect.position = evoWorldPos;
                popupRect.anchorMin = new UnityEngine.Vector2(0.5f, 0.5f);
                popupRect.anchorMax = new UnityEngine.Vector2(0.5f, 0.5f);
                popupRect.pivot = new UnityEngine.Vector2(0.5f, 1f); // Pivot at top center
                popupRect.anchoredPosition += new UnityEngine.Vector2(0f, -30f); // Offset down
                popupRect.sizeDelta = new UnityEngine.Vector2(300f, 0f);

                // Make sure it's rendered last (on top)
                popupObj.transform.SetAsLastSibling();

                // Add background (matching game's gray color)
                var bgImage = popupObj.AddComponent<UnityEngine.UI.Image>();
                bgImage.color = new UnityEngine.Color(0.35f, 0.35f, 0.4f, 0.98f); // Gray like game

                // Add gold outline (matching game's orange/gold border)
                var outline = popupObj.AddComponent<UnityEngine.UI.Outline>();
                outline.effectColor = new UnityEngine.Color(1f, 0.7f, 0.2f, 1f); // Orange/gold
                outline.effectDistance = new UnityEngine.Vector2(3f, 3f);

                // Add vertical layout group
                var layoutGroup = popupObj.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
                layoutGroup.spacing = 8f;
                layoutGroup.padding = new UnityEngine.RectOffset(15, 15, 12, 12);
                layoutGroup.childForceExpandWidth = true;
                layoutGroup.childForceExpandHeight = false;
                layoutGroup.childControlWidth = true;
                layoutGroup.childControlHeight = true; // Allow height control for proper sizing

                // Add content size fitter to auto-size both dimensions
                var sizeFitter = popupObj.AddComponent<UnityEngine.UI.ContentSizeFitter>();
                sizeFitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
                sizeFitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

                // Create title text (gold, centered, like game)
                var titleObj = new UnityEngine.GameObject("Title");
                titleObj.transform.SetParent(popupObj.transform, false);

                var titleLayout = titleObj.AddComponent<UnityEngine.UI.LayoutElement>();
                titleLayout.minHeight = 22f;
                titleLayout.preferredWidth = 280f;

                var titleText = titleObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                titleText.text = data.Name;
                titleText.fontSize = 14f;
                titleText.fontStyle = Il2CppTMPro.FontStyles.Bold;
                titleText.alignment = Il2CppTMPro.TextAlignmentOptions.Center;
                titleText.color = new UnityEngine.Color(1f, 0.85f, 0.3f, 1f); // Gold/yellow like game

                if (cachedFont != null) titleText.font = cachedFont;
                if (cachedFontMaterial != null) titleText.fontSharedMaterial = cachedFontMaterial;

                // Create description text (white, centered)
                var descObj = new UnityEngine.GameObject("Description");
                descObj.transform.SetParent(popupObj.transform, false);

                var descLayout = descObj.AddComponent<UnityEngine.UI.LayoutElement>();
                descLayout.minHeight = 30f;
                descLayout.preferredWidth = 280f;

                var descText = descObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                descText.text = data.Description ?? "No description available.";
                descText.fontSize = 11f;
                descText.alignment = Il2CppTMPro.TextAlignmentOptions.Center;
                descText.color = new UnityEngine.Color(1f, 1f, 1f, 1f); // White like game
                descText.enableWordWrapping = true;
                descText.overflowMode = Il2CppTMPro.TextOverflowModes.Overflow; // Don't clip

                if (cachedFont != null) descText.font = cachedFont;
                if (cachedFontMaterial != null) descText.fontSharedMaterial = cachedFontMaterial;

                // Create affected weapons section with icons
                if (data.AffectedWeaponTypes != null && data.AffectedWeaponTypes.Count > 0)
                {
                    // Create horizontal container for weapon icons
                    var iconsContainerObj = new UnityEngine.GameObject("WeaponIcons");
                    iconsContainerObj.transform.SetParent(popupObj.transform, false);

                    var iconsLayout = iconsContainerObj.AddComponent<UnityEngine.UI.LayoutElement>();
                    iconsLayout.minHeight = 32f;

                    var iconsHLayout = iconsContainerObj.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                    iconsHLayout.spacing = 4f;
                    iconsHLayout.childForceExpandWidth = false;
                    iconsHLayout.childForceExpandHeight = false;
                    iconsHLayout.childControlWidth = false;
                    iconsHLayout.childControlHeight = false;

                    // Add content size fitter for proper sizing
                    var iconsSizeFitter = iconsContainerObj.AddComponent<UnityEngine.UI.ContentSizeFitter>();
                    iconsSizeFitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
                    iconsSizeFitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

                    // Load and display weapon icons (limit to 12 to fit)
                    int iconCount = 0;
                    foreach (var weaponType in data.AffectedWeaponTypes)
                    {
                        if (iconCount >= 12) break; // Limit icons to avoid overflow

                        var sprite = LoadSpriteForRequirement(weaponType, data.WeaponsDict, data.PowerUpsDict);
                        if (sprite != null)
                        {
                            var iconObj = new UnityEngine.GameObject($"Icon_{weaponType}");
                            iconObj.transform.SetParent(iconsContainerObj.transform, false);

                            var iconRect = iconObj.AddComponent<UnityEngine.RectTransform>();
                            iconRect.sizeDelta = new UnityEngine.Vector2(28f, 28f);

                            var iconImage = iconObj.AddComponent<UnityEngine.UI.Image>();
                            iconImage.sprite = sprite;
                            iconImage.preserveAspect = true;

                            var iconLayoutElem = iconObj.AddComponent<UnityEngine.UI.LayoutElement>();
                            iconLayoutElem.preferredWidth = 28f;
                            iconLayoutElem.preferredHeight = 28f;

                            iconCount++;
                        }
                    }

                    // If there are more weapons than shown, add "..." indicator
                    if (data.AffectedWeaponTypes.Count > 12)
                    {
                        var moreObj = new UnityEngine.GameObject("MoreIndicator");
                        moreObj.transform.SetParent(iconsContainerObj.transform, false);

                        var moreLayout = moreObj.AddComponent<UnityEngine.UI.LayoutElement>();
                        moreLayout.preferredWidth = 28f;
                        moreLayout.preferredHeight = 28f;

                        var moreText = moreObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                        moreText.text = $"+{data.AffectedWeaponTypes.Count - 12}";
                        moreText.fontSize = 10f;
                        moreText.alignment = Il2CppTMPro.TextAlignmentOptions.Center;
                        moreText.color = new UnityEngine.Color(0.8f, 0.8f, 0.8f, 1f);

                        if (cachedFont != null) moreText.font = cachedFont;
                        if (cachedFontMaterial != null) moreText.fontSharedMaterial = cachedFontMaterial;
                    }

                    MelonLogger.Msg($"Displayed {iconCount} weapon icons for arcana");
                }

                MelonLogger.Msg($"Arcana popup shown for: {data.Name}");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error showing arcana popup: {ex.Message}");
            }
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

                // Check if this powerup is affected by the player's selected arcana
                var arcanaInfo = LevelUpItemUI_SetWeaponData_Patch.GetActiveArcanaForItem(page, type);
                if (arcanaInfo.HasValue && arcanaInfo.Value.sprite != null)
                {
                    var affectedWeaponTypes = LevelUpItemUI_SetWeaponData_Patch.GetArcanaAffectedWeaponTypes(arcanaInfo.Value.arcanaData);
                    LevelUpItemUI_SetWeaponData_Patch.DisplayArcanaIndicator(__instance, arcanaInfo.Value.name, arcanaInfo.Value.description, arcanaInfo.Value.sprite, affectedWeaponTypes, weaponsDict, powerUpsDict);
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
                    DisplayFormulaOnItemFoundPage(__instance, formula, dataManager, weaponType, weaponsDict, powerUpsDict);
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
            LevelUpItemUI_SetWeaponData_Patch.EvolutionFormula formula, object dataManager, WeaponType weaponType, object weaponsDict, object powerUpsDict)
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

                // Add arcana indicator if this weapon is affected by the active arcana
                var arcanaInfo = LevelUpItemUI_SetWeaponData_Patch.GetActiveArcanaForWeaponFromDataManager(dataManager, weaponType);
                if (arcanaInfo.HasValue && arcanaInfo.Value.sprite != null)
                {
                    var affectedWeaponTypes = LevelUpItemUI_SetWeaponData_Patch.GetArcanaAffectedWeaponTypes(arcanaInfo.Value.arcanaData);
                    LevelUpItemUI_SetWeaponData_Patch.DisplayArcanaIndicatorOnPickup(formulaObj.transform, arcanaInfo.Value.name, arcanaInfo.Value.description,
                        arcanaInfo.Value.sprite, affectedWeaponTypes, weaponsDict, powerUpsDict);
                }
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

    // Patch for ShopItemUI (merchant window) - weapon items
    [HarmonyPatch]
    public class ShopItemUI_SetWeaponData_Patch
    {
        // Popup state for merchant
        public static GameObject merchantPopup = null;

        static System.Reflection.MethodBase TargetMethod()
        {
            var assembly = typeof(WeaponData).Assembly;
            var shopItemType = assembly.GetType("Il2CppVampireSurvivors.ShopItemUI");
            if (shopItemType == null)
            {
                MelonLogger.Warning("ShopItemUI type not found");
                return null;
            }
            var method = shopItemType.GetMethod("SetWeaponData", BindingFlags.Public | BindingFlags.Instance);
            return method;
        }

        public static void Postfix(object __instance, WeaponData d, WeaponType t)
        {
            try
            {
                if (d == null) return;

                // Check if weapon can evolve
                if (string.IsNullOrEmpty(d.evoInto) || d.evoSynergy == null || d.evoSynergy.Length == 0)
                    return;

                MelonLogger.Msg($"[Merchant] Adding evo icon for weapon: {d.name}");

                // Get the ShopItemUI's transform
                var instanceType = __instance.GetType();
                var transformProp = instanceType.GetProperty("transform");
                if (transformProp == null) return;

                var transform = transformProp.GetValue(__instance) as UnityEngine.Transform;
                if (transform == null) return;

                // Get DataManager from _page property
                var pageProp = instanceType.GetProperty("_page", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (pageProp == null) return;

                var page = pageProp.GetValue(__instance);
                if (page == null) return;

                var dataProp = page.GetType().GetProperty("Data", BindingFlags.Public | BindingFlags.Instance);
                if (dataProp == null)
                    dataProp = page.GetType().GetProperty("_data", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (dataProp == null) return;

                var dataManager = dataProp.GetValue(page);
                if (dataManager == null) return;

                var weaponsDict = dataManager.GetType().GetMethod("GetConvertedWeapons")?.Invoke(dataManager, null);
                var powerUpsDict = dataManager.GetType().GetMethod("GetConvertedPowerUpData")?.Invoke(dataManager, null);
                if (weaponsDict == null || powerUpsDict == null) return;

                // Build evolution formulas (may have multiple)
                var formulas = BuildEvolutionFormulas(d, t, weaponsDict, powerUpsDict);
                if (formulas == null || formulas.Count == 0) return;

                // Add evolution icon to the shop item
                AddEvoIconToShopItem(transform, formulas, weaponsDict, powerUpsDict);

                // Check for arcana - try weapon first, then item (PowerUp)
                // Check for arcana - try weapon first, then item/PowerUp
                var arcanaInfo = LevelUpItemUI_SetWeaponData_Patch.GetActiveArcanaForWeaponFromDataManager(dataManager, t);

                // If not found as weapon, try as item/PowerUp using the same integer value
                if (!arcanaInfo.HasValue || arcanaInfo.Value.sprite == null)
                {
                    var itemType = (ItemType)(int)t;
                    arcanaInfo = LevelUpItemUI_SetWeaponData_Patch.GetActiveArcanaForItemFromDataManager(dataManager, itemType);
                }

                if (arcanaInfo.HasValue && arcanaInfo.Value.sprite != null)
                {
                    var affectedTypes = LevelUpItemUI_SetWeaponData_Patch.GetArcanaAffectedWeaponTypes(arcanaInfo.Value.arcanaData);
                    AddArcanaIconToShopItem(transform, arcanaInfo.Value.name, arcanaInfo.Value.description, arcanaInfo.Value.sprite, affectedTypes, weaponsDict, powerUpsDict);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in ShopItemUI.SetWeaponData patch: {ex}");
            }
        }

        private static System.Collections.Generic.List<LevelUpItemUI_SetWeaponData_Patch.EvolutionFormula> BuildEvolutionFormulas(WeaponData weaponData, WeaponType weaponType, object weaponsDict, object powerUpsDict)
        {
            MelonLogger.Msg($"[Merchant] BuildEvolutionFormulas for {weaponData.name}: evoInto='{weaponData.evoInto}', evoSynergy={weaponData.evoSynergy?.Length ?? 0}");

            // Always find all weapons that use this item as an ingredient
            // This covers both PowerUps (used by many weapons) and weapons with their own evolutions
            var formulas = FindWeaponsUsingItem(weaponType, weaponsDict, powerUpsDict);

            return formulas;
        }

        private static System.Collections.Generic.List<LevelUpItemUI_SetWeaponData_Patch.EvolutionFormula> FindWeaponsUsingItem(WeaponType itemType, object weaponsDict, object powerUpsDict)
        {
            var formulas = new System.Collections.Generic.List<LevelUpItemUI_SetWeaponData_Patch.EvolutionFormula>();
            var itemTypeStr = itemType.ToString();

            try
            {
                var dictType = weaponsDict.GetType();
                var keysProperty = dictType.GetProperty("Keys");
                var tryGetMethod = dictType.GetMethod("TryGetValue");

                if (keysProperty == null || tryGetMethod == null) return formulas;

                var keysCollection = keysProperty.GetValue(weaponsDict);
                if (keysCollection == null) return formulas;

                var getEnumeratorMethod = keysCollection.GetType().GetMethod("GetEnumerator");
                if (getEnumeratorMethod == null) return formulas;

                var enumerator = getEnumeratorMethod.Invoke(keysCollection, null);
                var enumeratorType = enumerator.GetType();
                var moveNextMethod = enumeratorType.GetMethod("MoveNext");
                var currentProperty = enumeratorType.GetProperty("Current");

                // Load the item sprite once (this is the PowerUp being purchased)
                var itemSprite = LevelUpItemUI_SetWeaponData_Patch.LoadSpriteForRequirement(itemType, weaponsDict, powerUpsDict);

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

                    var otherWeaponData = itemProp.GetValue(weaponList, new object[] { 0 }) as WeaponData;
                    if (otherWeaponData == null || otherWeaponData.evoSynergy == null || string.IsNullOrEmpty(otherWeaponData.evoInto))
                        continue;

                    // Check if this weapon uses our item in its evolution
                    foreach (var reqType in otherWeaponData.evoSynergy)
                    {
                        if (reqType.ToString() == itemTypeStr)
                        {
                            var formula = new LevelUpItemUI_SetWeaponData_Patch.EvolutionFormula();
                            formula.ResultName = otherWeaponData.evoInto;

                            // Load the weapon sprite (the weapon that evolves)
                            var wType = (WeaponType)weaponTypeKey;
                            var weaponSprite = LevelUpItemUI_SetWeaponData_Patch.LoadSpriteForRequirement(wType, weaponsDict, powerUpsDict);
                            if (weaponSprite != null)
                                formula.Ingredients.Add((weaponSprite, false));

                            // Add the item being purchased
                            if (itemSprite != null)
                                formula.Ingredients.Add((itemSprite, false));

                            // Load result sprite
                            formula.ResultSprite = LevelUpItemUI_SetWeaponData_Patch.LoadEvoResultSprite(otherWeaponData.evoInto, weaponsDict);

                            if (formula.Ingredients.Count > 0)
                                formulas.Add(formula);

                            break; // Found match for this weapon, move to next
                        }
                    }

                    if (formulas.Count >= 6) break; // Limit to 6 formulas
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[Merchant] Error finding weapons using item: {ex.Message}");
            }

            MelonLogger.Msg($"[Merchant] Found {formulas.Count} evolution formulas for item");
            return formulas;
        }

        // Data holder for merchant popup (to avoid closure issues with Il2Cpp)
        public class MerchantEvoPopupData
        {
            public UnityEngine.Transform IconTransform;
            public System.Collections.Generic.List<LevelUpItemUI_SetWeaponData_Patch.EvolutionFormula> Formulas;
            public object WeaponsDict;
            public object PowerUpsDict;
        }

        private static System.Collections.Generic.Dictionary<int, MerchantEvoPopupData> merchantPopupDataMap = new System.Collections.Generic.Dictionary<int, MerchantEvoPopupData>();

        public static void AddEvoIconToShopItem(UnityEngine.Transform shopItemTransform, System.Collections.Generic.List<LevelUpItemUI_SetWeaponData_Patch.EvolutionFormula> formulas, object weaponsDict, object powerUpsDict)
        {
            // Clean up existing
            var existing = shopItemTransform.Find("EvoIndicator_Merchant");
            if (existing != null)
                UnityEngine.Object.Destroy(existing.gameObject);

            // Get font from existing UI
            Il2CppTMPro.TMP_FontAsset font = null;
            UnityEngine.Material fontMaterial = null;
            var existingTMPs = shopItemTransform.GetComponentsInChildren<Il2CppTMPro.TextMeshProUGUI>(true);
            if (existingTMPs.Length > 0)
            {
                font = existingTMPs[0].font;
                fontMaterial = existingTMPs[0].fontSharedMaterial;
            }

            // Create container for "evo:" label + icon - position under coin cost
            var containerObj = new UnityEngine.GameObject("EvoIndicator_Merchant");
            containerObj.transform.SetParent(shopItemTransform, false);

            var containerRect = containerObj.AddComponent<UnityEngine.RectTransform>();
            containerRect.anchorMin = new UnityEngine.Vector2(0.5f, 1f); // Anchor to top-center
            containerRect.anchorMax = new UnityEngine.Vector2(0.5f, 1f);
            containerRect.pivot = new UnityEngine.Vector2(0f, 1f); // Pivot at left edge so content flows right
            containerRect.anchoredPosition = new UnityEngine.Vector2(0f, -35f); // Center, under coin area
            containerRect.sizeDelta = new UnityEngine.Vector2(300f, 24f); // Wide enough for evo + arcana + multiple icons

            float xPos = 0f;

            // Add "evo:" text label - same style as card text
            if (font != null)
            {
                var labelObj = new UnityEngine.GameObject("EvoLabel");
                labelObj.transform.SetParent(containerObj.transform, false);

                var labelRect = labelObj.AddComponent<UnityEngine.RectTransform>();
                labelRect.anchorMin = new UnityEngine.Vector2(0f, 0.5f);
                labelRect.anchorMax = new UnityEngine.Vector2(0f, 0.5f);
                labelRect.pivot = new UnityEngine.Vector2(0f, 0.5f);
                labelRect.anchoredPosition = new UnityEngine.Vector2(xPos, 0f);
                labelRect.sizeDelta = new UnityEngine.Vector2(50f, 24f);

                var labelTMP = labelObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                labelTMP.font = font;
                if (fontMaterial != null) labelTMP.fontSharedMaterial = fontMaterial;
                labelTMP.text = "evo:";
                labelTMP.fontSize = 18f;
                labelTMP.color = UnityEngine.Color.white;
                labelTMP.alignment = Il2CppTMPro.TextAlignmentOptions.Left;
                labelTMP.overflowMode = Il2CppTMPro.TextOverflowModes.Overflow;
                labelTMP.enableWordWrapping = false;

                xPos += 60f; // Width of "evo:" text plus space gap
            }

            // Create the clickable icon - shows formula count
            var iconObj = new UnityEngine.GameObject("EvoIcon_Merchant");
            iconObj.transform.SetParent(containerObj.transform, false);

            var iconRect = iconObj.AddComponent<UnityEngine.RectTransform>();
            iconRect.anchorMin = new UnityEngine.Vector2(0f, 0.5f);
            iconRect.anchorMax = new UnityEngine.Vector2(0f, 0.5f);
            iconRect.pivot = new UnityEngine.Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new UnityEngine.Vector2(xPos, 0f); // Vertically centered
            iconRect.sizeDelta = new UnityEngine.Vector2(24f, 24f); // Match text height

            // Add background
            var bgImage = iconObj.AddComponent<UnityEngine.UI.Image>();
            bgImage.color = new UnityEngine.Color(0.2f, 0.5f, 0.9f, 0.9f); // Blue tint
            bgImage.raycastTarget = true; // IMPORTANT: Enable raycasting for hover

            // Add icon text (formula count)
            if (font != null)
            {
                var textObj = new UnityEngine.GameObject("Text");
                textObj.transform.SetParent(iconObj.transform, false);
                var textRect = textObj.AddComponent<UnityEngine.RectTransform>();
                textRect.anchorMin = UnityEngine.Vector2.zero;
                textRect.anchorMax = UnityEngine.Vector2.one;
                textRect.offsetMin = UnityEngine.Vector2.zero;
                textRect.offsetMax = UnityEngine.Vector2.zero;

                var tmp = textObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                tmp.font = font;
                tmp.text = formulas.Count.ToString();
                tmp.fontSize = 18f;
                tmp.color = UnityEngine.Color.white;
                tmp.alignment = Il2CppTMPro.TextAlignmentOptions.Center;
                tmp.raycastTarget = false;
            }

            // Store popup data to avoid closure issues
            var popupData = new MerchantEvoPopupData
            {
                IconTransform = iconObj.transform,
                Formulas = formulas,
                WeaponsDict = weaponsDict,
                PowerUpsDict = powerUpsDict
            };
            int iconId = iconObj.GetInstanceID();
            merchantPopupDataMap[iconId] = popupData;

            // Add hover trigger
            var eventTrigger = iconObj.AddComponent<UnityEngine.EventSystems.EventTrigger>();

            // Pointer Enter
            var enterEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
            enterEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter;
            enterEntry.callback.AddListener((UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData>)((eventData) =>
            {
                MelonLogger.Msg("[Merchant] Hover ENTER on evo icon");
                if (merchantPopupDataMap.TryGetValue(iconId, out var data))
                {
                    ShowMerchantEvoPopup(data.IconTransform, data.Formulas, data.WeaponsDict, data.PowerUpsDict);
                }
            }));
            eventTrigger.triggers.Add(enterEntry);

            // Pointer Exit
            var exitEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
            exitEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit;
            exitEntry.callback.AddListener((UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData>)((eventData) =>
            {
                MelonLogger.Msg("[Merchant] Hover EXIT from evo icon");
                HideMerchantPopup();
            }));
            eventTrigger.triggers.Add(exitEntry);

            MelonLogger.Msg($"[Merchant] Added evo icon with ID {iconId}, raycastTarget={bgImage.raycastTarget}");
        }

        public static void AddArcanaIconToShopItem(UnityEngine.Transform shopItemTransform, string arcanaName, string arcanaDescription, UnityEngine.Sprite arcanaSprite, System.Collections.Generic.List<WeaponType> affectedTypes, object weaponsDict, object powerUpsDict)
        {
            // Clean up existing
            var existing = shopItemTransform.Find("ArcanaIcon_Merchant");
            if (existing != null)
                UnityEngine.Object.Destroy(existing.gameObject);

            // Find EvoIndicator_Merchant to add arcana after it
            UnityEngine.Transform parent = shopItemTransform.Find("EvoIndicator_Merchant");
            float xOffset = 68f; // Position after "evo:" + icon (about 42 + 20 + some spacing)

            if (parent == null)
            {
                // No evo indicator - shouldn't happen but handle it
                return;
            }

            // Get font from existing UI
            Il2CppTMPro.TMP_FontAsset font = null;
            UnityEngine.Material fontMaterial = null;
            var existingTMPs = shopItemTransform.GetComponentsInChildren<Il2CppTMPro.TextMeshProUGUI>(true);
            if (existingTMPs.Length > 0)
            {
                font = existingTMPs[0].font;
                fontMaterial = existingTMPs[0].fontSharedMaterial;
            }

            // Add "arcana:" text label
            if (font != null)
            {
                var labelObj = new UnityEngine.GameObject("ArcanaLabel");
                labelObj.transform.SetParent(parent, false);

                var labelRect = labelObj.AddComponent<UnityEngine.RectTransform>();
                labelRect.anchorMin = new UnityEngine.Vector2(0f, 0.5f);
                labelRect.anchorMax = new UnityEngine.Vector2(0f, 0.5f);
                labelRect.pivot = new UnityEngine.Vector2(0f, 0.5f);
                labelRect.anchoredPosition = new UnityEngine.Vector2(xOffset, 0f);
                labelRect.sizeDelta = new UnityEngine.Vector2(55f, 24f);

                var labelTMP = labelObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                labelTMP.font = font;
                if (fontMaterial != null) labelTMP.fontSharedMaterial = fontMaterial;
                labelTMP.text = "arcana:";
                labelTMP.fontSize = 18f;
                labelTMP.color = UnityEngine.Color.white;
                labelTMP.alignment = Il2CppTMPro.TextAlignmentOptions.Left;
                labelTMP.overflowMode = Il2CppTMPro.TextOverflowModes.Overflow;
                labelTMP.enableWordWrapping = false;

                xOffset += 58f;
            }

            // Create arcana icon
            var iconObj = new UnityEngine.GameObject("ArcanaIcon_Merchant");
            iconObj.transform.SetParent(parent, false);

            var iconRect = iconObj.AddComponent<UnityEngine.RectTransform>();
            iconRect.anchorMin = new UnityEngine.Vector2(0f, 0.5f);
            iconRect.anchorMax = new UnityEngine.Vector2(0f, 0.5f);
            iconRect.pivot = new UnityEngine.Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new UnityEngine.Vector2(xOffset, -1f);
            iconRect.sizeDelta = new UnityEngine.Vector2(20f, 20f);

            var iconImage = iconObj.AddComponent<UnityEngine.UI.Image>();
            iconImage.sprite = arcanaSprite;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = true;

            // Add hover trigger for arcana popup
            var eventTrigger = iconObj.AddComponent<UnityEngine.EventSystems.EventTrigger>();

            var enterEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
            enterEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter;
            enterEntry.callback.AddListener((UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData>)((data) =>
            {
                ShowMerchantArcanaPopup(iconObj.transform, arcanaName, arcanaDescription, affectedTypes, weaponsDict, powerUpsDict);
            }));
            eventTrigger.triggers.Add(enterEntry);

            var exitEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
            exitEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit;
            exitEntry.callback.AddListener((UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData>)((data) =>
            {
                HideMerchantPopup();
            }));
            eventTrigger.triggers.Add(exitEntry);

            MelonLogger.Msg($"[Merchant] Added arcana icon for {arcanaName}");
        }

        public static void ShowMerchantEvoPopup(UnityEngine.Transform anchor, System.Collections.Generic.List<LevelUpItemUI_SetWeaponData_Patch.EvolutionFormula> formulas, object weaponsDict, object powerUpsDict)
        {
            try
            {
                HideMerchantPopup();

                if (formulas == null || formulas.Count == 0) return;

                MelonLogger.Msg($"[Merchant] ShowMerchantEvoPopup called, anchor={anchor.name}, formulas={formulas.Count}");

                // Find "View - Merchant" to parent popup outside the scroll viewport mask
                UnityEngine.Transform popupParent = anchor;
                while (popupParent != null && popupParent.name != "View - Merchant")
                {
                    popupParent = popupParent.parent;
                }
                if (popupParent == null) popupParent = anchor.root;
                MelonLogger.Msg($"[Merchant] Using parent: {popupParent.name}");

                // Get font first
                Il2CppTMPro.TMP_FontAsset font = null;
                var existingTMPs = anchor.root.GetComponentsInChildren<Il2CppTMPro.TextMeshProUGUI>(true);
                if (existingTMPs.Length > 0) font = existingTMPs[0].font;

                // Calculate sizes
                float iconSize = 24f;
                float spacing = 3f;
                float textWidth = 12f;
                float padding = 8f;
                int maxFormulas = System.Math.Min(formulas.Count, 4);

                // Calculate popup dimensions
                // Each row: icon + ('+' + icon) * (ingredients-1) + arrow + result
                float maxRowWidth = 0f;
                foreach (var formula in formulas.Take(maxFormulas))
                {
                    float rowWidth = formula.Ingredients.Count * iconSize + (formula.Ingredients.Count - 1) * textWidth + textWidth + iconSize;
                    rowWidth += (formula.Ingredients.Count + 1) * spacing;
                    if (rowWidth > maxRowWidth) maxRowWidth = rowWidth;
                }

                float popupWidth = maxRowWidth + padding * 2;
                float popupHeight = maxFormulas * (iconSize + spacing) + padding * 2;

                var popupObj = new UnityEngine.GameObject("MerchantEvoPopup");
                popupObj.transform.SetParent(popupParent, false);
                merchantPopup = popupObj;

                var popupRect = popupObj.AddComponent<UnityEngine.RectTransform>();

                // Get anchor's world position and convert to popup parent's local space
                var anchorWorldPos = anchor.position;
                var localPos = popupParent.InverseTransformPoint(anchorWorldPos);

                popupRect.anchorMin = new UnityEngine.Vector2(0.5f, 0.5f);
                popupRect.anchorMax = new UnityEngine.Vector2(0.5f, 0.5f);
                popupRect.pivot = new UnityEngine.Vector2(0f, 0.5f);
                popupRect.anchoredPosition = new UnityEngine.Vector2(localPos.x + 60f, localPos.y);
                popupRect.sizeDelta = new UnityEngine.Vector2(popupWidth, popupHeight);
                MelonLogger.Msg($"[Merchant] Popup size: {popupWidth}x{popupHeight}");

                // Background
                var bgImage = popupObj.AddComponent<UnityEngine.UI.Image>();
                bgImage.color = new UnityEngine.Color(0.1f, 0.1f, 0.15f, 0.95f);

                // Add outline
                var outline = popupObj.AddComponent<UnityEngine.UI.Outline>();
                outline.effectColor = new UnityEngine.Color(0.3f, 0.5f, 1f, 1f);
                outline.effectDistance = new UnityEngine.Vector2(1f, 1f);

                // Create formula rows using direct positioning
                for (int f = 0; f < maxFormulas; f++)
                {
                    var formula = formulas[f];
                    float yPos = popupHeight / 2 - padding - iconSize / 2 - f * (iconSize + spacing);
                    float xPos = -popupWidth / 2 + padding;

                    // Add ingredient icons
                    for (int i = 0; i < formula.Ingredients.Count; i++)
                    {
                        if (i > 0 && font != null)
                        {
                            // Plus sign
                            var plusObj = new UnityEngine.GameObject("Plus");
                            plusObj.transform.SetParent(popupObj.transform, false);
                            var plusRect = plusObj.AddComponent<UnityEngine.RectTransform>();
                            plusRect.anchoredPosition = new UnityEngine.Vector2(xPos + textWidth / 2, yPos);
                            plusRect.sizeDelta = new UnityEngine.Vector2(textWidth, iconSize);

                            var plusTMP = plusObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                            plusTMP.font = font;
                            plusTMP.text = "+";
                            plusTMP.fontSize = 16f;
                            plusTMP.color = UnityEngine.Color.white;
                            plusTMP.alignment = Il2CppTMPro.TextAlignmentOptions.Center;

                            xPos += textWidth + spacing;
                        }

                        var (sprite, owned) = formula.Ingredients[i];
                        var ingObj = new UnityEngine.GameObject($"Ing_{f}_{i}");
                        ingObj.transform.SetParent(popupObj.transform, false);

                        var ingRect = ingObj.AddComponent<UnityEngine.RectTransform>();
                        ingRect.anchoredPosition = new UnityEngine.Vector2(xPos + iconSize / 2, yPos);
                        ingRect.sizeDelta = new UnityEngine.Vector2(iconSize, iconSize);

                        var ingImage = ingObj.AddComponent<UnityEngine.UI.Image>();
                        ingImage.sprite = sprite;
                        ingImage.preserveAspect = true;

                        // Green border if owned
                        if (owned)
                        {
                            var ingOutline = ingObj.AddComponent<UnityEngine.UI.Outline>();
                            ingOutline.effectColor = new UnityEngine.Color(0.2f, 1f, 0.2f, 1f);
                            ingOutline.effectDistance = new UnityEngine.Vector2(2f, 2f);
                        }

                        xPos += iconSize + spacing;
                    }

                    // Arrow
                    if (font != null)
                    {
                        var arrowObj = new UnityEngine.GameObject("Arrow");
                        arrowObj.transform.SetParent(popupObj.transform, false);
                        var arrowRect = arrowObj.AddComponent<UnityEngine.RectTransform>();
                        arrowRect.anchoredPosition = new UnityEngine.Vector2(xPos + textWidth / 2, yPos);
                        arrowRect.sizeDelta = new UnityEngine.Vector2(textWidth, iconSize);

                        var arrowTMP = arrowObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                        arrowTMP.font = font;
                        arrowTMP.text = "→";
                        arrowTMP.fontSize = 16f;
                        arrowTMP.color = UnityEngine.Color.white;
                        arrowTMP.alignment = Il2CppTMPro.TextAlignmentOptions.Center;

                        xPos += textWidth + spacing;
                    }

                    // Result
                    if (formula.ResultSprite != null)
                    {
                        var resultObj = new UnityEngine.GameObject("Result");
                        resultObj.transform.SetParent(popupObj.transform, false);

                        var resultRect = resultObj.AddComponent<UnityEngine.RectTransform>();
                        resultRect.anchoredPosition = new UnityEngine.Vector2(xPos + iconSize / 2, yPos);
                        resultRect.sizeDelta = new UnityEngine.Vector2(iconSize, iconSize);

                        var resultImage = resultObj.AddComponent<UnityEngine.UI.Image>();
                        resultImage.sprite = formula.ResultSprite;
                        resultImage.preserveAspect = true;

                        // Gold border for result
                        var resultOutline = resultObj.AddComponent<UnityEngine.UI.Outline>();
                        resultOutline.effectColor = new UnityEngine.Color(1f, 0.8f, 0.2f, 1f);
                        resultOutline.effectDistance = new UnityEngine.Vector2(1f, 1f);
                    }
                }

                // Show "+N more" if there are additional formulas
                if (formulas.Count > maxFormulas && font != null)
                {
                    var moreObj = new UnityEngine.GameObject("MoreText");
                    moreObj.transform.SetParent(popupObj.transform, false);

                    var moreRect = moreObj.AddComponent<UnityEngine.RectTransform>();
                    moreRect.anchoredPosition = new UnityEngine.Vector2(popupWidth / 2 - 20f, -popupHeight / 2 + 8f);
                    moreRect.sizeDelta = new UnityEngine.Vector2(40f, 12f);

                    var moreTMP = moreObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                    moreTMP.font = font;
                    moreTMP.text = $"+{formulas.Count - maxFormulas}";
                    moreTMP.fontSize = 9f;
                    moreTMP.color = new UnityEngine.Color(0.6f, 0.6f, 0.6f, 1f);
                    moreTMP.alignment = Il2CppTMPro.TextAlignmentOptions.Right;
                }

                // Ensure popup is on top
                popupObj.transform.SetAsLastSibling();
                MelonLogger.Msg($"[Merchant] Popup created successfully");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"[Merchant] Error creating popup: {ex}");
            }
        }

        public static void ShowMerchantArcanaPopup(UnityEngine.Transform anchor, string arcanaName, string arcanaDescription, System.Collections.Generic.List<WeaponType> affectedTypes, object weaponsDict, object powerUpsDict)
        {
            HideMerchantPopup();

            var parent = anchor;
            while (parent.parent != null && parent.parent.name != "Canvas - Game UI")
                parent = parent.parent;

            var popupObj = new UnityEngine.GameObject("MerchantArcanaPopup");
            popupObj.transform.SetParent(parent, false);
            merchantPopup = popupObj;

            var popupRect = popupObj.AddComponent<UnityEngine.RectTransform>();
            var anchorWorldPos = anchor.position;
            popupRect.position = anchorWorldPos + new UnityEngine.Vector3(100f, 0f, 0f);

            var bgImage = popupObj.AddComponent<UnityEngine.UI.Image>();
            bgImage.color = new UnityEngine.Color(0.15f, 0.1f, 0.2f, 0.95f);

            var outline = popupObj.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = new UnityEngine.Color(1f, 0.8f, 0.2f, 1f);
            outline.effectDistance = new UnityEngine.Vector2(2f, 2f);

            var layout = popupObj.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.padding = new UnityEngine.RectOffset(10, 10, 10, 10);
            layout.spacing = 5f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            var fitter = popupObj.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

            Il2CppTMPro.TMP_FontAsset font = null;
            var existingTMPs = anchor.root.GetComponentsInChildren<Il2CppTMPro.TextMeshProUGUI>(true);
            if (existingTMPs.Length > 0) font = existingTMPs[0].font;

            if (font != null)
            {
                // Title
                var titleObj = new UnityEngine.GameObject("Title");
                titleObj.transform.SetParent(popupObj.transform, false);
                var titleTMP = titleObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                titleTMP.font = font;
                titleTMP.text = $"✦ {arcanaName}";
                titleTMP.fontSize = 14f;
                titleTMP.color = new UnityEngine.Color(1f, 0.85f, 0.4f, 1f);

                // Description (truncated)
                var descObj = new UnityEngine.GameObject("Desc");
                descObj.transform.SetParent(popupObj.transform, false);
                var descTMP = descObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                descTMP.font = font;
                var shortDesc = arcanaDescription.Length > 100 ? arcanaDescription.Substring(0, 100) + "..." : arcanaDescription;
                descTMP.text = shortDesc;
                descTMP.fontSize = 10f;
                descTMP.color = UnityEngine.Color.white;

                var descLayout = descObj.AddComponent<UnityEngine.UI.LayoutElement>();
                descLayout.preferredWidth = 200f;
            }

            // Affected weapons row
            if (affectedTypes != null && affectedTypes.Count > 0)
            {
                var rowObj = new UnityEngine.GameObject("WeaponsRow");
                rowObj.transform.SetParent(popupObj.transform, false);

                var rowLayout = rowObj.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                rowLayout.spacing = 3f;
                rowLayout.childControlWidth = false;
                rowLayout.childControlHeight = false;

                int maxIcons = System.Math.Min(affectedTypes.Count, 8);
                for (int i = 0; i < maxIcons; i++)
                {
                    var sprite = LevelUpItemUI_SetWeaponData_Patch.LoadSpriteForRequirement(affectedTypes[i], weaponsDict, powerUpsDict);
                    if (sprite != null)
                    {
                        var iconObj = new UnityEngine.GameObject($"Icon_{i}");
                        iconObj.transform.SetParent(rowObj.transform, false);
                        var iconImage = iconObj.AddComponent<UnityEngine.UI.Image>();
                        iconImage.sprite = sprite;
                        iconImage.preserveAspect = true;

                        var iconLayout = iconObj.AddComponent<UnityEngine.UI.LayoutElement>();
                        iconLayout.preferredWidth = 20f;
                        iconLayout.preferredHeight = 20f;
                    }
                }
            }

            popupObj.transform.SetAsLastSibling();
        }

        public static void HideMerchantPopup()
        {
            if (merchantPopup != null)
            {
                UnityEngine.Object.Destroy(merchantPopup);
                merchantPopup = null;
            }
        }
    }

    // Patch for ShopItemUI (merchant window) - powerup items
    [HarmonyPatch]
    public class ShopItemUI_SetItemData_Patch
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            var assembly = typeof(WeaponData).Assembly;
            var shopItemType = assembly.GetType("Il2CppVampireSurvivors.ShopItemUI");
            if (shopItemType == null) return null;
            return shopItemType.GetMethod("SetItemData", BindingFlags.Public | BindingFlags.Instance);
        }

        public static void Postfix(object __instance, object d, object t)
        {
            try
            {
                if (d == null) return;

                // Get ItemType value
                int itemTypeInt = 0;
                var il2cppObj = t as Il2CppSystem.Object;
                if (il2cppObj != null)
                {
                    unsafe
                    {
                        IntPtr ptr = il2cppObj.Pointer;
                        int* valuePtr = (int*)((byte*)ptr.ToPointer() + 16);
                        itemTypeInt = *valuePtr;
                    }
                }

                var itemType = (ItemType)itemTypeInt;
                MelonLogger.Msg($"[Merchant] Processing powerup: {itemType}");

                // Get transform
                var instanceType = __instance.GetType();
                var transformProp = instanceType.GetProperty("transform");
                if (transformProp == null) return;

                var transform = transformProp.GetValue(__instance) as UnityEngine.Transform;
                if (transform == null) return;

                // Get DataManager
                var pageProp = instanceType.GetProperty("_page", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (pageProp == null) return;

                var page = pageProp.GetValue(__instance);
                if (page == null) return;

                var dataProp = page.GetType().GetProperty("Data", BindingFlags.Public | BindingFlags.Instance);
                if (dataProp == null)
                    dataProp = page.GetType().GetProperty("_data", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (dataProp == null) return;

                var dataManager = dataProp.GetValue(page);
                if (dataManager == null) return;

                var weaponsDict = dataManager.GetType().GetMethod("GetConvertedWeapons")?.Invoke(dataManager, null);
                var powerUpsDict = dataManager.GetType().GetMethod("GetConvertedPowerUpData")?.Invoke(dataManager, null);
                if (weaponsDict == null || powerUpsDict == null) return;

                // Find all weapons that use this powerup for evolution
                var formulas = FindWeaponsUsingPowerUp(itemType, weaponsDict, powerUpsDict);
                if (formulas.Count == 0) return;

                MelonLogger.Msg($"[Merchant] Found {formulas.Count} evolutions for powerup {itemType}");

                // Add evolution icon
                AddEvoIconForPowerUp(transform, formulas, weaponsDict, powerUpsDict);

                // Check for arcana
                var arcanaInfo = LevelUpItemUI_SetWeaponData_Patch.GetActiveArcanaForItemFromDataManager(dataManager, itemType);
                if (arcanaInfo.HasValue && arcanaInfo.Value.sprite != null)
                {
                    var affectedTypes = LevelUpItemUI_SetWeaponData_Patch.GetArcanaAffectedWeaponTypes(arcanaInfo.Value.arcanaData);
                    ShopItemUI_SetWeaponData_Patch.AddArcanaIconToShopItem(transform, arcanaInfo.Value.name, arcanaInfo.Value.description, arcanaInfo.Value.sprite, affectedTypes, weaponsDict, powerUpsDict);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in ShopItemUI.SetItemData patch: {ex}");
            }
        }

        private static System.Collections.Generic.List<LevelUpItemUI_SetWeaponData_Patch.EvolutionFormula> FindWeaponsUsingPowerUp(ItemType powerUpType, object weaponsDict, object powerUpsDict)
        {
            var formulas = new System.Collections.Generic.List<LevelUpItemUI_SetWeaponData_Patch.EvolutionFormula>();
            var powerUpTypeStr = powerUpType.ToString();

            try
            {
                var dictType = weaponsDict.GetType();
                var keysProperty = dictType.GetProperty("Keys");
                var tryGetMethod = dictType.GetMethod("TryGetValue");

                if (keysProperty == null || tryGetMethod == null) return formulas;

                var keysCollection = keysProperty.GetValue(weaponsDict);
                if (keysCollection == null) return formulas;

                var getEnumeratorMethod = keysCollection.GetType().GetMethod("GetEnumerator");
                if (getEnumeratorMethod == null) return formulas;

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

                    // Check if this weapon uses our powerup
                    foreach (var reqType in weaponData.evoSynergy)
                    {
                        if (reqType.ToString() == powerUpTypeStr)
                        {
                            var formula = new LevelUpItemUI_SetWeaponData_Patch.EvolutionFormula();
                            formula.ResultName = weaponData.evoInto;

                            // Load weapon sprite
                            var wType = (WeaponType)weaponTypeKey;
                            var weaponSprite = LevelUpItemUI_SetWeaponData_Patch.LoadSpriteForRequirement(wType, weaponsDict, powerUpsDict);
                            if (weaponSprite != null)
                                formula.Ingredients.Add((weaponSprite, false));

                            // Load powerup sprite
                            var powerUpSprite = LevelUpItemUI_SetWeaponData_Patch.LoadSpriteForRequirement((WeaponType)(int)powerUpType, weaponsDict, powerUpsDict);
                            if (powerUpSprite != null)
                                formula.Ingredients.Add((powerUpSprite, true));

                            // Load result sprite
                            formula.ResultSprite = LevelUpItemUI_SetWeaponData_Patch.LoadEvoResultSprite(weaponData.evoInto, weaponsDict);

                            if (formula.Ingredients.Count > 0)
                                formulas.Add(formula);

                            break;
                        }
                    }

                    if (formulas.Count >= 5) break; // Limit
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"Error finding weapons for powerup: {ex.Message}");
            }

            return formulas;
        }

        private static void AddEvoIconForPowerUp(UnityEngine.Transform shopItemTransform, System.Collections.Generic.List<LevelUpItemUI_SetWeaponData_Patch.EvolutionFormula> formulas, object weaponsDict, object powerUpsDict)
        {
            var existing = shopItemTransform.Find("EvoIcon_Merchant");
            if (existing != null)
                UnityEngine.Object.Destroy(existing.gameObject);

            var iconObj = new UnityEngine.GameObject("EvoIcon_Merchant");
            iconObj.transform.SetParent(shopItemTransform, false);

            var iconRect = iconObj.AddComponent<UnityEngine.RectTransform>();
            iconRect.anchorMin = new UnityEngine.Vector2(1f, 1f);
            iconRect.anchorMax = new UnityEngine.Vector2(1f, 1f);
            iconRect.pivot = new UnityEngine.Vector2(1f, 1f);
            iconRect.anchoredPosition = new UnityEngine.Vector2(-5f, -5f);
            iconRect.sizeDelta = new UnityEngine.Vector2(24f, 24f);

            var bgImage = iconObj.AddComponent<UnityEngine.UI.Image>();
            bgImage.color = new UnityEngine.Color(0.2f, 0.6f, 1f, 0.9f);

            Il2CppTMPro.TMP_FontAsset font = null;
            var existingTMPs = shopItemTransform.GetComponentsInChildren<Il2CppTMPro.TextMeshProUGUI>(true);
            if (existingTMPs.Length > 0) font = existingTMPs[0].font;

            if (font != null)
            {
                var textObj = new UnityEngine.GameObject("Text");
                textObj.transform.SetParent(iconObj.transform, false);
                var textRect = textObj.AddComponent<UnityEngine.RectTransform>();
                textRect.anchorMin = UnityEngine.Vector2.zero;
                textRect.anchorMax = UnityEngine.Vector2.one;
                textRect.offsetMin = UnityEngine.Vector2.zero;
                textRect.offsetMax = UnityEngine.Vector2.zero;

                var tmp = textObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                tmp.font = font;
                tmp.text = $"⟳{formulas.Count}";
                tmp.fontSize = 12f;
                tmp.color = UnityEngine.Color.white;
                tmp.alignment = Il2CppTMPro.TextAlignmentOptions.Center;
            }

            var eventTrigger = iconObj.AddComponent<UnityEngine.EventSystems.EventTrigger>();

            var enterEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
            enterEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter;
            enterEntry.callback.AddListener((UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData>)((data) =>
            {
                ShowMultiEvoPopup(iconObj.transform, formulas);
            }));
            eventTrigger.triggers.Add(enterEntry);

            var exitEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
            exitEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit;
            exitEntry.callback.AddListener((UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData>)((data) =>
            {
                ShopItemUI_SetWeaponData_Patch.HideMerchantPopup();
            }));
            eventTrigger.triggers.Add(exitEntry);
        }

        private static void ShowMultiEvoPopup(UnityEngine.Transform anchor, System.Collections.Generic.List<LevelUpItemUI_SetWeaponData_Patch.EvolutionFormula> formulas)
        {
            ShopItemUI_SetWeaponData_Patch.HideMerchantPopup();

            // Find "View - Merchant" to parent popup outside the scroll viewport mask
            UnityEngine.Transform popupParent = anchor;
            while (popupParent != null && popupParent.name != "View - Merchant")
            {
                popupParent = popupParent.parent;
            }
            if (popupParent == null) popupParent = anchor.root;

            var popupObj = new UnityEngine.GameObject("MerchantMultiEvoPopup");
            popupObj.transform.SetParent(popupParent, false);
            ShopItemUI_SetWeaponData_Patch.merchantPopup = popupObj;

            var popupRect = popupObj.AddComponent<UnityEngine.RectTransform>();

            // Get anchor's world position and convert to popup parent's local space
            var anchorWorldPos = anchor.position;
            var localPos = popupParent.InverseTransformPoint(anchorWorldPos);

            popupRect.anchorMin = new UnityEngine.Vector2(0.5f, 0.5f);
            popupRect.anchorMax = new UnityEngine.Vector2(0.5f, 0.5f);
            popupRect.pivot = new UnityEngine.Vector2(0f, 0.5f);
            popupRect.anchoredPosition = new UnityEngine.Vector2(localPos.x + 50f, localPos.y);

            var bgImage = popupObj.AddComponent<UnityEngine.UI.Image>();
            bgImage.color = new UnityEngine.Color(0.1f, 0.1f, 0.15f, 0.95f);

            var outline = popupObj.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = new UnityEngine.Color(0.3f, 0.5f, 1f, 1f);
            outline.effectDistance = new UnityEngine.Vector2(2f, 2f);

            var layout = popupObj.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.padding = new UnityEngine.RectOffset(10, 10, 10, 10);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            var fitter = popupObj.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

            Il2CppTMPro.TMP_FontAsset font = null;
            var existingTMPs = anchor.root.GetComponentsInChildren<Il2CppTMPro.TextMeshProUGUI>(true);
            if (existingTMPs.Length > 0) font = existingTMPs[0].font;

            if (font != null)
            {
                var titleObj = new UnityEngine.GameObject("Title");
                titleObj.transform.SetParent(popupObj.transform, false);
                var titleTMP = titleObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                titleTMP.font = font;
                titleTMP.text = $"Evolutions ({formulas.Count})";
                titleTMP.fontSize = 14f;
                titleTMP.color = new UnityEngine.Color(0.5f, 0.8f, 1f, 1f);
                titleTMP.alignment = Il2CppTMPro.TextAlignmentOptions.Center;

                var titleLayout = titleObj.AddComponent<UnityEngine.UI.LayoutElement>();
                titleLayout.preferredHeight = 20f;
            }

            float iconSize = 20f;
            int maxFormulas = System.Math.Min(formulas.Count, 4);

            for (int f = 0; f < maxFormulas; f++)
            {
                var formula = formulas[f];

                var rowObj = new UnityEngine.GameObject($"Row_{f}");
                rowObj.transform.SetParent(popupObj.transform, false);

                var rowLayout = rowObj.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                rowLayout.spacing = 4f;
                rowLayout.childControlWidth = false;
                rowLayout.childControlHeight = false;

                for (int i = 0; i < formula.Ingredients.Count; i++)
                {
                    if (i > 0 && font != null)
                    {
                        var plusObj = new UnityEngine.GameObject("Plus");
                        plusObj.transform.SetParent(rowObj.transform, false);
                        var plusTMP = plusObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                        plusTMP.font = font;
                        plusTMP.text = "+";
                        plusTMP.fontSize = 16f;
                        plusTMP.color = UnityEngine.Color.white;

                        var plusLayout = plusObj.AddComponent<UnityEngine.UI.LayoutElement>();
                        plusLayout.preferredWidth = 12f;
                        plusLayout.preferredHeight = iconSize;
                    }

                    var (sprite, owned) = formula.Ingredients[i];
                    var ingObj = new UnityEngine.GameObject($"Ing_{i}");
                    ingObj.transform.SetParent(rowObj.transform, false);

                    var ingImage = ingObj.AddComponent<UnityEngine.UI.Image>();
                    ingImage.sprite = sprite;
                    ingImage.preserveAspect = true;

                    var ingLayout = ingObj.AddComponent<UnityEngine.UI.LayoutElement>();
                    ingLayout.preferredWidth = iconSize;
                    ingLayout.preferredHeight = iconSize;

                    if (owned)
                    {
                        var ingOutline = ingObj.AddComponent<UnityEngine.UI.Outline>();
                        ingOutline.effectColor = new UnityEngine.Color(0.2f, 1f, 0.2f, 1f);
                        ingOutline.effectDistance = new UnityEngine.Vector2(2f, 2f);
                    }
                }

                if (font != null)
                {
                    var arrowObj = new UnityEngine.GameObject("Arrow");
                    arrowObj.transform.SetParent(rowObj.transform, false);
                    var arrowTMP = arrowObj.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                    arrowTMP.font = font;
                    arrowTMP.text = "→";
                    arrowTMP.fontSize = 16f;
                    arrowTMP.color = UnityEngine.Color.white;

                    var arrowLayout = arrowObj.AddComponent<UnityEngine.UI.LayoutElement>();
                    arrowLayout.preferredWidth = 16f;
                    arrowLayout.preferredHeight = iconSize;
                }

                if (formula.ResultSprite != null)
                {
                    var resultObj = new UnityEngine.GameObject("Result");
                    resultObj.transform.SetParent(rowObj.transform, false);

                    var resultImage = resultObj.AddComponent<UnityEngine.UI.Image>();
                    resultImage.sprite = formula.ResultSprite;
                    resultImage.preserveAspect = true;

                    var resultLayout = resultObj.AddComponent<UnityEngine.UI.LayoutElement>();
                    resultLayout.preferredWidth = iconSize;
                    resultLayout.preferredHeight = iconSize;

                    var resultOutline = resultObj.AddComponent<UnityEngine.UI.Outline>();
                    resultOutline.effectColor = new UnityEngine.Color(1f, 0.8f, 0.2f, 1f);
                    resultOutline.effectDistance = new UnityEngine.Vector2(2f, 2f);
                }
            }

            popupObj.transform.SetAsLastSibling();
        }
    }
}
