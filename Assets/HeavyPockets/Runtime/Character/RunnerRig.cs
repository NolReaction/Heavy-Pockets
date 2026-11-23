using System;
using UnityEngine;

namespace HeavyPockets
{
    internal sealed class RunnerRig
    {
        internal readonly Animator Animator;

        internal RunnerRig(GameObject model)
        {
            Animator = model.GetComponentInChildren<Animator>(true);
            if (Animator == null || Animator.avatar == null || !Animator.avatar.isHuman || !Animator.avatar.isValid)
                throw new InvalidOperationException("The Unity Starter Assets character needs a valid Humanoid avatar.");
        }

        internal Transform Bone(HumanBodyBones name)
        {
            var bone = Animator.GetBoneTransform(name);
            if (bone != null) return bone;
            throw new InvalidOperationException("The Unity Starter Assets avatar is missing " + name + ".");
        }
    }
}
