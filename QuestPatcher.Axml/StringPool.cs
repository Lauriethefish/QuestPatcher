using System;
using System.Collections.Generic;

namespace QuestPatcher.Axml
{
    internal class StringPool
    {
        private readonly Dictionary<string, int> _pool = new Dictionary<string, int>();
        private int _currentIdx;

        private int _idxOffset = -1;

        internal void Add(string str)
        {
            if(_idxOffset != -1)
            {
                throw new InvalidOperationException("Cannot add new string during the saving phase");
            }
            
            if(!_pool.ContainsKey(str))
            {
                _pool[str] = _currentIdx;
                _currentIdx++;
            }
        }

        internal int GetIndex(string str)
        {
            if(_idxOffset == -1)
            {
                throw new InvalidOperationException(nameof(GetIndex) + " should not be used until the string IDs have finished being prepared and the resource map offset is ready");
            }
            
            if(_pool.TryGetValue(str, out int idx))
            {
                // Offset by the size of the resource map. Strings associated with resource keys in the resource map come first and must be matched in index with their resource keys
                return idx + _idxOffset;
            }

            throw new InvalidOperationException("Tried to get index of string which had not been added yet. This string was not added during the preparation phase!");
        }

        internal string[] PrepareForSavePhase(ResourceMap resourceMap)
        {
            string[] result = new string[_pool.Count + resourceMap.Map.Count];

            // All string pool string indices must be offset by the size of the resource map, as resource map strings come first
            _idxOffset = resourceMap.Map.Count;
            
            foreach(var resource in resourceMap.Map)
            {
                result[resource.Value] = resource.Key.attributeName;
            }

            foreach(var stringItem in _pool)
            {
                result[stringItem.Value + _idxOffset] = stringItem.Key;
            }

            return result;
        }
    }
}
