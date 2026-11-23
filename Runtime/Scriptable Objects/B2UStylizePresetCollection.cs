using System;
using System.Collections.Generic;
using UnityEngine;
using static B2U.B2UDebug;

namespace B2U
{
    /// <summary>
    /// Stores an indexed and name-searchable collection of B2UStylizePreset assets for a B2UShaderProfile.
    /// </summary>
    [CreateAssetMenu(fileName = "New Stylize Preset Collection", menuName = "B2U/Fullscreen/Collections/Stylize Preset Collection")]
    public class B2UStylizePresetCollection : ScriptableObject
    {
        private const B2UMessageSource msgSrc = B2UMessageSource.Runtime;

        /// <summary>
        /// A list of the stylize presets on this collection.
        /// </summary>
        public List<B2UStylizePreset> presets = new List<B2UStylizePreset>();
        /// <summary>
        /// The active preset in this collection.
        /// </summary>
        public int activePreset = 0;

        /// <summary>
        /// A dictionary of the name of each preset and the preset object. Enables looking up the preset by name.
        /// </summary>
        [NonSerialized] private Dictionary<string, B2UStylizePreset> nameDictionary;

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
            if (nameDictionary == null) nameDictionary = new Dictionary<string, B2UStylizePreset>();
            else nameDictionary.Clear();

            for (int i = 0; i < presets.Count; i++)
            {
                B2UStylizePreset preset = presets[i];
                if (preset != null && !string.IsNullOrEmpty(preset.name) && !nameDictionary.ContainsKey(preset.name))
                {
                    nameDictionary.Add(preset.name, preset);
                }
            }
        }

        /// <summary>
        /// Binds this collection as the active stylize preset collection on the active B2UShaderProfile.
        /// </summary>
        public void SetAsActiveCollection()
        {
            if (B2URuntimeUtility.ActiveProfile != null)
            {
                B2URuntimeUtility.SetB2UShaderPropertyValue(B2UShaderProperty.StylizePresets, this);
            }
        }

        /// <summary>
        /// Retrieves a stylize preset by its list index.
        /// </summary>
        public B2UStylizePreset GetPreset(int index)
        {
            if (index >= 0 && index < presets.Count)
            {
                return presets[index];
            }
            B2ULogWarning(msgSrc, $"Index {index} is out of bounds for Stylize Preset Collection '{name}'.", this);
            return null;
        }

        /// <summary>
        /// Retrieves a stylize preset by its asset name.
        /// </summary>
        public B2UStylizePreset GetPreset(string presetName)
        {
            if (string.IsNullOrEmpty(presetName)) return null;

            if (nameDictionary == null)
            {
                BuildDictionary();
            }

            if (nameDictionary.TryGetValue(presetName, out B2UStylizePreset preset) && preset != null)
            {
                return preset;
            }
            B2ULogWarning(msgSrc, $"Preset '{presetName}' not found in Stylize Preset Collection '{name}'.", this);
            return null;
        }
    }
}