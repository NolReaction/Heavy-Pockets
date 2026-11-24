#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace HeavyPockets
{
    internal sealed class WorldPlanWindow : EditorWindow
    {
        private bool training = true, placing, dragging;
        private WorldPlan plan;
        private MapPiece[] palette = Array.Empty<MapPiece>();
        private string[] names = Array.Empty<string>();
        private int chosen, selected = -1, turns, grid = 1;
        private static readonly float[] Steps = { .5f, 1, 2, 4 };
        private static readonly string[] StepNames = { "0,5 м", "1 м", "2 м", "4 м" };
        private Vector2 center, dragOffset;
        private float zoom = 1, scale;
        private Rect canvas;
        private GameObject source, previewModel;
        private MapPiece previewPiece;
        private string notice;
        private GUIStyle caption;

        [MenuItem("Heavy Pockets/Карта/План карты")]
        internal static void Open()
        {
            var window = GetWindow<WorldPlanWindow>("План карты");
            window.minSize = new Vector2(640, 390);
            window.Show();
        }

        private void OnEnable() { wantsMouseMove = true; Undo.undoRedoPerformed += Repaint; Reload(); }
        private void OnDisable() { Undo.undoRedoPerformed -= Repaint; }
        private void Reload()
        {
            plan = MapPalette.Sheet(training);
            palette = MapPalette.Read();
            names = Array.ConvertAll(palette, piece => piece.Title);
            chosen = Mathf.Clamp(chosen, 0, Mathf.Max(0, palette.Length - 1));
            selected = -1; placing = dragging = false; notice = "";
        }

        private void OnGUI()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("Выйдите из Play Mode, чтобы редактировать карту.", MessageType.Info);
                return;
            }
            if (!plan) Reload();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                int next = EditorGUILayout.Popup(training ? 0 : 1, new[] {"Учебный полигон", "Грузовой порт"}, GUILayout.Width(145));
                if (next != (training ? 0 : 1)) { training = next == 0; center = Vector2.zero; zoom = 1; Reload(); }
                GUILayout.Space(10);
                GUILayout.Label("Сетка", GUILayout.Width(41));
                grid = EditorGUILayout.Popup(grid, StepNames, GUILayout.Width(65));
                if (GUILayout.Button("Вся карта", EditorStyles.toolbarButton)) { center = Vector2.zero; zoom = 1; }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Сохранить", EditorStyles.toolbarButton)) AssetDatabase.SaveAssetIfDirty(plan);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(215))) Sidebar();
                canvas = GUILayoutUtility.GetRect(100, 5000, 100, 5000, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
            }
            if (canvas.width <= 24 || canvas.height <= 24) return;
            scale = Mathf.Min((canvas.width - 22) / (RaidMap.RoomX * 2), (canvas.height - 22) / (RaidMap.RoomZ * 2)) * zoom;
            if (Event.current.type == EventType.Repaint) DrawPlan();
            HandleInput();
        }

        private bool Selected => selected >= 0 && selected < plan.Objects.Count;
        private void Sidebar()
        {
            GUILayout.Space(8);
            GUILayout.Label("Палитра", EditorStyles.boldLabel);
            if (palette.Length > 0) chosen = EditorGUILayout.Popup(chosen, names);
            if (position.height > 480 && palette.Length > 0)
                Preview(Selected ? plan.Objects[selected].Piece : palette[chosen]);
            using (new EditorGUI.DisabledScope(palette.Length == 0))
            {
                bool mode = GUILayout.Toggle(placing, "Разместить на карте", "Button");
                if (mode != placing) { placing = mode; selected = -1; turns = 0; }
            }
            GUILayout.Space(8);
            source = (GameObject)EditorGUILayout.ObjectField("Модель", source, typeof(GameObject), false);
            using (new EditorGUI.DisabledScope(!source))
                if (GUILayout.Button("Добавить / обновить в палитре"))
                {
                    try {
                        var registered = MapPalette.Register(source);
                        Reload(); chosen = Array.IndexOf(palette, registered);
                        notice = registered.Solids.Length == 0 && registered.Glazing.Length == 0 ? "Без коллизии. Для стен добавьте BoxCollider к префабу." : "Модель сохранена в палитре.";
                    } catch (ArgumentException ex) { notice = ex.Message; }
                }
            if (!string.IsNullOrEmpty(notice)) EditorGUILayout.HelpBox(notice, MessageType.Info);
            GUILayout.Space(14);
            if (Selected)
            {
                var item = plan.Objects[selected];
                GUILayout.Label(item.Piece ? item.Piece.Title : "Модель недоступна", EditorStyles.boldLabel);
                var coords = EditorGUILayout.Vector3Field("Положение, м", item.Position);
                if (coords != item.Position) { item.Position = coords; Set(item); }
                if (GUILayout.Button("Повернуть 90°")) Rotate();
                if (GUILayout.Button("Удалить с карты")) Remove();
            }
            else if (placing && palette.Length > 0)
            {
                GUILayout.Label("Поворот   " + (turns * 90) + "°");
                if (GUILayout.Button("Повернуть 90°")) Rotate();
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label("ЛКМ — поставить или выбрать\nПеретащите выбранный объект\nR — повернуть, Esc — отменить\nКолесо — масштаб, СКМ — сдвиг", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(8);
        }

        private void Preview(MapPiece piece)
        {
            if (piece != previewPiece)
            {
                previewPiece = piece;
                previewModel = null;
                if (piece)
                {
                    var kit = AssetDatabase.LoadAssetAtPath<PortCellAsset>("Assets/HeavyPockets/Resources/" + piece.Tile + ".asset");
                    if (kit && kit.Props?.Length > 0) previewModel = kit.Props[0].Prefab;
                }
            }
            if (!previewModel) return;
            Rect card = GUILayoutUtility.GetRect(40, 1000, 80, 80);
            EditorGUI.DrawRect(card, new Color(.09f,.12f,.13f));
            var image = AssetPreview.GetAssetPreview(previewModel);
            if (image) GUI.DrawTexture(card, image, ScaleMode.ScaleToFit, true);
            else if (AssetPreview.IsLoadingAssetPreview(previewModel.GetInstanceID())) Repaint();
        }

        private Vector2 Screen(Vector2 world) => canvas.center + Vector2.Scale(world-center, new Vector2(scale,-scale));
        private Vector2 World(Vector2 point) => center + Vector2.Scale(point-canvas.center, new Vector2(1/scale,-1/scale));
        private Vector2 Snap(Vector2 p) => new Vector2(Mathf.Round(p.x / Steps[grid]), Mathf.Round(p.y / Steps[grid])) * Steps[grid];
        private Rect Area(Bounds b)
        {
            Vector2 a = Screen(new Vector2(b.min.x, b.max.z)), end = Screen(new Vector2(b.max.x, b.min.z));
            return Rect.MinMaxRect(a.x, a.y, Mathf.Max(a.x+4, end.x), Mathf.Max(a.y+4, end.y));
        }
        private void RectOnCanvas(Rect area, Color tint)
        {
            Rect clipped = Rect.MinMaxRect(Mathf.Max(area.xMin,canvas.xMin), Mathf.Max(area.yMin,canvas.yMin),
                Mathf.Min(area.xMax,canvas.xMax), Mathf.Min(area.yMax,canvas.yMax));
            if (clipped.width > 0 && clipped.height > 0) EditorGUI.DrawRect(clipped, tint);
        }
        private void DrawPlan()
        {
            caption ??= new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.white }, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
            EditorGUI.DrawRect(canvas, new Color(.075f,.10f,.12f));
            var field = Area(new Bounds(Vector3.zero, new Vector3(RaidMap.RoomX*2, 0, RaidMap.RoomZ*2)));
            RectOnCanvas(field, new Color(.15f,.21f,.22f));
            float interval = Steps[grid];
            while (interval * scale < 10) interval *= 2;
            var line = new Color(.26f,.34f,.34f);
            for (float x = -RaidMap.RoomX; x <= RaidMap.RoomX; x += interval)
                RectOnCanvas(new Rect(Screen(new Vector2(x,0)).x, field.y, 1, field.height), line);
            for (float z = -RaidMap.RoomZ; z <= RaidMap.RoomZ; z += interval)
                RectOnCanvas(new Rect(field.x, Screen(new Vector2(0,z)).y, field.width, 1), line);
            var fixedColor = new Color(.32f,.39f,.39f);
            DrawBox(new Bounds(training ? TrainingBuildings.ChartHouse : ChartHouseLayout.Origin, new Vector3(12,1,8)), fixedColor, "Штурманская");
            DrawBox(new Bounds(training ? TrainingBuildings.Warehouse : ReceivingWarehouseLayout.Origin, new Vector3(14,1,9)), fixedColor, "Склад");
            if (training)
            {
                foreach (var part in TrainingLayout.Parts) RectOnCanvas(Area(part.Box), fixedColor);
                foreach (var run in TrainingLayout.Paving)
                    RectOnCanvas(Area(new Bounds(new Vector3(run.Area.center.x,0,run.Area.center.y), new Vector3(run.Area.width,1,run.Area.height))), fixedColor);
                if (Event.current.type == EventType.Repaint)
                {
                    GUI.BeginClip(canvas);
                    Vector3 OnCanvas(Vector2 p) => Screen(p) - canvas.position;
                    using (new Handles.DrawingScope(new Color(.60f,.57f,.44f)))
                        foreach (var approach in TrainingApproach.Layout(plan))
                        {
                            var path = approach.Sections;
                            for (int j=1; j<path.Length; j++)
                                Handles.DrawAAConvexPolygon(OnCanvas(path[j-1].Left),OnCanvas(path[j-1].Right),
                                    OnCanvas(path[j].Right),OnCanvas(path[j].Left));
                        }
                    GUI.EndClip();
                }
            }
            else
                foreach (var solid in OutdoorLayout.Occluders) RectOnCanvas(Area(solid), new Color(.32f,.39f,.39f,.5f));
            Vector2 spawn = training ? TrainingGround.Spawn : new Vector2(-4,-9);
            Vector2 dot = Screen(spawn);
            RectOnCanvas(new Rect(dot.x-3,dot.y-3,6,6), new Color(.97f,.81f,.44f));
            for (int i = 0; i < plan.Objects.Count; i++)
            {
                var item = plan.Objects[i];
                if (item.Piece) DrawObject(item, i == selected ? new Color(.78f,.49f,.23f) : new Color(.13f,.46f,.48f));
            }
            if (placing && canvas.Contains(Event.current.mousePosition) && palette.Length > 0)
            {
                var ghost = AtMouse();
                DrawObject(ghost, new Color(.56f,.76f,.69f,.70f));
            }
        }
        private void DrawObject(WorldPlan.Placement item, Color color)
        {
            var bounds = item.Place(item.Piece.Envelope);
            DrawBox(bounds, color, item.Piece.Title);
            var rect = Area(bounds);
            Vector3 face = item.Rotation * Vector3.forward;
            Vector2 tip = rect.center + Vector2.Scale(new Vector2(face.x, -face.z), rect.size * .5f);
            RectOnCanvas(new Rect(tip.x - 2, tip.y - 2, 4, 4), Color.white);
        }

        private void DrawBox(Bounds box, Color color, string label)
        {
            Rect rect = Area(box);
            RectOnCanvas(rect, color);
            if (rect.width >= 28 && canvas.Contains(rect.min) && canvas.Contains(rect.max)) GUI.Label(rect, label, caption);
        }
        private WorldPlan.Placement AtMouse()
        {
            Vector2 pos = Snap(World(Event.current.mousePosition));
            return new WorldPlan.Placement { Piece = palette[chosen], Position = new Vector3(pos.x,0,pos.y), Turns = turns };
        }
        private void Set(WorldPlan.Placement item)
        {
            if (item.Piece)
            {
                var bounds = item.Place(item.Piece.Envelope);
                item.Position.x += Mathf.Max(0,-RaidMap.RoomX-bounds.min.x) - Mathf.Max(0,bounds.max.x-RaidMap.RoomX);
                item.Position.z += Mathf.Max(0,-RaidMap.RoomZ-bounds.min.z) - Mathf.Max(0,bounds.max.z-RaidMap.RoomZ);
            }
            Undo.RecordObject(plan, "Изменить план карты");
            plan.Objects[selected] = item;
            EditorUtility.SetDirty(plan);
        }
        private void Rotate()
        {
            if (placing) turns = (turns + 1) % 4;
            else if (Selected) { var item = plan.Objects[selected]; item.Turns = (item.Turns + 1) & 3; Set(item); }
        }
        private void Remove()
        {
            if (!Selected) return;
            Undo.RecordObject(plan, "Удалить с карты");
            plan.Objects.RemoveAt(selected); selected = -1; EditorUtility.SetDirty(plan);
        }
        private void HandleInput()
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown && !EditorGUIUtility.editingTextField)
            {
                if (e.keyCode == KeyCode.R) Rotate();
                else if (e.keyCode == KeyCode.Delete) Remove();
                else if (e.keyCode == KeyCode.Escape) { placing = dragging = false; selected = -1; }
                else return;
                e.Use(); Repaint(); return;
            }
            if (e.type == EventType.MouseUp && dragging) { dragging = false; e.Use(); return; }
            if (!canvas.Contains(e.mousePosition)) return;
            if (e.type == EventType.ScrollWheel)
            {
                Vector2 anchor = World(e.mousePosition);
                float next = Mathf.Clamp(zoom * Mathf.Exp(-e.delta.y * .075f), .7f, 6);
                center = anchor - (anchor-center) * (zoom/next);
                zoom = next;
            }
            else if (e.type == EventType.MouseDrag && e.button == 2) center -= new Vector2(e.delta.x,-e.delta.y) / scale;
            else if (e.type == EventType.MouseDown && e.button == 0)
            {
                GUI.FocusControl(null);
                if (placing && palette.Length > 0)
                {
                    var item = AtMouse();
                    var bounds = item.Place(item.Piece.Envelope);
                    if (bounds.min.x < -RaidMap.RoomX || bounds.max.x > RaidMap.RoomX || bounds.min.z < -RaidMap.RoomZ || bounds.max.z > RaidMap.RoomZ) return;
                    Undo.RecordObject(plan, "Разместить на карте");
                    plan.Objects.Add(item); selected = plan.Objects.Count-1; placing = false; EditorUtility.SetDirty(plan);
                }
                else
                {
                    selected = -1;
                    for (int i = plan.Objects.Count-1; i >= 0; i--)
                    {
                        var item = plan.Objects[i];
                        if (!item.Piece || !Area(item.Place(item.Piece.Envelope)).Contains(e.mousePosition)) continue;
                        selected = i; dragOffset = new Vector2(item.Position.x,item.Position.z) - World(e.mousePosition);
                        dragging = true; break;
                    }
                }
            }
            else if (e.type == EventType.MouseDrag && dragging && Selected)
            {
                var item = plan.Objects[selected];
                var pos = Snap(World(e.mousePosition) + dragOffset);
                item.Position = new Vector3(pos.x,item.Position.y,pos.y); Set(item);
            }
            else { if (placing && e.type == EventType.MouseMove) Repaint(); return; }
            e.Use(); Repaint();
        }
    }
}
#endif
