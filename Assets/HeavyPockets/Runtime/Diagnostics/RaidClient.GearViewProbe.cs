#if DEVELOPMENT_BUILD || UNITY_EDITOR
using UnityEngine;

namespace HeavyPockets
{
    public sealed partial class RaidClient
    {
        internal LootItem ProbeGearItem(GearLocation slot)
        {
            var gear = InventoryState().Gear;
            return GearCatalog.IsSlot(slot) && gear != null && (int)slot < gear.Length ? gear[(int)slot] : default;
        }

        internal GearLocation ProbeActiveGear => InventoryState().ActiveGear;
        internal bool ProbeTipVisible => hud.ProbeTipVisible;
        internal bool ProbeAltEquip(bool bag, LootItem item) => ScriptedInput && inventoryOpen &&
            hud.ProbePointer(InventoryState(), bag, item.X + .5f, item.Y + .5f, EventType.MouseDown, false, true) &&
            !hud.IsHoldingItem;
        internal bool ProbeGearPointer(GearLocation slot, EventType type, bool ctrl = false) =>
            ScriptedInput && inventoryOpen && hud.ProbeGearPointer(InventoryState(), slot, type, ctrl);
        internal bool ProbeGearDropToGrid(bool bag, int x, int y, EventType type) =>
            ProbeTargetCell(bag, x, y, type);
        internal void ProbeGearHover(GearLocation slot)
        {
            if (ScriptedInput && inventoryOpen) hud.ProbeGearHover(InventoryState(), slot);
        }
    }
}
#endif
