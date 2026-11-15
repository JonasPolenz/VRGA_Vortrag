using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TatortVR.EditorTools
{
    /// <summary>
    /// Places models from Kenney's Furniture Kit (CC0, see ThirdParty/Kenney_FurnitureKit/License.txt).
    /// The kit uses its own units, has its pivots in a corner and comes in pastel colours, so every model is
    /// scaled, positioned by its bounding box and given the project's own materials. The front of every model
    /// (doors, seat, screen) is its local -Z side.
    /// </summary>
    internal static class TatortFurnitureKit
    {
        const string k_ModelFolder = "Assets/_TatortVR/ThirdParty/Kenney_FurnitureKit/Models/";

        /// <summary>Turns kit units into metres for most models: a chair ends up 0.94 m tall.</summary>
        public const float Scale = 0.2f;

        // Project materials by the material names used inside the kit.
        static readonly Dictionary<string, Material> s_Materials = new Dictionary<string, Material>();
        static readonly List<Vector3> s_Vertices = new List<Vector3>();

        public static void CreateMaterials()
        {
            s_Materials.Clear();
            s_Materials["wood"] = TatortEnvironmentBuilder.CreateMaterial("Kit_Holz", new Color(0.58f, 0.42f, 0.28f));
            s_Materials["woodDark"] = TatortEnvironmentBuilder.CreateMaterial("Kit_Holz_Dunkel", new Color(0.4f, 0.28f, 0.19f));
            s_Materials["carpet"] = TatortEnvironmentBuilder.CreateMaterial("Stoff", new Color(0.27f, 0.32f, 0.42f));
            s_Materials["carpetDarker"] = TatortEnvironmentBuilder.CreateMaterial("Kit_Stoff_Dunkel", new Color(0.19f, 0.23f, 0.31f));
            s_Materials["carpetBlue"] = TatortEnvironmentBuilder.CreateMaterial("Kit_Stoff_Blau", new Color(0.33f, 0.45f, 0.62f));
            s_Materials["carpetWhite"] = TatortEnvironmentBuilder.CreateMaterial("Weiss", new Color(0.96f, 0.96f, 0.96f));
            s_Materials["_defaultMat"] = s_Materials["carpetWhite"];
            s_Materials["metal"] = Metal("Kit_Metall", new Color(0.74f, 0.76f, 0.78f), 0.6f);
            s_Materials["metalLight"] = Metal("Kit_Metall_Hell", new Color(0.9f, 0.91f, 0.92f), 0.2f);
            s_Materials["metalMedium"] = Metal("Kit_Metall_Mittel", new Color(0.52f, 0.55f, 0.57f), 0.5f);
            s_Materials["metalDark"] = Metal("Kit_Metall_Dunkel", new Color(0.3f, 0.32f, 0.34f), 0.5f);
            var glass = TatortEnvironmentBuilder.CreateMaterial("Kit_Glas", new Color(0.62f, 0.74f, 0.8f));
            glass.SetFloat("_Smoothness", 0.9f);
            s_Materials["glass"] = glass;
            s_Materials["lamp"] = TatortEnvironmentBuilder.CreateMaterial("Lampenschirm", new Color(0.95f, 0.9f, 0.75f), emission: new Color(0.5f, 0.45f, 0.3f));
            s_Materials["plant"] = TatortEnvironmentBuilder.CreateMaterial("Kit_Pflanze", new Color(0.2f, 0.45f, 0.24f));
            s_Materials["fur"] = TatortEnvironmentBuilder.CreateMaterial("Kit_Fell", new Color(0.55f, 0.42f, 0.3f));
        }

        static Material Metal(string name, Color color, float metallic)
        {
            var material = TatortEnvironmentBuilder.CreateMaterial(name, color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", 0.55f);
            return material;
        }

        /// <summary>
        /// Adds a kit model whose bounding box stands with its bottom centre on <paramref name="position"/>.
        /// <paramref name="facing"/> is the direction the front of the model looks at.
        /// </summary>
        public static GameObject Place(string model, string name, Transform parent, Vector3 position, Vector3 facing,
            float scale = Scale, bool collider = true, params (string kitMaterial, Material material)[] swaps) =>
            Place(model, name, parent, position, Quaternion.LookRotation(-facing), scale, collider, swaps);

        /// <summary>Same with a free rotation, for things that do not stand upright.</summary>
        public static GameObject Place(string model, string name, Transform parent, Vector3 position, Quaternion rotation,
            float scale = Scale, bool collider = true, params (string kitMaterial, Material material)[] swaps)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_ModelFolder + model + ".fbx");
            if (prefab == null)
            {
                Debug.LogWarning($"[TatortSetup] Furniture model '{model}' is missing in {k_ModelFolder}");
                return null;
            }

            // Some models carry a rotation and scale of their own on the root.
            var go = Object.Instantiate(prefab, parent);
            go.name = name;
            go.transform.SetPositionAndRotation(Vector3.zero, rotation * prefab.transform.localRotation);
            go.transform.localScale = prefab.transform.localScale * scale;
            var bounds = MeshBounds(go);
            go.transform.position = position - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

            foreach (var renderer in go.GetComponentsInChildren<MeshRenderer>())
            {
                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                    materials[i] = Replacement(materials[i], swaps);
                renderer.sharedMaterials = materials;
                // The meshes are simple enough to be their own colliders, which keeps seats and shelves open.
                if (collider)
                    renderer.gameObject.AddComponent<MeshCollider>();
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, StaticEditorFlags.BatchingStatic);
            }

            return go;
        }

        /// <summary>Adds a kit model without collider that stands on whatever is below it (table, shelf, seat).</summary>
        public static GameObject PlaceOnTop(string model, string name, Transform parent, float x, float z, Vector3 facing,
            float scale = Scale, float fromHeight = 2f, params (string kitMaterial, Material material)[] swaps) =>
            Place(model, name, parent, new Vector3(x, SurfaceBelow(new Vector3(x, fromHeight, z)), z), facing, scale, false, swaps);

        /// <summary>Height of the first solid surface below a point, e.g. the table top for something standing on it.</summary>
        public static float SurfaceBelow(Vector3 from)
        {
            Physics.SyncTransforms();
            if (Physics.Raycast(from, Vector3.down, out var hit, from.y + 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            Debug.LogWarning($"[TatortSetup] Nothing below {from} to stand on");
            return 0f;
        }

        /// <summary>
        /// Tops of all boards below a point inside an open shelf, highest first. Casting again from just under
        /// each hit passes through the underside of that board, because rays ignore back faces.
        /// </summary>
        public static List<float> SurfacesBelow(Vector3 from, float minHeight)
        {
            Physics.SyncTransforms();
            var heights = new List<float>();
            var y = from.y;
            while (Physics.Raycast(new Vector3(from.x, y, from.z), Vector3.down, out var hit, y + 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                   && hit.point.y > minHeight)
            {
                heights.Add(hit.point.y);
                y = hit.point.y - 0.01f;
            }

            return heights;
        }

        static Material Replacement(Material original, (string kitMaterial, Material material)[] swaps)
        {
            if (original == null)
                return null;
            if (swaps != null)
            {
                foreach (var swap in swaps)
                {
                    if (swap.kitMaterial == original.name)
                        return swap.material;
                }
            }

            return s_Materials.TryGetValue(original.name, out var material) ? material : original;
        }

        // Exact bounds from the vertices; renderer bounds grow when a model is rotated.
        static Bounds MeshBounds(GameObject go)
        {
            var bounds = new Bounds();
            var empty = true;
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null)
                    continue;
                filter.sharedMesh.GetVertices(s_Vertices);
                var matrix = filter.transform.localToWorldMatrix;
                foreach (var vertex in s_Vertices)
                {
                    var point = matrix.MultiplyPoint3x4(vertex);
                    if (empty)
                        bounds = new Bounds(point, Vector3.zero);
                    else
                        bounds.Encapsulate(point);
                    empty = false;
                }
            }

            return bounds;
        }
    }
}
