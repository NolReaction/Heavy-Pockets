using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace HeavyPockets
{
    internal sealed class RunnerAppearance : MonoBehaviour
    {
        internal const int DefaultStyle = 0;
        internal const string ResourcePath = "Runner/Character";
        internal static readonly string[] Categories = { "Тон кожи", "Причёска", "Лицо", "Верх", "Брюки", "Расцветка" };
        private static readonly string[][] choices =
        {
            new[] { "Песочный", "Оливковый", "Бронзовый", "Тёмный" },
            new[] { "Короткая", "Хвост", "Каска", "Гребень" },
            new[] { "Классическое", "Широкие скулы", "Усы" },
            new[] { "Портовый жилет", "Кожаная куртка", "Рабочая рубашка" },
            new[] { "Плотный твил", "Карго с накладками", "Джинсы и кеды" },
            new[] { "Терракота", "Шалфей", "Шторм", "Песчаник" },
        };
        private static readonly Color32[] skinTones =
        {
            new Color32(200,149,112,255), new Color32(175,133,91,255),
            new Color32(151,99,70,255), new Color32(105,72,57,255),
        };
        private static readonly Color32[] shellColors =
        {
            new Color32(161,89,57,255), new Color32(103,121,90,255),
            new Color32(69,96,116,255), new Color32(183,153,103,255),
        };
        private static readonly Color32[] panelColors =
        {
            new Color32(56,77,83,255), new Color32(58,70,62,255),
            new Color32(43,58,77,255), new Color32(90,72,62,255),
        };
        private static readonly Color32[] trouserColors =
        {
            new Color32(66,88,100,255), new Color32(72,79,67,255),
            new Color32(55,68,85,255), new Color32(87,79,67,255),
        };

        private int colorId;
        private SkinnedMeshRenderer[] pieces;
        private MaterialPropertyBlock block;
        private int current = -1;
        private PlayableGraph previewIdle;

        internal static string[] Options(int category) => choices[category];
        internal static int Get(int style, int category)
        {
            int choice = (style >> (category * 2)) & 3;
            return choice < choices[category].Length ? choice : 0;
        }
        internal static int With(int style, int category, int option)
        {
            int shift = category * 2;
            int choice = Mathf.Clamp(option, 0, choices[category].Length - 1);
            return (Normalize(style) & ~(3 << shift)) | (choice << shift);
        }
        internal static int Normalize(int style)
        {
            if (style < 0) return DefaultStyle;
            int normalized = 0;
            for (int i = 0; i < choices.Length; i++) normalized |= Get(style, i) << (i * 2);
            return normalized;
        }

        internal static void Apply(GameObject model, int style)
        {
            var appearance = model.GetComponentInChildren<RunnerAppearance>(true);
            if (appearance == null)
                appearance = model.GetComponentInChildren<Animator>(true).gameObject.AddComponent<RunnerAppearance>();
            appearance.Dress(Normalize(style));
        }

        private void Dress(int style)
        {
            if (current == style) return;
            current = style;
            if (pieces == null)
            {
                pieces = GetComponentsInChildren<SkinnedMeshRenderer>(true);
                block = new MaterialPropertyBlock();
                colorId = Shader.PropertyToID("_Color");
            }
            int tone = Get(style, 0), palette = Get(style, 5);
            string hair = "Hair" + Get(style, 1), face = "Face" + Get(style, 2);
            string top = "Top" + Get(style, 3), leg = "Leg" + Get(style, 4), boot = "Boot" + Get(style, 4);
            foreach (var piece in pieces)
            {
                string part = piece.name;
                piece.enabled = part == hair || part == face || part == top || part == leg || part == boot;
                if (!piece.enabled) continue;
                var materials = piece.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Color color;
                    switch (materials[i].name)
                    {
                        case "Depot_Skin": color = skinTones[tone]; break;
                        case "Depot_SkinShade": color = (Color)skinTones[tone] * .76f; break;
                        case "Depot_Lips": color = (Color)skinTones[tone] * new Color(.83f, .74f, .78f); break;
                        case "Depot_Shell": color = shellColors[palette]; break;
                        case "Depot_Panel": color = panelColors[palette]; break;
                        case "Depot_Trousers": color = trouserColors[palette]; break;
                        case "Depot_Reinforcement": color = (Color)trouserColors[palette] * .72f; break;
                        default: continue;
                    }
                    color.a = 1;
                    block.Clear();
                    block.SetColor(colorId, color);
                    piece.SetPropertyBlock(block, i);
                }
            }
        }

        internal static GameObject CreatePreview(Transform parent, int style)
        {
            var model = Instantiate(Resources.Load<GameObject>(ResourcePath), parent, false);
            Apply(model, style);
            var appearance = model.GetComponent<RunnerAppearance>();
            var animator = model.GetComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var idle = Resources.Load<AnimationClip>("QuaterniusMotion/Idle");
            appearance.previewIdle = PlayableGraph.Create("Wardrobe idle");
            appearance.previewIdle.SetTimeUpdateMode(DirectorUpdateMode.UnscaledGameTime);
            var output = AnimationPlayableOutput.Create(appearance.previewIdle, "Character", animator);
            output.SetSourcePlayable(AnimationClipPlayable.Create(appearance.previewIdle, idle));
            appearance.previewIdle.Play();
            appearance.previewIdle.Evaluate(0);
            return model;
        }

        private void OnDestroy()
        {
            if (previewIdle.IsValid()) previewIdle.Destroy();
        }
    }
}
