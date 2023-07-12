using UnityEngine;

public class ProjTest : MonoBehaviour
{
    public Vector3 p;
    public GameObject[] childs;

    public void OnDrawGizmos()
    {
        Gizmos.DrawLine(childs[0].transform.position, childs[1].transform.position);
        Gizmos.DrawLine(childs[1].transform.position, childs[2].transform.position);
        Gizmos.DrawLine(childs[2].transform.position, childs[3].transform.position);

        var A = childs[0].transform.position;
        var B = childs[1].transform.position;
        var C = childs[2].transform.position;
        var D = childs[3].transform.position;

        // Line AB represented as a1x + b1y = c1
        var a1 = B.z - A.z;
        var b1 = A.x - B.x;
        var c1 = a1 * (A.x) + b1 * (A.z);

        // Line CD represented as a2x + b2y = c2
        var a2 = D.z - C.z;
        var b2 = C.x - D.x;
        var c2 = a2 * (C.x) + b2 * (C.z);

        var determinant = a1 * b2 - a2 * b1;

        var x = (b2 * c1 - b1 * c2) / determinant;
        var y = (a1 * c2 - a2 * c1) / determinant;
        p = new Vector3(x, transform.position.y, y);


        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(p, 0.1f);
        Gizmos.DrawLine(B, p);
        Gizmos.DrawLine(C, p);
    }
}
