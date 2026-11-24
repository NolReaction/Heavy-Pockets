using System;
using UnityEngine;

namespace HeavyPockets
{
    internal enum StarterMotion { Idle, Walk, Run, JumpStart, Fall, Land, WalkLand, RunLand }

    internal static class StarterRunner
    {
        internal const string ModelPath = "UnityStarter/Models/Armature";
        private const string Animations = "UnityStarter/Animations/";
        internal const string ControllerPath = Animations + "StarterAssetsThirdPerson";
        // Measured on the skinned source mesh in its neutral pose, in metres.
        private const float SourceStature = 1.76186f, SourceSole = .008026f;
        private static readonly AnimationClip[] previewClips = new AnimationClip[8];

        internal static GameObject Instantiate(Transform parent)
        {
            var model = Resources.Load<GameObject>(ModelPath);
            var controller = Resources.Load<RuntimeAnimatorController>(ControllerPath);
            if (model == null || controller == null)
                throw new InvalidOperationException("The Unity Starter Assets character or controller is missing.");

            var instance = UnityEngine.Object.Instantiate(model, parent, false);
            instance.name = "Unity Starter Assets Armature";
            float scale = RaidMap.StandingHeight / SourceStature;
            instance.transform.localScale *= scale;
            instance.transform.localPosition -= Vector3.up * (SourceSole * scale);
            var animator = instance.GetComponentInChildren<Animator>(true);
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.fireEvents = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return instance;
        }

        internal static AnimationClip Clip(StarterMotion motion)
        {
            int index = (int)motion;
            if (previewClips[index] != null) return previewClips[index];

            (string asset, string clip) = motion switch
            {
                StarterMotion.Idle => ("Stand--Idle", "Idle"),
                StarterMotion.Walk => ("Locomotion--Walk_N", "Walk_N"),
                StarterMotion.Run => ("Locomotion--Run_N", "Run_N"),
                StarterMotion.JumpStart => ("Jump--Jump", "JumpStart"),
                StarterMotion.Fall => ("Jump--InAir", "InAir"),
                StarterMotion.Land => ("Jump--Jump", "JumpLand"),
                StarterMotion.WalkLand => ("Locomotion--Walk_N_Land", "Walk_N_Land"),
                StarterMotion.RunLand => ("Locomotion--Run_N_Land", "Run_N_Land"),
                _ => throw new ArgumentOutOfRangeException(nameof(motion))
            };
            foreach (var candidate in Resources.LoadAll<AnimationClip>(Animations + asset + ".anim"))
            {
                if (candidate.name == clip) return previewClips[index] = candidate;
            }
            throw new InvalidOperationException("Missing Unity Starter Assets clip " + clip);
        }

        internal static string Label(StarterMotion motion) => motion switch
        {
            StarterMotion.Idle => "Покой",
            StarterMotion.Walk => "Ходьба",
            StarterMotion.Run => "Бег",
            StarterMotion.JumpStart => "Отрыв",
            StarterMotion.Fall => "В воздухе",
            StarterMotion.Land => "Приземление",
            StarterMotion.WalkLand => "Приземление в шаг",
            StarterMotion.RunLand => "Приземление в бег",
            _ => throw new ArgumentOutOfRangeException(nameof(motion))
        };
    }
}
