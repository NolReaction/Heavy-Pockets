using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HeavyPockets
{
    internal sealed partial class RaidScenery
    {
        private readonly Transform root, architecture;
        private readonly Material concrete, steel, teal, amber, chalk, exitLight, black;
        private readonly Material floorFinish, glass, skyGlass, sodium, oxide;
        private readonly Font font;
        private readonly Material signInk;
        private readonly List<Mesh> shellMeshes = new List<Mesh>();
        private static readonly Dictionary<PrimitiveType, Mesh> primitiveMeshes = new Dictionary<PrimitiveType, Mesh>();

        internal RaidScenery(Transform root)
        {
            this.root = root;
            architecture = new GameObject("Freight yard").transform;
            architecture.SetParent(root, false);
            concrete = Surface("Cast concrete", new Color32(149, 142, 128, 255), SurfaceFinish.Concrete);
            floorFinish = Surface("Warehouse slab", new Color32(111, 115, 111, 255), SurfaceFinish.Concrete);
            steel = Surface("Galvanized structural steel", new Color32(109, 124, 125, 255), SurfaceFinish.Steel);
            teal = Surface("Petrol enamel", new Color32(50, 79, 84, 255), SurfaceFinish.Paint);
            amber = Surface("Safety ochre", new Color32(225, 148, 57, 255), SurfaceFinish.Paint);
            chalk = Surface("Warm white paint", new Color32(219, 217, 193, 255), SurfaceFinish.Paint);
            black = Surface("Carbon steel", new Color32(45, 54, 57, 255), SurfaceFinish.Rubber);
            oxide = Surface("Oxide primer", new Color32(126, 78, 59, 255), SurfaceFinish.Paint);
            exitLight = Surface("Exit signal", new Color32(85, 211, 166, 255), SurfaceFinish.Signal, 0.8f);
            glass = Surface("Obscured wire glass", new Color32(132, 164, 174, 255), SurfaceFinish.Glass, 0.28f);
            skyGlass = Surface("Clear clerestory", new Color32(165, 189, 190, 255), SurfaceFinish.Glass, 0.38f);
            sodium = Surface("Sodium lamp", new Color32(255, 202, 132, 255), SurfaceFinish.Lamp, 1.15f);
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            font.RequestCharactersInTexture("ПРИЁМКА ГРУЗ ВЫХОД →", 64);
            signInk = new Material(Resources.Load<Shader>("DepotSign"));
            RefreshSignAtlas(font);
            Font.textureRebuilt += RefreshSignAtlas;

            BuildRoom();
            BakeArchitecture();
            BuildCoast();
            BuildCrate();
            BuildHarborPrint();
            BuildLighting();
        }

        private void RefreshSignAtlas(Font rebuilt)
        {
            if (rebuilt == font)
                signInk.mainTexture = font.material.mainTexture;
        }

        internal void Dispose()
        {
            Font.textureRebuilt -= RefreshSignAtlas;
            Object.Destroy(signInk);
            foreach (Mesh mesh in shellMeshes)
                Object.Destroy(mesh);
            DisposeLighting();
            DisposeHarborPrint();
            DisposePropMeshes();
            DisposeSurfaces();
        }

        private static GameObject Shape(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 size, Material material)
        {
            GameObject go;
            if (primitiveMeshes.TryGetValue(type, out Mesh mesh) && mesh != null)
            {
                go = new GameObject(name);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>();
            }
            else
            {
                go = GameObject.CreatePrimitive(type);
                go.name = name;
                primitiveMeshes[type] = go.GetComponent<MeshFilter>().sharedMesh;
                var collider = go.GetComponent<Collider>();
                collider.enabled = false;
                Object.Destroy(collider);
            }
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        private static Transform Panel(Transform parent, string name, float x, float y, float z, float yaw)
        {
            var panel = new GameObject(name).transform;
            panel.SetParent(parent, false);
            panel.localPosition = new Vector3(x, y, z);
            panel.localRotation = Quaternion.Euler(0, yaw, 0);
            return panel;
        }

        private static Transform Part(Transform parent, string name, float x, float y, float z, float w, float h, float d, Material material)
        {
            return Shape(parent, PrimitiveType.Cube, name, new Vector3(x, y, z), new Vector3(w, h, d), material).transform;
        }

        private void WallText(Transform parent, string label, Vector3 pos, float yaw, float letterSize, Color color)
        {
            var holder = Panel(parent, label, pos.x, pos.y, pos.z, yaw);
            var text = holder.gameObject.AddComponent<TextMesh>();
            text.font = font;
            text.fontSize = 64;
            text.characterSize = letterSize;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.text = label;
            text.color = color;
            var renderer = holder.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = signInk;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        // All solid architectural surfaces are stationary; pack them by shared material.
        // Sign meshes retain their dynamic font atlas and the cargo/actors have separate lifetimes.
        private void BakeArchitecture()
        {
            var parts = new Dictionary<Material, List<CombineInstance>>();
            Matrix4x4 toLocal = architecture.worldToLocalMatrix;
            foreach (MeshFilter filter in architecture.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null || !filter.sharedMesh.isReadable || filter.GetComponent<TextMesh>() != null)
                    continue;
                var renderer = filter.GetComponent<MeshRenderer>();
                Material material = renderer.sharedMaterial;
                if (!parts.TryGetValue(material, out var list))
                {
                    list = new List<CombineInstance>();
                    parts.Add(material, list);
                }
                list.Add(new CombineInstance
                {
                    mesh = filter.sharedMesh,
                    transform = toLocal * filter.transform.localToWorldMatrix
                });
                renderer.enabled = false;
                Object.Destroy(filter.gameObject);
            }
            foreach (var group in parts)
            {
                var mesh = new Mesh { name = group.Key.name + " architecture", indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(group.Value.ToArray(), true, true);
                mesh.UploadMeshData(true);
                shellMeshes.Add(mesh);
                var go = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(architecture, false);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = group.Key;
                if (group.Key == glass || group.Key == skyGlass || group.Key == sodium || group.Key == exitLight)
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }
    }
}
