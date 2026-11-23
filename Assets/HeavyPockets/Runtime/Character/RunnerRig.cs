using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeavyPockets
{
    internal sealed class RunnerRig
    {
        private readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
        internal readonly Animator Animator;

        internal RunnerRig(GameObject model)
        {
            Animator = model.GetComponentInChildren<Animator>(true);
            if (Animator == null) Animator = model.AddComponent<Animator>();
            foreach (Transform node in Animator.GetComponentsInChildren<Transform>(true))
                bones[node.name] = node;
        }

        internal Transform Bone(string name)
        {
            if (bones.TryGetValue(name, out var bone)) return bone;
            throw new InvalidOperationException("Runner is missing the bone " + name + ".");
        }
    }
}
