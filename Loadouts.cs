using System;
using System.Collections.Generic;
using System.Linq;
using ESAPI = ExtraSlots.API;
using Slot = ExtraSlots.Slots.Slot;

namespace ExtraSlotsHotSwap
{
    internal sealed class StoredItem
    {
        internal readonly string SlotID;
        internal readonly bool Equipped;
        internal readonly ItemDrop.ItemData Item;

        internal StoredItem(string slotID, bool equipped, ItemDrop.ItemData item)
        {
            SlotID = slotID ?? "";
            Equipped = equipped;
            Item = item;
        }
    }

    internal sealed class Loadout
    {
        // Items of the active loadout live in the Extra Slots panel, so this list is only
        // filled for stashed loadouts (and for items that could not be put back into the panel).
        internal readonly List<StoredItem> Items = new List<StoredItem>();
    }

    internal static class Loadouts
    {
        // Player custom-data key. Format: int version, int active index, int loadout count, then for each loadout
        // int item count, (string slot id, bool equipped) per item, and a ZPackage holding a vanilla Inventory.Save
        // where item index is stored as grid position y.
        public const string CustomDataKey = "ExtraSlotsHotSwap";
        private const int DataVersion = 1;

        private static readonly List<Loadout> loadouts = new List<Loadout>();
        private static int active;
        private static float stashedWeight;

        private static Player owner;
        private static bool unreadable;
        private static bool busy;

        internal static int Count => loadouts.Count;
        internal static int Active => active;

        internal static bool IsReady(Player player) => player != null && ReferenceEquals(owner, player) && !unreadable && loadouts.Count > 0;

        internal static bool HasStashedItems(int index) => index >= 0 && index < loadouts.Count && loadouts[index].Items.Count > 0;

        internal static float GetStashedWeight(Player player) => ReferenceEquals(owner, player) ? stashedWeight : 0f;

        internal static void Invalidate(Player player)
        {
            if (ReferenceEquals(owner, player))
                owner = null;
        }

        internal static void EnsureLoaded(Player player)
        {
            if (ReferenceEquals(owner, player) || busy || player.m_isLoading || !ObjectDB.instance)
                return;

            busy = true;
            try
            {
                owner = player;
                unreadable = false;
                loadouts.Clear();
                active = 0;

                if (player.m_customData.TryGetValue(CustomDataKey, out string data) && !string.IsNullOrEmpty(data))
                {
                    try
                    {
                        Deserialize(data);
                    }
                    catch (Exception e)
                    {
                        // Saved data stays untouched in player custom data, nothing is written until it can be read again
                        unreadable = true;
                        loadouts.Clear();
                        HotSwapPlugin.LogError($"Saved loadouts could not be read, loadouts are disabled for this character to keep the saved data intact.\n{e}");
                    }
                }

                if (!unreadable && loadouts.Count == 0)
                    loadouts.Add(new Loadout());

                active = Math.Max(0, Math.Min(active, loadouts.Count - 1));
            }
            finally
            {
                busy = false;
            }

            UpdateStashedWeight();
            player.m_inventory.UpdateTotalWeight();
            LoadoutBar.MarkDirty();

            if (!unreadable)
                HotSwapPlugin.LogInfo($"Loaded {loadouts.Count} loadout(s), {loadouts.Sum(loadout => loadout.Items.Count)} stashed item(s), active loadout {active + 1}");
        }

        internal static void Save(Player player)
        {
            if (!IsReady(player))
                return;

            try
            {
                if (loadouts.Count == 1 && loadouts[0].Items.Count == 0)
                    player.m_customData.Remove(CustomDataKey);
                else
                    player.m_customData[CustomDataKey] = Serialize();
            }
            catch (Exception e)
            {
                HotSwapPlugin.LogError($"Loadouts could not be saved, previously saved data was kept.\n{e}");
            }
        }

        internal static void Cycle(Player player, int direction)
        {
            if (!IsReady(player) || loadouts.Count < 2)
                return;

            SwitchTo(player, (active + direction + loadouts.Count) % loadouts.Count);
        }

        internal static bool AddLoadout(Player player)
        {
            if (!IsReady(player))
                return false;

            if (loadouts.Count >= HotSwapPlugin.maxLoadouts.Value)
            {
                Message(player, $"Maximum of {HotSwapPlugin.maxLoadouts.Value} loadouts reached");
                return false;
            }

            loadouts.Add(new Loadout());
            if (!SwitchTo(player, loadouts.Count - 1))
            {
                loadouts.RemoveAt(loadouts.Count - 1);
                return false;
            }

            return true;
        }

        internal static bool RemoveLoadout(Player player, int index)
        {
            if (!IsReady(player) || index < 0 || index >= loadouts.Count)
                return false;

            if (loadouts.Count < 2)
            {
                Message(player, "The last loadout can't be removed");
                return false;
            }

            bool isActive = index == active;
            if (isActive && !CanSwitch(player))
            {
                Message(player, "Can't remove loadout right now");
                return false;
            }

            Inventory inventory = player.m_inventory;
            Loadout loadout = loadouts[index];
            loadout.Items.RemoveAll(stored => stored.Item?.m_shared == null);

            // Items of the active loadout are in the panel, they move to the regular inventory along with anything left stashed
            List<StoredItem> current = isActive ? CollectPanelItems(player, inventory) : new List<StoredItem>();

            List<Vector2i> free = GetFreeRegularPositions(inventory);
            int returned = current.Count + loadout.Items.Count;
            if (free.Count < returned)
            {
                Message(player, $"Loadout {index + 1} holds {returned} items, free up {returned - free.Count} more inventory slots to remove it");
                return false;
            }

            foreach (StoredItem stored in current)
                player.UnequipItem(stored.Item, triggerEquipEffects: false);

            int cell = 0;
            foreach (StoredItem stored in current)
            {
                stored.Item.m_gridPos = free[cell++];
                stored.Item.m_equipped = false;
            }

            while (loadout.Items.Count > 0)
            {
                ItemDrop.ItemData item = loadout.Items[0].Item;
                loadout.Items.RemoveAt(0);
                Place(inventory, item, free[cell++]);
            }

            loadouts.RemoveAt(index);

            // The previous loadout takes over the panel when the active one is removed
            List<ItemDrop.ItemData> toEquip = new List<ItemDrop.ItemData>();
            if (isActive)
            {
                active = Math.Max(0, index - 1);
                RestoreItems(inventory, loadouts[active], toEquip);
            }
            else if (index < active)
            {
                active--;
            }

            UpdateStashedWeight();
            inventory.Changed();

            foreach (ItemDrop.ItemData item in toEquip)
                player.EquipItem(item, triggerEquipEffects: false);

            LoadoutBar.MarkDirty();

            string switched = isActive ? $", switched to loadout {active + 1}" : "";
            Message(player, returned > 0 ? $"Loadout {index + 1} removed, {returned} items returned to inventory{switched}" : $"Loadout {index + 1} removed{switched}");
            return true;
        }

        internal static bool SwitchTo(Player player, int target)
        {
            if (!IsReady(player) || target < 0 || target >= loadouts.Count || target == active)
                return false;

            if (!CanSwitch(player))
            {
                Message(player, "Can't switch loadout right now");
                return false;
            }

            Inventory inventory = player.m_inventory;
            Loadout outgoing = loadouts[active];
            Loadout incoming = loadouts[target];

            // Collect first, equipment changes must not interfere with reading the panel
            List<StoredItem> current = CollectPanelItems(player, inventory);

            foreach (StoredItem stored in current)
                player.UnequipItem(stored.Item, triggerEquipEffects: false);

            // Every item is always either in player inventory or in a loadout, never in both and never in neither
            foreach (StoredItem stored in current)
                if (inventory.m_inventory.Remove(stored.Item))
                {
                    stored.Item.m_equipped = false;
                    outgoing.Items.Add(stored);
                }

            List<ItemDrop.ItemData> toEquip = new List<ItemDrop.ItemData>();
            int misplaced = RestoreItems(inventory, incoming, toEquip);

            int left = incoming.Items.Count;
            active = target;

            UpdateStashedWeight();
            inventory.Changed();

            foreach (ItemDrop.ItemData item in toEquip)
                player.EquipItem(item, triggerEquipEffects: false);

            LoadoutBar.MarkDirty();

            if (left > 0)
                Message(player, $"Loadout {active + 1}: {left} items did not fit and stay stashed in the loadout");
            else if (misplaced > 0)
                Message(player, $"Loadout {active + 1}: {misplaced} items were put into the inventory because their slot is unavailable");
            else if (HotSwapPlugin.showSwitchMessage.Value)
                Message(player, $"Loadout {active + 1}");

            return true;
        }

        // Vanilla tombstone only takes the player inventory, stashed loadouts get tombstones of their own
        internal static void DropStashedOnDeath(Player player)
        {
            if (!HotSwapPlugin.dropAllLoadoutsOnDeath.Value || !IsReady(player) || !player.m_tombstone)
                return;

            // No tombstone is created with this world modifier, items stay with the character
            if (ZoneSystem.instance.GetGlobalKey(GlobalKeys.DeathKeepInventory))
                return;

            int dropped = 0;
            try
            {
                foreach (Loadout loadout in loadouts)
                    loadout.Items.RemoveAll(stored => stored.Item?.m_shared == null);

                PlayerProfile profile = Game.instance.GetPlayerProfile();
                while (loadouts.Any(loadout => loadout.Items.Count > 0))
                {
                    UnityEngine.GameObject obj = UnityEngine.Object.Instantiate(player.m_tombstone, player.GetCenterPoint(), player.transform.rotation);
                    Inventory grave = obj.GetComponent<Container>()?.GetInventory();

                    // Only regular rows are used so recovered items never land in a slot they do not fit
                    int width = grave != null ? grave.GetWidth() : 0;
                    int cells = grave != null ? width * Math.Min(grave.GetHeight(), ESAPI.GetInventoryHeightPlayer()) : 0;
                    if (cells < 1)
                    {
                        ZNetScene.instance.Destroy(obj);
                        HotSwapPlugin.LogError("Tombstone has no usable inventory, stashed loadouts stay with the character.");
                        break;
                    }

                    // Every item is always either in a loadout or in a tombstone, never in both and never in neither
                    int cell = 0;
                    foreach (Loadout loadout in loadouts)
                        while (loadout.Items.Count > 0 && cell < cells)
                        {
                            ItemDrop.ItemData item = loadout.Items[0].Item;
                            loadout.Items.RemoveAt(0);
                            Place(grave, item, new Vector2i(cell % width, cell / width));
                            cell++;
                            dropped++;
                        }

                    grave.Changed();
                    obj.GetComponent<TombStone>().Setup(profile.GetName(), profile.GetPlayerID());
                }
            }
            catch (Exception e)
            {
                HotSwapPlugin.LogError($"Stashed loadouts could not be dropped on death, remaining items stay with the character.\n{e}");
            }

            if (dropped == 0)
                return;

            UpdateStashedWeight();
            player.m_inventory.UpdateTotalWeight();
            LoadoutBar.MarkDirty();
            HotSwapPlugin.LogInfo($"Dropped {dropped} stashed item(s) on death");
        }

        private static bool CanSwitch(Player player)
        {
            if (player.IsDead() || player.IsTeleporting() || player.InAttack() || player.InDodge())
                return false;

            // Vanilla refuses to equip items in the same conditions
            if (player.IsSwimming() && !player.IsOnGround())
                return false;

            return !InventoryGui.instance || !InventoryGui.instance.m_dragGo;
        }

        private static List<StoredItem> CollectPanelItems(Player player, Inventory inventory)
        {
            List<StoredItem> current = new List<StoredItem>();
            foreach (Slot slot in ESAPI.GetExtraSlots())
            {
                if (slot == null || slot.IsEmptySlot || IsShared(slot) || !ESAPI.IsGridPositionASlot(slot.GridPosition))
                    continue;

                ItemDrop.ItemData item = inventory.GetItemAt(slot.GridPosition.x, slot.GridPosition.y);
                if (item != null && !current.Any(stored => ReferenceEquals(stored.Item, item)))
                    current.Add(new StoredItem(slot.ID, player.IsItemEquiped(item), item));
            }

            return current;
        }

        // Moves loadout items into the panel, returns how many had to go to the regular inventory instead
        private static int RestoreItems(Inventory inventory, Loadout incoming, List<ItemDrop.ItemData> toEquip)
        {
            int misplaced = 0;
            foreach (StoredItem stored in incoming.Items.ToList())
            {
                if (stored.Item?.m_shared == null)
                {
                    incoming.Items.Remove(stored);
                    continue;
                }

                Vector2i position = FindSlotPosition(inventory, stored);
                if (position.x < 0)
                {
                    position = GetFreeRegularPositions(inventory).DefaultIfEmpty(new Vector2i(-1, -1)).First();
                    if (position.x < 0)
                        continue; // stays in the loadout until there is a place for it

                    misplaced++;
                }

                incoming.Items.Remove(stored);
                Place(inventory, stored.Item, position);
                if (stored.Equipped)
                    toEquip.Add(stored.Item);
            }

            return misplaced;
        }

        // Items in shared slots are not part of any loadout and stay in the panel on switch
        private static bool IsShared(Slot slot)
        {
            return HotSwapPlugin.shareAmmo.Value && slot.IsAmmoSlot
                || HotSwapPlugin.shareFood.Value && slot.IsFoodSlot
                || HotSwapPlugin.shareQuickSlots.Value && slot.IsQuickSlot;
        }

        private static Vector2i FindSlotPosition(Inventory inventory, StoredItem stored)
        {
            Slot slot = string.IsNullOrEmpty(stored.SlotID) ? null : ESAPI.FindSlot(stored.SlotID);
            if (slot == null || slot.IsEmptySlot || !slot.IsActive || !slot.ItemFits(stored.Item) || !ESAPI.IsGridPositionASlot(slot.GridPosition))
                return new Vector2i(-1, -1);

            Vector2i position = slot.GridPosition;
            return inventory.GetItemAt(position.x, position.y) == null ? position : new Vector2i(-1, -1);
        }

        private static List<Vector2i> GetFreeRegularPositions(Inventory inventory)
        {
            List<Vector2i> positions = new List<Vector2i>();
            int height = Math.Min(ESAPI.GetInventoryHeightPlayer(), inventory.GetHeight());
            for (int y = 0; y < height; y++)
                for (int x = 0; x < inventory.GetWidth(); x++)
                    if (inventory.GetItemAt(x, y) == null)
                        positions.Add(new Vector2i(x, y));

            return positions;
        }

        private static void Place(Inventory inventory, ItemDrop.ItemData item, Vector2i position)
        {
            item.m_gridPos = position;
            item.m_equipped = false;
            inventory.m_inventory.Add(item);
        }

        private static void UpdateStashedWeight()
        {
            // Calculated from shared data as ItemData.GetWeight is altered by Extra Slots depending on item grid position
            stashedWeight = 0f;
            foreach (Loadout loadout in loadouts)
                foreach (StoredItem stored in loadout.Items)
                {
                    ItemDrop.ItemData item = stored.Item;
                    if (item?.m_shared == null)
                        continue;

                    float weight = item.m_shared.m_weight * item.m_stack;
                    if (item.m_shared.m_scaleWeightByQuality != 0f && item.m_quality != 1)
                        weight += weight * (item.m_quality - 1) * item.m_shared.m_scaleWeightByQuality;

                    stashedWeight += weight;
                }
        }

        private static void Message(Player player, string text) => player.Message(MessageHud.MessageType.TopLeft, text);

        private static string Serialize()
        {
            ZPackage pkg = new ZPackage();
            pkg.Write(DataVersion);
            pkg.Write(active);
            pkg.Write(loadouts.Count);
            foreach (Loadout loadout in loadouts)
            {
                List<StoredItem> items = loadout.Items.Where(stored => stored.Item?.m_shared != null && stored.Item.m_dropPrefab != null).ToList();

                Inventory inventory = new Inventory(CustomDataKey, null, 1, Math.Max(1, items.Count));
                pkg.Write(items.Count);
                for (int i = 0; i < items.Count; i++)
                {
                    pkg.Write(items[i].SlotID);
                    pkg.Write(items[i].Equipped);
                    items[i].Item.m_gridPos = new Vector2i(0, i);
                    inventory.m_inventory.Add(items[i].Item);
                }

                ZPackage itemsPkg = new ZPackage();
                inventory.Save(itemsPkg);
                pkg.Write(itemsPkg);
            }

            return pkg.GetBase64();
        }

        private static void Deserialize(string data)
        {
            ZPackage pkg = new ZPackage(data);
            int version = pkg.ReadInt();
            if (version != DataVersion)
                throw new InvalidOperationException($"Unsupported loadout data version {version}");

            int savedActive = pkg.ReadInt();
            int count = pkg.ReadInt();
            if (count < 0 || count > 64)
                throw new InvalidOperationException($"Invalid loadout count {count}");

            List<Loadout> loaded = new List<Loadout>();
            for (int l = 0; l < count; l++)
            {
                int itemsCount = pkg.ReadInt();
                if (itemsCount < 0 || itemsCount > 255)
                    throw new InvalidOperationException($"Invalid item count {itemsCount}");

                string[] slotIDs = new string[itemsCount];
                bool[] equipped = new bool[itemsCount];
                for (int i = 0; i < itemsCount; i++)
                {
                    slotIDs[i] = pkg.ReadString();
                    equipped[i] = pkg.ReadBool();
                }

                Inventory inventory = new Inventory(CustomDataKey, null, 1, Math.Max(1, itemsCount));
                inventory.Load(pkg.ReadPackage());

                // Items of removed mods have no prefab and are skipped by vanilla loading the same way as in player inventory
                Loadout loadout = new Loadout();
                foreach (ItemDrop.ItemData item in inventory.m_inventory.OrderBy(item => item.m_gridPos.y))
                {
                    int index = item.m_gridPos.y;
                    if (item.m_shared == null || index < 0 || index >= itemsCount)
                        continue;

                    item.m_equipped = false;
                    loadout.Items.Add(new StoredItem(slotIDs[index], equipped[index], item));
                }

                loaded.Add(loadout);
            }

            loadouts.AddRange(loaded);
            active = savedActive;
        }
    }
}
