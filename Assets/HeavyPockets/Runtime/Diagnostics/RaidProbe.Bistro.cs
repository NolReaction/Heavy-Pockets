#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections;
using UnityEngine;

namespace HeavyPockets
{
    public sealed partial class RaidProbe
    {
        private IEnumerator CheckBistro()
        {
            var plan = WorldPlan.Load(true);
            var site = plan ? plan.Objects.Find(p => p.Piece && p.Piece.Title == "Угловое бистро") : default;
            if (!site.Piece) { Fail("Training map has no corner bistro"); yield break; }
            Vector2 Point(float x, float z)
            {
                var world = site.Position + site.Rotation * new Vector3(x, 0f, z);
                return new Vector2(world.x, world.z);
            }
            IEnumerator At(float x, float z, float? heading = null) => Walk(Point(x, z), heading, sprint: true);
            IEnumerator Meet(string mark)
            {
                Signal(mark);
                yield return new WaitUntil(() => Exists("A." + mark) && Exists("B." + mark));
            }
            IEnumerator Face(Vector2 peer)
            {
                Vector2 difference = peer - position;
                Client.SetProbeMotion(Vector2.zero, Mathf.Atan2(difference.x, difference.y) * Mathf.Rad2Deg, 0f);
                yield return new WaitForSecondsRealtime(.32f);
            }
            bool inside = role == "A";
            if (Client.IsThirdPerson) Client.TogglePerspective();
            yield return At(8.65f, -9.60f);
            yield return At(6.95f, -7.45f);
            if (inside) yield return At(4.05f, -5.3f);
            yield return Face(inside ? Point(6.95f, -7.45f) : Point(4.05f, -5.3f));
            if (inside && (Client.OwnMotion.Airborne || Mathf.Abs(Client.OwnMotion.Height - site.Position.y - .07f) > .025f))
            { Fail("First runner did not walk onto the bistro floor"); yield break; }
            yield return Meet("at-door");
            Phase("peer visible through the open bistro doorway");
            yield return new WaitUntil(() => Client.ProbeRemotePresent);
            yield return Meet("door-visible");

            if (!inside) yield return At(8.40f,-7.20f);
            float ownX = inside ? 4.5f : 8.2f;
            float peerX = inside ? 8.2f : 4.5f;
            Vector2 across = Point(peerX, -2.45f) - Point(ownX, -2.45f);
            float acrossYaw = Mathf.Atan2(across.x, across.y) * Mathf.Rad2Deg;
            yield return At(ownX, -2.45f, acrossYaw);
            if (!inside)
            {
                Vector2 inward = (Point(4.5f,-2.45f) - position).normalized;
                float heading = Mathf.Atan2(inward.x,inward.y) * Mathf.Rad2Deg;
                for (int tick = 0; tick < 16; tick++)
                {
                    Client.SetProbeMotion(inward, heading, 0f);
                    yield return new WaitForSecondsRealtime(.08f);
                }
                Client.SetProbeMotion(Vector2.zero,heading,0);
                yield return new WaitForSecondsRealtime(.22f);
                Vector3 local = Quaternion.Inverse(site.Rotation) * (new Vector3(position.x,0f,position.y) - site.Position);
                if(local.x < 6.67f || local.x > 7.45f) { Fail("Display window did not stop the approaching runner at its glass: "+local); yield break; }
                yield return At(ownX, -2.45f, acrossYaw);
            }
            yield return Face(Point(peerX,-2.45f));
            yield return Meet("at-glass");
            Phase("peer visible through the bistro glass");
            yield return new WaitUntil(() => Client.ProbeRemotePresent);
            Capture("bistro-window");
            yield return Meet("glass-visible");

            yield return At(ownX, -1.15f, acrossYaw);
            yield return Face(Point(peerX,-1.15f));
            yield return Meet("at-pier");
            Phase("bistro masonry hides the peer");
            yield return new WaitUntil(() => !Client.ProbeRemotePresent);
            int poses = remotePoses;
            yield return new WaitForSecondsRealtime(.45f);
            if (remotePoses != poses) { Fail("The opaque pier leaked a remote pose"); yield break; }
            yield return Meet("pier-hidden");

            if (inside) yield return At(3.55f,-4.55f);
            else
            {
                yield return At(8.40f,-7.20f);
                yield return At(6.95f,-7.45f);
                yield return At(4.05f,-5.3f);
            }
            yield return Face(inside ? Point(4.05f,-5.3f) : Point(3.55f,-4.55f));
            Phase("both runners on the bistro floor");
            yield return new WaitUntil(() => Client.ProbeRemotePresent);
            if (Client.OwnMotion.Airborne || Mathf.Abs(Client.OwnMotion.Height - site.Position.y - .07f) > .025f)
            { Fail("Runner needed a jump to enter the bistro"); yield break; }
            yield return Meet("both-inside");
        }
    }
}
#endif
