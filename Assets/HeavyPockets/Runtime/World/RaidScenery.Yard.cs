using System.Collections.Generic;
using UnityEngine;

namespace HeavyPockets
{
    internal sealed partial class RaidScenery
    {
        private void BuildYardStructures()
        {
            Bounds pierBounds = default;
            bool hasPiers = false;
            int container = 0;
            foreach (var solid in OutdoorLayout.Solids)
            {
                Bounds bounds = solid.Bounds;
                Vector3 c = bounds.center, size = bounds.size;
                bool alongZ = size.z > size.x;
                var site = Panel(architecture, solid.Kind.ToString(), c.x, bounds.min.y, c.z, alongZ ? 90 : 0);
                float width = Mathf.Max(size.x, size.z), depth = Mathf.Min(size.x, size.z);
                switch (solid.Kind)
                {
                    case OutdoorKind.Container:
                        BuildFreightContainer(site, width, size.y, depth, container++);
                        break;
                    case OutdoorKind.Shed:
                        BuildServiceShed(site, width, size.y, depth);
                        break;
                    case OutdoorKind.Barrier:
                        BuildConcreteBarrier(site, width, size.y, depth);
                        break;
                    case OutdoorKind.Pier:
                        Part(site, "Portal crane leg", 0, size.y / 2, 0, width - .22f, size.y, depth - .22f, amber);
                        Part(site, "Crane wheel box", 0, .56f, 0, width, 1.12f, depth, black);
                        Part(site, "Drive cover", 0, 1.08f, 0, width, .33f, depth, teal);
                        if (hasPiers) pierBounds.Encapsulate(bounds);
                        else { pierBounds = bounds; hasPiers = true; }
                        break;
                    case OutdoorKind.LocomotiveDeck:
                    case OutdoorKind.WagonDeck:
                        site.localRotation = Quaternion.identity;
                        BuildRailDeck(site, size, solid.Kind == OutdoorKind.LocomotiveDeck);
                        break;
                    case OutdoorKind.DieselHood:
                        site.localRotation = Quaternion.identity;
                        BuildDieselHood(site, size);
                        break;
                    case OutdoorKind.DriverCab:
                        site.localRotation = Quaternion.identity;
                        BuildDriverCab(site, size);
                        break;
                    case OutdoorKind.TimberLoad:
                        site.localRotation = Quaternion.identity;
                        BuildTimberLoad(site, size);
                        break;
                    case OutdoorKind.SignalPost:
                        Part(site, "Customs post", 0, size.y / 2, 0, width, size.y, depth, teal);
                        Part(site, "Reflective post sleeve", 0, .65f, 0, width + .012f, .52f, depth + .012f, amber);
                        break;
                }
            }
            if (hasPiers) BuildPortalCrane(pierBounds);
            BuildBoundary();
        }

        private void BuildRailDeck(Transform site, Vector3 size, bool engine)
        {
            float halfX = size.x / 2, halfZ = size.z / 2;
            Part(site, "Underframe", 0, .90f, 0, size.x - .43f, .52f, size.z - .53f, black);
            Part(site, "Riveted solebar", 0, 1.22f, 0, size.x - .08f, .32f, size.z - .14f, engine ? oxide : teal);
            Part(site, "Steel deck", 0, size.y - .06f, 0, size.x, .12f, size.z, black);
            for (int end = -1; end <= 1; end += 2)
            {
                float bogie = end * (halfZ - 2.09f);
                Part(site, "Bogie transom", 0, .65f, bogie, 2.02f, .37f, 2.54f, black);
                Part(site, "Buffer beam", 0, 1.02f, end * (halfZ - .18f), size.x - .08f, .34f, .28f, engine ? oxide : teal);
                Part(site, "Coupler", 0, .88f, end * halfZ, .30f, .28f, .52f, steel);
                for (int side = -1; side <= 1; side += 2)
                {
                    RailCylinder(site, "Buffer head", new Vector3(side * .87f,1.02f,end * halfZ), .36f, .13f, true, black);
                    for (int axle = -1; axle <= 1; axle += 2)
                    {
                        float z = bogie + axle * .85f;
                        RailCylinder(site, "Wheel tyre", new Vector3(side * .76f,.535f,z), .92f, .18f, false, steel);
                        RailCylinder(site, "Wheel hub", new Vector3(side * .86f,.535f,z), .70f, .06f, false, black);
                        Part(site, "Axlebox", side * 1.015f, .52f, z, .24f, .35f, .48f, steel);
                    }
                    Part(site, "Bogie face", side * 1.01f, .77f, bogie, .14f, .20f, 2.36f, black);
                    if (!engine) continue;
                    float x = side * (halfX - .08f);
                    Part(site, "Corner grab iron", x, 1.85f, end * (halfZ - .19f), .052f, .82f, .052f, amber);
                    for (int step = 0; step < 3; step++)
                        Part(site, "Shunter end step", x, .48f + step * .26f, end * (halfZ - .57f), .19f, .065f, .65f, steel);
                }
            }
            if (!engine) return;
            foreach (int side in new[] { -1, 1 })
            {
                float x = side * (halfX - .07f);
                Part(site, "Walkway edge stripe", x, 1.30f, 0, .12f, .078f, size.z - .56f, amber);
                Part(site, "Walkway handrail", x, 2.36f, 2.02f, .054f, .054f, 6.45f, amber);
                for (int i = 0; i < 5; i++)
                    Part(site, "Handrail stanchion", x, 1.91f, -1.17f + 1.59f * i, .05f, .89f, .05f, amber);
            }
        }

        private static void RailCylinder(Transform site, string label, Vector3 center, float diameter, float length,
            bool alongTrack, Material surface)
        {
            var cylinder = Shape(site, PrimitiveType.Cylinder, label, center,
                new Vector3(diameter, length * .5f, diameter), surface);
            cylinder.transform.localRotation = alongTrack ? Quaternion.Euler(90, 0, 0) : Quaternion.Euler(0, 0, 90);
        }

        private void BuildDieselHood(Transform site, Vector3 size)
        {
            float halfX = size.x / 2, nose = size.z / 2;
            Part(site, "Diesel hood", 0, 2.24f, 0, size.x - .07f, 1.57f, size.z - .07f, teal);
            Part(site, "Engine roof panel", 0, size.y - .085f, 0, size.x, .17f, size.z, teal);
            Part(site, "Radiator face", 0, 2.30f, nose, size.x - .26f, 1.10f, .08f, black);
            for (int j = 0; j < 8; j++)
                Part(site, "Front grill blade", 0, 1.84f + j * .13f, nose + .046f, size.x - .36f, .042f, .035f, steel);
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * halfX;
                Part(site, "Belt line", x, 2.88f, 0, .042f, .16f, size.z - .08f, chalk);
                Part(site, "Radiator aperture", x, 2.32f, nose - 1.32f, .039f, .87f, 2.08f, black);
                for (int j = 0; j < 11; j++)
                    Part(site, "Radiator louvre", x + side * .030f, 2.32f, nose - 2.25f + j * .19f, .042f, .80f, .068f, steel);
                for (int j = 0; j < 3; j++)
                    Part(site, "Engine compartment latch", x, 2.10f, -nose + .56f + j * 1.03f, .061f, .16f, .08f, black);
                RailCylinder(site, "Headlamp rim", new Vector3(side * .64f,2.95f,nose), .30f, .10f, true, black);
                RailCylinder(site, "Headlamp lens", new Vector3(side * .64f,2.95f,nose + .063f), .215f, .046f, true, sodium);
            }
            Part(site, "Exhaust plinth", 0, size.y + .06f, -1.15f, .78f, .12f, .72f, teal);
            Part(site, "Exhaust stack", 0, size.y + .43f, -1.15f, .37f, .68f, .48f, black);
            Part(site, "Hinged exhaust cap", 0, size.y + .80f, -1.12f, .47f, .09f, .56f, black);
        }

        private void BuildDriverCab(Transform site, Vector3 size)
        {
            float halfX = size.x / 2, halfZ = size.z / 2;
            Part(site, "Cab skirt", 0, 2.22f, 0, size.x - .16f, 1.48f, size.z - .14f, teal);
            Part(site, "Window surround", 0, 3.38f, 0, size.x - .11f, 1.10f, size.z - .11f, chalk);
            Part(site, "Bridge windscreen", 0, 3.40f, 0, size.x - .06f, .77f, size.z - .06f, glass);
            Part(site, "Rain gutter roof", 0, size.y - .10f, 0, size.x, .20f, size.z, oxide);
            foreach (int side in new[] { -1, 1 })
            {
                foreach (int end in new[] { -1, 1 })
                    Part(site, "Cab corner pillar", side * (halfX - .085f), 3.40f, end * (halfZ - .085f), .16f, .81f, .16f, chalk);
                Part(site, "Side window divider", side * (halfX - .031f), 3.39f, -.10f, .065f, .81f, .075f, black);
                Part(site, "Windscreen divider", 0, 3.39f, side * (halfZ - .032f), .086f, .81f, .066f, black);
                Part(site, "Locomotive number panel", side * (halfX - .045f), 2.32f, 0, .079f, .45f, 1.10f, oxide);
                WallText(site, "ТГ  07", new Vector3(side * (halfX + .005f),2.33f,0), -side * 90, .043f, chalk.color);
            }
        }

        private void BuildTimberLoad(Transform site, Vector3 size)
        {
            const float diameter = .70f;
            for (int layer = 0; layer < 3; layer++)
                for (int i = 0; i < 3 - layer; i++)
                {
                    float x = (i - (2 - layer) * .5f) * diameter;
                    float z = .10f * Mathf.Sin(layer + i * 2.7f);
                    float length = size.z - .23f - .12f * ((i + layer) % 3);
                    float y = 1.66f + layer * .55f;
                    RailCylinder(site, "Roundwood", new Vector3(x,y,z), diameter, length, true, oxide);
                    for (int end = -1; end <= 1; end += 2)
                        RailCylinder(site, "Sawn end grain", new Vector3(x,y,z + end * (length / 2 + .01f)), diameter * .92f, .026f, true, amber);
                }
            for (int end = -1; end <= 1; end += 2)
            {
                float z = end * size.z * .37f;
                Part(site, "Bolster", 0, 1.43f, z, 2.73f, .18f, .24f, black);
                for (int side = -1; side <= 1; side += 2)
                {
                    Part(site, "Timber stake", side * (size.x / 2 + .022f), 2.18f, z, .11f, 1.59f, .20f, teal);
                    YardBeam(site, "Binding strap", new Vector3(side * 1.12f,1.68f,z), new Vector3(side * .21f,size.y,z), .073f, .080f, black);
                }
                Part(site, "Binding strap", 0, size.y, z, .46f, .045f, .08f, black);
            }
        }

        private void BuildConcreteBarrier(Transform site, float width, float height, float depth)
        {
            Part(site, "Precast footing", 0, .14f * height, 0, width, .28f * height, depth, concrete);
            Part(site, "Rub rail", 0, .60f * height, 0, width - .04f, .80f * height, depth * .81f, concrete);
            foreach (int side in new[] { -1, 1 })
            {
                Part(site, "Amber reflector", side * (width / 2 - .18f), height * .78f, 0, .18f, .16f, depth * .83f, amber);
                Part(site, "Lifting point", side * width * .32f, height * .92f, 0, .11f, .12f, depth * .84f, black);
            }
        }

        private void BuildFreightContainer(Transform site, float length, float height, float depth, int serial)
        {
            Material paint = serial % 4 == 0 ? oxide : serial % 4 == 1 ? teal : serial % 4 == 2 ? steel : chalk;
            Part(site, "Container chassis", 0, height / 2, 0, length - .08f, height - .04f, depth - .14f, paint);
            int folds = Mathf.Max(8, Mathf.RoundToInt(length / .38f));
            var flutes = new List<Vector3>(folds * 24);
            for (int face = -1; face <= 1; face += 2)
            {
                float edge = length / 2 - .14f, panelZ = face * (depth / 2 - .012f);
                float pitch = 2 * edge / folds;
                for (int i = 0; i < folds; i++)
                {
                    float x = face * (-edge + i * pitch);
                    Vector3 a = new Vector3(x, .12f, panelZ);
                    Vector3 b = a + new Vector3(face * pitch * .21f, 0, 0);
                    Vector3 c = a + new Vector3(face * pitch * .35f, 0, -face * .05f);
                    Vector3 d = a + new Vector3(face * pitch * .84f, 0, -face * .05f);
                    Vector3 e = a + new Vector3(face * pitch, 0, 0);
                    AddPanelStrip(flutes, a, b, height - .25f);
                    AddPanelStrip(flutes, b, c, height - .25f);
                    AddPanelStrip(flutes, c, d, height - .25f);
                    AddPanelStrip(flutes, d, e, height - .25f);
                }
                Part(site, "Top rail", 0, height - .072f, panelZ, length, .144f, .055f, paint);
                Part(site, "Bottom rail", 0, .065f, panelZ, length, .13f, .055f, paint);
            }
            MakeYardFaces(site, "Pressed corrugated sides", paint, flutes);
            for (int end = -1; end <= 1; end += 2)
            {
                var doors = Panel(site, "Container end", end * (length / 2 - .045f), 0, 0, end * 90);
                Part(doors, "Closed end frame", 0, height / 2, 0, depth, height, .09f, paint);
                for (int side = -1; side <= 1; side += 2)
                {
                    Part(doors, "Corner casting", side * (depth / 2 - .09f), height / 2, .035f, .17f, height, .08f, paint);
                    if (end < 0) continue;
                    Part(doors, "Door gasket", side * .025f, height * .5f, .049f, .022f, height * .91f, .014f, black);
                    Part(doors, "Door locking rod", side * depth * .26f, height * .51f, .074f, .036f, height * .85f, .04f, steel);
                    Part(doors, "Locking handle", side * (depth * .26f + .10f), height * .35f, .089f, .23f, .043f, .052f, black);
                }
            }
            if (serial % 3 == 0)
            {
                var label = Panel(site, "Shipping line", -length * .26f, height * .68f, -depth / 2 - .026f, 0);
                Part(label, "Worn enamel placard", 0, 0, 0, 2.45f, .63f, .018f, teal);
                WallText(label, "МОРФЛОТ", new Vector3(0, .015f, -.012f), 0, .040f, chalk.color);
            }
        }

        private static void AddPanelStrip(List<Vector3> faces, Vector3 a, Vector3 b, float height)
        {
            Vector3 up = Vector3.up * height;
            faces.Add(a); faces.Add(b); faces.Add(b + up); faces.Add(a + up);
        }

        private void MakeYardFaces(Transform site, string name, Material material, List<Vector3> faces)
        {
            var indices = new int[faces.Count / 4 * 6];
            var uv = new Vector2[faces.Count];
            for (int i = 0, t = 0; i < faces.Count; i += 4, t += 6)
            {
                indices[t] = indices[t + 3] = i;
                indices[t + 1] = i + 1;
                indices[t + 2] = indices[t + 4] = i + 2;
                indices[t + 5] = i + 3;
                uv[i + 1] = Vector2.right; uv[i + 2] = Vector2.one; uv[i + 3] = Vector2.up;
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(faces);
            mesh.uv = uv;
            mesh.triangles = indices;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            shellMeshes.Add(mesh);
            var part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            part.transform.SetParent(site, false);
            part.GetComponent<MeshFilter>().sharedMesh = mesh;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private void BuildServiceShed(Transform site, float width, float height, float depth)
        {
            // The clerestory and roof sit inside the common collision envelope.
            Part(site, "Foundation", 0, .19f, 0, width, .38f, depth, concrete);
            Part(site, "Galvanized equipment house", 0, height / 2, 0, width - .15f, height - .23f, depth - .15f, steel);
            Part(site, "Roof fascia", 0, height - .20f, 0, width, .36f, depth, teal);
            Part(site, "Roof cap", 0, height - .04f, 0, width, .08f, depth, chalk);
            for (int side = -1; side <= 1; side += 2)
            {
                var face = Panel(site, "Metal siding", 0, 0, side * (depth / 2 - .055f), side < 0 ? 0 : 180);
                Part(face, "Foundation stripe", 0, .69f, 0, width - .12f, .70f, .08f, teal);
                for (float x = -width / 2 + .75f; x < width / 2 - .2f; x += 1.13f)
                    Part(face, "Standing seam", x, height * .50f, -.015f, .038f, height - .72f, .045f, steel);
                if (side > 0) continue;
                float shutter = width * .30f, doorX = -width * .20f;
                Part(face, "Loading shutter", doorX, height * .39f, -.045f, shutter, height * .78f, .035f, teal);
                for (float y = .4f; y < height * .74f; y += .28f)
                    Part(face, "Door slat", doorX, y, -.068f, shutter - .12f, .04f, .024f, steel);
                for (int pane = 0; pane < 3; pane++)
                {
                    float x = width * .10f + pane * width * .125f, y = height * .62f;
                    Part(face, "Window reveal", x, y, -.048f, width * .12f, height * .25f, .04f, black);
                    Part(face, "Wire glass", x, y, -.077f, width * .12f - .15f, height * .25f - .14f, .02f, glass);
                    Part(face, "Transom", x, y + .09f, -.092f, width * .12f, .055f, .02f, teal);
                }
            }
        }

        private static void YardBeam(Transform parent, string name, Vector3 from, Vector3 to, float width, float depth, Material material)
        {
            Vector3 mid = (from + to) / 2, delta = to - from;
            var beam = Part(parent, name, mid.x, mid.y, mid.z, width, delta.magnitude, depth, material);
            beam.localRotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
        }

        private void BuildPortalCrane(Bounds piers)
        {
            var frame = Panel(architecture, "Ship-to-shore portal", piers.center.x, piers.max.y, piers.center.z, 0);
            float sx = piers.extents.x - .65f, sz = piers.extents.z - .65f;
            float stern = -sx - 3.2f, bow = sx + 14f, armCenter = (stern + bow) / 2;
            foreach (int side in new[] { -1, 1 })
            {
                Part(frame, "Portal cap", 0, -.18f, side * sz, 2 * sx + 1.3f, .82f, 1.15f, amber);
                Part(frame, "Portal cap", side * sx, -.2f, 0, .85f, .70f, 2 * sz + 1.3f, amber);
                YardBeam(frame, "High knee stay", new Vector3(-sx, -3.1f, side * sz), new Vector3(-sx + 3.2f, -.28f, side * sz), .36f, .34f, amber);
                YardBeam(frame, "High knee stay", new Vector3(sx, -3.1f, side * sz), new Vector3(sx - 3.2f, -.28f, side * sz), .36f, .34f, amber);
                float z = side * 1.8f;
                Part(frame, "Boom upper chord", armCenter, 2.95f, z, bow - stern, .34f, .30f, amber);
                Part(frame, "Trolley running rail", armCenter, .93f, z, bow - stern, .32f, .36f, black);
                const int cells = 11;
                for (int i = 0; i < cells; i++)
                {
                    float left = Mathf.Lerp(stern, bow, i / (float)cells);
                    float right = Mathf.Lerp(stern, bow, (i + 1) / (float)cells);
                    YardBeam(frame, "Boom lattice", new Vector3(left,.98f,z), new Vector3((left+right)*.5f,2.88f,z), .15f, .17f, amber);
                    YardBeam(frame, "Boom lattice", new Vector3((left+right)*.5f,2.88f,z), new Vector3(right,.98f,z), .15f, .17f, amber);
                }
                var head = new Vector3(-1, 10.1f, z);
                YardBeam(frame, "A-frame shear leg", new Vector3(-sx - 1f, 2.9f, z), head, .34f, .36f, teal);
                YardBeam(frame, "A-frame shear leg", new Vector3(sx - 1f, 2.9f, z), head, .34f, .36f, teal);
                YardBeam(frame, "Suspension stay", head, new Vector3(bow - .7f, 3.04f, z), .095f, .095f, black);
                YardBeam(frame, "Counterweight stay", head, new Vector3(stern, 3.04f, z), .09f, .09f, black);
            }
            foreach (float x in new[] { -sx, sx })
                Part(frame, "Gantry crosshead", x, .38f, 0, .58f, .53f, 2 * sz, amber);
            Part(frame, "A-frame crown", -1, 10.16f, 0, .60f, .46f, 4.02f, teal);
            Part(frame, "Counterweight block", stern + 1.65f, 2.75f, 0, 3.30f, 3.2f, 4.35f, oxide);
            Part(frame, "Counterweight band", stern + 1.65f, 3.55f, 0, 3.35f, .39f, 4.40f, chalk);
            Part(frame, "Boom nose", bow, 2.0f, 0, .39f, 2.2f, 4.05f, amber);
            float trolley = sx + 5.6f;
            Part(frame, "Suspended trolley", trolley, .55f, 0, 2.8f, .63f, 4.2f, teal);
            Part(frame, "Driver cabin", trolley - 1.35f, -.64f, -1.05f, 2.16f, 1.83f, 2.08f, chalk);
            Part(frame, "Driver glazing", trolley - 1.35f, -.44f, -1.08f, 2.19f, 1.06f, 2.10f, glass);
            Part(frame, "Driver sill", trolley - 1.35f, -1.15f, -1.05f, 2.23f, .18f, 2.14f, teal);
            for (int side = -1; side <= 1; side += 2)
            {
                for (int end = -1; end <= 1; end += 2)
                    YardBeam(frame, "Hoist rope", new Vector3(trolley + end * .85f, .28f, side * 1.35f),
                        new Vector3(trolley + end * 2.24f, -4.42f, side * .88f), .045f, .045f, black);
                Part(frame, "Empty spreader", trolley, -4.55f, side * .9f, 5.18f, .31f, .18f, amber);
                Part(frame, "Spreader crossbar", trolley + side * 2.25f, -4.56f, 0, .24f, .32f, 1.98f, amber);
            }
            Part(frame, "Gantry floodlight lens", 0, -.55f, -sz + .40f, 1.16f, .12f, .31f, sodium);
            Part(frame, "Harbor crane nameplate", 0, -.05f, -sz - .60f, 3.95f, .78f, .06f, teal);
            WallText(frame, "ПОРТ  04", new Vector3(0, -.05f, -sz - .64f), 0, .075f, chalk.color);
        }

        private void BuildBoundary()
        {
            for (int side = -1; side <= 1; side += 2)
            {
                var edge = Panel(architecture, "Cable boundary", 0, 0, side * (RaidMap.RoomZ + .38f), 0);
                BuildCableRun(edge, RaidMap.RoomX * 2 + .78f);
            }
            var railEdge = Panel(architecture, "Rail boundary", -RaidMap.RoomX - .38f, 0, 0, 90);
            BuildCableRun(railEdge, RaidMap.RoomZ * 2);
            var harborEdge = Panel(architecture, "Harbor safety rail", RaidMap.RoomX + .14f, .14f, 0, 90);
            BuildCableRun(harborEdge, RaidMap.RoomZ * 2);
        }

        private void BuildCableRun(Transform edge, float length)
        {
            int spans = Mathf.CeilToInt(length / 6f);
            for (int i = 0; i <= spans; i++)
            {
                float x = length * (i / (float)spans - .5f);
                Part(edge, "Edge post", x, .52f, 0, .11f, 1.04f, .11f, steel);
                if (i % 4 == 0) Part(edge, "Boundary reflector", x, .90f, 0, .117f, .13f, .117f, chalk);
            }
            Part(edge, "Upper wire rope", 0, .91f, 0, length, .036f, .036f, steel);
            Part(edge, "Lower wire rope", 0, .46f, 0, length, .025f, .025f, black);
        }

        private void BuildHarborLaunch()
        {
            // Waterline is 1.6 m below the quay. The hull flares toward the deck.
            Transform boat = Panel(architecture, "Pilot launch", 88, -1.6f, 4, -11);
            Vector2[] rim = {
                new Vector2(-2.35f,-8.7f), new Vector2(2.35f,-8.7f), new Vector2(2.98f,-6.8f),
                new Vector2(3.13f,-1), new Vector2(2.98f,3.8f), new Vector2(2.20f,6.8f),
                new Vector2(0,9.3f), new Vector2(-2.20f,6.8f), new Vector2(-2.98f,3.8f),
                new Vector2(-3.13f,-1), new Vector2(-2.98f,-6.8f)
            };
            var hull = new List<Vector3>();
            var stripe = new List<Vector3>();
            var deck = new List<Vector3>();
            for (int i = 0; i < rim.Length; i++)
            {
                Vector2 a = rim[i], b = rim[(i + 1) % rim.Length];
                Vector3 bottomA = new Vector3(a.x * .78f, -.75f, a.y * .90f);
                Vector3 bottomB = new Vector3(b.x * .78f, -.75f, b.y * .90f);
                Vector3 topA = new Vector3(a.x, 1.36f + Mathf.Max(0,a.y - 3) * .10f, a.y);
                Vector3 topB = new Vector3(b.x, 1.36f + Mathf.Max(0,b.y - 3) * .10f, b.y);
                Vector3 bandA = Vector3.Lerp(bottomA, topA, .74f), bandB = Vector3.Lerp(bottomB, topB, .74f);
                hull.Add(bottomA); hull.Add(bandA); hull.Add(bandB); hull.Add(bottomB);
                Vector3 trimA = Vector3.Lerp(bottomA, topA, .80f), trimB = Vector3.Lerp(bottomB, topB, .80f);
                stripe.Add(bandA); stripe.Add(trimA); stripe.Add(trimB); stripe.Add(bandB);
                hull.Add(trimA); hull.Add(topA); hull.Add(topB); hull.Add(trimB);
                deck.Add(topA); deck.Add(new Vector3(0,topA.y,a.y)); deck.Add(new Vector3(0,topB.y,b.y)); deck.Add(topB);
            }
            MakeYardFaces(boat, "Flared pilot hull", teal, hull);
            MakeYardFaces(boat, "Cream boot stripe", chalk, stripe);
            // Along the port side the projection onto the centreline changes winding.
            for (int i = 0; i < deck.Count; i += 4)
            {
                if (Vector3.Cross(deck[i + 1] - deck[i], deck[i + 3] - deck[i]).y >= 0) continue;
                (deck[i + 1], deck[i + 3]) = (deck[i + 3], deck[i + 1]);
            }
            MakeYardFaces(boat, "Launch deck", floorFinish, deck);
            Part(boat, "Engine casing", 0, 1.78f, -3.2f, 4.1f, 1.12f, 4.85f, oxide);
            Part(boat, "Wheelhouse sill", 0, 2.10f, 1.30f, 4.26f, 1.60f, 5.50f, chalk);
            Part(boat, "Bridge windows", 0, 3.48f, 1.30f, 4.12f, 1.25f, 5.36f, glass);
            Part(boat, "Sun visor", 0, 4.18f, 1.30f, 4.53f, .22f, 5.94f, chalk);
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i <= 3; i++)
                    Part(boat, "Bridge mullion", side * 2.05f, 3.50f, -1.33f + i * 1.76f, .095f, 1.35f, .13f, chalk);
                Part(boat, "Twin exhaust", side * 1.36f, 4.16f, -2.37f, .47f, 2.1f, .57f, black);
                Part(boat, "Exhaust cowl", side * 1.36f, 5.09f, -2.37f, .60f, .29f, .69f, oxide);
                for (int i = 0; i < 3; i++)
                {
                    float z = -5.8f + i * 3;
                    Shape(boat, PrimitiveType.Cylinder, "Rubber fender", new Vector3(side * 3.02f,.86f,z),
                        new Vector3(.82f,.15f,.82f), black).transform.localRotation = Quaternion.Euler(0,0,90);
                }
            }
            YardBeam(boat, "Aerial mast", new Vector3(0,4.23f,-.2f), new Vector3(0,7.36f,-.64f), .13f, .14f, amber);
            Part(boat, "Radar array", 0, 6.9f, -.58f, 2.2f, .15f, .20f, chalk);
            Part(boat, "Masthead light", 0, 7.46f, -.64f, .23f, .32f, .23f, sodium);
        }
    }
}
