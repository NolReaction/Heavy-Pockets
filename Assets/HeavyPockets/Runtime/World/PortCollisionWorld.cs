using UnityEngine;

namespace HeavyPockets
{
    internal static class PortCollisionWorld
    {
        // The player motor queries these same boxes directly. Other Unity physics
        // queries and rigidbodies also need a floor when running without scenery.
        private static GameObject current;

        internal static void Select(bool training, Transform parent)
        {
            if (TrainingGround.Active == training && current != null) return;
            if (current != null)
            {
                current.SetActive(false);
                Object.Destroy(current);
            }
            TrainingGround.Select(training);
            WorldMechanisms.Reset();
            Create(parent);
        }

        internal static void Create(Transform parent)
        {
            RaidMap.LoadLayout();
            var port = new GameObject("Port hitboxes");
            port.transform.SetParent(parent, false);
            current = port;
            AddBox(port, new Bounds(new Vector3(0f, -.25f, 0f),
                new Vector3(RaidMap.RoomX * 2f, .5f, RaidMap.RoomZ * 2f)));

            var chest = new GameObject("Cargo chest hitbox");
            chest.transform.SetParent(port.transform, false);
            foreach (Bounds bounds in RaidMap.CollisionBoxes)
                AddBox(bounds == RaidMap.CargoCase ? chest : port, bounds);
            WorldMechanisms.CreateColliders(port.transform);
        }

        private static void AddBox(GameObject owner, Bounds bounds)
        {
            var box = owner.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;
        }
    }
}
