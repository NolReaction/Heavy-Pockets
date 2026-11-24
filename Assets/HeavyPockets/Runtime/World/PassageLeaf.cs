using UnityEngine;

namespace HeavyPockets
{
    // Streams can destroy and recreate a visual leaf independently of the persistent collider.
    internal sealed class PassageLeaf : MonoBehaviour
    {
        private WorldMechanism mechanism;
        private Vector3 closedPosition, closedScale;
        private Quaternion closedRotation;
        private float applied = float.NaN;

        internal void Initialize(WorldMechanism owner)
        {
            mechanism = owner;
            closedPosition = transform.localPosition;
            closedRotation = transform.localRotation;
            closedScale = transform.localScale;
            Apply();
        }

        private void OnEnable() => Apply();
        private void FixedUpdate() => Apply();
        private void LateUpdate() => Apply();

        private void Apply()
        {
            if (mechanism == null) return;
            float amount = mechanism.Amount;
            if (applied == amount) return;
            applied = amount;
            if (mechanism.IsShutter)
            {
                transform.localPosition = closedPosition + closedRotation * Vector3.up * (mechanism.Lift * closedScale.y);
                transform.localScale = new Vector3(closedScale.x,
                    closedScale.y * (1f - mechanism.Lift / mechanism.Size.y), closedScale.z);
            }
            else transform.localRotation = closedRotation * mechanism.LeafRotation;
        }
    }
}
