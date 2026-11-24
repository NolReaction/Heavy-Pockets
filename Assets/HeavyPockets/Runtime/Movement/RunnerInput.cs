using UnityEngine;

namespace HeavyPockets
{
    internal static class RunnerInput
    {
        internal static Vector2 Keys() => new Vector2(
            (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
            (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f));

        internal static Vector2 WorldDirection(Vector2 keys, float yaw)
        {
            keys = Vector2.ClampMagnitude(keys, 1f);
            Vector3 world = Quaternion.Euler(0, yaw, 0) * new Vector3(keys.x, 0, keys.y);
            return new Vector2(world.x, world.z);
        }

        internal static MoveButtons Buttons(bool jump)
        {
            var buttons = jump ? MoveButtons.Jump : MoveButtons.None;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) buttons |= MoveButtons.Sprint;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) buttons |= MoveButtons.Crouch;
            return buttons;
        }

        internal static void Look(ref float yaw, ref float pitch)
        {
            yaw = Mathf.Repeat(yaw + Input.GetAxisRaw("Mouse X") * 2.1f, 360f);
            pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * 2.1f, -RaidCameraBoom.MaxPitch, RaidCameraBoom.MaxPitch);
        }
    }
}
