#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections;
using UnityEngine;

namespace HeavyPockets
{
    public sealed partial class RaidProbe
    {
        private static bool ChartHousePresent()
        {
            foreach (var owner in Object.FindObjectsByType<ChartHouseAssets>(FindObjectsSortMode.None))
                if (owner.Finishes != null && owner.GetComponentInChildren<MeshFilter>() is MeshFilter filter && filter.sharedMesh) return true;
            return false;
        }

        private IEnumerator CheckChartHouse()
        {
            // B starts north of the partition. Both routes go around it on the east.
            if (role == "A") yield return Walk(new Vector2(4, -5));
            yield return Walk(new Vector2(4, 5));
            Vector3 house = ChartHouseLayout.Origin;
            float doorX = house.x + (ChartHouseLayout.DoorLeft + ChartHouseLayout.DoorRight) * .5f;
            var entrance = new Vector2(doorX, house.z - 5.3f);
            yield return Walk(entrance);
            yield return Walk(new Vector2(doorX, house.z - 1.8f));
            Phase("chart house interior loads");
            yield return new WaitUntil(ChartHousePresent);
            Signal("house-entered");
            if (role == "A")
            {
                Capture("chart-house");
                Phase("second player leaves for the southeast corner");
                yield return new WaitUntil(() => Exists("B.house-unloaded"));
                if (!ChartHousePresent()) { Fail("The other client unloaded the occupied chart house"); yield break; }
                Signal("house-retained");
                yield return new WaitUntil(() => Exists("B.house-returned"));
                yield break;
            }

            yield return Walk(entrance);
            yield return Walk(new Vector2(6f, 4f));
            yield return Walk(new Vector2(6f, -39f), sprint: true);
            yield return Walk(new Vector2(66f, -49f), sprint: true);
            Phase("distant chart house unloads");
            yield return new WaitUntil(() => !ChartHousePresent());
            Signal("house-unloaded");
            yield return new WaitUntil(() => Exists("A.house-retained"));
            yield return Walk(new Vector2(6f, -39f), sprint: true);
            yield return Walk(new Vector2(6f, 4f), sprint: true);
            yield return Walk(entrance);
            yield return Walk(new Vector2(doorX, house.z - 1.8f));
            Phase("chart house returns on approach");
            yield return new WaitUntil(ChartHousePresent);
            Signal("house-returned");
        }
    }
}
#endif
