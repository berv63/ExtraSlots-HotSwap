using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ExtraSlotsHotSwap
{
    [BepInPlugin(pluginID, pluginName, pluginVersion)]
    [BepInDependency("shudnal.ExtraSlots", BepInDependency.DependencyFlags.HardDependency)]
    public class HotSwapPlugin : BaseUnityPlugin
    {
        public const string pluginID = "berv.ExtraSlotsHotSwap";
        public const string pluginName = "Extra Slots HotSwap";
        public const string pluginVersion = "0.0.12";

        internal static HotSwapPlugin instance;
        internal static ManualLogSource logger;

        private readonly Harmony harmony = new Harmony(pluginID);

        internal static ConfigEntry<int> maxLoadouts;
        internal static ConfigEntry<bool> showSwitchMessage;
        internal static ConfigEntry<bool> dropAllLoadoutsOnDeath;
        internal static ConfigEntry<bool> repairStoredLoadouts;
        internal static ConfigEntry<bool> upgradeStoredLoadouts;
        internal static ConfigEntry<bool> beltCancelsWeight;
        internal static ConfigEntry<bool> shareAmmo;
        internal static ConfigEntry<bool> shareFood;
        internal static ConfigEntry<bool> shareQuickSlots;
        internal static ConfigEntry<KeyboardShortcut> previousLoadoutHotkey;
        internal static ConfigEntry<KeyboardShortcut> nextLoadoutHotkey;
        internal static ConfigEntry<bool> suppressMouseWhileModifierHeld;
        internal static ConfigEntry<bool> hotkeysInInventory;
        internal static ConfigEntry<Vector2> barOffset;
        internal static ConfigEntry<float> buttonSize;

        private void Awake()
        {
            instance = this;
            logger = Logger;

            maxLoadouts = Config.Bind("General", "Max loadouts", 8, new ConfigDescription("Maximum amount of loadouts the + button will create.", new AcceptableValueRange<int>(1, 12)));
            showSwitchMessage = Config.Bind("General", "Show switch message", true, "Show a message in the top left corner when the loadout is switched.");
            dropAllLoadoutsOnDeath = Config.Bind("General", "Drop all loadouts on death", false, "Disabled: only the active loadout goes to the tombstone, other loadouts stay with the character. Enabled: items of all other loadouts are dropped on death as well, in extra tombstones at the same spot. Recovered items return to the inventory, not to their loadouts.");
            repairStoredLoadouts = Config.Bind("General", "Repair stored loadouts", true, "The repair button of a workstation also repairs items of loadouts that are not active, one item per click like the items you carry. The usual station type and level rules apply.");
            upgradeStoredLoadouts = Config.Bind("General", "Upgrade stored loadouts", true, "The upgrade tab of a workstation also lists items of loadouts that are not active. An upgraded item stays in its loadout. Needs one free inventory slot while the upgrade is made.");
            beltCancelsWeight = Config.Bind("General", "Megingjord cancels loadout weight", true, "A loadout that has a Megingjord (or another belt that adds carry weight) equipped in it weighs nothing, whether it is the active loadout or not. Each loadout needs a belt of its own, loadouts without one keep their weight. Items in the regular inventory keep their weight.");
            shareAmmo = Config.Bind("General", "Share ammo between loadouts", false, "Disabled: every loadout holds its own ammo. Enabled: items in the ammo slots stay in place when the loadout is switched. Ammo already stored in another loadout goes to the inventory when that loadout is next switched to, if its slot is taken.");
            shareFood = Config.Bind("General", "Share food between loadouts", false, "Disabled: every loadout holds its own food. Enabled: items in the food slots stay in place when the loadout is switched. Food already stored in another loadout goes to the inventory when that loadout is next switched to, if its slot is taken.");
            shareQuickSlots = Config.Bind("General", "Share quick slots between loadouts", false, "Disabled: every loadout holds its own quick slot items, such as potions. Enabled: items in the quick slots stay in place when the loadout is switched, whatever they are. Items already stored in another loadout go to the inventory when that loadout is next switched to, if their slot is taken.");

            previousLoadoutHotkey =Config.Bind("Hotkeys", "Previous loadout", new KeyboardShortcut(KeyCode.Mouse0, KeyCode.LeftAlt), "Switch to the previous loadout. Does not work while menus other than the inventory are open.");
            nextLoadoutHotkey = Config.Bind("Hotkeys", "Next loadout", new KeyboardShortcut(KeyCode.Mouse1, KeyCode.LeftAlt), "Switch to the next loadout. Does not work while menus other than the inventory are open.");
            suppressMouseWhileModifierHeld = Config.Bind("Hotkeys", "Suppress attack and block while modifier is held", true, "If a loadout hotkey uses a mouse button, attack and block are ignored while its modifier keys are held so switching does not swing your weapon.");
            hotkeysInInventory = Config.Bind("Hotkeys", "Switch loadouts in inventory", true, "Loadout hotkeys also work while the inventory screen is open. If a loadout hotkey uses a mouse button, clicks on inventory items are ignored while its modifier keys are held.");

            barOffset = Config.Bind("Panel", "Loadout bar offset", new Vector2(0f, 10f), "Offset of the loadout bar from the bottom left corner of the Extra Slots equipment panel. X moves it right, Y moves it down.");
            buttonSize = Config.Bind("Panel", "Button size", 36f, new ConfigDescription("Size of the loadout buttons.", new AcceptableValueRange<float>(20f, 64f)));

            beltCancelsWeight.SettingChanged += (sender, args) => Loadouts.OnBeltSettingChanged();
            barOffset.SettingChanged += (sender, args) => LoadoutBar.MarkDirty();
            buttonSize.SettingChanged += (sender, args) => LoadoutBar.MarkDirty();

            harmony.PatchAll();
        }

        private void Update()
        {
            Player player = Player.m_localPlayer;
            if (!player)
                return;

            Loadouts.EnsureLoaded(player);
            LoadoutBar.Update(player);
            Loadouts.UpdateCarryBelt(player);

            if (!Loadouts.IsReady(player) || Loadouts.Count < 2)
                return;

            if (IsShortcutDown(nextLoadoutHotkey.Value) && CanUseHotkey(player))
                Loadouts.Cycle(player, 1);
            else if (IsShortcutDown(previousLoadoutHotkey.Value) && CanUseHotkey(player))
                Loadouts.Cycle(player, -1);
        }

        private void OnDestroy()
        {
            LoadoutBar.Destroy();
            harmony?.UnpatchSelf();
            instance = null;
        }

        // Alt + click is alternative placement while building, leave it alone there
        private static bool CanUseHotkey(Player player) => player.TakeInput() ? !player.InPlaceMode() : IsInventoryHotkeyAvailable();

        // Inventory screen blocks regular player input, loadout hotkeys still work there unless something else is in front of it
        private static bool IsInventoryHotkeyAvailable()
        {
            return hotkeysInInventory.Value && InventoryGui.IsVisible()
                && !global::Console.IsVisible() && !TextInput.IsVisible() && !Menu.IsVisible() && !Minimap.IsOpen()
                && (!Chat.instance || !Chat.instance.HasFocus());
        }

        // The click of a mouse hotkey must not pick up or use the item under the cursor as well
        internal static bool ShouldSuppressInventoryClick()
        {
            Player player = Player.m_localPlayer;
            return player && Loadouts.IsReady(player) && Loadouts.Count > 1 && IsInventoryHotkeyAvailable()
                && (IsMouseShortcutModifierHeld(previousLoadoutHotkey.Value) || IsMouseShortcutModifierHeld(nextLoadoutHotkey.Value));
        }

        private static bool IsShortcutDown(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None || !ZInput.GetKeyDown(shortcut.MainKey, logWarning: false))
                return false;

            return shortcut.Modifiers.All(modifier => ZInput.GetKey(modifier, logWarning: false));
        }

        private static bool IsMouseShortcutModifierHeld(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey < KeyCode.Mouse0 || shortcut.MainKey > KeyCode.Mouse6 || !shortcut.Modifiers.Any())
                return false;

            return shortcut.Modifiers.All(modifier => ZInput.GetKey(modifier, logWarning: false));
        }

        internal static bool ShouldSuppressMouse(Player player)
        {
            return suppressMouseWhileModifierHeld.Value
                && Loadouts.IsReady(player) && Loadouts.Count > 1
                && !player.InPlaceMode()
                && (IsMouseShortcutModifierHeld(previousLoadoutHotkey.Value) || IsMouseShortcutModifierHeld(nextLoadoutHotkey.Value));
        }

        internal static void LogInfo(object data) => logger.LogInfo(data);
        internal static void LogWarning(object data) => logger.LogWarning(data);
        internal static void LogError(object data) => logger.LogError(data);

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.UpdateTotalWeight))]
        private static class Inventory_UpdateTotalWeight_AddStashedLoadouts
        {
            private static void Prefix(Inventory __instance)
            {
                Player player = Player.m_localPlayer;
                Loadouts.weightless = player && __instance == player.m_inventory && Loadouts.BeginWeightCalculation(player);
            }

            private static void Postfix(Inventory __instance)
            {
                Player player = Player.m_localPlayer;
                if (!player || __instance != player.m_inventory)
                    return;

                __instance.m_totalWeight += Loadouts.GetStashedWeight(player);
            }

            private static System.Exception Finalizer(System.Exception __exception)
            {
                Loadouts.weightless = false;
                return __exception;
            }
        }

        // Items in the Extra Slots panel weigh nothing in the total while a carry weight belt is worn
        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetWeight))]
        private static class ItemData_GetWeight_BeltCancelsWeight
        {
            private static void Postfix(ItemDrop.ItemData __instance, ref float __result)
            {
                if (Loadouts.weightless && ExtraSlots.API.IsGridPositionASlot(__instance.m_gridPos))
                    __result = 0f;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Save))]
        private static class Player_Save_SaveLoadouts
        {
            private static void Prefix(Player __instance) => Loadouts.Save(__instance);
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Load))]
        private static class Player_Load_ReloadLoadouts
        {
            private static void Postfix(Player __instance) => Loadouts.Invalidate(__instance);
        }

        [HarmonyPatch]
        private static class InventoryGrid_OnDown_SuppressClickOnHotkey
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(InventoryGrid), nameof(InventoryGrid.OnLeftDown));
                yield return AccessTools.Method(typeof(InventoryGrid), nameof(InventoryGrid.OnRightDown));
            }

            private static bool Prefix() => !ShouldSuppressInventoryClick();
        }

        // Only the repair button of the crafting panel asks for worn items
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.GetWornItems))]
        private static class Inventory_GetWornItems_AddStashedLoadouts
        {
            private static void Postfix(Inventory __instance, List<ItemDrop.ItemData> worn)
            {
                Player player = Player.m_localPlayer;
                if (!repairStoredLoadouts.Value || !player || __instance != player.m_inventory)
                    return;

                Loadouts.AddStashedWornItems(player, worn);
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipeList))]
        private static class InventoryGui_UpdateRecipeList_ListStashedLoadouts
        {
            private static void Prefix() => Loadouts.listingUpgrades = true;

            private static System.Exception Finalizer(System.Exception __exception)
            {
                Loadouts.listingUpgrades = false;
                return __exception;
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.GetAllItems), typeof(string), typeof(List<ItemDrop.ItemData>))]
        private static class Inventory_GetAllItems_AddStashedLoadouts
        {
            private static void Postfix(Inventory __instance, string name, List<ItemDrop.ItemData> items)
            {
                Player player = Player.m_localPlayer;
                if (!Loadouts.listingUpgrades || !upgradeStoredLoadouts.Value || !player || __instance != player.m_inventory)
                    return;

                Loadouts.AddStashedItemsByName(player, name, items);
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
        private static class InventoryGui_DoCrafting_UpgradeStashedItem
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(InventoryGui __instance, Player player)
            {
                if (upgradeStoredLoadouts.Value && player && player == Player.m_localPlayer)
                    Loadouts.BeginStashedUpgrade(player, __instance.m_craftUpgradeItem);
            }

            [HarmonyPriority(Priority.Last)]
            private static System.Exception Finalizer(Player player, System.Exception __exception)
            {
                if (player)
                    Loadouts.EndStashedUpgrade(player);

                return __exception;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.CreateTombStone))]
        private static class Player_CreateTombStone_DropStashedLoadouts
        {
            private static void Postfix(Player __instance)
            {
                if (__instance == Player.m_localPlayer)
                    Loadouts.DropStashedOnDeath(__instance);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
        private static class Player_SetControls_SuppressMouseOnHotkey
        {
            private static void Prefix(Player __instance, ref bool attack, ref bool attackHold, ref bool block, ref bool blockHold)
            {
                if (__instance != Player.m_localPlayer || !ShouldSuppressMouse(__instance))
                    return;

                attack = false;
                attackHold = false;
                block = false;
                blockHold = false;
            }
        }
    }
}
