#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections;
using UnityEngine;

namespace HeavyPockets
{
    public sealed partial class RaidProbe
    {
        private IEnumerator CheckFreightSurfaces()
        {
            if (role == "A")
                yield return Walk(new Vector2(-5f, -6f), 270f);
            else
            {
                yield return Walk(new Vector2(3.2f, 1.3f));
                yield return Walk(new Vector2(3.2f, -8.8f));
                yield return Walk(new Vector2(-9.1f, -11f), 0f, 0f);
            }
            Signal("surface-ready");
            yield return new WaitUntil(() => Exists("A.surface-ready") && Exists("B.surface-ready"));

            // Each client checks its own decoded copy of the authoritative pose.
            int number = 0;
            foreach (OutdoorSolid solid in OutdoorLayout.Solids)
            {
                if (solid.Kind != OutdoorKind.FreightCrate) continue;
                number++;
                float top = solid.Bounds.max.y;
                Phase("land and stand on freight crate " + number);
                if (role == "A")
                {
                    // Take off beside the face on each level. A jump started from rest
                    // in the middle of the previous crate may fall short of the next lip.
                    for (int i = 0; i < 17; i++)
                    {
                        Client.SetProbeMotion(Vector2.left, 270f, 0f);
                        yield return new WaitForSecondsRealtime(.08f);
                    }
                    float face = solid.Bounds.max.x + .30f;
                    if (Mathf.Abs(position.x - face) > .10f || Client.OwnMotion.Airborne)
                    { Fail("Freight crate side did not stop the grounded runner at its edge"); yield break; }
                    Client.SetProbeMotion(Vector2.left, 270f, 0f, MoveButtons.Jump);
                    while (position.x > solid.Bounds.center.x + .12f)
                    {
                        yield return new WaitForSecondsRealtime(.07f);
                        Client.SetProbeMotion(Vector2.left, 270f, 0f);
                    }
                    Client.SetProbeMotion(Vector2.zero, 270f, 0f);
                }
                yield return new WaitUntil(() =>
                {
                    RaidLocomotion motion = role == "A" ? Client.OwnMotion : remoteMotion;
                    return !motion.Airborne && Mathf.Abs(motion.Height - top) < .002f && motion.VerticalSpeed == 0f;
                });
                Signal("freight-" + number);
                yield return new WaitUntil(() => Exists("A.freight-" + number) && Exists("B.freight-" + number));
            }
            if (number != 3) { Fail("Missing freight crate course"); yield break; }

            Phase("fall off the high crate without jumping");
            while (true)
            {
                if (role == "A") Client.SetProbeMotion(Vector2.down, 180f, 0f);
                RaidLocomotion motion = role == "A" ? Client.OwnMotion : remoteMotion;
                if (motion.Airborne && motion.VerticalSpeed < -.2f) break;
                yield return new WaitForSecondsRealtime(.05f);
            }
            if (role == "A") Client.SetProbeMotion(Vector2.zero, 180f, 0f);
            yield return new WaitUntil(() =>
            {
                RaidLocomotion motion = role == "A" ? Client.OwnMotion : remoteMotion;
                return !motion.Airborne && motion.Height == 0f && motion.VerticalSpeed == 0f;
            });
            Signal("landed");
            yield return new WaitUntil(() => Exists("A.landed") && Exists("B.landed"));
        }

        private IEnumerator CheckLocomotion()
        {
            float sign = role == "A" ? -1f : 1f;
            float yaw = sign > 0f ? 90f : 270f;
            yield return Walk(new Vector2(-sign * 4.7f, 2.7f), yaw);
            Phase("two players face each other for locomotion checks");
            Signal("motion-ready");
            yield return new WaitUntil(() => Exists("A.motion-ready") && Exists("B.motion-ready"));

            Client.SetProbeMotion(Vector2.zero, yaw, 0f, MoveButtons.Crouch);
            yield return new WaitUntil(() => Client.OwnMotion.Crouch > .92f);
            Client.SetProbeMotion(Vector2.zero, yaw, 0f, MoveButtons.Crouch | MoveButtons.Jump);
            yield return new WaitForSecondsRealtime(.18f);
            if (Client.OwnMotion.Airborne) { Fail("Crouched jump started without standing clearance"); yield break; }
            Client.SetProbeMotion(Vector2.zero, yaw, 0f);
            yield return new WaitUntil(() => Client.OwnMotion.Crouch < .05f);
            Client.SetProbeMotion(Vector2.zero, yaw, 0f, MoveButtons.Jump);
            yield return new WaitUntil(() => Client.OwnMotion.Height > .4f);
            yield return new WaitUntil(() => Client.OwnMotion.VerticalSpeed < -.8f);
            yield return new WaitUntil(() => !Client.OwnMotion.Airborne);

            Vector2 start = position;
            float started = Time.unscaledTime;
            bool sprinted = false;
            do
            {
                Client.SetProbeMotion(Vector2.right * sign, yaw, 0f, MoveButtons.Sprint);
                yield return new WaitForSecondsRealtime(.065f);
                sprinted |= Client.OwnMotion.Sprinting;
            } while (Time.unscaledTime - started < .55f);
            if (!sprinted || Vector2.Distance(position, start) < 1.65f)
            { Fail("Sprint did not reach running speed"); yield break; }
            Vector2 slideStart = position;
            Client.SetProbeMotion(Vector2.right * sign, yaw, 0f, MoveButtons.Sprint | MoveButtons.Crouch);
            yield return new WaitUntil(() => Client.OwnMotion.Sliding);
            do
            {
                Client.SetProbeMotion(Vector2.zero, yaw, 0f, MoveButtons.Crouch);
                yield return new WaitForSecondsRealtime(.09f);
            } while (Client.OwnMotion.Sliding);
            if (Client.OwnMotion.Crouch < .9f || Client.OwnMotion.Sprinting ||
                Vector2.Distance(position, slideStart) < 1.5f)
            { Fail("Slide did not carry momentum into a crouch"); yield break; }
            Client.SetProbeMotion(Vector2.zero, yaw, 0f);
            yield return new WaitUntil(() => Client.OwnMotion.Crouch < .05f);
            Signal("motion-ok");
            yield return new WaitUntil(() => Exists("A.motion-ok") && Exists("B.motion-ok"));
        }
    }
}
#endif
