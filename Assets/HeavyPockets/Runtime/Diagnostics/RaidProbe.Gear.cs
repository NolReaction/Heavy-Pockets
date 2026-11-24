#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace HeavyPockets
{
    public sealed partial class RaidProbe
    {
        private LootItem[] gearSlots = Array.Empty<LootItem>();
        private GearLocation activeSlot;
        private int gearDenials;

        private IEnumerator ChooseSlot(int key, GearLocation target)
        {
            Client.SelectGearKey(key);
            yield return new WaitUntil(() => activeSlot == target);
            yield return new WaitForSecondsRealtime(.20f);
        }

        private bool DropOnEquipment(LootItem item, GearLocation from, GearLocation to, bool turn = false)
        {
            bool fromGrid = from == GearLocation.Crate || from == GearLocation.Bag;
            bool pressed = fromGrid
                ? Client.ProbeCellInput(from == GearLocation.Bag, item.X, item.Y, EventType.MouseDown)
                : Client.ProbeGearPointer(from, EventType.MouseDown);
            return pressed && (!turn || Client.ProbeRotateHeld()) && Client.ProbeGearPointer(to, EventType.MouseDrag) &&
                Client.ProbeGearPointer(to, EventType.MouseUp) && Client.ProbeEquipmentState.Gear[(int)to].Id == item.Id;
        }

        private IEnumerator IntoEquipment(LootItem item, GearLocation from, GearLocation to)
        {
            Phase("dragging " + item.Type + " from " + from + " to " + to);
            if (!DropOnEquipment(item, from, to))
            { Fail("Equipment drop was missing from its new slot on release"); yield break; }
            yield return new WaitUntil(() => gearSlots[(int)to].Id == item.Id &&
                !bag.Any(i => i.Id == item.Id) && !crate.Any(i => i.Id == item.Id));
            yield return new WaitForSecondsRealtime(.21f);
        }


        private IEnumerator IntoBag(GearLocation slot, int x, int y)
        {
            var item = gearSlots[(int)slot];
            Phase("dragging " + slot + " into bag");
            if (!Client.ProbeGearPointer(slot, EventType.MouseDown) ||
                !Client.ProbeTargetCell(true, x, y, EventType.MouseDrag) ||
                !Client.ProbeTargetCell(true, x, y, EventType.MouseUp) ||
                !Client.ProbeVisiblePlacement(item.Id, true, x, y, false))
            { Fail("Equipment did not immediately settle into the bag: " + slot); yield break; }
            yield return new WaitUntil(() => gearSlots[(int)slot].Id == 0 && At(bag, item.Id, x, y, false));
            yield return new WaitForSecondsRealtime(.21f);
        }

        private IEnumerator FillEquipment()
        {
            Phase("checking issued equipment");
            yield return new WaitUntil(() => gearSlots.Length == GearCatalog.Slots);
            for (int slot = (int)GearLocation.Primary; slot <= (int)GearLocation.Pouch; slot++)
                if (gearSlots[slot].Id == 0 || !GearCatalog.Accepts((GearLocation)slot, gearSlots[slot].Type) ||
                    string.IsNullOrEmpty(LootCatalog.Description(gearSlots[slot].Type)))
                { Fail("Missing issued equipment or item description in slot " + slot); yield break; }
            for (var slot = GearLocation.Primary; slot <= GearLocation.Pouch; slot++)
                for (byte type = 1; type <= LootCatalog.TypeCount; type++)
                {
                    if (!GearCatalog.Accepts(slot, type)) continue;
                    var rarity = LootCatalog.Rarity(type);
                    if (rarity < LootRarity.Rare || rarity > LootRarity.Legendary)
                    { Fail("Equipment outside the three supported rarity tiers: " + type); yield break; }
                }
            if (GearCatalog.PouchLevel(gearSlots) != 2 || GearCatalog.BagRows(gearSlots) != 6 ||
                GearCatalog.Available(GearLocation.PouchCell3, gearSlots))
            { Fail("Base loadout was not carried into the new raid"); yield break; }
            yield return ChooseSlot(2, GearLocation.Secondary);
            yield return ChooseSlot(1, GearLocation.Primary);

            var cargo = crate.Where(i => GearCatalog.Accepts(GearLocation.PouchCell, i.Type))
                .Where(i => LootCatalog.Width(i.Type, false) * LootCatalog.Height(i.Type, false) <= 2)
                .OrderByDescending(i => LootCatalog.Width(i.Type, false) * LootCatalog.Height(i.Type, false)).First();
            bool turn = LootCatalog.Height(cargo.Type, cargo.Rotated) > 1;
            if (DropOnEquipment(cargo, GearLocation.Crate, GearLocation.PouchCell3, turn))
            { Fail("A locked pouch pocket accepted an item"); yield break; }
            Client.ProbeCancelDrag();
            int denials = gearDenials;
            link.MoveGear(cargo.Id, GearLocation.Crate, GearLocation.PouchCell3);
            yield return new WaitUntil(() => gearDenials == denials + 1);
            link.MoveGear(gearSlots[(int)GearLocation.Pouch].Id, GearLocation.Pouch, GearLocation.Bag);
            yield return new WaitUntil(() => gearDenials == denials + 2);
            yield return new WaitForSecondsRealtime(.35f);
            Phase("two independent drops before either acknowledgement");
            var regular = gearSlots[(int)GearLocation.Backpack];
            var expedition = crate.Single(i => i.Type == GearCatalog.ExpeditionPack);
            if (!DropOnEquipment(cargo, GearLocation.Crate, GearLocation.PouchCell, turn) ||
                !Client.ProbeAltEquip(false, expedition) || Client.ProbeGearItem(GearLocation.Backpack).Id != expedition.Id)
            { Fail("Pouch drop or simultaneous Alt equip was lost"); yield break; }
            yield return new WaitUntil(() => gearSlots[(int)GearLocation.PouchCell].Id == cargo.Id &&
                gearSlots[(int)GearLocation.Backpack].Id == expedition.Id && crate.All(i => i.Id != expedition.Id));
            bool wide = LootCatalog.Width(cargo.Type, cargo.Rotated ^ turn) == 2;
            if (gearSlots[(int)GearLocation.PouchCell].Rotated != (cargo.Rotated ^ turn) ||
                (wide && GearCatalog.PouchOwner(GearLocation.PouchCell2, gearSlots) != GearLocation.PouchCell))
            { Fail("Authoritative pouch footprint differed from the preview"); yield break; }
            yield return new WaitForSecondsRealtime(.21f);
            yield return ChooseSlot(3, GearLocation.PouchCell);
            yield return ChooseSlot(wide ? 4 : 3, GearLocation.PouchCell);

            var primary = gearSlots[(int)GearLocation.Primary];
            var secondary = gearSlots[(int)GearLocation.Secondary];
            yield return IntoEquipment(primary, GearLocation.Primary, GearLocation.Secondary);
            if (gearSlots[(int)GearLocation.Primary].Id != secondary.Id)
            { Fail("Weapon swap lost the displaced weapon"); yield break; }
            yield return IntoEquipment(primary, GearLocation.Secondary, GearLocation.Primary);

            Phase("expanding the existing inventory");
            if (GearCatalog.BagRows(gearSlots) != 9 || !crate.Any(i => i.Id == regular.Id))
            { Fail("Backpack swap failed to extend the existing grid or return the field pack"); yield break; }
            yield return IntoBag(GearLocation.Secondary, 0, 8);
            var field = crate.Single(i => i.Id == regular.Id);
            if (DropOnEquipment(field, GearLocation.Crate, GearLocation.Backpack))
            { Fail("A smaller backpack hid an occupied row"); yield break; }
            Client.ProbeCancelDrag();
            int spaceDenials = noSpace;
            link.MoveGear(field.Id, GearLocation.Crate, GearLocation.Backpack);
            yield return new WaitUntil(() => noSpace > spaceDenials);
            if (gearSlots[(int)GearLocation.Backpack].Id != expedition.Id || !At(bag, secondary.Id, 0, 8, false))
            { Fail("Denied backpack swap changed cargo or equipment"); yield break; }
            Capture("gear-crate");
            yield return new WaitForSecondsRealtime(.22f);
            if (!Client.ProbeAltEquip(true, bag.Single(i => i.Id == secondary.Id)))
            { Fail("Alt equip from bag started an ordinary drag"); yield break; }
            yield return new WaitUntil(() => gearSlots[(int)GearLocation.Secondary].Id == secondary.Id);
            yield return IntoEquipment(field, GearLocation.Crate, GearLocation.Backpack);
            yield return IntoBag(GearLocation.Backpack, 0, 0);
            if (GearCatalog.BagRows(gearSlots) != 4)
            { Fail("Removing the pack did not leave the base inventory"); yield break; }
            Capture("empty-backpack");
            yield return new WaitForSecondsRealtime(.22f);
            yield return IntoEquipment(bag.Single(i => i.Id == regular.Id), GearLocation.Bag, GearLocation.Backpack);
        }

        private IEnumerator ChangeArmor()
        {
            var armor = gearSlots[(int)GearLocation.Armor];
            yield return IntoBag(GearLocation.Armor, 0, 0);
            Capture("empty-armor");
            yield return new WaitForSecondsRealtime(.22f);
            yield return IntoEquipment(bag.Single(i => i.Id == armor.Id), GearLocation.Bag, GearLocation.Armor);
            yield return CaptureEquipment();
            Client.ToggleInventory();
            yield return ChooseSlot(2, GearLocation.Secondary);
            yield return ChooseSlot(3, GearLocation.PouchCell);
            Capture("gear-quick");
            yield return new WaitForSecondsRealtime(.22f);
            Signal("gear", "Swapped both weapons, armor and backpacks. Verified capacity changes, protected occupied row and locked pouch cells.");
        }
    }
}
#endif
