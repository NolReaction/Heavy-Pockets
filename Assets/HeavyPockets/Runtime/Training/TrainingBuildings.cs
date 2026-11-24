using System.Collections.Generic;
using UnityEngine;

namespace HeavyPockets
{
    internal static class TrainingBuildings
    {
        internal static readonly Vector3 ChartHouse = new Vector3(41f, 0, 9f);
        internal static readonly Vector3 Warehouse = new Vector3(49f, 0, 29f);

        internal static void AddTranslated(List<Bounds> destination, Bounds[] geometry, Vector3 offset)
        {
            foreach (Bounds source in geometry)
            {
                var placed = source;
                placed.center += offset;
                destination.Add(placed);
            }
        }
    }

    internal sealed partial class RaidScenery
    {
        private void InitializeTrainingBuildings()
        {
            foreach (var surface in surfaces) cellMaterials[surface.name] = surface;
            AddBuilding("ChartHouse", TrainingBuildings.ChartHouse, true);
            AddBuilding("ReceivingWarehouse", TrainingBuildings.Warehouse, false);
            BuildHarborPrint(TrainingBuildings.Warehouse);
        }

        private void AddBuilding(string resource, Vector3 origin, bool chartHouse)
        {
            var proxy = Panel(root, resource + " silhouette", origin.x, origin.y, origin.z, 0);
            if (chartHouse) BuildChartHouseProxy(proxy, Vector3.zero);
            else
            {
                var shell = new ChartHouseMeshes(false);
                foreach (var wall in ReceivingWarehouseLayout.FrontMasonry) shell.Box(oxide,wall);
                foreach (var wall in ReceivingWarehouseLayout.SideMasonry) shell.Box(oxide,wall);
                shell.Box(teal,ReceivingWarehouseLayout.Roof);
                shell.FinishProxy(proxy,proxy.gameObject.AddComponent<ChartHouseAssets>());
                MountDistantWarehouseLeaves(proxy, origin);
            }
            portCells.Add(new PortCell {
                Key = PortCellAsset.Key(origin.x, origin.z), ResourcePath = "PortBuildings/" + resource,
                Proxy = proxy.gameObject, House = chartHouse, Offset = origin,
                Visibility = BuildingVisibility,
                Extents = chartHouse ? new Vector2(6f,4f) : new Vector2(7f,4.5f)
            });
        }
    }
}
