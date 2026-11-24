using UnityEngine;

namespace HeavyPockets
{
    internal sealed partial class RaidScenery
    {
        private void BuildRoom()
        {
            BuildYardPaving();
            BuildRailAndQuay();
            BuildPartition();
            BuildYardStructures();
            BuildExtraction();
            BuildHarborLaunch();
        }

        private void BuildYardPaving()
        {
            Transform a = architecture;
            // Keep the drivable surfaces flush with the common movement plane.
            Part(a, "Receiving apron", 0, -.052f, -1, 30, .12f, 20, concrete);
            for (float x = -10; x < 15; x += 5)
                Part(a, "Saw cut", x, .011f, -1, .028f, .003f, 20, floorFinish);
            for (float z = -6; z < 9; z += 5)
                Part(a, "Saw cut", 0, .012f, z, 30, .003f, .028f, floorFinish);
            Part(a, "Harbor service road", 11, -.03f, 0, 7, .103f, 115, floorFinish);
            Part(a, "North service road", 8, -.029f, 31, 116, .11f, 6, floorFinish);
            Part(a, "Dispatch spur", RaidMap.Exit.x, -.028f, 40, 6, .121f, 17, floorFinish);
            Part(a, "Crane runway", 38, -.056f, 20, 19, .14f, 18, concrete);

            for (int z = -54; z < 56; z += 7)
            {
                if (Mathf.Abs(z - 31) < 6) continue;
                Part(a, "Broken lane line", 11, .03f, z, .10f, .006f, 2.4f, chalk);
            }
            for (int x = -46; x < 64; x += 7)
            {
                if (Mathf.Abs(x - 11) < 6 || Mathf.Abs(x - RaidMap.Exit.x) < 5) continue;
                Part(a, "Broken lane line", x, .035f, 31, 2.4f, .006f, .10f, chalk);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                Part(a, "Stop bar", 11 + side * 1.65f, .031f, 31 + side * 4.7f, 2.8f, .006f, .27f, chalk);
                Part(a, "Receiving bay edging", side * 4f, .019f, -1, .11f, .006f, 11.6f, chalk);
                Part(a, "Cargo bay end", side * 3f, .02f, 4.75f, 2f, .006f, .11f, chalk);
            }
            for (int i = 0; i < 7; i++)
                Part(a, "Dock hazard hatching", 5.3f, .019f, -4 + 1.22f * i, .16f, .006f, 1.28f, amber)
                    .localRotation = Quaternion.Euler(0, -37, 0);
        }

        private void BuildRailAndQuay()
        {
            Transform a = architecture;
            const float railX = -54f;
            Part(a, "Railway ballast bed", railX, -.064f, 0, 4.8f, .15f, 120, floorFinish);
            for (float z = -60; z <= 60; z += .95f)
                Part(a, "Sleeper", railX, .023f, z, 2.53f, .03f, .22f, oxide);
            foreach (int side in new[] { -1, 1 })
                Part(a, "Siding rail", railX + side * .76f, .048f, 0, .075f, .055f, 120, steel);
            Part(a, "Quay promenade", RaidMap.RoomX - 3.5f, -.205f, 0, 7, .45f, 116, concrete);
            Part(a, "Seawall", RaidMap.RoomX + .16f, -1.65f, 0, .55f, 3.4f, 116, concrete);
            Part(a, "Quay cap", RaidMap.RoomX + .16f, .09f, 0, .85f, .18f, 116, steel);
            for (int z = -53; z <= 55; z += 12)
            {
                Part(a, "Hardwood rubbing fender", RaidMap.RoomX + .50f, -.78f, z + 1.5f, .30f, 1.9f, .49f, black);
                Part(a, "Hardwood rubbing fender", RaidMap.RoomX + .50f, -.78f, z + 2.3f, .30f, 1.9f, .49f, black);
                Part(a, "Bollard footing", RaidMap.RoomX + .30f, .22f, z, .47f, .14f, .72f, black);
                Part(a, "Quay bollard", RaidMap.RoomX + .30f, .40f, z, .31f, .3f, .43f, amber);
                Part(a, "Bollard crown", RaidMap.RoomX + .30f, .57f, z, .38f, .10f, .72f, amber);
                Part(a, "Quayside warning stripe", RaidMap.RoomX - .65f, .024f, z, .13f, .006f, 8, amber);
            }
        }

        private void BuildPartition()
        {
            Transform a = architecture;
            float width = 2 * RaidMap.WallX;
            Part(a, "Partition footing", 0, 0.18f, 0, width, 0.36f, 2 * RaidMap.WallZ, concrete);
            Part(a, "Enamel partition", 0, 1.33f, 0, width, 1.96f, 2 * RaidMap.WallZ, teal);
            Part(a, "Wear strip", 0, 0.40f, 0, width, 0.10f, 0.32f, black);
            Part(a, "Top capping", 0, 2.30f, 0, width + 0.035f, 0.08f, 0.33f, steel);
            Part(a, "Partition task lamp", 0, 2.46f, 0, .54f, .22f, .30f, black);
            Part(a, "Task lamp diffuser", 0, 2.47f, -.155f, .43f, .11f, .028f, sodium);
            Part(a, "Task lamp diffuser", 0, 2.47f, .155f, .43f, .11f, .028f, sodium);
            for (int face = -1; face <= 1; face += 2)
            {
                Transform side = Panel(a, "Partition face", 0, 0, face * 0.15f, face < 0 ? 0 : 180);
                for (int j = 0; j < 4; j++)
                {
                    float cx = -1.5f + j;
                    Part(side, "Panel seam", cx - 0.46f, 1.34f, -0.007f, 0.012f, 1.78f, 0.009f, black);
                    for (int h = 0; h < 3; h++)
                        Part(side, "Rivet", cx - 0.44f, 0.50f + h * 0.80f, -0.011f, 0.021f, 0.021f, 0.01f, steel);
                }
                Part(side, "Safety band", 0, 0.69f, -0.011f, width - 0.04f, 0.048f, 0.009f, amber);
                // Printed metal plates are deliberately compact at the two close spawn viewpoints.
                Part(side, "Routing placard backing", 0, 1.60f, -0.019f, 1.65f, 0.41f, 0.024f, black);
                Part(side, "Routing placard", 0, 1.6f, -0.035f, 1.59f, 0.35f, 0.01f, face < 0 ? amber : chalk);
                WallText(side, face < 0 ? "ГРУЗ   →" : "ПРИЁМКА", new Vector3(0, 1.6f, -0.044f), 0, 0.031f, black.color);
                if (face < 0)
                {
                    for (int k = 0; k < 2; k++)
                    {
                        float cx = 1.39f + k * 0.26f;
                        Part(side, "Route chevron upper", cx, 1.43f, -0.013f, 0.24f, 0.047f, 0.012f, amber)
                            .localRotation = Quaternion.Euler(0, 0, -44f);
                        Part(side, "Route chevron lower", cx, 1.28f, -0.013f, 0.24f, 0.047f, 0.012f, amber)
                            .localRotation = Quaternion.Euler(0, 0, 44f);
                    }
                }
                else
                {
                    for (int k = 0; k < 4; k++)
                        Part(side, "End hazard mark", 1.55f + k * 0.105f, 1.1f, -0.013f, 0.05f, 0.24f, 0.012f, amber)
                            .localRotation = Quaternion.Euler(0, 0, -22f);
                }
            }
        }

        private void BuildExtraction()
        {
            Vector2 point = RaidMap.Exit;
            Transform area = Panel(architecture, "Customs exit", point.x, 0, point.y, 0);
            float w = RaidMap.ExitX, d = RaidMap.ExitZ;
            Part(area, "Extraction painted stand", 0, .04f, 0, 2 * w, .012f, 2 * d, teal);
            for (int side = -1; side <= 1; side += 2)
            {
                Part(area, "Reflective exit stripe", side * (w - .065f), .052f, 0, .12f, .01f, 2 * d, exitLight);
                Part(area, "Reflective exit stripe", 0, .052f, side * (d - .065f), 2 * w, .01f, .12f, exitLight);
            }
            for (int i = 0; i < 2; i++)
                for (int side = -1; side <= 1; side += 2)
                    Part(area, "Approach arrow", side * .23f, .042f, -2.05f - i * .82f, .64f, .008f, .12f, chalk)
                        .localRotation = Quaternion.Euler(0, side * 31, 0);
            Part(area, "Customs gantry", 0, 4.04f, 0, 5.55f, .31f, .29f, teal);
            Part(area, "Enamel exit panel", 0, 4.50f, -.06f, 3.9f, .85f, .17f, black);
            Part(area, "Upper signal strip", 0, 4.99f, -.06f, 3.87f, .12f, .19f, exitLight);
            Part(area, "Task light lens", 0, 4.35f, -.34f, .87f, .085f, .17f, sodium);
            foreach (int side in new[] { -1, 1 })
                WallText(area, "ВЫХОД", new Vector3(0, 4.56f, -.06f + side * .10f), side < 0 ? 0 : 180,
                    .076f, exitLight.color, exitLight.GetFloat("_Glow"));
        }
    }
}
