using System;
using System.Collections.Generic;

namespace QuestPatcher.Axml
{
    /// <summary>
    /// Handles the AXML resource map during saving
    /// </summary>
    internal class ResourceMap
    {
        internal Dictionary<(string attributeName, int resourceKey), int> Map { get; } = new Dictionary<(string attributeName, int resourceKey), int>();
        
        private int _currentIdx;
        
        internal void Add(string attributeName, int resourceKey)
        {
            var key = (attributeName, resourceKey);
            if(!Map.ContainsKey(key))
            {
                Map[key] = _currentIdx;
                _currentIdx++;
            }
        }

        internal int GetIndex(string resourceName, int resourceKey)
        {
            if(Map.TryGetValue((resourceName, resourceKey), out int idx))
            {
                return idx;
            }

            throw new InvalidOperationException("Tried to get index of resource which had not been added yet. Resource IDs were not prepared before saving!");
        }

        internal int[] Save()
        {
            int[] result = new int[Map.Count];
            
            foreach(var resource in Map)
            {
                result[resource.Value] = resource.Key.resourceKey;
            }

            return result;
        }
    }
}
