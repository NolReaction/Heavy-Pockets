#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections;
using System.Linq;
using UnityEngine;

namespace HeavyPockets
{
    public sealed partial class RaidProbe
    {
        private IEnumerator SearchWarehouseCases()
        {
            Vector3 origin = ReceivingWarehouseLayout.Origin;
            Vector2 Point(float x,float z) => new Vector2(origin.x+x,origin.z+z);
            yield return Walk(Point(0,-5.7f));
            yield return Walk(Point(0,1.55f));
            yield return Walk(Point(-3.52f,1.70f),-90f,12f);
            Phase("first cargo box");
            var first = RaidContainers.BoundsFor(1);
            if (!RaidMap.CanOpen(position,default,1) || RaidMap.CanOpen(position,default,2))
            { Fail("Cargo box interaction zones overlap"); yield break; }

            int before = crateFrames;
            if (role == "A")
            {
                yield return new WaitUntil(() => Exists("B.case-observer"));
                Client.Interact();
            }
            else
            {
                Signal("case-observer");
                yield return new WaitUntil(() => TryReadLid(first,out float angle) && angle > 75f);
                if (crateFrames != before) { Fail("A bystander received private cargo"); yield break; }
                Client.Interact();
            }
            yield return new WaitUntil(() => crateFrames > before && searchComplete);
            if (!TryReadLid(first,out float opened) || opened < 80f)
            { Fail("Loot arrived with a closed first lid"); yield break; }
            uint tool = crate.Single(i => i.Type == LootCatalog.FoldingRule).Id;
            Signal("first-case-item",tool.ToString());
            string other = role=="A" ? "B" : "A";
            yield return new WaitUntil(() => Exists(other+".first-case-item"));
            if (Marker(other+".first-case-item") != tool.ToString())
            { Fail("Two visitors saw different tool cases"); yield break; }
            if (role=="A") link.Take(tool);
            yield return new WaitUntil(() => !crate.Any(i=>i.Id==tool) && (role=="B" || bag.Any(i=>i.Id==tool)));
            yield return CloseCase();

            yield return Walk(Point(2.65f,2.18f),0f,9f);
            Phase("second cargo box");
            before=crateFrames;
            Client.Interact();
            yield return new WaitUntil(() => crateFrames>before && searchComplete);
            if (crate.Any(i=>i.Id==tool) || !crate.Any(i=>i.Type==LootCatalog.PunchTicket))
            { Fail("Second box did not maintain its own manifest"); yield break; }
            Signal("second-case-ready");
            yield return new WaitUntil(()=>Exists(other+".second-case-ready"));
            if (role=="A") link.Store(tool);
            yield return new WaitUntil(()=>crate.Any(i=>i.Id==tool));
            Signal("stored-in-second");
            yield return new WaitUntil(()=>Exists(other+".stored-in-second"));
            if (role=="B") link.Take(tool);
            yield return new WaitUntil(()=>!crate.Any(i=>i.Id==tool) && (role=="A" || bag.Any(i=>i.Id==tool)));
            yield return CloseCase();
            yield return Walk(Point(0,1.55f));
            yield return Walk(Point(-3.52f,1.70f),-90f,12f);
            before=crateFrames;
            Client.Interact();
            yield return new WaitUntil(()=>crateFrames>before && searchComplete);
            if (crate.Any(i=>i.Type==LootCatalog.FoldingRule))
            { Fail("First box respawned a removed item"); yield break; }
            yield return CloseCase();
            Capture("searched-freight");
            Signal("two-cases-verified");
        }

        private IEnumerator CloseCase()
        {
            int before = closes;
            link.Close();
            yield return new WaitUntil(()=>closes>before);
        }

        private static bool TryReadLid(Bounds bounds,out float angle)
        {
            foreach (Transform candidate in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (candidate.name != "Lid" || !candidate.parent) continue;
                var floor = candidate.parent.position;
                if (Mathf.Abs(floor.x-bounds.center.x)>.02f || Mathf.Abs(floor.z-bounds.center.z)>.02f) continue;
                angle=Quaternion.Angle(Quaternion.identity,candidate.localRotation);
                return true;
            }
            angle=0f;
            return false;
        }
    }
}
#endif
