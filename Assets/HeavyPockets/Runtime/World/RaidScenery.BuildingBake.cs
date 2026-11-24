#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HeavyPockets
{
    internal sealed partial class RaidScenery
    {
        // Small reusable kits borrow the original prefabs and baked meshes. Their metadata
        // is rebuilt together with the port, so a change to an interior appears in both scenes.
        internal static void BakeBuildingKits()
        {
            const string folder = "Assets/HeavyPockets/Resources/PortBuildings";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets/HeavyPockets/Resources", "PortBuildings");
            Write("ChartHouse", ChartHouseLayout.Origin, true, "ChartRadio/ChartRadio");
            Write("ReceivingWarehouse", ReceivingWarehouseLayout.Origin, false, "DockWarehouse/ReceivingWarehouse", "DockWeighing/FloorScale");

            void Write(string name, Vector3 origin, bool house, params string[] prefabs)
            {
                string tileName = PortCellAsset.TileName(PortCellAsset.Key(origin.x,origin.z));
                var source = AssetDatabase.LoadAssetAtPath<PortCellAsset>(CellOutput + tileName + ".asset");
                if (!source) throw new InvalidOperationException("Missing baked building tile " + tileName);
                string path = folder + "/" + name + ".asset";
                var kit = AssetDatabase.LoadAssetAtPath<PortCellAsset>(path);
                if (!kit)
                {
                    kit = ScriptableObject.CreateInstance<PortCellAsset>();
                    AssetDatabase.CreateAsset(kit,path);
                }
                var props = new List<PortCellAsset.PlacedProp>();
                foreach (string art in prefabs)
                {
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HeavyPockets/Art/" + art + ".prefab");
                    int index = Array.FindIndex(source.Props, p => p.Prefab == model);
                    if (!model || index < 0) throw new InvalidOperationException("Missing placed prefab " + art);
                    var part = source.Props[index];
                    part.Position -= origin;
                    props.Add(part);
                }
                kit.Props = props.ToArray();
                kit.Surfaces = Array.Empty<PortCellAsset.Surface>();
                kit.Signs = Array.Empty<PortCellAsset.Lettering>();
                kit.ChartHouseShell = house ? source.ChartHouseShell : Array.Empty<Mesh>();
                kit.ChartHouseDetail = house ? source.ChartHouseDetail : Array.Empty<Mesh>();
                EditorUtility.SetDirty(kit);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
