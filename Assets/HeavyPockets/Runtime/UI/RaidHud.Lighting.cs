using UnityEngine;

namespace HeavyPockets
{
    internal sealed partial class RaidHud
    {
        private readonly struct LightSlider
        {
            internal readonly HarborLightParameter Parameter;
            internal readonly string Label;
            internal readonly float Limit, Step;

            internal LightSlider(HarborLightParameter parameter, string label, float limit, float step)
            {
                Parameter = parameter;
                Label = label;
                Limit = limit;
                Step = step;
            }
        }

        private static readonly LightSlider[] LightSliders = {
            new LightSlider(HarborLightParameter.Time, "Время суток", 24f, .25f),
            new LightSlider(HarborLightParameter.Sun, "Яркость солнца", 2f, .05f),
            new LightSlider(HarborLightParameter.Clouds, "Облачность", 1f, .01f),
            new LightSlider(HarborLightParameter.Wind, "Скорость ветра", 30f, .5f),
            new LightSlider(HarborLightParameter.Haze, "Дымка", 1f, .01f)
        };
        private static readonly Color32[] StyleSwatches = {
            new Color32(157, 199, 205, 255), new Color32(247, 165, 92, 255), new Color32(158, 158, 217, 255)
        };
        private static int ResetRow => StyleSwatches.Length + LightSliders.Length;
        private static int BackRow => ResetRow + 1;
        private int lightFocus, lightPress = -1;
        private bool lightChanged;
        private readonly float[] lastLightValues = new float[LightSliders.Length];
        private readonly string[] lightReadouts = new string[LightSliders.Length];

        internal void OpenLightingControls()
        {
            lightFocus = RaidScenery.EnvironmentStyle;
            lightPress = -1;
        }

        internal void CloseLightingControls()
        {
            if (lightChanged) RaidScenery.SaveLightingSettings();
            lightChanged = false;
            lightPress = -1;
        }

        private static float SettingsRail(float width) => Mathf.Clamp(width * .32f, 280f, 348f);

        internal static bool PointerOverSettings()
        {
            float scale = Mathf.Min(Screen.width / 840f, Screen.height / 500f, 2.2f);
            return scale <= 0f || Input.mousePosition.x <= SettingsRail(Screen.width / scale) * scale;
        }

        private static Rect LightRowRect(int row, float rail, float height)
        {
            const float edge = 20f, top = 60f;
            float width = rail - 2f * edge;
            if (row < StyleSwatches.Length)
                return new Rect(edge, top + row * 31f, width, 27f);
            if (row < ResetRow)
            {
                float step = Mathf.Min(61f, (height - 228f) / LightSliders.Length);
                return new Rect(edge, 166f + (row - StyleSwatches.Length) * step, width, step - 3f);
            }
            float backWidth = 77f;
            return row == BackRow
                ? new Rect(rail - edge - backWidth, height - 49f, backWidth, 32f)
                : new Rect(edge, height - 49f, width - backWidth - 8f, 32f);
        }

        private static bool IsLightSlider(int row) => row >= StyleSwatches.Length && row < ResetRow;

        private void SetLightValue(int row, float value)
        {
            LightSlider slider = LightSliders[row - StyleSwatches.Length];
            value = Mathf.Clamp(Mathf.Round(value / slider.Step) * slider.Step, 0f, slider.Limit);
            if (Mathf.Approximately(value, RaidScenery.GetLightingValue(slider.Parameter))) return;
            RaidScenery.SetLightingValue(slider.Parameter, value);
            lightChanged = true;
        }

        private void SetLightFromPointer(int row, Rect rect, float x)
        {
            LightSlider slider = LightSliders[row - StyleSwatches.Length];
            SetLightValue(row, Mathf.InverseLerp(rect.x + 4f, rect.xMax - 4f, x) * slider.Limit);
        }

        private void ActivateLightButton(int row)
        {
            CloseLightingControls();
            if (row < StyleSwatches.Length)
                MenuAction((RaidMenuAction)((int)RaidMenuAction.HarborDaylight + row));
            else if (row == ResetRow) RaidScenery.ResetLightingSettings();
            else if (row == BackRow) MenuAction(RaidMenuAction.BackToMenu);
        }

        private void LightingKey(Event ev)
        {
            if (ev.keyCode == KeyCode.DownArrow || ev.keyCode == KeyCode.UpArrow || ev.keyCode == KeyCode.Tab)
            {
                int step = ev.keyCode == KeyCode.UpArrow || ev.keyCode == KeyCode.Tab && ev.shift ? -1 : 1;
                lightFocus = (lightFocus + step + BackRow + 1) % (BackRow + 1);
            }
            else if (IsLightSlider(lightFocus) && (ev.keyCode == KeyCode.LeftArrow || ev.keyCode == KeyCode.RightArrow))
            {
                LightSlider slider = LightSliders[lightFocus - StyleSwatches.Length];
                SetLightValue(lightFocus, RaidScenery.GetLightingValue(slider.Parameter) +
                    (ev.keyCode == KeyCode.LeftArrow ? -slider.Step : slider.Step));
            }
            else if (!IsLightSlider(lightFocus) && (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter || ev.keyCode == KeyCode.Space))
                ActivateLightButton(lightFocus);
            else return;
            ev.Use();
        }

        private void DrawLightingControls(float width, float height)
        {
            float rail = SettingsRail(width);
            var ev = Event.current;
            if (!Application.isFocused) CloseLightingControls();
            if (Application.isFocused && ev.type == EventType.KeyDown) { LightingKey(ev); return; }
            if (ev.type == EventType.KeyUp && lightChanged && lightPress < 0) CloseLightingControls();
            if (Application.isFocused && ev.button == 0 &&
                (ev.type == EventType.MouseDown || ev.type == EventType.MouseDrag || ev.type == EventType.MouseUp))
            {
                int hit = -1;
                for (int i = 0; i <= BackRow; i++)
                    if (LightRowRect(i, rail, height).Contains(ev.mousePosition)) { hit = i; break; }
                if (ev.type == EventType.MouseDown && hit >= 0) lightPress = lightFocus = hit;
                if (lightPress < 0) return;
                if (IsLightSlider(lightPress))
                    SetLightFromPointer(lightPress, LightRowRect(lightPress, rail, height), ev.mousePosition.x);
                if (ev.type == EventType.MouseUp)
                {
                    if (hit == lightPress && !IsLightSlider(hit)) ActivateLightButton(hit);
                    else CloseLightingControls();
                }
                ev.Use();
                return;
            }
            if (ev.type != EventType.Repaint) return;
            if (lightPress >= 0 && !Input.GetMouseButton(0)) CloseLightingControls();
            Block(0, 0, rail, height, StationInk);
            Block(rail - 1f, 0, 1f, height, Rule);
            Text(20, 12, rail - 40, 35, "Оформление порта", 24, Paper, true);
            for (int i = 0; i <= BackRow; i++)
            {
                Rect rect = LightRowRect(i, rail, height);
                bool focus = i == lightFocus, hover = Application.isFocused && rect.Contains(ev.mousePosition);
                if (IsLightSlider(i))
                {
                    DrawLightingSlider(i, rect, focus || hover);
                    continue;
                }
                bool style = i < StyleSwatches.Length;
                bool selected = style && RaidScenery.EnvironmentStyle == i;
                Color accent = style ? (Color)StyleSwatches[i] : Mint;
                Color fill = Color.Lerp(StationRow, accent, selected || lightPress == i ? .24f : hover || focus ? .12f : 0f);
                Block(rect.x, rect.y, rect.width, rect.height, fill);
                if (selected || focus) Block(rect.x, rect.y, 3f, rect.height, accent);
                if (style) Block(rect.x + 12, rect.center.y - 4f, 8f, 8f, accent);
                float inset = style ? 30f : 9f;
                string text = style ? RaidScenery.EnvironmentStyleName(i) : i == ResetRow ? "Сбросить настройки" : "Назад";
                Text(rect.x + inset, rect.y, rect.width - inset * 1.5f, rect.height, text, 14,
                    selected ? accent : Paper, selected, style ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter);
            }
        }

        private void DrawLightingSlider(int row, Rect rect, bool focus)
        {
            int index = row - StyleSwatches.Length;
            LightSlider slider = LightSliders[index];
            float value = RaidScenery.GetLightingValue(slider.Parameter);
            if (lightReadouts[index] == null || value != lastLightValues[index])
            {
                lastLightValues[index] = value;
                int minutes = Mathf.RoundToInt(value * 60f) % 1440;
                lightReadouts[index] = slider.Parameter switch
                {
                    HarborLightParameter.Time => $"{minutes / 60:00}:{minutes % 60:00}",
                    HarborLightParameter.Sun => value.ToString("0.00").Replace('.', ',') + "×",
                    HarborLightParameter.Wind => value.ToString("0.#").Replace('.', ',') + " м/с",
                    _ => Mathf.RoundToInt(value * 100f) + " %"
                };
            }
            Text(rect.x, rect.y, rect.width - 80f, 21f, slider.Label, 14, focus ? Paper : Muted, focus);
            Text(rect.xMax - 84f, rect.y, 84f, 21f, lightReadouts[index], 13, Paper, false, TextAnchor.MiddleRight);
            float left = rect.x + 4f, width = rect.width - 8f, y = rect.y + 32f;
            float travel = width * Mathf.Clamp01(value / slider.Limit);
            Block(left, y, width, 3f, StationRow);
            Block(left, y, travel, 3f, focus ? Mint : Rule);
            Block(left + travel - 3f, y - 3f, 6f, 9f, focus ? Mint : Paper);
        }
    }
}
