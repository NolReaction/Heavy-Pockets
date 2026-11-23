using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeavyPockets
{
    internal sealed class RunnerRig
    {
        private readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
        internal readonly Animator Animator;
        internal readonly SkinnedMeshRenderer Body;

        internal RunnerRig(GameObject model)
        {
            Animator = model.GetComponentInChildren<Animator>(true);
            if (Animator == null) Animator = model.AddComponent<Animator>();
            foreach (Transform node in Animator.GetComponentsInChildren<Transform>(true))
                bones[node.name] = node;
            Transform ankle = Bone("foot_l");
            var skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            // Hair and eyes may both be skinned. Find the full body for the sole measurements.
            foreach (var skin in skins)
            {
                if (Array.IndexOf(skin.bones, ankle) < 0) continue;
                if (Body == null || skin.localBounds.size.magnitude > Body.localBounds.size.magnitude)
                    Body = skin;
            }
            if (Body == null) throw new InvalidOperationException("Runner has no body skinned to the left ankle.");
        }

        internal Transform Bone(string name)
        {
            if (bones.TryGetValue(name, out var bone)) return bone;
            throw new InvalidOperationException("Runner is missing the bone " + name + ".");
        }
    }
}
