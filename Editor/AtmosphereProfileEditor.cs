using UnityEditor;

[CustomEditor(typeof(AtmosphereProfile))]
public class AtmosphereProfileEditor : Editor
{
    public override void OnInspectorGUI()
    {
        using (var changed = new EditorGUI.ChangeCheckScope())
        {
            base.OnInspectorGUI();

            if (changed.changed)
            {
                var atmosphereProfile = target as AtmosphereProfile;
                atmosphereProfile.ProfileChanged();
            }
        }
    }
}