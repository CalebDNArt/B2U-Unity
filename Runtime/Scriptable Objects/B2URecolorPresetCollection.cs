using System;
using System.Collections.Generic;
using UnityEngine;
using static B2U.B2UDebug;

namespace B2U
{
    /// <summary>
    /// Stores an indexed and name-searchable collection of B2URecolorPreset assets for a B2UShaderProfile.
    /// </summary>
    [CreateAssetMenu(fileName = "New Recolor Preset Collection", menuName = "B2U/Fullscreen/Collections/Recolor Preset Collection")]
    public class B2URecolorPresetCollection : ScriptableObject
    {
        private const B2UMessageSource msgSrc = B2UMessageSource.Runtime;

        /// <summary>
        /// A list of the recolor presets on this collection.
        /// </summary>
        public List<B2URecolorPreset> presets = new List<B2URecolorPreset>();
        /// <summary>
        /// The active preset in this collection.
        /// </summary>
        public int activePreset = 0;

        /// <summary>
        /// A dictionary of the name of each preset and the preset object. Enables looking up the preset by name.
        /// </summary>
        [NonSerialized] private Dictionary<string, B2URecolorPreset> nameDictionary;

        private void OnEnable()
        {
            BuildDictionary();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            BuildDictionary();
        }
#endif

        /// <summary>
        /// Rebuilds the runtime name-to-preset lookup dictionary.
        /// </summary>
        public void BuildDictionary()
        {
            if (nameDictionary == null) nameDictionary = new Dictionary<string, B2URecolorPreset>();
            else nameDictionary.Clear();

            for (int i = 0; i < presets.Count; i++)
            {
                B2URecolorPreset preset = presets[i];
                if (preset != null && !string.IsNullOrEmpty(preset.name) && !nameDictionary.ContainsKey(preset.name))
                {
                    nameDictionary.Add(preset.name, preset);
                }
            }
        }

        /// <summary>
        /// Binds this collection as the active recolor preset collection on the active B2UShaderProfile.
        /// </summary>
        public void SetAsActiveCollection()
        {
            if (B2URuntimeUtility.ActiveProfile != null)
            {
                B2URuntimeUtility.SetB2UShaderPropertyValue(B2UShaderProperty.RecolorPresets, this);
            }
        }

        /// <summary>
        /// Retrieves a recolor preset by its list index.
        /// </summary>
        public B2URecolorPreset GetPreset(int index)
        {
            if (index >= 0 && index < presets.Count)
            {
                return presets[index];
            }
            B2ULogWarning(msgSrc, $"Index {index} is out of bounds for Recolor Preset Collection '{name}'.", this);
            return null;
        }

        /// <summary>
        /// Retrieves a recolor preset by its asset name.
        /// </summary>
        public B2URecolorPreset GetPreset(string presetName)
        {
            if (string.IsNullOrEmpty(presetName)) return null;

            if (nameDictionary == null)
            {
                BuildDictionary();
            }

            if (nameDictionary.TryGetValue(presetName, out B2URecolorPreset preset) && preset != null)
            {
                return preset;
            }
            B2ULogWarning(msgSrc, $"Preset '{presetName}' not found in Recolor Preset Collection '{name}'.", this);
            return null;
        }
    }
}