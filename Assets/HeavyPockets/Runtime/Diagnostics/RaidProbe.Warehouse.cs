#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections;
using UnityEngine;

namespace HeavyPockets
{
    public sealed partial class RaidProbe
    {
        private IEnumerator CheckWarehouse()
        {
            var door = WorldMechanisms.WarehouseDoor;
            var gate = WorldMechanisms.WarehouseGate;
            Vector3 g = gate.Pivot;
            var interiorControl = new Vector2(g.x + 1.65f, g.z + 1.15f);
            var exteriorControl = new Vector2(g.x + 1.65f, g.z - 1.15f);
            Vector3 hinge = ReceivingWarehouseLayout.DoorHinge;
            float x = hinge.x + ReceivingWarehouseLayout.DoorWidth * .5f;
            var outside = new Vector2(x, hinge.z - .83f);
            var inside = new Vector2(x, hinge.z + 1.53f);
            if (role == "A") yield return Walk(new Vector2(-30.8f, -9), sprint:true);
            else yield return Walk(new Vector2(3, 4));
            yield return Walk(new Vector2(-30.8f, 4), sprint:true);
            yield return Walk(new Vector2(-30.8f, 17), sprint:true);
            yield return Walk(outside, 0f);
            Phase("warehouse tile");
            yield return new WaitUntil(() => Object.FindFirstObjectByType<WarehouseFloorScale>());
            var scale = Object.FindFirstObjectByType<WarehouseFloorScale>();
            Vector3 platform = scale.transform.position;
            if (door.IsOpen) { Fail("Warehouse started open"); yield break; }
            if (role == "B")
            {
                Signal("door-ready");
                yield return new WaitUntil(() => door.Target == 1 && !door.Moving);
                Signal("saw-opening");
                yield return new WaitUntil(() => Exists("A.in-doorway"));
                Client.Interact();
                yield return new WaitForSecondsRealtime(.55f);
                if (!door.IsOpen) { Fail("Door closed on the other player"); yield break; }
                Signal("swing-blocked");
                yield return new WaitUntil(() => Exists("A.weighed"));
                Client.Interact();
                yield return new WaitUntil(() => !door.IsOpen && !door.Moving);
                // Leave enough space for the next operation to swing out towards the yard.
                yield return Walk(new Vector2(x, hinge.z - 1.85f), 0f);
                Signal("closed-outside");
                yield return new WaitUntil(() => Exists("A.opened-inside") && door.Target == -1 && !door.Moving);
                yield return Walk(inside, 0f);
                Signal("both-through");
                yield return Walk(new Vector2(platform.x+.15f, platform.z), 90f);
                Phase("two people on scale, east-facing observer");
                yield return new WaitUntil(() => Mathf.Abs(scale.Reading - 2f * WarehouseFloorScale.RunnerMass) < 1f);
                Signal("double-load");
                yield return new WaitUntil(() => Exists("A.double-load"));
                yield return Walk(inside, 180f);
                yield return new WaitUntil(() => Exists("A.single-load"));
                yield return Walk(interiorControl, 180f);
                yield return new WaitUntil(() => Exists("A.in-gate") && gate.IsOpen && !gate.Moving);
                Client.Interact();
                yield return new WaitForSecondsRealtime(.55f);
                if (!gate.IsOpen) { Fail("Lowered shutter onto another player"); yield break; }
                Signal("gate-blocked");
                yield return new WaitUntil(() => Exists("A.passed-gate"));
                Client.Interact();
                yield return new WaitUntil(() => !gate.IsOpen && !gate.Moving);
                Signal("gate-closed");
                yield return new WaitUntil(() => Exists("A.raised-outside") && gate.IsOpen && !gate.Moving);
                yield return Walk(new Vector2(g.x, g.z + 1.1f), 180f);
                yield return Walk(new Vector2(g.x, g.z - 1.1f), 180f);
                Signal("gate-crossed");
                yield break;
            }

            yield return new WaitUntil(() => Exists("B.door-ready"));
            Client.SetProbeMotion(Vector2.up, 0, 0);
            yield return new WaitForSecondsRealtime(.85f);
            Client.SetProbeMotion(Vector2.zero, 0, 0);
            if (position.y >= hinge.z) { Fail("Walked through a closed warehouse door"); yield break; }
            yield return Walk(outside, 0f);
            Client.Interact();
            yield return new WaitUntil(() => door.IsOpen && Exists("B.saw-opening"));
            yield return Walk(new Vector2(x,hinge.z + .2f), 0f);
            Signal("in-doorway");
            yield return new WaitUntil(() => Exists("B.swing-blocked"));
            yield return Walk(inside, 0f);
            yield return Walk(new Vector2(platform.x, platform.z), 0f);
            Phase("weigh the grounded player");
            yield return new WaitUntil(() => Mathf.Abs(scale.Reading - WarehouseFloorScale.RunnerMass) < 1f);
            Signal("weighed");
            yield return new WaitUntil(() => Exists("B.closed-outside") && !door.IsOpen);
            yield return Walk(inside, 180f);
            yield return new WaitUntil(() => scale.Reading < 1f);
            Client.Interact();
            yield return new WaitUntil(() => door.Target == -1 && !door.Moving);
            Signal("opened-inside");
            yield return new WaitUntil(() => Exists("B.both-through"));
            yield return Walk(new Vector2(platform.x-.15f, platform.z), -90f);
            Phase("two people on scale, west-facing observer");
            yield return new WaitUntil(() => Mathf.Abs(scale.Reading - 2f * WarehouseFloorScale.RunnerMass) < 1f);
            Signal("double-load");
            yield return new WaitUntil(() => Exists("B.double-load") &&
                Mathf.Abs(scale.Reading - WarehouseFloorScale.RunnerMass) < 1f);
            Signal("single-load");

            Phase("warehouse shutter");
            yield return Walk(new Vector2(g.x, g.z + 1.1f), 180f);
            Client.SetProbeMotion(Vector2.down, 180f, 0);
            yield return new WaitForSecondsRealtime(.85f);
            Client.SetProbeMotion(Vector2.zero, 180f, 0);
            if (position.y <= g.z) { Fail("Walked through a closed shutter"); yield break; }
            yield return Walk(interiorControl, 180f);
            Client.Interact();
            yield return new WaitUntil(() => gate.IsOpen && !gate.Moving);
            yield return Walk(new Vector2(g.x, g.z + .8f), 180f);
            yield return Walk(new Vector2(g.x, g.z), 180f);
            Signal("in-gate");
            yield return new WaitUntil(() => Exists("B.gate-blocked"));
            yield return Walk(new Vector2(g.x, g.z - 1.15f), 180f);
            yield return Walk(exteriorControl, 0f);
            Signal("passed-gate");
            yield return new WaitUntil(() => Exists("B.gate-closed") && !gate.IsOpen && !gate.Moving);
            Client.Interact();
            yield return new WaitUntil(() => gate.IsOpen && !gate.Moving);
            Signal("raised-outside");
            yield return new WaitUntil(() => Exists("B.gate-crossed"));
        }
    }
}
#endif
