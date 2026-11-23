using UnityEditor;
using UnityEngine;

namespace B2U
{
    public class B2UMasterShaderGUI : ShaderGUI
    {
        public static Texture2D defaultIcon;
        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            // Stops drawing of material properties when a gamobject with that material is clicked
            if (Selection.activeGameObject != null)
            {
                EditorGUILayout.HelpBox("B2U Master Material properties are managed globally by the active B2U Shader Preset and B2U Palette Data", MessageType.Info);
                return;
            }

            base.OnGUI(materialEditor, properties);
        }

        public override void OnMaterialPreviewGUI(MaterialEditor materialEditor, Rect r, GUIStyle background)
        {
            // Pulls an icon to draw rather than the material preview since it doesnt show anything anyways. Also stops editor stuttering due to preview compilation.
            if (defaultIcon == null) defaultIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("MatIcon t:Texture2D")[0]));
            GUI.DrawTexture(r, defaultIcon);
        }
    }
}