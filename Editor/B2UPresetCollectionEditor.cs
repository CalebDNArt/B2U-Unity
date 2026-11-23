#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace B2U
{
    /// <summary>
    /// Base custom Inspector for B2U fullscreen preset collections, providing a reorderable list
    /// and an inline embedded Inspector for the currently selected preset.
    /// </summary>
    public abstract class B2UPresetCollectionEditor : Editor
    {
        private ReorderableList presetList;
        private SerializedProperty presetsProp;
        private SerializedProperty activePresetProp;

        private Editor embeddedPresetEditor;
        private Object lastTargetObject;

        private static GUIStyle s_HeaderStyle;
        private static readonly GUIContent SetActiveBtnContent = new GUIContent(
            "Set as Active Collection",
            "Binds this preset collection to the active B2UShaderProfile."
        );

        protected virtual void OnEnable()
        {
            presetsProp = serializedObject.FindProperty("presets");
            activePresetProp = serializedObject.FindProperty("activePreset");

            presetList = new ReorderableList(serializedObject, presetsProp, true, true, true, true)
            {
                drawHeaderCallback = rect =>
                {
                    EditorGUI.LabelField(rect, "Presets (Drag handles to reorder)");
                },
                onReorderCallbackWithDetails = (list, oldIndex, newIndex) =>
                {
                    if (activePresetProp.intValue == oldIndex)
                    {
                        activePresetProp.intValue = newIndex;
                    }
                    else if (oldIndex < activePresetProp.intValue && newIndex >= activePresetProp.intValue)
                    {
                        activePresetProp.intValue--;
                    }
                    else if (oldIndex > activePresetProp.intValue && newIndex <= activePresetProp.intValue)
                    {
                        activePresetProp.intValue++;
                    }
                    serializedObject.ApplyModifiedProperties();
                },
                onRemoveCallback = list =>
                {
                    ReorderableList.defaultBehaviours.DoRemoveButton(list);
                    if (activePresetProp.intValue >= presetsProp.arraySize)
                    {
                        activePresetProp.intValue = Mathf.Max(-1, presetsProp.arraySize - 1);
                    }
                    serializedObject.ApplyModifiedProperties();
                }
            };

            presetList.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                rect.y += 2f;
                SerializedProperty element = presetsProp.GetArrayElementAtIndex(index);

                bool isEditing = (index == activePresetProp.intValue);
                if (isEditing)
                {
                    Rect bgRect = new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height);
                    EditorGUI.DrawRect(bgRect, new Color(0.3f, 0.5f, 0.8f, 0.3f));
                }

                Rect indexRect = new Rect(rect.x, rect.y, 25f, EditorGUIUtility.singleLineHeight);
                EditorGUI.LabelField(indexRect, $"{index}:");

                Rect objRect = new Rect(rect.x + 25f, rect.y, rect.width - 95f, EditorGUIUtility.singleLineHeight);
                EditorGUI.PropertyField(objRect, element, GUIContent.none);

                Rect btnRect = new Rect(rect.x + rect.width - 65f, rect.y, 65f, EditorGUIUtility.singleLineHeight);
                using (new EditorGUI.DisabledScope(isEditing))
                {
                    if (GUI.Button(btnRect, isEditing ? "Editing" : "Edit"))
                    {
                        activePresetProp.intValue = index;
                        serializedObject.ApplyModifiedProperties();

                        if (B2URuntimeUtility.ActiveProfile != null)
                        {
                            SetPreset(index);
                        }
                        GUI.FocusControl(null);
                    }
                }
            };
        }

        protected virtual void OnDisable()
        {
            if (embeddedPresetEditor != null)
            {
                DestroyImmediate(embeddedPresetEditor);
                embeddedPresetEditor = null;
            }
        }

        public abstract void SetPreset(int index);
        public abstract void SetAsActiveCollection();

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (GUILayout.Button(SetActiveBtnContent, GUILayout.Height(30f)))
            {
                SetAsActiveCollection();
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space(10f);

            presetList.DoLayoutList();

            serializedObject.ApplyModifiedProperties();

            DrawEmbeddedPresetEditor();
        }

        private void DrawEmbeddedPresetEditor()
        {
            int activeIdx = activePresetProp.intValue;
            if (activeIdx < 0 || activeIdx >= presetsProp.arraySize) return;

            SerializedProperty activeElement = presetsProp.GetArrayElementAtIndex(activeIdx);
            Object activeObject = activeElement.objectReferenceValue;
            if (activeObject == null) return;

            if (embeddedPresetEditor == null || lastTargetObject != activeObject)
            {
                if (embeddedPresetEditor != null) DestroyImmediate(embeddedPresetEditor);
                embeddedPresetEditor = CreateEditor(activeObject);
                lastTargetObject = activeObject;
            }

            if (s_HeaderStyle == null)
            {
                s_HeaderStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter
                };
            }

            EditorGUILayout.Space(15f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField($"--- Editing: {activeObject.name} ---", s_HeaderStyle);
            EditorGUILayout.Space(5f);

            EditorGUI.indentLevel++;
            embeddedPresetEditor.OnInspectorGUI();
            EditorGUI.indentLevel--;

            EditorGUILayout.EndVertical();
        }
    }

    [CustomEditor(typeof(B2UStatusEffectPresetCollection))]
    public class B2UStatusEffectPresetCollectionEditor : B2UPresetCollectionEditor
    {
        public override void SetPreset(int index)
        {
            B2URuntimeUtility.SetStatusEffectPreset(index);
        }

        public override void SetAsActiveCollection()
        {
            ((B2UStatusEffectPresetCollection)target).SetAsActiveCollection();
        }
    }

    [CustomEditor(typeof(B2URecolorPresetCollection))]
    public class B2URecolorPresetCollectionEditor : B2UPresetCollectionEditor
    {
        public override void SetPreset(int index)
        {
            B2URuntimeUtility.SetRecolorPreset(index);
        }

        public override void SetAsActiveCollection()
        {
            ((B2URecolorPresetCollection)target).SetAsActiveCollection();
        }
    }

    [CustomEditor(typeof(B2UStylizePresetCollection))]
    public class B2UStylizePresetCollectionEditor : B2UPresetCollectionEditor
    {
        public override void SetPreset(int index)
        {
            B2URuntimeUtility.SetStylizePreset(index);
        }

        public override void SetAsActiveCollection()
        {
            ((B2UStylizePresetCollection)target).SetAsActiveCollection();
        }
    }
}
#endif