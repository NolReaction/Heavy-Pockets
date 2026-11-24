using System;
using UnityEngine;

namespace HeavyPockets
{
    internal sealed partial class RaidHud
    {
        private enum GraphicsRow { Msaa, WindowCookies, Tonemapping, Exposure, Occlusion, Strength, Radius, Reflections, Intensity, Reset, Back }
        private static readonly int[] SampleCounts = { 0, 2, 4, 8 };
        private static readonly string[] SampleNames = { "Выкл", "2×", "4×", "8×" };
        private GraphicsRow graphicsFocus;
        private GraphicsRow? graphicsPress;
        private int pressedSample;

        internal void OpenGraphicsControls()
        {
            graphicsFocus = GraphicsRow.Msaa;
            graphicsPress = null;
        }

        internal void CloseGraphicsControls()
        {
            graphicsPress = null;
            RaidGraphicsSettings.Save();
        }

        private static Rect GraphicsRect(GraphicsRow row, float rail, float height)
        {
            const float edge = 20f;
            float w = rail - edge * 2f;
            if (row == GraphicsRow.Msaa) return new Rect(edge, 66f, w, 46f);
            if (row == GraphicsRow.Reset) return new Rect(edge, height - 49f, w - 85f, 32f);
            if (row == GraphicsRow.Back) return new Rect(rail - edge - 77f, height - 49f, 77f, 32f);
            const float top = 122f;
            int rows = (int)GraphicsRow.Reset - 1;
            float step = Mathf.Min(52f, (height - 80f - top) / rows);
            float rowHeight = GraphicsSlider(row) ? step - 5f : Mathf.Min(35f, step - 5f);
            return new Rect(edge, top + ((int)row - 1) * step, w, rowHeight);
        }

        private static Rect SampleButton(Rect row, int i)
        {
            float width = (row.width - 12f) / 4f;
            return new Rect(row.x + i * (width + 4f), row.y + 21f, width, row.height - 21f);
        }

        private static bool GraphicsSlider(GraphicsRow row) =>
            row is GraphicsRow.Strength or GraphicsRow.Radius or GraphicsRow.Intensity or GraphicsRow.Exposure;

        private static bool GraphicsAvailable(GraphicsRow row) => row switch
        {
            GraphicsRow.Strength or GraphicsRow.Radius => RaidGraphicsSettings.AmbientOcclusion,
            GraphicsRow.Intensity => RaidGraphicsSettings.LocalReflections,
            GraphicsRow.Exposure => RaidGraphicsSettings.Tonemapping,
            _ => true
        };

        private static float GraphicsValue(GraphicsRow row) => row switch
        {
            GraphicsRow.Strength => RaidGraphicsSettings.OcclusionStrength,
            GraphicsRow.Radius => RaidGraphicsSettings.OcclusionRadius,
            GraphicsRow.Exposure => RaidGraphicsSettings.ExposureEV,
            _ => RaidGraphicsSettings.ReflectionIntensity
        };

        private static void GraphicsRange(GraphicsRow row, out float min, out float max, out float step)
        {
            (min, max, step) = row switch
            {
                GraphicsRow.Radius => (.1f, 1.2f, .02f),
                GraphicsRow.Exposure => (RaidToneMapping.MinExposure, RaidToneMapping.MaxExposure, .1f),
                _ => (0f, 2f, .05f)
            };
        }

        private static void SetGraphicsValue(GraphicsRow row, float value)
        {
            GraphicsRange(row, out float min, out float max, out float step);
            value = Mathf.Clamp(min + Mathf.Round((value - min) / step) * step, min, max);
            if (Mathf.Approximately(value, GraphicsValue(row))) return;
            if (row == GraphicsRow.Strength) RaidGraphicsSettings.OcclusionStrength = value;
            else if (row == GraphicsRow.Radius) RaidGraphicsSettings.OcclusionRadius = value;
            else if (row == GraphicsRow.Exposure) RaidGraphicsSettings.ExposureEV = value;
            else RaidGraphicsSettings.ReflectionIntensity = value;
        }

        private void ActivateGraphics(GraphicsRow row)
        {
            switch (row)
            {
                case GraphicsRow.WindowCookies: RaidGraphicsSettings.WindowCookies = !RaidGraphicsSettings.WindowCookies; break;
                case GraphicsRow.Tonemapping: RaidGraphicsSettings.Tonemapping = !RaidGraphicsSettings.Tonemapping; break;
                case GraphicsRow.Occlusion: RaidGraphicsSettings.AmbientOcclusion = !RaidGraphicsSettings.AmbientOcclusion; break;
                case GraphicsRow.Reflections: RaidGraphicsSettings.LocalReflections = !RaidGraphicsSettings.LocalReflections; break;
                case GraphicsRow.Reset: RaidGraphicsSettings.RestoreDefaults(); break;
                case GraphicsRow.Back: MenuAction(RaidMenuAction.BackToMenu); break;
            }
        }

        private void GraphicsKey(Event ev)
        {
            bool up = ev.keyCode == KeyCode.UpArrow || ev.keyCode == KeyCode.Tab && ev.shift;
            bool down = ev.keyCode == KeyCode.DownArrow || ev.keyCode == KeyCode.Tab;
            int side = ev.keyCode == KeyCode.RightArrow ? 1 : ev.keyCode == KeyCode.LeftArrow ? -1 : 0;
            bool enter = ev.keyCode == KeyCode.Space || ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter;
            if (up || down)
            {
                int count = (int)GraphicsRow.Back + 1;
                do graphicsFocus = (GraphicsRow)(((int)graphicsFocus + (up ? -1 : 1) + count) % count);
                while (!GraphicsAvailable(graphicsFocus));
            }
            else if (graphicsFocus == GraphicsRow.Msaa && (side != 0 || enter))
            {
                int i = Array.IndexOf(SampleCounts, RaidGraphicsSettings.Msaa);
                i = (i + (side < 0 ? -1 : 1) + SampleCounts.Length) % SampleCounts.Length;
                RaidGraphicsSettings.Msaa = SampleCounts[i];
            }
            else if (side != 0 && GraphicsSlider(graphicsFocus))
            {
                GraphicsRange(graphicsFocus, out _, out _, out float step);
                SetGraphicsValue(graphicsFocus, GraphicsValue(graphicsFocus) + side * step);
            }
            else if (enter && !GraphicsSlider(graphicsFocus)) ActivateGraphics(graphicsFocus);
            else return;
            ev.Use();
        }

        private bool GraphicsPointer(EventType type, Vector2 point, float rail, float height)
        {
            if (type == EventType.MouseDown)
            {
                graphicsPress = null;
                for (int i = 0; i <= (int)GraphicsRow.Back; i++)
                {
                    var row = (GraphicsRow)i;
                    Rect target = GraphicsRect(row, rail, height);
                    if (!GraphicsAvailable(row) || !target.Contains(point)) continue;
                    if (row == GraphicsRow.Msaa)
                    {
                        pressedSample = -1;
                        for (int j = 0; j < SampleCounts.Length; j++)
                            if (SampleButton(target, j).Contains(point)) pressedSample = j;
                        if (pressedSample < 0) break;
                    }
                    graphicsPress = graphicsFocus = row;
                    break;
                }
            }
            if (!graphicsPress.HasValue) return false;
            GraphicsRow pressed = graphicsPress.Value;
            Rect rect = GraphicsRect(pressed, rail, height);
            if (GraphicsSlider(pressed))
            {
                GraphicsRange(pressed, out float min, out float max, out _);
                float t = Mathf.InverseLerp(rect.x + 4f, rect.xMax - 4f, point.x);
                SetGraphicsValue(pressed, Mathf.Lerp(min, max, t));
            }
            if (type == EventType.MouseUp)
            {
                graphicsPress = null;
                if (pressed == GraphicsRow.Msaa)
                {
                    if (SampleButton(rect, pressedSample).Contains(point)) RaidGraphicsSettings.Msaa = SampleCounts[pressedSample];
                }
                else if (!GraphicsSlider(pressed) && rect.Contains(point)) ActivateGraphics(pressed);
            }
            return true;
        }

        private void DrawGraphicsControls(float width, float height)
        {
            float rail = SettingsRail(width);
            var ev = Event.current;
            if (!Application.isFocused) graphicsPress = null;
            if (Application.isFocused && ev.type == EventType.KeyDown) { GraphicsKey(ev); return; }
            if (Application.isFocused && ev.button == 0 &&
                (ev.type == EventType.MouseDown || ev.type == EventType.MouseDrag || ev.type == EventType.MouseUp))
            {
                if (GraphicsPointer(ev.type, ev.mousePosition, rail, height)) ev.Use();
                return;
            }
            if (ev.type != EventType.Repaint) return;
            Block(0, 0, rail, height, StationInk);
            Block(rail - 1f, 0, 1f, height, Rule);
            Text(20, 9, rail - 40f, 32f, "Графика", 24, Paper, true);
            Text(20, 41, rail - 40f, 21f, "Цветовое пространство", 12, Muted);
            string colorSpace = QualitySettings.activeColorSpace == ColorSpace.Linear ? "Linear" : "Gamma";
            Text(20, 41, rail - 40f, 21f, colorSpace, 12, Mint, true, TextAnchor.MiddleRight);
            Text(20f, height - 79f, rail - 40f, 24f, "ПКМ над сценой + WASD для движения", 12, Muted);
            for (int i = 0; i <= (int)GraphicsRow.Back; i++)
            {
                var row = (GraphicsRow)i;
                Rect rect = GraphicsRect(row, rail, height);
                bool highlight = row == graphicsFocus || Application.isFocused && rect.Contains(ev.mousePosition);
                DrawGraphicsRow(row, rect, highlight);
            }
        }

        private void DrawGraphicsRow(GraphicsRow row, Rect r, bool highlight)
        {
            if (row == GraphicsRow.Msaa)
            {
                Text(r.x, r.y, r.width, 20f, "Сглаживание MSAA", 14, highlight ? Mint : Paper, true);
                for (int i = 0; i < SampleCounts.Length; i++)
                {
                    Rect button = SampleButton(r, i);
                    bool active = SampleCounts[i] == RaidGraphicsSettings.Msaa;
                    Block(button.x, button.y, button.width, button.height, active ? Mint : StationRow);
                    Text(button.x, button.y, button.width, button.height, SampleNames[i], 13,
                        active ? StationInk : Paper, active, TextAnchor.MiddleCenter);
                }
                return;
            }
            if (GraphicsSlider(row))
            {
                GraphicsRange(row, out float min, out float max, out _);
                float value = GraphicsValue(row);
                bool enabled = GraphicsAvailable(row);
                Color color = enabled ? highlight ? Paper : Muted : Color.Lerp(Muted, StationInk, .48f);
                string name = row switch
                {
                    GraphicsRow.Radius => "Радиус",
                    GraphicsRow.Strength => "Сила затенения",
                    GraphicsRow.Exposure => "Экспозиция",
                    _ => "Яркость отражений"
                };
                string readout = row switch
                {
                    GraphicsRow.Exposure => value.ToString("+0.0;-0.0;0.0") + " EV",
                    GraphicsRow.Radius => Mathf.RoundToInt(value * 100f) + " см",
                    _ => Mathf.RoundToInt(value * 100f) + " %"
                };
                Text(r.x + 8f, r.y, r.width - 80f, 22f, name, 14, color, enabled && highlight);
                Text(r.xMax - 70f, r.y, 70f, 22f, readout, 13, color, false, TextAnchor.MiddleRight);
                float length = r.width - 8f, filled = length * Mathf.InverseLerp(min, max, value);
                float y = r.y + r.height * .68f;
                Block(r.x + 4f, y, length, 3f, StationRow);
                Block(r.x + 4f, y, filled, 3f, highlight && enabled ? Mint : Rule);
                if (row == GraphicsRow.Exposure) Block(r.center.x - .5f, y + 3f, 1f, 3f, Muted);
                Block(r.x + filled + 1f, y - 3f, 6f, 9f, color);
                return;
            }
            bool? on = row switch
            {
                GraphicsRow.Occlusion => RaidGraphicsSettings.AmbientOcclusion,
                GraphicsRow.Reflections => RaidGraphicsSettings.LocalReflections,
                GraphicsRow.WindowCookies => RaidGraphicsSettings.WindowCookies,
                GraphicsRow.Tonemapping => RaidGraphicsSettings.Tonemapping,
                _ => null
            };
            var buttonColor = highlight ? Color.Lerp(StationRow, Mint, .16f) : StationRow;
            float h = Mathf.Min(r.height, 35f);
            Block(r.x, r.y, r.width, h, buttonColor);
            if (highlight) Block(r.x, r.y, 3f, h, Mint);
            string label = row switch
            {
                GraphicsRow.Occlusion => "Контактные тени (AO)",
                GraphicsRow.Reflections => "Отражения интерьеров",
                GraphicsRow.WindowCookies => "Свет от оконных рам",
                GraphicsRow.Tonemapping => "Тональная компрессия",
                GraphicsRow.Reset => "По умолчанию",
                _ => "Назад"
            };
            Text(r.x + 9f, r.y, r.width - (on.HasValue ? 55f : 18f), h, label, 14, Paper, on.HasValue,
                on.HasValue ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter);
            if (on.HasValue)
                Text(r.xMax - 53f, r.y, 43f, h, on.Value ? "Вкл" : "Выкл", 13, on.Value ? Mint : Muted, true, TextAnchor.MiddleRight);
        }
    }
}
