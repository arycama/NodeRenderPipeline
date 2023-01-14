using UnityEngine;

public static class CameraExtensions
{
    public static Vector2Int Resolution(this Camera camera) => new Vector2Int(camera.pixelWidth, camera.pixelHeight);

    public static int MipCount(this Camera camera) => Texture2DExtensions.MipCount(camera.pixelWidth, camera.pixelHeight);
}
