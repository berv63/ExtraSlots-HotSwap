using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ExtraSlotsHotSwap
{
    internal class LoadoutButton : MonoBehaviour, IPointerClickHandler
    {
        internal Action onLeftClick;
        internal Action onRightClick;

        public void OnPointerClick(PointerEventData eventData)
        {
            // That click already was a loadout hotkey
            if (HotSwapPlugin.ShouldSuppressInventoryClick())
                return;

            if (eventData.button == PointerEventData.InputButton.Left)
                onLeftClick?.Invoke();
            else if (eventData.button == PointerEventData.InputButton.Right)
                onRightClick?.Invoke();
        }
    }

    internal static class LoadoutBar
    {
        private const string BarName = "ExtraSlotsHotSwapLoadoutBar";
        private const float buttonSpace = 6f;

        private static readonly Color activeColor = new Color(1f, 0.72f, 0.36f, 1f);
        private static readonly Color inactiveColor = new Color(1f, 1f, 1f, 0.9f);
        private static readonly Color activeTextColor = new Color(1f, 0.85f, 0.45f, 1f);
        private static readonly Color inactiveTextColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        private static readonly Color plainBackground = new Color(0f, 0f, 0f, 0.6f);

        private static RectTransform root;
        private static bool isDirty = true;

        internal static void MarkDirty() => isDirty = true;

        internal static void Destroy()
        {
            if (root)
                UnityEngine.Object.Destroy(root.gameObject);

            root = null;
        }

        // Runs every frame, the bar is attached to Extra Slots equipment panel so it follows panel position
        internal static void Update(Player player)
        {
            if (!InventoryGui.instance || !InventoryGui.IsVisible())
                return;

            RectTransform panel = ExtraSlots.EquipmentPanel.equipmentBackground;
            if (!panel)
                return;

            if (!root)
            {
                root = new GameObject(BarName, typeof(RectTransform)).GetComponent<RectTransform>();
                root.gameObject.layer = panel.gameObject.layer;
                root.SetParent(panel, worldPositionStays: false);
                root.anchorMin = Vector2.zero;
                root.anchorMax = Vector2.zero;
                root.pivot = new Vector2(0f, 1f);
                isDirty = true;
            }

            bool ready = Loadouts.IsReady(player);
            if (root.gameObject.activeSelf != ready)
                root.gameObject.SetActive(ready);

            if (!ready || !isDirty)
                return;

            isDirty = false;
            Rebuild();
        }

        private static void Rebuild()
        {
            for (int i = root.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(root.GetChild(i).gameObject);

            float size = HotSwapPlugin.buttonSize.Value;
            int count = Loadouts.Count;
            bool canAdd = count < HotSwapPlugin.maxLoadouts.Value;

            root.anchoredPosition = new Vector2(HotSwapPlugin.barOffset.Value.x, -HotSwapPlugin.barOffset.Value.y);
            bool canRemove = count > 1;

            root.sizeDelta = new Vector2((count + (canAdd ? 1 : 0) + (canRemove ? 1 : 0)) * (size + buttonSpace), size);

            for (int i = 0; i < count; i++)
            {
                int index = i;
                AddButton(i, (i + 1).ToString(), size, i == Loadouts.Active,
                    () => Loadouts.SwitchTo(Player.m_localPlayer, index),
                    () => Loadouts.RemoveLoadout(Player.m_localPlayer, index));
            }

            if (canAdd)
                AddButton(count, "+", size, false, () => Loadouts.AddLoadout(Player.m_localPlayer), null);

            // Removes the active loadout, its items go back to the regular inventory
            if (canRemove)
                AddButton(count + (canAdd ? 1 : 0), "-", size, false, () => Loadouts.RemoveLoadout(Player.m_localPlayer, Loadouts.Active), null);
        }

        private static void AddButton(int position, string label, float size, bool isActive, Action onLeftClick, Action onRightClick)
        {
            RectTransform rect = new GameObject($"Loadout{label}", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = root.gameObject.layer;
            rect.SetParent(root, worldPositionStays: false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = Vector2.one * size;
            rect.anchoredPosition = new Vector2(position * (size + buttonSpace), 0f);

            Image image = rect.gameObject.AddComponent<Image>();
            Sprite sprite = GetSlotSprite();
            if (sprite)
            {
                image.sprite = sprite;
                image.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
                image.color = isActive ? activeColor : inactiveColor;
            }
            else
            {
                image.color = isActive ? new Color(activeColor.r * 0.6f, activeColor.g * 0.6f, activeColor.b * 0.6f, 0.9f) : plainBackground;
            }

            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            LoadoutButton handler = rect.gameObject.AddComponent<LoadoutButton>();
            handler.onLeftClick = onLeftClick;
            handler.onRightClick = onRightClick;

            TMP_Text template = InventoryGui.instance.m_weight;
            if (!template)
                return;

            TMP_Text text = UnityEngine.Object.Instantiate(template, rect);
            text.name = "Label";
            text.gameObject.SetActive(true);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.pivot = Vector2.one * 0.5f;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            text.rectTransform.localScale = Vector3.one;
            text.rectTransform.localRotation = Quaternion.identity;
            text.enableAutoSizing = false;
            text.fontSize = Mathf.Round(size * 0.5f);
            text.alignment = TextAlignmentOptions.Center;
            text.overflowMode = TextOverflowModes.Overflow;
            text.margin = Vector4.zero;
            text.raycastTarget = false;
            text.color = isActive ? activeTextColor : inactiveTextColor;
            text.text = label;
        }

        private static Sprite GetSlotSprite()
        {
            GameObject element = InventoryGui.instance.m_playerGrid ? InventoryGui.instance.m_playerGrid.m_elementPrefab : null;
            Image image = element ? element.GetComponent<Image>() : null;
            return image ? image.sprite : null;
        }
    }
}
