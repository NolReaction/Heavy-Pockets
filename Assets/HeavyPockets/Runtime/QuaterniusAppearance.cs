using UnityEngine;

namespace HeavyPockets
{
    internal static class QuaterniusAppearance
    {
        internal static void AttachHair(GameObject model)
        {
            foreach (Transform bone in model.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name != "Head") continue;
                var style = Resources.Load<GameObject>("QuaterniusHair");
                if (style) Object.Instantiate(style, bone, false);
                return;
            }
        }
    }
}
