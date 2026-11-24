using System;
using UnityEngine;

namespace HeavyPockets
{
    internal sealed partial class RaidHud
    {
        private static readonly StarterMotion[] PreviewMotions = (StarterMotion[])Enum.GetValues(typeof(StarterMotion));
        private static readonly Color StudioSlate = new Color32(38, 58, 64, 255);
        private AnimationStudio studio;
        private Action leavePreview;
        private int previewRow, previewPress = -1;
        private bool turningPreview, testPreview;
        private int PreviewClipCount => testPreview ? 2 : PreviewMotions.Length;
        internal StarterMotion PreviewMotion => PreviewMotions[previewRow];

        internal void OpenAnimationPreview(bool graphics, Action leave)
        {
            CloseAnimationPreview();
            leavePreview = leave;
            previewRow = 0;
            testPreview = false;
            if (graphics)
            {
                studio = new AnimationStudio();
                studio.Select(PreviewMotion);
            }
        }

        internal void CloseAnimationPreview()
        {
            studio?.Dispose();
            studio = null;
            leavePreview = null;
            turningPreview = false;
            previewPress = -1;
        }

        internal void UpdateAnimationPreview() => studio?.Advance(Time.unscaledDeltaTime);

        internal void AnimationPreviewKey(int rowStep, int stepFrame, bool toggle)
        {
            if (leavePreview == null) return;
            if (rowStep != 0) SelectPreview((previewRow + rowStep + PreviewClipCount) % PreviewClipCount);
            if (stepFrame != 0) studio?.Step(stepFrame);
            if (toggle) studio?.TogglePlayback();
        }

        private void SelectPreview(int row)
        {
            previewRow = row;
            if (testPreview) studio?.SelectTest(row == 1);
            else studio?.Select(PreviewMotion);
        }

        private void SelectPreviewModel(bool test)
        {
            if (testPreview == test) return;
            testPreview = test;
            turningPreview = false;
            SelectPreview(0);
        }

        private void DrawAnimationPreview(float width, float height)
        {
            var ev = Event.current;
            bool paint = ev.type == EventType.Repaint;
            float rail = Mathf.Clamp(width * .375f, 300f, 390f);
            const float margin = 25f, top = 130f;
            float content = rail - 2 * margin;
            float rowHeight = Mathf.Min(44f, (height - top - 64f) / PreviewClipCount);
            Rect stage = new Rect(rail, 0, width - rail, height);
            if (paint)
            {
                Block(0, 0, rail, height, StationInk);
                Block(rail, 0, stage.width, height, StudioSlate);
                DrawAnimationStudio(stage);
                Block(rail, 0, 2, height, Amber);
                PocketMark(margin, 18, Amber);
                Text(margin + 28, 12, content - 28, 30, "Heavy Pockets", 16, Paper, true);
                Text(margin, 49, content, 35, "Анимации", 29, Paper, true);
            }

            if (Application.isFocused && ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Tab)
            {
                SelectPreviewModel(!testPreview);
                ev.Use();
                return;
            }
            float tabWidth = (content - 5f) * .5f;
            if (PreviewButton(new Rect(margin, 88, tabWidth, 30f), 110, "Unity Starter", !testPreview))
            { SelectPreviewModel(false); return; }
            if (PreviewButton(new Rect(margin + tabWidth + 5f, 88, tabWidth, 30f), 111, "Testchar", testPreview))
            { SelectPreviewModel(true); return; }

            for (int i = 0; i < PreviewClipCount; i++)
            {
                var row = new Rect(margin, top + i * rowHeight, content, rowHeight - 4f);
                string label = testPreview ? (i == 0 ? "Движение из FBX" : "Т-поза") : StarterRunner.Label(PreviewMotions[i]);
                if (PreviewButton(row, i, label, i == previewRow))
                {
                    SelectPreview(i);
                    return;
                }
            }
            if (PreviewButton(new Rect(margin, height - 48f, content, 32f), 100, "Назад"))
            {
                leavePreview?.Invoke();
                return;
            }

            if (studio != null && studio.Ready && DrawPreviewControls(stage)) return;
            if (!Application.isFocused) { turningPreview = false; previewPress = -1; return; }
            if (ev.type == EventType.MouseDown && ev.button == 0 && ev.mousePosition.y < height - 76f && stage.Contains(ev.mousePosition))
            {
                turningPreview = true;
                ev.Use();
            }
            else if (turningPreview && ev.type == EventType.MouseDrag)
            {
                studio?.Turn(-ev.delta.x * .65f);
                ev.Use();
            }
            else if (ev.type == EventType.MouseUp)
            {
                turningPreview = false;
                previewPress = -1;
            }
        }

        private void DrawAnimationStudio(Rect stage)
        {
            if (studio == null) return;
            GUI.color = Color.white;
            GUI.DrawTexture(stage, studio.Backdrop, ScaleMode.StretchToFill);
            if (!studio.Ready)
            {
                Text(stage.x, stage.center.y - 20f, stage.width, 40f,
                    studio.Loading ? "Загрузка модели…" : "Не удалось загрузить модель", 17, Paper, false, TextAnchor.MiddleCenter);
                return;
            }
            float imageHeight = Mathf.Min(stage.height - 90f, stage.width * 1.2f);
            float imageWidth = imageHeight * (5f / 6f);
            var portrait = new Rect(stage.center.x - imageWidth * .5f, (stage.height - imageHeight - 74f) * .5f, imageWidth, imageHeight);
            if (!studio.CloseUp)
            {
                float y = portrait.y + portrait.height * studio.FloorY;
                GUI.DrawTexture(new Rect(portrait.center.x - portrait.width * .29f, y - 10f,
                    portrait.width * .58f, 20f), studio.Contact);
            }
            GUI.DrawTexture(portrait, studio.Image, ScaleMode.StretchToFill, true);
        }

        private bool DrawPreviewControls(Rect stage)
        {
            const float button = 34f, space = 6f;
            float width = Mathf.Min(stage.width - 38f, 460f);
            float x = stage.center.x - width * .5f, y = stage.height - 66f;
            if (Event.current.type == EventType.Repaint)
            {
                Block(x, y, width, 50f, StationInk);
                Text(x + width - 81f, y + 3f, 73f, 17f, $"{studio.Frame + 1} / {studio.FrameCount}",
                    12, Muted, false, TextAnchor.MiddleRight);
            }
            Rect slider = new Rect(x + 8f, y + 7f, width - 98f, 10f);
            float fraction = studio.Fraction;
            float seek = GUI.HorizontalSlider(slider, fraction, 0f, 1f);
            if (!Mathf.Approximately(fraction, seek)) studio.Seek(seek);

            float cursor = x + 8f;
            y += 23f;
            if (PreviewButton(new Rect(cursor, y, button, 22f), 101, "‹")) { studio.Step(-1); return true; }
            cursor += button + space;
            if (PreviewButton(new Rect(cursor, y, 76f, 22f), 102, studio.Playing ? "Пауза" : "Пуск"))
            { studio.TogglePlayback(); return true; }
            cursor += 76f + space;
            if (PreviewButton(new Rect(cursor, y, button, 22f), 103, "›")) { studio.Step(1); return true; }
            if (PreviewButton(new Rect(x + width - 125f, y, 117f, 22f), 104, studio.CloseUp ? "В полный рост" : "Приблизить"))
            { studio.SetCloseUp(!studio.CloseUp); return true; }
            return false;
        }

        private bool PreviewButton(Rect rect, int id, string caption, bool selected = false)
        {
            var ev = Event.current;
            if (ev.type == EventType.Repaint)
            {
                Color fill = selected ? StudioSlate : StationRow;
                if (rect.Contains(ev.mousePosition)) fill = Color.Lerp(fill, Mint, .2f);
                Block(rect.x, rect.y, rect.width, rect.height, fill);
                if (selected) Block(rect.x, rect.y, 3f, rect.height, Mint);
                Text(rect.x, rect.y, rect.width, rect.height, caption, caption.Length == 1 ? 19 : 14,
                    selected ? Mint : Paper, true, TextAnchor.MiddleCenter);
            }
            if (!Application.isFocused || ev.button != 0 || !rect.Contains(ev.mousePosition)) return false;
            if (ev.type == EventType.MouseDown) { previewPress = id; ev.Use(); }
            else if (ev.type == EventType.MouseUp)
            {
                bool click = previewPress == id;
                previewPress = -1;
                ev.Use();
                return click;
            }
            return false;
        }
    }
}
