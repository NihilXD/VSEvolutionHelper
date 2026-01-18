# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

VSEvolutionHelper is a MelonLoader mod for Vampire Survivors that displays evolution requirement icons on level-up cards, showing which weapons/items are needed to evolve the displayed option. The mod also highlights icons for requirements the player already owns.

## Current Status

### What Works
- ✅ Evolution requirement icons display correctly on weapon/PowerUp level-up cards
- ✅ Inventory detection works for both weapons and PowerUps
- ✅ Icons are loaded from the game's sprite atlas
- ✅ Ownership checking correctly identifies what player has in inventory

### Known Issues

#### Issue 1: Visual Highlighting Not Effective
**Problem**: Icons for owned items need visual highlighting, but all attempts have been too subtle or distorted the icons.

**Approaches Tried (All Failed or Too Subtle)**:
1. Color tinting (green, cyan, lime, gold) - Changes icon appearance too much, makes it unrecognizable
2. Scaling (1.3x, 1.5x, 1.8x) - Works but not noticeable enough alone
3. Outline component - Creates orange artifacts
4. Shadow component with gold glow - Very subtle "doubling" effect, barely visible
5. Text label "(owned)" - Never displays (Unity UI Text needs font, not available)

**What's Needed**: An unmissable visual effect that:
- Doesn't change the icon's color (keeps it recognizable)
- Doesn't distort the icon's appearance
- Is immediately obvious (not subtle)
- Maybe: bright border, pulsing animation, star/checkmark overlay, or duplicate icon as glowing background

**Current Code Location**: `DisplayEvolutionIcons()` method around line 581-667

#### Issue 2: PowerUp Cards Show Only One Weapon
**Problem**: When a PowerUp card is displayed (e.g., Spinach card), it should show ALL weapons that can use it for evolution, but currently only shows the first weapon found.

**Example**: Spinach is used by Fire Wand, Magic Wand, Axe, etc. - but the card only shows Fire Wand icon.

**Root Cause**: In the `SetItemData` patch (around line 640-675), the code breaks after finding the first weapon:
```csharp
if (weaponRequiresThisPowerUp)
{
    // Found first weapon - loads it
    break; // ❌ This stops looking for other weapons!
}
```

**What's Needed**: Keep looping through all weapons, collect ALL that use this PowerUp, and display all their icons (not just the first one).

**Code Location**: `SetItemData` patch around line 679-750

## Build Commands

Build the project:
```bash
dotnet build VSEvolutionHelper.csproj
```

The output DLL will be located at `bin/Debug/netstandard2.1/VSEvolutionHelper.dll`

## Deployment

After building, copy to mods folder (game must be closed):
```bash
cp "C:\Users\Nihil\source\repos\VSEvolutionHelper\bin\Debug\netstandard2.1\VSEvolutionHelper.dll" "D:\SteamLibrary\steamapps\common\Vampire Survivors\Mods\VSEvolutionHelper.dll"
```

## Project Type and Dependencies

This is a .NET Standard 2.1 class library targeting the MelonLoader modding framework for Unity IL2CPP games.

**Critical Dependencies:**
- MelonLoader: The main modding framework
- 0Harmony: For runtime patching of game methods
- Il2CppInterop.Runtime: For interacting with IL2CPP-compiled code
- Game assemblies: Located in `D:\SteamLibrary\steamapps\common\Vampire Survivors\MelonLoader\`

## Architecture

### Entry Point
The mod initializes through `VSEvolutionHelper.OnInitializeMelon()` which creates a Harmony instance and manually patches level-up UI methods.

### Harmony Patches

#### 1. LevelUpItemUI_SetWeaponData_Patch
**Patches**: When a weapon card is displayed during level-up

**What it does**:
1. Reads weapon's evolution data (`baseData.evoSynergy` - list of requirements)
2. For each requirement (WeaponType enum), loads the sprite from the game's atlas
3. Checks if player owns each requirement using `PlayerOwnsRequirement()`
4. Displays icons in `_EvoIcons` UI array using `DisplayEvolutionIcons()`

**Code Location**: Around line 48-240

#### 2. LevelUpItemUI_SetItemData_Patch
**Patches**: When a PowerUp/item card is displayed during level-up

**What it does**:
1. Iterates through ALL weapons in the game
2. Checks which weapons require THIS PowerUp in their `evoSynergy` list
3. Loads sprites for those weapons and displays them
4. **BUG**: Currently breaks after finding first weapon instead of collecting all

**Code Location**: Around line 679-750

### Key Helper Methods

#### PlayerOwnsRequirement(LevelUpPage page, WeaponType reqType)
**Purpose**: Checks if player currently owns a weapon or PowerUp

**How it works**:
1. Navigates: `page._gameSession.ActiveCharacter`
2. Checks `WeaponsManager.ActiveEquipment` (List of Equipment with Type property)
3. Checks `AccessoriesManager.ActiveEquipment` (List of Equipment with Type property)
4. Returns true if Type matches reqType

**Code Location**: Around line 553-610

#### DisplayEvolutionIcons(LevelUpItemUI instance, List<Sprite> sprites, List<bool> hasItems)
**Purpose**: Displays requirement icons in the UI

**How it works**:
1. Gets `_EvoIcons` array (5 Image components named "w", "w (1)", "w (2)", etc.)
2. For each sprite, sets it on the corresponding Image component
3. If `hasItems[i]` is true, applies highlighting effect (currently: subtle shadow)
4. If `hasItems[i]` is false, normal appearance

**Code Location**: Around line 532-677

**Current Highlighting Code**:
```csharp
if (playerHasItem)
{
    // Keep icon normal appearance
    iconImage.color = new Color(1f, 1f, 1f, 1f);

    // Add subtle gold shadow (barely visible)
    var shadow = iconImage.gameObject.AddComponent<UnityEngine.UI.Shadow>();
    shadow.effectColor = new Color(1f, 0.8f, 0f, 0.5f);
    shadow.effectDistance = new UnityEngine.Vector2(3f, 3f);

    // Try to add text "(owned)" - doesn't work without font
    var text = newObj.AddComponent<UnityEngine.UI.Text>();
    text.text = "(owned)"; // Never displays
}
```

### Inventory Detection Details

**Player inventory is split into two managers:**

1. **Weapons**: `page._gameSession.ActiveCharacter.WeaponsManager.ActiveEquipment`
   - Type: List of Equipment objects
   - Equipment.Type = WeaponType enum (FIREBALL, KNIFE, WHIP, etc.)

2. **PowerUps**: `page._gameSession.ActiveCharacter.AccessoriesManager.ActiveEquipment`
   - Type: List of Equipment objects
   - Equipment.Type = ItemType enum (POWER, SPEED, ARMOR, etc.)

**Note**: Both use the same Equipment class structure, just different managers.

### IL2CPP Considerations
- Use `Il2CppSystem.Collections.Generic.List` not System.Collections.Generic.List
- Access list items by index with `list[i]` syntax
- WeaponType/ItemType enums work with ToString() for comparison
- Reflection is needed to access properties (use BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)

## Code Structure

**Single File Architecture:** All code is in `Class1.cs` containing:
- Main mod class (VSEvolutionHelper : MelonMod)
- LevelUpItemUI_SetWeaponData_Patch (for weapon cards)
- LevelUpItemUI_SetItemData_Patch (for PowerUp cards)
- Helper methods: PlayerOwnsRequirement, CheckEquipmentList, DisplayEvolutionIcons, LoadSpriteForRequirement, TryLoadFromWeapons, etc.

## Testing

**In-Game Testing:**
1. Start game with mod installed
2. Begin a run and level up
3. Check weapon/PowerUp cards for small requirement icons at bottom
4. Icons should show what's needed for evolution
5. Owned items should have some visual indicator (currently very subtle shadow)

**Log File Location:**
```
D:\SteamLibrary\steamapps\common\Vampire Survivors\MelonLoader\Latest.log
```

**Key Log Messages:**
- `Loading evolution icons for [WeaponName] -> [EvolutionName]`
- `Loaded: [SpriteName] (owned: True/False)`
- `HIGHLIGHT: [SpriteName] -> (owned) text added` (if text succeeds)
- `Displayed N evolution icon(s)`

## Visual Debugging

The `_EvoIcons` array is located at this UI path:
```
GAME UI/Canvas - Game UI/Safe Area/View - Level Up/PanelContainer/Scroll View/Viewport/Content/[WeaponName]/evo/w
GAME UI/.../[WeaponName]/evo/w (1)
GAME UI/.../[WeaponName]/evo/w (2)
etc.
```

These are Unity UI Image components that display the requirement icons.
