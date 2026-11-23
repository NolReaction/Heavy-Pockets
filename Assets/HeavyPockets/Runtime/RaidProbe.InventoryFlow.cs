#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace HeavyPockets
{
    public sealed partial class RaidProbe
    {
        private bool freshBag;

        private IEnumerator TestPauseMenu()
        {
            Phase("pause menu and bag navigation");
            Client.EscapePressed();
            if (Client.ProbeScreen != RaidScreen.Menu || Client.MovementFromKeys(Vector2.up, true, true) != Vector2.zero)
            { Fail("Esc menu did not suspend movement"); yield break; }
            yield return new WaitForSecondsRealtime(.12f);
            Capture("menu");
            yield return new WaitForSecondsRealtime(.18f);
            if (!Client.ProbeMenuClick(RaidMenuAction.Bag) || !Client.ProbeInventoryOpen)
            { Fail("Menu did not open the player's bag"); yield break; }
            Client.EscapePressed();
            if (Client.ProbeInventoryOpen || Client.ProbeScreen != RaidScreen.Menu ||
                !Client.ProbeMenuClick(RaidMenuAction.Resume) || Client.ProbeScreen != RaidScreen.Play)
            { Fail("Esc from the bag or Resume did not return to the expected view"); yield break; }
            Client.EscapePressed();
            Client.EscapePressed();
            if (Client.ProbeScreen != RaidScreen.Play) { Fail("Esc did not resume"); yield break; }
            Signal("menu-ok");
        }

        private IEnumerator Replay()
        {
            Phase("return from the extraction receipt");
            yield return new WaitForSecondsRealtime(.25f);
            if (Client.ProbeScreen != RaidScreen.Receipt) { Fail("No extraction receipt"); yield break; }
            if (role == "B")
            {
                yield return new WaitUntil(() => Exists("A.next-looted"));
                if (Client.ProbeRound != 1 || Client.ProbeScreen != RaidScreen.Receipt)
                { Fail("Next round replaced B's receipt before the player chose to enter"); yield break; }
            }
            if (!Client.ProbeMenuClick(RaidMenuAction.Base) || Client.ProbeScreen != RaidScreen.Base)
            { Fail("Return button did not open the base"); yield break; }
            if (role == "A")
            {
                foreach (int level in new[] { 2, 3, 4, 1, 2 })
                {
                    if (!Client.ProbeMenuClick((RaidMenuAction)((int)RaidMenuAction.Pouch1 + level - 1)))
                    { Fail("Missing base pouch selector"); yield break; }
                    yield return new WaitUntil(() => Client.ProbeEquipmentState.BasePouchLevel == level);
                }
                Capture("pouch-base");
                yield return new WaitForSecondsRealtime(.18f);
            }
            if (!Client.ProbeMenuClick(RaidMenuAction.Join)) { Fail("Next raid button missing"); yield break; }
            if (role == "A")
            {
                yield return new WaitUntil(() => Client.ProbeQueued);
                Capture("base");
                yield return new WaitForSecondsRealtime(.22f);
                if (!Client.ProbeMenuClick(RaidMenuAction.CancelQueue)) { Fail("Cancel queue missing"); yield break; }
                yield return new WaitUntil(() => !Client.ProbeQueued && Client.ProbeCanJoin);
                if (!Client.ProbeMenuClick(RaidMenuAction.Join)) { Fail("Rejoining queue failed"); yield break; }
                yield return new WaitUntil(() => Client.ProbeQueued);
                Signal("waiting-next");
            }

            Phase("same connection enters fresh raid");
            yield return new WaitUntil(() => Client.ProbeRound == 2 && havePose && freshBag);
            if (Client.ProbeScreen != RaidScreen.Play || bag.Length != 0 || crateFrames != 0 ||
                Vector2.Distance(position, RaidMap.Spawn(role == "A" ? 0 : 1)) > .2f)
            { Fail("New round did not restore the expected spawn and empty inventory"); yield break; }
            if (role == "A")
            {
                yield return Walk(new Vector2(2.95f, -1f));
                yield return Walk(new Vector2(2.95f, 1.7f));
                yield return Walk(new Vector2(1.15f, 1.70f));
                Phase("cursor-only sweep in a fresh crate");
                link.Open();
                yield return new WaitUntil(() => crateFrames > 0);
                if (crate.Length != 0 || Client.ProbeSelectedItem != 0)
                { Fail("A fresh crate opened with a discovery or automatic selection"); yield break; }
                yield return new WaitUntil(() => searchComplete);
                yield return TestSweep();
                yield return FillEquipment();
                yield return WalkWithInventory();
                yield return ChangeArmor();
                Signal("next-looted");
            }
            Signal("new-raid");
            yield return new WaitUntil(() => Exists("A.new-raid") && Exists("B.new-raid"));
            yield return new WaitForSecondsRealtime(.22f);
        }

        private IEnumerator WalkWithInventory()
        {
            Phase("walking with a crate and a personal bag");
            Client.SetProbeMotion(Vector2.zero, 0f, 3f);
            Vector2 start = position;
            Vector2 right = Client.MovementFromKeys(Vector2.right, true, false);
            if (right != Vector2.right || Client.MovementFromKeys(Vector2.up, false, false) != Vector2.zero ||
                Mathf.Abs(Client.MovementFromKeys(Vector2.one, true, false).magnitude - 1f) > .001f)
            { Fail("Inventory keyboard direction, focus or diagonal speed is wrong"); yield break; }
            var item = crate[0];
            if (!Client.ProbeCellInput(false, item.X, item.Y, EventType.MouseDown))
            { Fail("Could not lift an item before walking"); yield break; }
            while (position.x < start.x + .26f)
            {
                Client.SetProbeMotion(Client.MovementFromKeys(Vector2.right, true, false), 0f, 3f);
                yield return new WaitForSecondsRealtime(.06f);
            }
            if (!Client.ProbeCrateOpen || !Client.ProbeInventoryOpen ||
                !Client.ProbeCellInput(false, item.X, item.Y, EventType.MouseDrag))
            { Fail("Moving interrupted the open crate or the held item"); yield break; }
            Client.ProbeCancelDrag();
            while (Client.ProbeCrateOpen || position.x < 3.2f)
            {
                Client.SetProbeMotion(Client.MovementFromKeys(Vector2.right, true, false), 0f, 3f);
                yield return new WaitForSecondsRealtime(.06f);
            }
            Client.SetProbeMotion(Vector2.zero, 0f, 3f);
            if (!Client.ProbeInventoryOpen || RaidMap.CanOpen(position))
            { Fail("Leaving the crate did not preserve the personal inventory"); yield break; }
            Capture("field-pack");
            yield return new WaitForSecondsRealtime(.20f);
            Signal("pack-walk", start.x.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                + " -> " + position.x.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        }

        private IEnumerator TestSweep()
        {
            Client.ProbeHover(true, .5f, .5f);
            if (Client.ProbeSelectedItem != 0 || Client.ProbeHoveredItem != 0)
            { Fail("Discovery or empty space selected an unrelated item"); yield break; }
            Client.ProbeTransferKey(true);
            Client.ProbeTransferKey(false);
            yield return new WaitForSecondsRealtime(.34f);
            if (bag.Length != 0) { Fail("F on empty space transferred an item"); yield break; }

            // Choose the closest pair of occupied cells. The segment between them crosses
            // only those two footprints, irrespective of how this crate was generated.
            var cells = (from item in crate
                from x in Enumerable.Range(item.X, LootCatalog.Width(item.Type, item.Rotated))
                from y in Enumerable.Range(item.Y, LootCatalog.Height(item.Type, item.Rotated))
                select new { item.Id, point = new Vector2(x + .5f, y + .5f) }).ToArray();
            var pair = (from a in cells from b in cells where a.Id != b.Id
                orderby (a.point - b.point).sqrMagnitude select new { a, b }).First();
            Client.ProbeHover(false, pair.a.point.x, pair.a.point.y);
            if (Client.ProbeHoveredItem != pair.a.Id) { Fail("Hover and F source disagree"); yield break; }
            Client.ProbeTransferKey(true);
            Client.ProbeHover(false, pair.b.point.x, pair.b.point.y);
            Client.ProbeHover(false, pair.a.point.x, pair.a.point.y);
            Client.ProbeTransferKey(false);
            yield return new WaitUntil(() => bag.Length == 2 && bag.Any(i => i.Id == pair.a.Id) && bag.Any(i => i.Id == pair.b.Id));
            var left = bag.Single(i => i.Id == pair.a.Id);
            var right = bag.Single(i => i.Id == pair.b.Id);
            Client.ProbeHover(true, left.X + .5f, left.Y + .5f);
            Capture("sweep");
            yield return new WaitForSecondsRealtime(.28f);
            Client.ProbeTransferKey(true);
            Client.ProbeHover(true, right.X + .5f, right.Y + .5f);
            yield return new WaitUntil(() => bag.Length == 0 && crate.Any(i => i.Id == right.Id));
            Client.ProbeTransferKey(false);
            Signal("sweep", pair.a.Id + "," + pair.b.Id);
        }
    }
}
#endif
