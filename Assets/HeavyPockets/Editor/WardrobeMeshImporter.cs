using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HeavyPockets.Editor
{
    internal static class WardrobeMeshImporter
    {
        private const string Character = "Assets/HeavyPockets/Resources/Runner/Character.prefab";
        private const string Materials = "Assets/HeavyPockets/Characters/DepotRunner";

        [MenuItem("Heavy Pockets/Character/Update wardrobe meshes from FBX…")]
        private static void PickModel()
        {
            string file = EditorUtility.OpenFilePanel("Export from ArtSource/DepotRunner.blend", Path.GetTempPath(), "fbx");
            if (string.IsNullOrEmpty(file)) return;
            string temp = AssetDatabase.GenerateUniqueAssetPath("Assets/DepotWardrobeImport.fbx");
            try
            {
                File.Copy(file, temp);
                AssetDatabase.ImportAsset(temp, ImportAssetOptions.ForceSynchronousImport);
                var importer = (ModelImporter)AssetImporter.GetAtPath(temp);
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.importAnimation = false;
                importer.isReadable = true;
                importer.optimizeGameObjects = false;
                importer.SaveAndReimport();
                UpdateMeshes(AssetDatabase.LoadAssetAtPath<GameObject>(temp));
                Debug.Log("Depot Runner wardrobe meshes updated.");
            }
            finally { AssetDatabase.DeleteAsset(temp); }
        }

        // The animation bindings remain on the native prefab. Match the two FBX
        // coordinate systems by rest-position landmarks before baking any vertices.
        private static Matrix4x4 RigFrame(Dictionary<string, Transform> bones)
        {
            Vector3 hips = bones["pelvis"].position;
            Vector3 span = bones["hand_r"].position - bones["hand_l"].position;
            Vector3 x = span.normalized;
            Vector3 y = Vector3.ProjectOnPlane(bones["Head"].position - hips, x).normalized;
            Vector3 z = Vector3.Cross(x, y).normalized;
            if (Vector3.Dot(z, bones["ball_l"].position - bones["foot_l"].position) < 0) z = -z;
            var frame = Matrix4x4.identity;
            frame.SetColumn(0, x * span.magnitude);
            frame.SetColumn(1, y * span.magnitude);
            frame.SetColumn(2, z * span.magnitude);
            frame.SetColumn(3, new Vector4(hips.x, hips.y, hips.z, 1));
            return frame;
        }

        private static Mesh ConvertMesh(SkinnedMeshRenderer source, Matrix4x4 toCharacter, Transform[] bones)
        {
            var mesh = Object.Instantiate(source.sharedMesh);
            var matrix = toCharacter * source.transform.localToWorldMatrix;
            var normalMatrix = matrix.inverse.transpose;
            var positions = mesh.vertices;
            var normals = mesh.normals;
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i] = matrix.MultiplyPoint3x4(positions[i]);
                normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
            }
            mesh.vertices = positions;
            mesh.normals = normals;
            mesh.bindposes = bones.Select(b => b.worldToLocalMatrix).ToArray();
            if (matrix.determinant < 0)
            {
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    int[] indices = mesh.GetTriangles(sub);
                    for (int i = 0; i < indices.Length; i += 3)
                        (indices[i], indices[i + 1]) = (indices[i + 1], indices[i]);
                    mesh.SetTriangles(indices, sub);
                }
            }
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        internal static void UpdateMeshes(GameObject imported)
        {
            var sources = imported.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToDictionary(s => s.name);
            var sourceNodes = imported.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
            var model = PrefabUtility.LoadPrefabContents(Character);
            var updates = new List<(SkinnedMeshRenderer target, Transform[] bones, Material[] materials, Mesh mesh)>();
            try
            {
                var targets = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var nodes = model.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
                Matrix4x4 convert = RigFrame(nodes) * RigFrame(sourceNodes).inverse;
                foreach (var target in targets)
                {
                    if (!sources.TryGetValue(target.name, out var source))
                        throw new InvalidDataException("FBX is missing the wardrobe part " + target.name);
                    var bones = source.bones.Select(b => nodes[b.name]).ToArray();
                    for (int i = 0; i < bones.Length; i++)
                        if (Vector3.Distance(bones[i].position, convert.MultiplyPoint3x4(source.bones[i].position)) > .002f)
                            throw new InvalidDataException("Changed skeleton rest position at " + bones[i].name);
                    var materials = source.sharedMaterials.Select(m =>
                        AssetDatabase.LoadAssetAtPath<Material>(Materials + "/" + m.name + ".mat")).ToArray();
                    if (materials.Any(m => m == null))
                        throw new InvalidDataException("Missing a Unity material for part " + target.name);
                    var mesh = ConvertMesh(source, convert, bones);
                    mesh.name = target.sharedMesh.name;
                    updates.Add((target, bones, materials, mesh));
                }
                if (sources.Count != targets.Length)
                    throw new InvalidDataException("Register new part names in the wardrobe prefab and catalog first.");
                foreach (var update in updates)
                {
                    EditorUtility.CopySerialized(update.mesh, update.target.sharedMesh);
                    EditorUtility.SetDirty(update.target.sharedMesh);
                    update.target.bones = update.bones;
                    update.target.sharedMaterials = update.materials;
                }
                PrefabUtility.SaveAsPrefabAsset(model, Character);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                foreach (var update in updates) Object.DestroyImmediate(update.mesh);
                PrefabUtility.UnloadPrefabContents(model);
            }
        }
    }
}
