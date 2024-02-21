using UnityEngine;

public class CoreUtils
{
    /// <summary>
    /// List of look at matrices for cubemap faces.
    /// Ref: https://msdn.microsoft.com/en-us/library/windows/desktop/bb204881(v=vs.85).aspx
    /// </summary>
    static public readonly Vector3[] lookAtList =
    {
            new Vector3(1.0f, 0.0f, 0.0f),
            new Vector3(-1.0f, 0.0f, 0.0f),
            new Vector3(0.0f, 1.0f, 0.0f),
            new Vector3(0.0f, -1.0f, 0.0f),
            new Vector3(0.0f, 0.0f, 1.0f),
            new Vector3(0.0f, 0.0f, -1.0f),
        };

    /// <summary>
    /// List of up vectors for cubemap faces.
    /// Ref: https://msdn.microsoft.com/en-us/library/windows/desktop/bb204881(v=vs.85).aspx
    /// </summary>
    static public readonly Vector3[] upVectorList =
    {
            new Vector3(0.0f, 1.0f, 0.0f),
            new Vector3(0.0f, 1.0f, 0.0f),
            new Vector3(0.0f, 0.0f, -1.0f),
            new Vector3(0.0f, 0.0f, 1.0f),
            new Vector3(0.0f, 1.0f, 0.0f),
            new Vector3(0.0f, 1.0f, 0.0f),
        };

    static RenderTexture m_EmptyUAV;

    /// <summary>
    /// Empty 1x1 texture usable as a dummy UAV.
    /// </summary>
    public static RenderTexture emptyUAV
    {
        get
        {
            if (m_EmptyUAV == null)
            {
                m_EmptyUAV = new RenderTexture(1, 1, 0);
                m_EmptyUAV.enableRandomWrite = true;
                m_EmptyUAV.Create();
            }

            return m_EmptyUAV;
        }
    }
}