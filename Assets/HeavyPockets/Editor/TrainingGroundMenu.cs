using UnityEditor;
using UnityEditor.SceneManagement;

namespace HeavyPockets.Editor
{
    internal static class TrainingGroundMenu
    {
        private const string Menu = "Heavy Pockets/Play Training Ground";

        [MenuItem(Menu, false, 3)]
        private static void Play()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene("Assets/HeavyPockets/TrainingGround.unity");
            EditorApplication.EnterPlaymode();
        }

        [MenuItem(Menu, true)]
        private static bool CanPlay() => !EditorApplication.isPlayingOrWillChangePlaymode;
    }
}
