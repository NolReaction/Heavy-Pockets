#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace HeavyPockets
{
    internal sealed partial class RaidScenery
    {
        private const string CellOutput = "Assets/HeavyPockets/Resources/";
        private sealed class TileGeometry
        {
            internal Transform Near, Far;
            internal PlantMesh Woods, Woodline, Sedge;
            internal readonly List<PortCellAsset.PlacedProp> Props = new List<PortCellAsset.PlacedProp>();

            internal void Place(string artPath, Vector3 position, Quaternion rotation, float scale = 1f)
            {
                string path = "Assets/HeavyPockets/Art/" + artPath + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!prefab) throw new FileNotFoundException("Missing harbor prop for tile bake.", path);
                Props.Add(new PortCellAsset.PlacedProp {
                    Prefab = prefab, Position = position, Rotation = rotation, Scale = scale
                });
            }
        }

        // All procedural high-poly meshes are authored here, before a build. The player reads native meshes.
        private sealed class CellBake
        {
            internal Transform Root;
            internal readonly SortedDictionary<Vector2Int, TileGeometry> Tiles =
                new SortedDictionary<Vector2Int, TileGeometry>(Comparer<Vector2Int>.Create((a,b) =>
                    a.x == b.x ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x)));

            internal TileGeometry At(float x, float z)
            {
                var key = PortCellAsset.Key(x,z);
                if (Tiles.TryGetValue(key,out var tile)) return tile;
                int seed = unchecked(7423 + 3947 * key.x + 113 * key.y);
                tile = new TileGeometry {
                    Near = Panel(Root,"Near",0,0,0,0), Far = Panel(Root,"Horizon",0,0,0,0),
                    Woods = new PlantMesh(seed), Woodline = new PlantMesh(seed+1), Sedge = new PlantMesh(seed+2)
                };
                Tiles.Add(key,tile);
                return tile;
            }

            internal void PlaceWarehouse(string artPath, Bounds footprint)
            {
                Vector3 footing = new Vector3(footprint.center.x, footprint.min.y, footprint.center.z);
                At(footing.x, footing.z).Place(artPath, footing, Quaternion.identity);
            }
        }

        [MenuItem("Heavy Pockets/Карта/Пересчитать клетки порта", true)]
        private static bool CanBakePortCells() => !EditorApplication.isPlayingOrWillChangePlaymode;

        [MenuItem("Heavy Pockets/Карта/Пересчитать клетки порта")]
        public static void BakePortCells()
        {
            if (!CanBakePortCells()) throw new System.InvalidOperationException("Bake port cells in Edit Mode.");
            var preview = EditorSceneManager.NewPreviewScene();
            var holder = new GameObject("Port bake workspace") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(holder, preview);
            var scenery = new RaidScenery(holder.transform, true);
            try
            {
                var bake = new CellBake { Root = holder.transform };
                int serial = 0;
                foreach (var solid in OutdoorLayout.Solids)
                {
                    if (solid.Kind == OutdoorKind.Pier || solid.Kind == OutdoorKind.SignalPost) continue;
                    var c = solid.Bounds.center;
                    var tile = bake.At(c.x,c.z);
                    scenery.BuildYardSolid(tile.Near, solid, serial);
                    scenery.BuildSolidHorizon(tile.Far, solid, serial);
                    if (solid.Kind == OutdoorKind.Container) ++serial;
                }
                bake.PlaceWarehouse("DockWarehouse/ReceivingWarehouse", OutdoorLayout.ReceivingWarehouse.Bounds);
                bake.PlaceWarehouse("DockWarehouse/QuayWorkshop", OutdoorLayout.QuayWorkshop.Bounds);
                var houseTile = bake.At(ChartHouseLayout.Origin.x, ChartHouseLayout.Origin.z);
                houseTile.Place("ChartRadio/ChartRadio", ChartHouseLayout.Origin + ChartHouseLayout.RadioPosition,
                    ChartHouseLayout.RadioRotation, ChartHouseLayout.RadioScale);
                scenery.BakeVegetation(bake);
                if (!AssetDatabase.IsValidFolder(CellOutput + "PortCells"))
                    AssetDatabase.CreateFolder(CellOutput.TrimEnd('/'),"PortCells");
                string catalogPath = CellOutput + "PortHorizon.asset";
                var catalog = ScriptableObject.CreateInstance<PortCellAsset>();
                catalog.HorizonCells = new PortCellAsset[bake.Tiles.Count];
                AssetDatabase.CreateAsset(catalog,catalogPath);
                int i=0;
                foreach (var entry in bake.Tiles)
                {
                    var (key,tile) = (entry.Key,entry.Value);
                    string path = CellOutput + PortCellAsset.TileName(key) + ".asset";
                    var detail = ScriptableObject.CreateInstance<PortCellAsset>();
                    detail.Cell = key;
                    detail.Props = tile.Props.ToArray();
                    AssetDatabase.CreateAsset(detail,path);
                    scenery.WriteTile(detail,tile.Near,tile.Woods,tile.Sedge,path);
                    if (key == PortCellAsset.Key(ChartHouseLayout.Origin.x, ChartHouseLayout.Origin.z))
                        scenery.BakeChartHouseMeshes(detail,path);

                    var distant = ScriptableObject.CreateInstance<PortCellAsset>();
                    distant.name = $"Horizon {key.x},{key.y}";
                    distant.Cell = key;
                    AssetDatabase.AddObjectToAsset(distant,catalogPath);
                    scenery.WriteTile(distant,tile.Far,tile.Woodline,null,catalogPath);
                    catalog.HorizonCells[i++] = distant;
                }
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
                foreach (string guid in AssetDatabase.FindAssets("t:PortCellAsset", new[] {CellOutput+"PortCells"}))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var tile = AssetDatabase.LoadAssetAtPath<PortCellAsset>(path);
                    if (tile != null && !bake.Tiles.ContainsKey(tile.Cell) && Path.GetFileName(path).StartsWith("Tile_"))
                        AssetDatabase.DeleteAsset(path);
                }
                Debug.Log($"Port streaming bake: {catalog.HorizonCells.Length} native tiles.");
            }
            finally
            {
                Object.DestroyImmediate(holder);
                foreach (Mesh generated in scenery.shellMeshes) Object.DestroyImmediate(generated);
                foreach (Material surface in scenery.surfaces) Object.DestroyImmediate(surface);
                Object.DestroyImmediate(scenery.signInk);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        partial void BakeChartHouseMeshes(PortCellAsset tile, string path);

        private void WriteTile(PortCellAsset asset, Transform source, PlantMesh trees, PlantMesh grass, string path)
        {
            var groups = new Dictionary<Material,List<CombineInstance>>();
            var surfacesToSave = new List<PortCellAsset.Surface>();
            var signs = new List<PortCellAsset.Lettering>();
            foreach (var text in source.GetComponentsInChildren<TextMesh>())
                signs.Add(new PortCellAsset.Lettering { Text=text.text, Position=text.transform.position,
                    Rotation=text.transform.rotation, Size=text.characterSize, Color=text.color });
            foreach (var filter in source.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.GetComponent<TextMesh>() != null || filter.sharedMesh == null) continue;
                var material = filter.GetComponent<Renderer>().sharedMaterial;
                if (!groups.TryGetValue(material,out var faces)) groups.Add(material,faces = new List<CombineInstance>());
                faces.Add(new CombineInstance { mesh=filter.sharedMesh, transform=filter.transform.localToWorldMatrix });
            }
            foreach (var group in groups)
            {
                int count=0;
                foreach (var face in group.Value) count += face.mesh.vertexCount;
                var mesh = new Mesh { name=group.Key.name,
                    indexFormat=count>65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.CombineMeshes(group.Value.ToArray(),true,true);
                bool casts = group.Key != glass && group.Key != skyGlass && group.Key != sodium && group.Key != exitLight;
                SaveMesh(surfacesToSave,mesh,group.Key.name,casts,path);
            }
            SavePlant(surfacesToSave,trees,true,path);
            SavePlant(surfacesToSave,grass,false,path);
            asset.Surfaces = surfacesToSave.ToArray();
            asset.Signs = signs.ToArray();
            EditorUtility.SetDirty(asset);
        }

        private static void SavePlant(List<PortCellAsset.Surface> surfaces, PlantMesh source, bool casts, string path)
        {
            if (source == null || source.Vertices.Count == 0) return;
            var mesh = new Mesh { name=casts ? "Shelterbelt" : "Quayside sedge",
                indexFormat=source.Vertices.Count>65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(source.Vertices);
            mesh.SetColors(source.Colors);
            if (source.CardUVs != null) mesh.SetUVs(0, source.CardUVs);
            mesh.SetTriangles(source.Triangles,0);
            mesh.RecalculateNormals();
            SaveMesh(surfaces,mesh,"Coastal vegetation and granite",casts,path);
        }

        private static void SaveMesh(List<PortCellAsset.Surface> list, Mesh mesh, string material, bool casts, string path)
        {
            AssetDatabase.AddObjectToAsset(mesh,path);
            list.Add(new PortCellAsset.Surface { Mesh=mesh, Material=material, CastShadows=casts });
        }

        private void BuildSolidHorizon(Transform parent, OutdoorSolid solid, int serial)
        {
            Bounds bounds = solid.Bounds;
            Vector3 mid = bounds.center, size = bounds.size;
            var site = Panel(parent,solid.Kind.ToString(),mid.x,0f,mid.z,0f);
            Material paint = steel;
            switch (solid.Kind)
            {
                case OutdoorKind.Container:
                    paint = serial%4 == 0 ? oxide : serial%4 == 1 ? teal : serial%4 == 2 ? steel : chalk;
                    break;
                case OutdoorKind.FreightCrate: paint = oxide; break;
                case OutdoorKind.Barrier: paint = concrete; break;
                case OutdoorKind.Shed:
                    paint=oxide;
                    Part(site,"Roof",0,size.y-.10f,0,size.x,.20f,size.z,teal);
                    size.y-=.20f; mid.y=size.y*.5f;
                    break;
                case OutdoorKind.WagonDeck:
                case OutdoorKind.LocomotiveDeck:
                    paint=black; mid.y=1.02f; size.y=.9f;
                    break;
                case OutdoorKind.DieselHood:
                    paint=teal; size.y=1.6f; mid.y=bounds.max.y-size.y*.5f;
                    break;
                case OutdoorKind.DriverCab:
                    Part(site,"Cab roof",0,3.98f,0,size.x,.2f,size.z,oxide);
                    Part(site,"Cab windows",0,3.40f,0,size.x-.1f,.95f,size.z-.1f,glass);
                    paint=teal; mid.y=2.2f; size.y=1.47f;
                    break;
                case OutdoorKind.TimberLoad:
                    paint=oxide; mid.y=2.24f; size.y=1.55f;
                    break;
            }
            Part(site,"Distant hull",0,mid.y,0,size.x,size.y,size.z,paint);
        }
    }
}
#endif
