using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TatortVR.EditorTools
{
    /// <summary>
    /// Small procedural meshes for evidence, tools and props. They are saved as assets under
    /// Assets/_TatortVR/Meshes so the scene can reference them; rebuilding overwrites them in place.
    /// </summary>
    public static class TatortMeshes
    {
        const string k_Parent = "Assets/_TatortVR";
        const string k_FolderName = "Meshes";

        /// <summary>Flat, irregular puddle in the XZ plane, facing up.</summary>
        public static Mesh Blob(string name, float radiusX, float radiusZ, int seed, int segments = 28, float irregularity = 0.2f)
        {
            var random = new System.Random(seed);
            float Phase() => (float)(random.NextDouble() * Mathf.PI * 2.0);
            float p1 = Phase(), p2 = Phase(), p3 = Phase();

            var vertices = new List<Vector3> { Vector3.zero };
            var normals = new List<Vector3> { Vector3.up };
            for (var i = 0; i < segments; i++)
            {
                // A few low frequencies so the outline reads as a liquid, not a star.
                var angle = i / (float)segments * Mathf.PI * 2f;
                var wobble = 1f + irregularity * (0.55f * Mathf.Sin(2f * angle + p1) + 0.3f * Mathf.Sin(3f * angle + p2) + 0.15f * Mathf.Sin(5f * angle + p3));
                vertices.Add(new Vector3(Mathf.Cos(angle) * radiusX * wobble, 0f, Mathf.Sin(angle) * radiusZ * wobble));
                normals.Add(Vector3.up);
            }

            var triangles = new List<int>();
            for (var i = 0; i < segments; i++)
            {
                triangles.Add(0);
                triangles.Add(1 + (i + 1) % segments);
                triangles.Add(1 + i);
            }

            return Save(name, vertices, normals, triangles);
        }

        /// <summary>
        /// Outline in the XY plane extruded along Z, centred on Z = 0. Corners sharper than
        /// <paramref name="smoothAngle"/> stay hard edges, flatter ones are shaded smooth.
        /// </summary>
        public static Mesh Prism(string name, IReadOnlyList<Vector2> outline, float depth, float smoothAngle = 40f)
        {
            var points = new List<Vector2>(outline);
            if (SignedArea(points) < 0f)
                points.Reverse();
            var count = points.Count;
            var half = depth / 2f;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();

            // Caps. The outline is counter-clockwise seen from -Z, front faces need clockwise winding.
            var capTriangles = Triangulate(points);
            for (var side = -1; side <= 1; side += 2)
            {
                var start = vertices.Count;
                foreach (var p in points)
                {
                    vertices.Add(new Vector3(p.x, p.y, side * half));
                    normals.Add(new Vector3(0f, 0f, side));
                }

                for (var i = 0; i < capTriangles.Count; i += 3)
                {
                    triangles.Add(start + capTriangles[i]);
                    triangles.Add(start + capTriangles[side < 0 ? i + 2 : i + 1]);
                    triangles.Add(start + capTriangles[side < 0 ? i + 1 : i + 2]);
                }
            }

            // Sides, one quad per outline edge.
            var edgeNormals = new Vector2[count];
            for (var i = 0; i < count; i++)
            {
                var edge = points[(i + 1) % count] - points[i];
                edgeNormals[i] = new Vector2(edge.y, -edge.x).normalized;
            }

            for (var i = 0; i < count; i++)
            {
                var next = (i + 1) % count;
                var previous = (i + count - 1) % count;
                var startNormal = Vector2.Angle(edgeNormals[previous], edgeNormals[i]) < smoothAngle
                    ? (edgeNormals[previous] + edgeNormals[i]).normalized : edgeNormals[i];
                var endNormal = Vector2.Angle(edgeNormals[i], edgeNormals[next]) < smoothAngle
                    ? (edgeNormals[i] + edgeNormals[next]).normalized : edgeNormals[i];

                var start = vertices.Count;
                vertices.Add(new Vector3(points[i].x, points[i].y, -half));
                vertices.Add(new Vector3(points[next].x, points[next].y, -half));
                vertices.Add(new Vector3(points[next].x, points[next].y, half));
                vertices.Add(new Vector3(points[i].x, points[i].y, half));
                normals.Add(startNormal);
                normals.Add(endNormal);
                normals.Add(endNormal);
                normals.Add(startNormal);
                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }

            return Save(name, vertices, normals, triangles);
        }

        /// <summary>
        /// Profile of (radius, height) points revolved around the Y axis. Trace the profile upwards on the outside
        /// and downwards on the inside for hollow objects; repeat a point to get a hard edge there.
        /// </summary>
        public static Mesh Lathe(string name, IReadOnlyList<Vector2> profile, int segments = 16)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            var ring = segments + 1;
            for (var k = 0; k < profile.Count; k++)
            {
                var tangent = Tangent(profile, k);
                var normal = new Vector2(tangent.y, -tangent.x).normalized;
                for (var s = 0; s <= segments; s++)
                {
                    var angle = s / (float)segments * Mathf.PI * 2f;
                    var cos = Mathf.Cos(angle);
                    var sin = Mathf.Sin(angle);
                    vertices.Add(new Vector3(profile[k].x * cos, profile[k].y, profile[k].x * sin));
                    normals.Add(new Vector3(normal.x * cos, normal.y, normal.x * sin));
                }
            }

            for (var k = 0; k < profile.Count - 1; k++)
            {
                if (profile[k] == profile[k + 1])
                    continue;
                for (var s = 0; s < segments; s++)
                {
                    var a = k * ring + s;
                    var d = (k + 1) * ring + s;
                    triangles.AddRange(new[] { a, d + 1, a + 1, a, d, d + 1 });
                }
            }

            return Save(name, vertices, normals, triangles);
        }

        /// <summary>Counter-clockwise outline of a rectangle with rounded corners, centred on the origin.</summary>
        public static Vector2[] RoundedRect(float width, float height, float radius, int cornerSegments = 4)
        {
            var points = new List<Vector2>();
            var halfWidth = width / 2f - radius;
            var halfHeight = height / 2f - radius;
            var centers = new[]
            {
                new Vector2(halfWidth, -halfHeight), new Vector2(halfWidth, halfHeight),
                new Vector2(-halfWidth, halfHeight), new Vector2(-halfWidth, -halfHeight),
            };
            for (var corner = 0; corner < 4; corner++)
            {
                for (var s = 0; s <= cornerSegments; s++)
                {
                    var angle = (-90f + corner * 90f + 90f * s / cornerSegments) * Mathf.Deg2Rad;
                    points.Add(centers[corner] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
                }
            }

            return points.ToArray();
        }

        static Vector2 Tangent(IReadOnlyList<Vector2> profile, int k)
        {
            var hasPrevious = k > 0 && profile[k - 1] != profile[k];
            var hasNext = k < profile.Count - 1 && profile[k + 1] != profile[k];
            if (hasPrevious && hasNext)
                return profile[k + 1] - profile[k - 1];
            if (hasNext)
                return profile[k + 1] - profile[k];
            return hasPrevious ? profile[k] - profile[k - 1] : Vector2.up;
        }

        static float SignedArea(IReadOnlyList<Vector2> points)
        {
            var area = 0f;
            for (var i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                area += a.x * b.y - b.x * a.y;
            }

            return area / 2f;
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        // Ear clipping for a simple counter-clockwise polygon; returns counter-clockwise triangles.
        static List<int> Triangulate(IReadOnlyList<Vector2> points)
        {
            var remaining = new List<int>();
            for (var i = 0; i < points.Count; i++)
                remaining.Add(i);
            var result = new List<int>();

            var guard = points.Count * points.Count;
            while (remaining.Count > 3 && guard-- > 0)
            {
                var clipped = false;
                for (var i = 0; i < remaining.Count; i++)
                {
                    var ia = remaining[(i + remaining.Count - 1) % remaining.Count];
                    var ib = remaining[i];
                    var ic = remaining[(i + 1) % remaining.Count];
                    Vector2 a = points[ia], b = points[ib], c = points[ic];
                    if (Cross(b - a, c - b) <= 1e-9f)
                        continue;

                    var containsOther = false;
                    foreach (var index in remaining)
                    {
                        if (index == ia || index == ib || index == ic)
                            continue;
                        var p = points[index];
                        if (Cross(b - a, p - a) >= 0f && Cross(c - b, p - b) >= 0f && Cross(a - c, p - c) >= 0f)
                        {
                            containsOther = true;
                            break;
                        }
                    }

                    if (containsOther)
                        continue;
                    result.AddRange(new[] { ia, ib, ic });
                    remaining.RemoveAt(i);
                    clipped = true;
                    break;
                }

                if (!clipped)
                    break;
            }

            if (remaining.Count == 3)
                result.AddRange(remaining);
            else
                Debug.LogWarning("[TatortSetup] Could not triangulate an outline completely");
            return result;
        }

        static Mesh Save(string name, List<Vector3> vertices, List<Vector3> normals, List<int> triangles)
        {
            var folder = $"{k_Parent}/{k_FolderName}";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(k_Parent, k_FolderName);

            var path = $"{folder}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            var isNew = mesh == null;
            if (isNew)
                mesh = new Mesh { name = name };
            else
                mesh.Clear();

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            if (isNew)
                AssetDatabase.CreateAsset(mesh, path);
            else
                EditorUtility.SetDirty(mesh);
            return mesh;
        }
    }
}
