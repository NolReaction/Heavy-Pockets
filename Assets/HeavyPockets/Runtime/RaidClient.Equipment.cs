using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeavyPockets
{
    public sealed partial class RaidClient
    {
        private LootItem[] equipment = new LootItem[GearCatalog.Slots];
        private GearLocation activeGear;
        private readonly GearPlacementView gearMoves = new GearPlacementView();

        private void ReceiveGear(RaidEvent message)
        {
            equipment = message.Gear;
            if (!extracted) basePouchLevel = (byte)GearCatalog.PouchLevel(equipment);
            activeGear = message.ActiveGear;
            gearMoves.ConfirmGear();
        }

        private void GearReply(RaidEvent message)
        {
            gearMoves.Reply(message.Operation, message.Status);
        }

        private void ClearGear()
        {
            equipment = new LootItem[GearCatalog.Slots];
            gearMoves.Reset();
            activeGear = 0;
        }

        private void SelectEquipment(GearLocation slot)
        {
            if (!HasOwnPose || disconnected || extracted || screen != RaidScreen.Play || !GearCatalog.Selectable(slot)) return;
            var gear = InventoryState().Gear;
            slot = GearCatalog.PouchOwner(slot, gear);
            var selected = gear[(int)slot];
            if (selected.Id == 0) return;
            if (inventoryOpen) hud.FocusGear(slot, selected);
            Link.SelectGear(slot);
        }

        internal void SelectGearKey(int digit)
        {
            switch (digit)
            {
                case 1: SelectEquipment(GearLocation.Primary); break;
                case 2: SelectEquipment(GearLocation.Secondary); break;
                case 3: SelectEquipment(GearLocation.PouchCell); break;
                case 4: SelectEquipment(GearLocation.PouchCell2); break;
            }
        }

        private void ReadGearKeys()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) SelectGearKey(1);
            else if (Input.GetKeyDown(KeyCode.Alpha2)) SelectGearKey(2);
            else if (Input.GetKeyDown(KeyCode.Alpha3)) SelectGearKey(3);
            else if (Input.GetKeyDown(KeyCode.Alpha4)) SelectGearKey(4);
        }

        internal void RequestGearMove(uint id, GearLocation from, GearLocation to, int x = -1, int y = -1, bool rotated = false)
        {
            if (!inventoryOpen || !HasOwnPose || disconnected || extracted || screen != RaidScreen.Play ||
                id == 0 ||
                (!GearCatalog.IsSlot(from) && !GearCatalog.IsSlot(to)) ||
                (byte)from > (byte)GearLocation.Crate || (byte)to > (byte)GearLocation.Crate ||
                ((from == GearLocation.Crate || to == GearLocation.Crate) && !crateOpen)) return;

            if (gearMoves.Count >= 8) return;
            var current = InventoryState();
            var source = GearCatalog.IsSlot(from) ? current.Gear[(int)from] :
                Array.Find(from == GearLocation.Bag ? current.BagItems : current.CrateItems, i => i.Id == id);
            if (source.Id != id) return;
            if (from == GearLocation.Pouch || to == GearLocation.Pouch) return;
            if (GearCatalog.IsSlot(to)) { x = y = -1; if (!GearCatalog.IsPouchCell(to)) rotated = false; }
            var status = GearCatalog.PlanMove(source, from, to, x, y, rotated, current.BagItems, current.CrateItems,
                current.Gear, current.Unknown, out var target, out var displaced);
            if (status != RaidStatus.Placed) return;
            uint operation = Link.MoveGear(id, from, to, x, y, rotated);
            if (operation == 0) return;
            gearMoves.Add(operation, from, to, target, displaced);
        }

        private RaidHud.State WithGear(RaidHud.State state)
        {
            state.Gear = equipment;
            state.ActiveGear = activeGear;
            gearMoves.Project(ref state.BagItems, ref state.CrateItems, ref state.Gear, state.Unknown, state.Crate);
            if (state.Gear[(int)activeGear].Id == 0) state.ActiveGear = 0;
            return state;
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        internal RaidHud.State ProbeEquipmentState => InventoryState();
#endif
    }
}
