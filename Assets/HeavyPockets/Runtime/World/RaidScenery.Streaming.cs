using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HeavyPockets
{
    internal sealed partial class RaidScenery
    {
        // Distance to the nearest point of the 24 m square, not to its centre.
        private const float EnterDistance = 32f, LeaveDistance = 44f;
        private sealed class PortCell
        {
            internal PortCellAsset Horizon;
            internal TileRequest Near;
            internal GameObject Proxy, Detail;
            internal string ResourcePath;
            internal bool House, Failed;
        }

        private readonly List<PortCell> portCells = new List<PortCell>();
        private readonly Dictionary<string, Material> cellMaterials = new Dictionary<string, Material>();
        private PortCell loadingCell;
        private sealed class TileRequest
        {
            private static readonly Dictionary<string, TileRequest> active = new Dictionary<string, TileRequest>();
            internal readonly ResourceRequest Request;
            private readonly string path;
            private int references;

            private TileRequest(string path)
            {
                this.path = path;
                Request = Resources.LoadAsync<PortCellAsset>(path);
                Request.completed += _ => Collect();
            }

            internal static TileRequest Acquire(string path)
            {
                if (!active.TryGetValue(path, out var tile))
                    active.Add(path, tile = new TileRequest(path));
                tile.references++;
                return tile;
            }

            internal void Release()
            {
                references--;
                if (Request.isDone) Collect();
            }

            private void Collect()
            {
                // A cancelled, unfinished request can be adopted by a newly opened port.
                if (references != 0 || !active.TryGetValue(path, out var current) || current != this) return;
                active.Remove(path);
                if (!(Request.asset is PortCellAsset asset)) return;
                foreach (var surface in asset.Surfaces)
                    if (surface.Mesh != null) Resources.UnloadAsset(surface.Mesh);
                UnloadMeshes(asset.ChartHouseShell);
                UnloadMeshes(asset.ChartHouseDetail);
                Resources.UnloadAsset(asset);
            }

            private static void UnloadMeshes(Mesh[] meshes)
            {
                if (meshes == null) return;
                foreach (Mesh mesh in meshes) if (mesh != null) Resources.UnloadAsset(mesh);
            }
        }
        private Vector2 lastStreamingPoint = new Vector2(float.PositiveInfinity, 0f);
        internal bool StreamingSettled { get; private set; }
        internal int LoadedCellCount { get; private set; }

        private void InitializeStreaming()
        {
            foreach (Material surface in surfaces) cellMaterials[surface.name] = surface;
            var catalog = Resources.Load<PortCellAsset>("PortHorizon");
            if (catalog == null || catalog.HorizonCells == null)
                throw new System.InvalidOperationException("The baked PortHorizon catalog is missing.");
            Vector2Int houseKey = PortCellAsset.Key(ChartHouseLayout.Origin.x, ChartHouseLayout.Origin.z);
            foreach (PortCellAsset distant in catalog.HorizonCells)
            {
                var cell = new PortCell { Horizon = distant, House = distant.Cell == houseKey,
                    ResourcePath = PortCellAsset.TileName(distant.Cell) };
                cell.Proxy = InstantiateCell(distant, "Port silhouette");
                if (cell.House) BuildChartHouseProxy(cell.Proxy.transform);
                portCells.Add(cell);
            }
            TickStreaming(Vector2.zero);
        }

        private GameObject InstantiateCell(PortCellAsset asset, string label)
        {
            var tile = new GameObject($"{label} [{asset.Cell.x},{asset.Cell.y}]");
            tile.transform.SetParent(root, false);
            foreach (var surface in asset.Surfaces)
            {
                var part = new GameObject(surface.Material, typeof(MeshFilter), typeof(MeshRenderer));
                part.transform.SetParent(tile.transform, false);
                part.GetComponent<MeshFilter>().sharedMesh = surface.Mesh;
                var r = part.GetComponent<MeshRenderer>();
                r.sharedMaterial = cellMaterials[surface.Material];
                r.shadowCastingMode = surface.CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
            foreach (var sign in asset.Signs)
            {
                WallText(tile.transform, sign.Text, sign.Position, 0, sign.Size, sign.Color);
                tile.transform.GetChild(tile.transform.childCount - 1).localRotation = sign.Rotation;
            }
            return tile;
        }

        private static float DistanceSquared(Vector2 point, Vector2Int key)
        {
            float x = Mathf.Max(0f, Mathf.Max(key.x * PortCellAsset.Width - point.x, point.x - (key.x + 1) * PortCellAsset.Width));
            float z = Mathf.Max(0f, Mathf.Max(key.y * PortCellAsset.Width - point.y, point.y - (key.y + 1) * PortCellAsset.Width));
            return x*x + z*z;
        }

        internal void TickStreaming(Vector2 playerXZ)
        {
            if (StreamingSettled && (playerXZ - lastStreamingPoint).sqrMagnitude < 1f) return;
            lastStreamingPoint = playerXZ;
            StreamingSettled = false;
            if (loadingCell != null && DistanceSquared(playerXZ, loadingCell.Horizon.Cell) > LeaveDistance * LeaveDistance)
                CancelTileLoad();
            if (loadingCell != null && loadingCell.Near.Request.isDone &&
                (!loadingCell.House || HarborInteriorMaterials.Ready))
            {
                var cell = loadingCell;
                var asset = cell.Near.Request.asset as PortCellAsset;
                if (asset == null)
                {
                    CancelTileLoad();
                    cell.Failed = true;
                    Debug.LogError($"Missing detailed harbor tile {cell.ResourcePath}.");
                }
                else
                {
                    loadingCell = null;
                    cell.Detail = InstantiateCell(asset, "Port cell");
                    if (cell.House)
                    {
                        try { BuildChartHouse(cell.Detail.transform, asset.ChartHouseShell, asset.ChartHouseDetail); }
                        catch (System.Exception ex)
                        {
                            // Keep the distant hull and allow the remaining map to stream.
                            cell.Detail.SetActive(false);
                            Object.Destroy(cell.Detail);
                            cell.Detail = null;
                            cell.Near.Release();
                            cell.Near = null;
                            cell.Failed = true;
                            Debug.LogException(ex);
                            return;
                        }
                        finally { HarborInteriorMaterials.CancelPrefetch(); }
                    }
                    cell.Proxy.SetActive(false);
                    LoadedCellCount++;
                    return; // Budget one activation or unload per frame.
                }
            }

            PortCell next = null;
            float nearest = EnterDistance * EnterDistance;
            foreach (var cell in portCells)
            {
                float distance = DistanceSquared(playerXZ, cell.Horizon.Cell);
                if (cell.Detail != null && distance > LeaveDistance * LeaveDistance)
                {
                    cell.Proxy.SetActive(true);
                    cell.Detail.SetActive(false);
                    Object.Destroy(cell.Detail);
                    cell.Detail = null;
                    cell.Near.Release();
                    cell.Near = null;
                    LoadedCellCount--;
                    return;
                }
                if (cell.Detail == null && !cell.Failed && distance <= nearest)
                {
                    nearest = distance;
                    next = cell;
                }
            }
            if (loadingCell != null) return;
            if (next == null) { StreamingSettled = true; return; }
            loadingCell = next;
            if (next.House) HarborInteriorMaterials.Prefetch();
            next.Near = TileRequest.Acquire(next.ResourcePath);
        }

        private void CancelTileLoad()
        {
            if (loadingCell.House) HarborInteriorMaterials.CancelPrefetch();
            loadingCell.Near.Release();
            loadingCell.Near = null;
            loadingCell = null;
        }

        private void DisposeStreaming()
        {
            if (loadingCell != null) CancelTileLoad();
            foreach (var cell in portCells)
            {
                if (cell.Detail != null)
                {
                    cell.Detail.SetActive(false);
                    Object.Destroy(cell.Detail);
                    cell.Near.Release();
                }
                Object.Destroy(cell.Proxy);
            }
            portCells.Clear();
        }
    }
}
