#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections;
using UnityEngine;

namespace HeavyPockets
{
    public sealed partial class RaidProbe
    {
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
