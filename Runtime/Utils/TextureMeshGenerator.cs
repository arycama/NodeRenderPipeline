using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

public class TextureMeshGenerator
{
    public static Mesh Generate(Texture2D texture, int maxVertices = 100)
    {
        var pixels = texture.GetPixels();
        var width = texture.width;
        var height = texture.height;
        var visibleTexels = new bool[width, height];

        for (var i = 0; i < pixels.Length; i++)
        {
            // Check if this pixel is transparent
            if (pixels[i].r <= 0f)
                continue;

            // Calculate coords
            var x = i % width;
            var y = i / width;

            // Check if neighboring pixels are transparent
            visibleTexels[x, y] = true;
        }

        var rcpWidth = 1f / texture.width;
        var rcpHeight = 1f / texture.height;
        var candidatePoints = new List<Vector2>();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!visibleTexels[x, y])
                    continue;

                var r = IsPixelVisible(x + 1, y + 0, width, height, visibleTexels);
                var tr = IsPixelVisible(x + 1, y + 1, width, height, visibleTexels);
                var t = IsPixelVisible(x + 0, y + 1, width, height, visibleTexels);
                var tl = IsPixelVisible(x - 1, y + 1, width, height, visibleTexels);
                var l = IsPixelVisible(x - 1, y + 0, width, height, visibleTexels);
                var bl = IsPixelVisible(x - 1, y - 1, width, height, visibleTexels);
                var b = IsPixelVisible(x + 0, y - 1, width, height, visibleTexels);
                var br = IsPixelVisible(x + 1, y - 1, width, height, visibleTexels);

                // Check top right
                if (!r && !tr && !t)
                    candidatePoints.Add(new Vector2((x + 1f) * rcpWidth, (y + 1f) * rcpHeight));

                // Check top left
                if (!t && !tl && !l)
                    candidatePoints.Add(new Vector2((x + 0f) * rcpWidth, (y + 1f) * rcpHeight));

                // Check bottom left
                if (!l && !bl && !b)
                    candidatePoints.Add(new Vector2((x + 0f) * rcpWidth, (y + 0f) * rcpHeight));

                // Check bottom right
                if (!b && !br && !r)
                    candidatePoints.Add(new Vector2((x + 1f) * rcpWidth, (y + 0f) * rcpHeight));
            }
        }

        var points = GetConvexHull(candidatePoints);
        var safety = 0;

        // Trim vertices
        while (points.Count > maxVertices)
        {
            var shortestEdge = float.MaxValue;
            var shortestIndex = 0;
            var newVertex = Vector2.zero;
            for (var i = 0; i < points.Count; i++)
            {
                var a = points[(i + 1) % points.Count];
                var b = points[(i + 2) % points.Count];

                // Find where both vertices intersect
                var A = points[(i + 0) % points.Count];
                var B = points[(i + 1) % points.Count];
                var C = points[(i + 2) % points.Count];
                var D = points[(i + 3) % points.Count];

                // Line AB represented as a1x + b1y = c1
                var a1 = B.y - A.y;
                var b1 = A.x - B.x;
                var c1 = a1 * A.x + b1 * A.y;

                // Line CD represented as a2x + b2y = c2
                var a2 = D.y - C.y;
                var b2 = C.x - D.x;
                var c2 = a2 * C.x + b2 * C.y;

                var determinant = a1 * b2 - a2 * b1;

                var x = (b2 * c1 - b1 * c2) / determinant;
                var y = (a1 * c2 - a2 * c1) / determinant;

                var area = AreaOfTriangle(a, b, new Vector2(x, y));

                if (area < shortestEdge)
                {
                    shortestIndex = i;
                    shortestEdge = area;
                    newVertex = new Vector2(x, y);
                }
            }

            // Remove one vertex and modify the longer one
            points[(shortestIndex + 1) % points.Count] = newVertex;
            points.RemoveAt((shortestIndex + 2) % points.Count);
        }

        var vertices = points.Select(point => (Vector3)(point - Vector2.one * 0.5f)).ToArray();
        var vertexIds = Enumerable.Range(0, vertices.Length - 1).ToList();
        var triangles = new List<int>();

        var c = 0;
        while (vertexIds.Count >= 3)
        {
            if (safety++ > 1000)
            {
                Debug.LogError("Infinite Loop");
                break;
            }

            var vid0 = vertexIds[(c + 0) % vertexIds.Count];
            var vid1 = vertexIds[(c + 1) % vertexIds.Count];
            var vid2 = vertexIds[(c + 2) % vertexIds.Count];

            var inCircle = false;
            for (var i = 0; i < vertexIds.Count; i++)
            {
                // Skip current points
                if (i == c + 0 || i == c + 1 || i == c + 2)
                    continue;

                var v0 = vertices[vid0];
                var v1 = vertices[vid1];
                var v2 = vertices[vid2];
                var v3 = vertices[vertexIds[i]];

                if (!InCircle(v0, v1, v2, v3))
                    continue;

                inCircle = true;
                break;
            }

            if (inCircle)
            {
                c = (c + 1) % vertexIds.Count;
            }
            else
            {
                triangles.Add(vid2);
                triangles.Add(vid1);
                triangles.Add(vid0);
                vertexIds.RemoveAt((c + 1) % vertexIds.Count);
            }
        }

        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, points);
        mesh.SetTriangles(triangles, 0);
        return mesh;
    }

    private static bool InCircle(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        return (
            ((a.x - d.x) * (a.x - d.x) + (a.y - d.y) * (a.y - d.y)) * ((b.x - d.x) * (c.y - d.y) - (c.x - d.x) * (b.y - d.y)) -
            ((b.x - d.x) * (b.x - d.x) + (b.y - d.y) * (b.y - d.y)) * ((a.x - d.x) * (c.y - d.y) - (c.x - d.x) * (a.y - d.y)) +
            ((c.x - d.x) * (c.x - d.x) + (c.y - d.y) * (c.y - d.y)) * ((a.x - d.x) * (b.y - d.y) - (b.x - d.x) * (a.y - d.y))
        ) > 0;
    }

    public static float AreaOfTriangle(Vector2 pt1, Vector2 pt2, Vector2 pt3)
    {
        var a = Vector2.Distance(pt1, pt2);
        var b = Vector2.Distance(pt2, pt3);
        var c = Vector2.Distance(pt3, pt1);
        var s = (a + b + c) / 2;
        return Mathf.Sqrt(s * (s - a) * (s - b) * (s - c));
    }

    private static bool IsPixelVisible(int x, int y, int width, int height, bool[,] points)
    {
        return x >= 0 && x < width && y >= 0 && y < height && points[x, y];
    }

    private static List<Vector2> GetConvexHull(List<Vector2> points)
    {
        //The list with points on the convex hull
        var convexHull = new List<Vector2>();

        //Step 1. Find the vertex with the smallest x coordinate
        //If several have the same x coordinate, find the one with the smallest z
        var startVertex = points[0];
        var startPos = startVertex;

        for (var i = 1; i < points.Count; i++)
        {
            var testPos = points[i];

            //Because of precision issues, we use Mathf.Approximately to test if the x positions are the same
            if (testPos.x < startPos.x || (Mathf.Approximately(testPos.x, startPos.x) && testPos.y < startPos.y))
            {
                startVertex = points[i];
                startPos = startVertex;
            }
        }

        //This vertex is always on the convex hull
        convexHull.Add(startVertex);
        points.Remove(startVertex);

        //Step 2. Loop to generate the convex hull
        var currentPoint = convexHull[0];

        //Store colinear points here - better to create this list once than each loop
        var colinearPoints = new List<Vector2>();
        var counter = 0;

        while (true)
        {
            //After 2 iterations we have to add the start position again so we can terminate the algorithm
            //Cant use convexhull.count because of colinear points, so we need a counter
            if (counter == 2)
            {
                points.Add(convexHull[0]);
            }

            //Pick next point randomly
            var nextPoint = points[Random.Range(0, points.Count)];

            //To 2d space so we can see if a point is to the left is the vector ab
            var a = currentPoint;
            var b = nextPoint;

            //Test if there's a point to the right of ab, if so then it's the new b
            for (var i = 0; i < points.Count; i++)
            {
                //Dont test the point we picked randomly
                if (points[i] == nextPoint)
                    continue;

                var c = points[i];

                //Where is c in relation to a-b
                // < 0 -> to the right
                // = 0 -> on the line
                // > 0 -> to the left
                //float relation = Geometry.IsAPointLeftOfVectorOrOnTheLine(a, b, c);
                var relation = (a.x - c.x) * (b.y - c.y) - (a.y - c.y) * (b.x - c.x);

                //Colinear points
                //Cant use exactly 0 because of floating point precision issues
                //This accuracy is smallest possible, if smaller points will be missed if we are testing with a plane
                var accuracy = 0.00001f;

                if (relation < accuracy && relation > -accuracy)
                {
                    colinearPoints.Add(points[i]);
                }
                //To the right = better point, so pick it as next point on the convex hull
                else if (relation < 0f)
                {
                    nextPoint = points[i];

                    b = nextPoint;

                    //Clear colinear points
                    colinearPoints.Clear();
                }
                // To the left = worse point so do nothing
            }

            //If we have colinear points
            if (colinearPoints.Count > 0)
            {
                colinearPoints.Add(nextPoint);

                // Sort this list, so we can add the colinear points in correct order
                colinearPoints = colinearPoints.OrderBy(n => Vector2.SqrMagnitude(n - currentPoint)).ToList();

                convexHull.AddRange(colinearPoints);

                currentPoint = colinearPoints[colinearPoints.Count - 1];

                // Remove the points that are now on the convex hull
                for (var i = 0; i < colinearPoints.Count; i++)
                {
                    points.Remove(colinearPoints[i]);
                }

                colinearPoints.Clear();
            }
            else
            {
                convexHull.Add(nextPoint);
                points.Remove(nextPoint);
                currentPoint = nextPoint;
            }

            //Have we found the first point on the hull? If so we have completed the hull
            if (currentPoint == convexHull[0])
            {
                // Then remove it because it is the same as the first point, and we want a convex hull with no duplicates
                convexHull.RemoveAt(convexHull.Count - 1);
                break;
            }

            counter++;
        }

        return convexHull;
    }
}