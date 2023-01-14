using UnityEngine;
using UnityEngine.Rendering;

public static class GeometryUtilities
{
    public static Mesh GeneratePlane(int divisions, float size = 1f)
    {
        var interval = size / divisions;

        var vertices = new Vector3[(divisions + 1) * (divisions + 1)];
        var uvs = new Vector2[vertices.Length];
        var normals = new Vector3[vertices.Length];
        var tangents = new Vector4[vertices.Length];

        for (int i = 0, z = 0; z <= divisions; z++)
        {
            for (int x = 0; x <= divisions; x++, i++)
            {
                vertices[i] = new Vector3(x * interval, 0, z * interval);
                uvs[i] = new Vector2(x / (float)divisions, z / (float)divisions);
                normals[i] = new Vector3(0, 1, 0);
                tangents[i] = new Vector4(1, 0, 0, -1);
            }
        }

        var triangles = new int[divisions * divisions * 6];
        for (int ti = 0, vi = 0, y = 0; y < divisions; y++, vi++)
        {
            for (int x = 0; x < divisions; x++, ti += 6, vi++)
            {
                triangles[ti] = vi;
                triangles[ti + 3] = triangles[ti + 2] = vi + 1;
                triangles[ti + 4] = triangles[ti + 1] = vi + divisions + 1;
                triangles[ti + 5] = vi + divisions + 2;
            }
        }

        var halfSize = size / 2f;
        var center = new Vector3(-halfSize, 0, -halfSize);
        var sizeVector = new Vector3(size, 0, size);
        var bounds = new Bounds(center, sizeVector);

        var mesh = new Mesh()
        {
            name = "Plane",
            indexFormat = vertices.Length < ushort.MaxValue ? IndexFormat.UInt16 : IndexFormat.UInt32,
            vertices = vertices,
            //normals = normals,
            //tangents = tangents,
            //uv = uvs,
            //bounds = bounds
        };

        mesh.SetTriangles(triangles, 0, true);

        return mesh;
    }

    // Solves the quadratic equation of the form: a*t^2 + b*t + c = 0.
    // Returns 'false' if there are no real roots, 'true' otherwise.
    public static bool SolveQuadraticEquation(float a, float b, float c, out Vector2 roots)
    {
        var discriminant = b * b - 4f * a * c;
        var sqrtDet = Mathf.Sqrt(discriminant);

        roots.x = (-b - sqrtDet) / (2f * a);
        roots.y = (-b + sqrtDet) / (2f * a);

        return discriminant >= 0f;
    }

    // This simplified version assume that we care about the result only when we are inside the sphere
    // Assume Sphere is at the origin (i.e start = position - spherePosition) and dir is normalized
    // Ref: http://http.developer.nvidia.com/GPUGems/gpugems_ch19.html
    public static float IntersectRaySphereSimple(Vector3 start, Vector3 dir, float radius)
    {
        float b = Vector3.Dot(dir, start) * 2.0f;
        float c = Vector3.Dot(start, start) - radius * radius;
        float discriminant = b * b - 4.0f * c;

        return Mathf.Abs(Mathf.Sqrt(discriminant) - b) * 0.5f;
    }

    // Assume Sphere is at the origin (i.e start = position - spherePosition)
    public static bool IntersectRaySphere(Vector3 start, Vector3 dir, float radius, out Vector2 intersections)
    {
        float a = Vector3.Dot(dir, dir);
        float b = Vector3.Dot(dir, start) * 2.0f;
        float c = Vector3.Dot(start, start) - radius * radius;

        return SolveQuadraticEquation(a, b, c, out intersections);
    }

    public static Vector3 IntersectRayPlane(Vector3 rayOrigin, Vector3 rayDirection, Vector3 planeOrigin, Vector3 planeNormal)
    {
        float dist = Vector3.Dot(planeNormal, planeOrigin - rayOrigin) / Vector3.Dot(planeNormal, rayDirection);
        return rayOrigin + rayDirection * dist;
    }

    // Same as above but return intersection distance and true / false if the ray hit/miss
    public static bool IntersectRayPlane(Vector3 rayOrigin, Vector3 rayDirection, Vector3 planePosition, Vector3 planeNormal, out float t)
    {
        bool res = false;
        t = -1.0f;

        float denom = Vector3.Dot(planeNormal, rayDirection);
        if (Mathf.Abs(denom) > 1e-5)
        {
            Vector3 d = planePosition - rayOrigin;
            t = Vector3.Dot(d, planeNormal) / denom;
            res = (t >= 0);
        }

        return res;
    }

    /// <summary> Transforms a bounds by a matrix </summary>
    public static Bounds Transform(this Bounds bounds, Matrix4x4 matrix)
    {
        var result = new Bounds();
        for (var j = 0; j < 8; j++)
        {
            var x = j & 1;
            var y = (j >> 1) & 1;
            var z = j >> 2;

            var position = bounds.min + Vector3.Scale(bounds.size, new Vector3(x, y, z));
            var matrixPosition = matrix.MultiplyPoint3x4(position);

            if (j == 0)
            {
                result = new Bounds(matrixPosition, Vector3.zero);
            }
            else
            {
                result.Encapsulate(matrixPosition);
            }
        }

        return result;
    }

    private static readonly Plane[] cullingPlaneArray = new Plane[6];

    public static void CalculateFrustumPlanes(Matrix4x4 matrix, Vector4[] cullingPlaneList)
    {
        GeometryUtility.CalculateFrustumPlanes(matrix, cullingPlaneArray);
        for (var i = 0; i < 6; i++)
        {
            var p = cullingPlaneArray[i];
            cullingPlaneList[i] = new Vector4(p.normal.x, p.normal.y, p.normal.z, p.distance);
        }
    }

    public static void CalculateFrustumPlanes(Camera camera, Vector4[] cullingPlaneList)
    {
        // View projection matrices for Camera-relative rendering
        var View = camera.worldToCameraMatrix;
        View.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
        CalculateFrustumPlanes(camera.projectionMatrix * View, cullingPlaneList);
    }
}
