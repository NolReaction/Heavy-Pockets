using UnityEngine;

namespace HeavyPockets
{
    internal sealed partial class RaidScenery
    {
        private const float Eaves = 4.65f;
        private const float Sill = 3.04f;

        private void BuildRoom()
        {
            BuildFloor();
            BuildSideWall(RaidMap.RoomX, 90);
            BuildSideWall(-RaidMap.RoomX, -90);
            BuildEndWall(RaidMap.RoomZ, 0, true);
            BuildEndWall(-RaidMap.RoomZ, 180, false);
            BuildRoof();
            BuildPartition();
            BuildExtraction();
        }

        private void BuildFloor()
        {
            Transform a = architecture;
            Part(a, "Sealed floor", 0, -0.10f, 0, 2 * RaidMap.RoomX + 0.3f, 0.2f, 2 * RaidMap.RoomZ + 0.3f, floorFinish);
            for (float x = -6.4f; x < 8; x += 3.2f)
                Part(a, "Expansion joint", x, 0.001f, 0, 0.014f, 0.002f, 12, black);
            for (float z = -4; z < 6; z += 4)
                Part(a, "Expansion joint", 0, 0.001f, z, 16, 0.002f, 0.014f, black);

            // A goods lane along the clear side of the partition leads to the loading door.
            for (int i = 0; i < 7; i++)
            {
                float z = -4.7f + 1.15f * i;
                Part(a, "Traffic lane paint", 3.12f, 0.004f, z, 0.075f, 0.004f, 0.72f, chalk);
                Part(a, "Maintenance hatching", -7.02f, 0.004f, z, 0.38f, 0.004f, 0.07f, amber)
                    .localRotation = Quaternion.Euler(0, -32, 0);
            }
            Part(a, "Linear drain", 0, 0.004f, -4.55f, 10.1f, 0.004f, 0.14f, black);
            for (int i = 0; i < 65; i++)
                Part(a, "Drain grille tooth", -4.96f + i * 0.155f, 0.007f, -4.55f, 0.043f, 0.004f, 0.123f, steel);
        }

        // Local Z points out of the bay; the protected flat at mid-height on the east wall holds the port print.
        private void BuildSideWall(float x, float yaw)
        {
            Transform wall = Panel(architecture, "Clerestory wall", x, 0, 0, yaw);
            Part(wall, "Poured wall", 0, Sill / 2, 0.11f, 12.35f, Sill, 0.22f, concrete);
            Part(wall, "Enamel wainscot", 0, 0.48f, -0.014f, 12, 0.96f, 0.03f, teal);
            Part(wall, "Enamel edging", 0, 0.99f, -0.026f, 12, 0.036f, 0.034f, steel);
            Part(wall, "Skirting", 0, 0.115f, -0.042f, 12, 0.23f, 0.072f, black);
            Part(wall, "Clerestory sill", 0, Sill, -0.025f, 12.2f, 0.115f, 0.23f, black);
            Part(wall, "Clerestory head", 0, 4.26f, -0.016f, 12.2f, 0.15f, 0.22f, black);
            Part(wall, "Ring beam", 0, 4.48f, 0.05f, 12.35f, 0.35f, 0.23f, teal);
            for (int i = 0; i < 12; i++)
            {
                float cx = -5.5f + i;
                Part(wall, "Daylight pane", cx, 3.65f, 0.045f, 0.98f, 1.12f, 0.025f, (i % 4 == 2) ? glass : skyGlass);
                Part(wall, "Upper transom", cx, 3.93f, -0.018f, 1, 0.036f, 0.054f, steel);
                Part(wall, "Glazing bar", cx - 0.5f, 3.65f, -0.025f, 0.046f, 1.20f, 0.075f, black);
            }
            for (int i = 0; i < 4; i++)
            {
                float cx = -6 + 4f * i;
                // The broad east display bay is free of uprights between its concrete pilasters.
                if (x > 0 && cx == -2f) cx = -2.82f;
                Part(wall, "Steel pier", cx, 2.32f, -0.04f, 0.17f, Eaves, 0.095f, steel);
                Part(wall, "Pier flange", cx, 2.32f, -0.10f, 0.22f, Eaves, 0.035f, teal);
            }

            // A flush maintenance cabinet on the blind wall makes the service side distinguishable from the exit.
            if (x < 0)
            {
                Part(wall, "Cabinet recess", 1.6f, 1.46f, -0.044f, 2.0f, 1.1f, 0.085f, black);
                for (int i = 0; i < 3; i++)
                {
                    float cx = 0.96f + 0.64f * i;
                    Part(wall, "Service hatch", cx, 1.46f, -0.101f, 0.62f, 1.04f, 0.04f, teal);
                    Part(wall, "Cabinet latch", cx + 0.21f, 1.46f, -0.129f, 0.045f, 0.11f, 0.022f, steel);
                    for (int l = 0; l < 4; l++)
                        Part(wall, "Vent louvre", cx, 1.66f + 0.039f * l, -0.125f, 0.34f, 0.012f, 0.014f, black);
                }
            }
        }

        private void BuildEndWall(float z, float yaw, bool north)
        {
            Transform wall = Panel(architecture, "Masonry end wall", 0, 0, z, yaw);
            Part(wall, "Masonry bearing wall", 0, Eaves / 2, 0.13f, 16.4f, Eaves, 0.26f, concrete);
            Part(wall, "Oil resistant wainscot", 0, 0.48f, -0.014f, 16, 0.96f, 0.03f, teal);
            Part(wall, "Enamel edging", 0, 0.99f, -0.025f, 16, 0.035f, 0.04f, steel);
            Part(wall, "Impact rail", 0, 0.16f, -0.046f, 16, 0.22f, 0.07f, black);
            for (int i = -2; i <= 2; i++)
            {
                float cx = 3.8f * i;
                Part(wall, "Concrete pier", cx, Eaves / 2, -0.04f, 0.25f, Eaves, 0.09f, concrete);
                Part(wall, "Pier cuff", cx, 0.51f, -0.088f, 0.28f, 1.03f, 0.08f, teal);
            }

            float pipeStart = -7.85f, pipeEnd = north ? 3.55f : 7.85f;
            float mid = (pipeStart + pipeEnd) / 2f, width = pipeEnd - pipeStart;
            for (int j = 0; j < 2; j++)
            {
                float y = 3.42f + j * 0.23f;
                Transform pipe = Shape(wall, PrimitiveType.Cylinder, "Service pipe", new Vector3(mid, y, -0.16f),
                    new Vector3(0.10f, width / 2, 0.10f), j == 0 ? oxide : steel).transform;
                pipe.localRotation = Quaternion.Euler(0, 0, 90);
                for (float cx = pipeStart + 0.6f; cx < pipeEnd - 0.1f; cx += 2.38f)
                    Part(wall, "Pipe saddle", cx, y, -0.15f, 0.06f, 0.15f, 0.18f, black);
            }

            if (!north)
            {
                // Short steel louvers face into the room from above shoulder height.
                Part(wall, "Louvre duct recess", 0.85f, 2.25f, -0.045f, 3.55f, 0.8f, 0.095f, black);
                for (int i = 0; i < 7; i++)
                    Part(wall, "Intake louvre", 0.85f, 1.95f + i * 0.10f, -0.121f, 3.40f, 0.044f, 0.058f, steel);
                for (int i = -1; i <= 1; i++)
                    Part(wall, "Grille guard", 0.85f + 1.4f * i, 2.25f, -0.15f, 0.038f, 0.75f, 0.028f, teal);
            }
        }

        private void BuildRoof()
        {
            Transform a = architecture;
            Part(a, "Pressed metal roof", 0, 4.95f, 0, 16.35f, 0.13f, 12.35f, teal);
            for (int i = -8; i <= 8; i++)
                Part(a, "Roof decking rib", 0.91f * i, 4.84f, 0, 0.095f, 0.085f, 12, steel);
            for (int i = -1; i <= 1; i++)
            {
                float z = 4.4f * i;
                Part(a, "Rafter web", 0, 4.65f, z, 16.2f, 0.38f, 0.075f, teal);
                Part(a, "Rafter lower flange", 0, 4.44f, z, 16.2f, 0.085f, 0.26f, steel);
                for (int side = -1; side <= 1; side += 2)
                    Part(a, "Rafter haunch", 7.48f * side, 4.15f, z, 1.35f, 0.13f, 0.18f, teal)
                        .localRotation = Quaternion.Euler(0, 0, -33 * side);
            }
            BuildFixture(-3.6f, 0.3f);
            BuildFixture(4.8f, 3.0f);
        }

        private void BuildFixture(float x, float z)
        {
            Transform a = architecture;
            for (int side = -1; side <= 1; side += 2)
                Part(a, "Fixture hanger", x, 4.58f, z + side * 0.66f, 0.033f, 0.63f, 0.03f, black);
            Part(a, "Pendant housing", x, 4.31f, z, 0.37f, 0.14f, 1.7f, steel);
            Part(a, "Pendant lip", x, 4.24f, z, 0.44f, 0.05f, 1.8f, black);
            Part(a, "Warm diffuser", x, 4.205f, z, 0.32f, 0.045f, 1.65f, sodium);
        }

        private void BuildPartition()
        {
            Transform a = architecture;
            float width = 2 * RaidMap.WallX;
            Part(a, "Partition footing", 0, 0.18f, 0, width, 0.36f, 2 * RaidMap.WallZ, concrete);
            Part(a, "Enamel partition", 0, 1.33f, 0, width, 1.96f, 2 * RaidMap.WallZ, teal);
            Part(a, "Wear strip", 0, 0.40f, 0, width, 0.10f, 0.32f, black);
            Part(a, "Top capping", 0, 2.30f, 0, width + 0.035f, 0.08f, 0.33f, steel);
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
            Transform area = Panel(architecture, "Exit apron", point.x, 0, point.y, 0);
            float w = RaidMap.ExitX, d = RaidMap.ExitZ;
            Part(area, "Dock leveller frame", 0, 0.012f, 0, 2*w, 0.015f, 2*d, black);
            Part(area, "Dock leveller plate", 0, 0.021f, 0, 2*w - 0.14f, 0.014f, 2*d - 0.13f, teal);
            for (int s = -1; s <= 1; s += 2)
            {
                for (int corner = -1; corner <= 1; corner += 2)
                {
                    Part(area, "Apron corner signal", s * (w - 0.046f), 0.034f, corner * (d - 0.27f), 0.06f, 0.02f, 0.46f, exitLight);
                    Part(area, "Apron corner return", s * (w - 0.26f), 0.034f, corner * (d - 0.042f), 0.48f, 0.02f, 0.06f, exitLight);
                }
                for (int j = 0; j < 9; j++)
                    Part(area, "Leveller tread", s * 0.92f, 0.032f, -0.63f + 0.16f * j, 0.16f, 0.009f, 0.04f, steel);
            }

            Transform gate = Panel(architecture, "Extraction roller gate", point.x, 0, RaidMap.RoomZ, 0);
            Part(gate, "Door reveal", 0, 1.59f, -0.045f, 3.05f, 3.18f, 0.08f, black);
            Part(gate, "Galvanized roller curtain", 0, 1.55f, -0.104f, 2.76f, 3.02f, 0.045f, teal);
            for (int i = 0; i < 17; i++)
            {
                float y = 0.13f + i * 0.175f;
                Part(gate, "Rolled steel slat", 0, y, -0.14f, 2.75f, 0.044f, 0.035f, steel);
            }
            for (int s = -1; s <= 1; s += 2)
            {
                Part(gate, "Door guide rail", s * 1.44f, 1.55f, -0.15f, 0.105f, 3.10f, 0.13f, black);
                Part(gate, "Dock bumper", s * 1.61f, 0.45f, -0.09f, 0.20f, 0.62f, 0.16f, black);
                Part(gate, "Door rail highlight", s * 1.44f, 1.61f, -0.224f, 0.024f, 3.0f, 0.012f, steel);
            }
            Part(gate, "Roller hood", 0, 3.18f, -0.12f, 3.15f, 0.27f, 0.23f, teal);
            Part(gate, "Signal housing", 0, 2.47f, -0.17f, 1.77f, 0.43f, 0.07f, black);
            WallText(gate, "ВЫХОД", new Vector3(0, 2.47f, -0.213f), 0, 0.044f, exitLight.color);
            Part(gate, "Signal lens", 0, 3.18f, -0.247f, 2.62f, 0.052f, 0.025f, exitLight);
            Part(gate, "Call station", -1.79f, 1.43f, -0.07f, 0.23f, 0.37f, 0.11f, black);
            Part(gate, "Call indicator", -1.79f, 1.48f, -0.14f, 0.105f, 0.08f, 0.04f, exitLight);
        }
    }
}
