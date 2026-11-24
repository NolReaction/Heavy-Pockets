#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HeavyPockets
{
    internal static class MapPalette
    {
        private const string Root = "Assets/HeavyPockets";
        internal const string Library = Root + "/Art/MapPieces";
        private const string Kits = Root + "/Resources/MapKits";

        internal static WorldPlan Sheet(bool training)
        {
            string path = Root + "/Resources/" + WorldPlan.Resource(training) + ".asset";
            return AssetAt<WorldPlan>(path);
        }

        private static T AssetAt<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset) return asset;
            string folder = Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), Path.GetFileName(folder));
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        internal static MapPiece[] Read()
        {
            if (!AssetDatabase.IsValidFolder(Library)) return Array.Empty<MapPiece>();
            var result = new List<MapPiece>();
            foreach (string guid in AssetDatabase.FindAssets("t:MapPiece", new[] {Library}))
                result.Add(AssetDatabase.LoadAssetAtPath<MapPiece>(AssetDatabase.GUIDToAssetPath(guid)));
            result.Sort((a,b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
            return result.ToArray();
        }

        internal static MapPiece Register(GameObject prefab)
        {
            string source = AssetDatabase.GetAssetPath(prefab);
            if (!prefab || string.IsNullOrEmpty(source) || AssetDatabase.LoadMainAssetAtPath(source) != prefab ||
                PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.NotAPrefab)
                throw new ArgumentException("Выберите префаб или модель из окна Project.");
            // The streamed parent replaces the prefab root's position and rotation, preserving its scale.
            var origin = Matrix4x4.TRS(prefab.transform.position, prefab.transform.rotation, Vector3.one).inverse;
            Bounds envelope = default;
            bool found = false;
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !Active(renderer.transform, prefab.transform)) continue;
                var box = GeometryUtility.CalculateBounds(Corners(renderer.localBounds), origin * renderer.localToWorldMatrix);
                if (found) envelope.Encapsulate(box);
                else { envelope = box; found = true; }
            }
            if (!found) throw new ArgumentException("У выбранной модели нет видимой геометрии.");
            var walls = new List<Bounds>();
            var glazing = new List<Bounds>();
            int transparent = LayerMask.NameToLayer("TransparentFX");
            foreach (var box in prefab.GetComponentsInChildren<BoxCollider>(true))
                if (box.enabled && !box.isTrigger && Active(box.transform, prefab.transform))
                {
                    var shapes = box.gameObject.layer == transparent ? glazing : walls;
                    shapes.Add(GeometryUtility.CalculateBounds(Corners(new Bounds(box.center, box.size)), origin * box.transform.localToWorldMatrix));
                }

            string guid = AssetDatabase.AssetPathToGUID(source);
            var kit = AssetAt<PortCellAsset>(Kits + "/" + guid + ".asset");
            kit.Props = new[] { new PortCellAsset.PlacedProp { Prefab = prefab, Rotation = Quaternion.identity, Scale = 1 } };
            kit.Surfaces = Array.Empty<PortCellAsset.Surface>();
            kit.Signs = Array.Empty<PortCellAsset.Lettering>();
            var piece = AssetAt<MapPiece>(Library + "/" + guid + ".asset");
            if (string.IsNullOrEmpty(piece.Title)) piece.Title = ObjectNames.NicifyVariableName(prefab.name);
            piece.Tile = "MapKits/" + guid;
            piece.Envelope = envelope;
            piece.Solids = walls.ToArray();
            piece.Glazing = glazing.ToArray();
            EditorUtility.SetDirty(kit);
            EditorUtility.SetDirty(piece);
            AssetDatabase.SaveAssets();
            return piece;
        }

        private static bool Active(Transform child, Transform root)
        {
            for (var t = child; t != null; t = t.parent)
            {
                if (!t.gameObject.activeSelf) return false;
                if (t == root) break;
            }
            return true;
        }

        private static Vector3[] Corners(Bounds b)
        {
            var result = new Vector3[8];
            for (int i = 0; i < 8; i++)
                result[i] = new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
            return result;
        }
    }
}
#endif
