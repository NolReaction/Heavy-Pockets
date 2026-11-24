using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HeavyPockets
{
    internal sealed partial class RaidScenery
    {
        // Distance to the nearest point of the 24 m square, not to its centre.
        private const float EnterDistance = 32f, LeaveDistance = 44f;
        // A two-storey facade and its windows are visible from across the training field.
        private const float BuildingVisibility = 3.5f;
        private const int MaxPendingCells = 3;
        private sealed class PortCell
        {
            internal Vector2Int Key;
            internal Vector3 Offset;
            internal Vector2 Extents, CenterShift;
            internal bool Planned;
            internal Quaternion Rotation;
            internal float Visibility = 1f;
            internal TileRequest Near;
            internal GameObject Proxy, Detail;
            internal string ResourcePath;
            internal bool House, Failed;
        }

        private readonly List<PortCell> portCells = new List<PortCell>();
        private readonly Dictionary<string, Material> cellMaterials = new Dictionary<string, Material>();
        private sealed class TileRequest
        {
            private static readonly Dictionary<string, TileRequest> active = new Dictionary<string, TileRequest>();
            internal static bool Idle => active.Count == 0;
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
        // A cancelled cell can still finish loading borrowed meshes from the other map.
        internal static bool ReleasedAllCells => TileRequest.Idle;
        internal int LoadedCellCount { get; private set; }

        internal static System.Collections.IEnumerator PreloadMapAssets(bool training)
        {
            // These are the large dependencies used by the spawn view on both maps.
            // Resources.Load in the builders then reuses Unity's loaded objects.
            var requests = new List<ResourceRequest> {
                Resources.LoadAsync<GameObject>(StarterRunner.ModelPath),
                Resources.LoadAsync<RuntimeAnimatorController>(StarterRunner.ControllerPath),
                Resources.LoadAsync<GameObject>("CargoChest"), Resources.LoadAsync<GameObject>("DispatchDesk"),
                Resources.LoadAsync<Material>("SurfaceGarden/Coastal surface"),
                Resources.LoadAsync<Texture2D>("DepotGrain"), Resources.LoadAsync<Texture2D>("HarborScreenprint"),
                Resources.LoadAsync<Texture2D>("HarborCloudField")
            };
            if (training)
            {
                requests.Add(Resources.LoadAsync<Texture2D>("TrainingDirectory"));
                requests.Add(Resources.LoadAsync<Texture2D>("SurfaceGarden/Coastal Branches"));
                requests.Add(Resources.LoadAsync<Material>("SurfaceGarden/Meadow blades"));
                requests.Add(Resources.LoadAsync<Material>(FloorMaterials + "Quayside setts"));
                requests.Add(Resources.LoadAsync<Material>(FloorMaterials + "Basalt cobbles"));
                foreach (var sample in TrainingLayout.FloorSamples)
                    requests.Add(Resources.LoadAsync<Material>(FloorMaterials + sample.Material));
            }
            else
            {
                requests.Add(Resources.LoadAsync<PortCellAsset>("PortHorizon"));
                requests.Add(Resources.LoadAsync<Texture2D>("ShoreConifers"));
                requests.Add(Resources.LoadAsync<Texture2D>("QuaysidePlants"));
                requests.Add(Resources.LoadAsync<Texture2D>("QuaysideBark"));
            }
            foreach (var request in requests)
                if (!request.isDone) yield return request;
        }

        private void RegisterPlanCells(WorldPlan plan)
        {
            if (!plan) return;
            foreach (var item in plan.Objects)
            {
                if (!item.Piece || string.IsNullOrEmpty(item.Piece.Tile)) continue;
                var box = item.Place(item.Piece.Envelope);
                portCells.Add(new PortCell {
                    Planned = true, ResourcePath = item.Piece.Tile, Offset = item.Position, Rotation = item.Rotation,
                    Extents = new Vector2(box.extents.x, box.extents.z),
                    CenterShift = new Vector2(box.center.x - item.Position.x, box.center.z - item.Position.z),
                    Visibility = Mathf.Clamp(Mathf.Max(box.size.x, box.size.y, box.size.z) / 5f, 1f, 4f)
                });
            }
        }

        private void InitializeStreaming()
        {
            foreach (Material surface in surfaces) cellMaterials[surface.name] = surface;
            var catalog = Resources.Load<PortCellAsset>("PortHorizon");
            if (catalog == null || catalog.HorizonCells == null)
                throw new System.InvalidOperationException("The baked PortHorizon catalog is missing.");
            Vector2Int houseKey = PortCellAsset.Key(ChartHouseLayout.Origin.x, ChartHouseLayout.Origin.z);
            var warehouse = ReceivingWarehouseLayout.Origin;
            Vector2Int warehouseKey = PortCellAsset.Key(warehouse.x, warehouse.z);
            var workshop = OutdoorLayout.QuayWorkshop.Bounds.center;
            Vector2Int workshopKey = PortCellAsset.Key(workshop.x, workshop.z);
            foreach (PortCellAsset distant in catalog.HorizonCells)
            {
                var cell = new PortCell { Key = distant.Cell, House = distant.Cell == houseKey,
                    Visibility = distant.Cell == houseKey || distant.Cell == warehouseKey || distant.Cell == workshopKey
                        ? BuildingVisibility : 1f,
                    ResourcePath = PortCellAsset.TileName(distant.Cell) };
                InstantiateCell(distant, "Port silhouette", out cell.Proxy);
                if (cell.House) BuildChartHouseProxy(cell.Proxy.transform,ChartHouseLayout.Origin);
                if (distant.Cell == warehouseKey)
                    MountDistantWarehouseLeaves(cell.Proxy.transform, Vector3.zero);
                portCells.Add(cell);
            }
        }

        private void MountDistantWarehouseLeaves(Transform parent, Vector3 parentOrigin)
        {
            var center = (TrainingGround.Active ? TrainingBuildings.Warehouse : ReceivingWarehouseLayout.Origin) - parentOrigin;
            Part(parent, "Distant receiving floor", center.x, center.y - .045f, center.z,
                ReceivingWarehouseLayout.Width, .09f, ReceivingWarehouseLayout.Depth, concrete);
            var mechanism = WorldMechanisms.PlacedWarehouseDoor;
            var hinge = mechanism.Pivot - parentOrigin;
            var leaf = Panel(parent, "Distant personnel door", hinge.x, hinge.y, hinge.z, 0);
            float w = ReceivingWarehouseLayout.DoorWidth, h = ReceivingWarehouseLayout.DoorHeight;
            Part(leaf, "Steel", w*.5f, h*.5f, 0, w, h, ReceivingWarehouseLayout.DoorThickness, steel);
            mechanism.Bind(leaf);

            var gate = WorldMechanisms.PlacedWarehouseGate;
            var bottom = gate.Pivot - parentOrigin;
            var shutter = Panel(parent, "Distant loading shutter", bottom.x, bottom.y, bottom.z, 0);
            h = ReceivingWarehouseLayout.GateHeight;
            Part(shutter, "Curtain", 0, h*.5f, 0, ReceivingWarehouseLayout.GateWidth, h,
                ReceivingWarehouseLayout.GateThickness, teal);
            gate.Bind(shutter);
        }

        private void InstantiateCell(PortCellAsset asset, string label, out GameObject tile, Vector3 offset = default, bool planned = false)
        {
            tile = new GameObject($"{label} [{asset.Cell.x},{asset.Cell.y}]");
            tile.transform.SetParent(root, false);
            tile.transform.localPosition = offset;
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
            if (asset.Props != null)
                foreach (var prop in asset.Props)
                {
                    var instance = Object.Instantiate(prop.Prefab, tile.transform, false).transform;
                    instance.SetLocalPositionAndRotation(prop.Position, prop.Rotation);
                    instance.localScale *= prop.Scale;
                    if (planned)
                    {
                        // Only the shared collision world participates in physics.
                        foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                        continue;
                    }
                    if (instance.TryGetComponent<WarehouseFloorScale>(out var scale)) FloorScale = scale;
                    var door = instance.Find(ReceivingWarehouseLayout.DoorLeafName);
                    if (door != null)
                    {
                        WorldMechanisms.PlacedWarehouseDoor.Bind(door);
                        MountFreightCases(instance);
                        Object.Instantiate(Resources.Load<GameObject>("DispatchDesk"), instance, false);
                        if (TrainingGround.Active) BuildWarehouseFloorFinish(instance);
                    }
                    var gate = instance.Find(ReceivingWarehouseLayout.GateLeafName);
                    if (gate != null) WorldMechanisms.PlacedWarehouseGate.Bind(gate);
                }
        }

        private static float DistanceSquared(Vector2 point, PortCell cell)
        {
            if (cell.Extents != Vector2.zero)
            {
                float dx = Mathf.Max(0, Mathf.Abs(point.x - cell.Offset.x - cell.CenterShift.x) - cell.Extents.x);
                float dz = Mathf.Max(0, Mathf.Abs(point.y - cell.Offset.z - cell.CenterShift.y) - cell.Extents.y);
                return (dx*dx + dz*dz) / (cell.Visibility * cell.Visibility);
            }
            var key = cell.Key;
            float x = Mathf.Max(0f, Mathf.Max(key.x * PortCellAsset.Width - point.x, point.x - (key.x + 1) * PortCellAsset.Width));
            float z = Mathf.Max(0f, Mathf.Max(key.y * PortCellAsset.Width - point.y, point.y - (key.y + 1) * PortCellAsset.Width));
            return (x*x + z*z) / (cell.Visibility * cell.Visibility);
        }

        internal void TickStreaming(Vector2 playerXZ)
        {
            if (StreamingSettled && (playerXZ - lastStreamingPoint).sqrMagnitude < 1f) return;
            lastStreamingPoint = playerXZ;
            StreamingSettled = false;
            PortCell ready = null, next = null;
            float nextDistance = EnterDistance * EnterDistance, readyDistance = float.PositiveInfinity;
            int pending = 0;
            foreach (var cell in portCells)
            {
                if (cell.Failed) continue;
                float distance = DistanceSquared(playerXZ, cell);
                if (cell.Near == null)
                {
                    if (distance <= nextDistance) { next = cell; nextDistance = distance; }
                    continue;
                }
                if (distance > LeaveDistance * LeaveDistance)
                {
                    if (cell.Detail)
                    {
                        if (cell.Proxy) cell.Proxy.SetActive(true);
                        cell.Detail.SetActive(false);
                        Object.Destroy(cell.Detail);
                        cell.Detail = null;
                        LoadedCellCount--;
                    }
                    else if (cell.House) HarborInteriorMaterials.CancelPrefetch();
                    cell.Near.Release();
                    cell.Near = null;
                    return;
                }
                if (cell.Detail) continue;
                pending++;
                if (distance < readyDistance && cell.Near.Request.isDone && (!cell.House || HarborInteriorMaterials.Ready))
                {
                    ready = cell;
                    readyDistance = distance;
                }
            }

            // Overlap disk reads, while budgeting a single activation (or unload) per frame.
            if (next != null && pending < MaxPendingCells)
            {
                if (next.House) HarborInteriorMaterials.Prefetch();
                next.Near = TileRequest.Acquire(next.ResourcePath);
            }
            if (ready != null) ActivateCell(ready);
            else StreamingSettled = pending == 0 && next == null;
        }

        private void ActivateCell(PortCell cell)
        {
            try
            {
                if (!(cell.Near.Request.asset is PortCellAsset asset))
                    throw new System.InvalidOperationException($"Missing detailed harbor tile {cell.ResourcePath}.");
                InstantiateCell(asset, "Port cell", out cell.Detail, cell.Offset, cell.Planned);
                if (cell.Planned) cell.Detail.transform.localRotation = cell.Rotation;
                if (cell.House)
                    BuildChartHouse(cell.Detail.transform, asset.ChartHouseShell, asset.ChartHouseDetail,
                        cell.Extents == Vector2.zero ? ChartHouseLayout.Origin : Vector3.zero);
                if (cell.Proxy) cell.Proxy.SetActive(false);
                LoadedCellCount++;
            }
            catch (System.Exception ex)
            {
                if (cell.Detail)
                {
                    cell.Detail.SetActive(false);
                    Object.Destroy(cell.Detail);
                    cell.Detail = null;
                }
                cell.Near.Release();
                cell.Near = null;
                cell.Failed = true;
                Debug.LogException(ex);
            }
            finally
            {
                if (cell.House) HarborInteriorMaterials.CancelPrefetch();
            }
        }

        private void DisposeStreaming()
        {
            foreach (var cell in portCells)
            {
                if (cell.Detail)
                {
                    cell.Detail.SetActive(false);
                    Object.Destroy(cell.Detail);
                }
                else if (cell.Near != null && cell.House) HarborInteriorMaterials.CancelPrefetch();
                cell.Near?.Release();
                Object.Destroy(cell.Proxy);
            }
            portCells.Clear();
        }
    }
}
