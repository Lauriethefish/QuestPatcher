using System.Collections.Generic;
using System.IO;

namespace QuestPatcher.Axml
{
    /// <summary>
    /// Used to store information about the state of the document while saving AXML.
    /// </summary>
    internal class SavingContext
    {
        /// <summary>
        /// Represents a pool to reuse strings and resource IDs in the document.
        /// </summary>
        /// <typeparam name="T">Type of the values stored by the pool</typeparam>
        public class Pool<T> where T: notnull
        {
            private readonly Dictionary<T, int> _pool = new();
            private int _nextIndex;

            /// <summary>
            /// Pools or returns the index in the pool of the specified value.
            /// </summary>
            /// <param name="value">The value to pool or return the index of</param>
            /// <returns>The index (now) in the pool of the specified value</returns>
            public int GetIndex(T value)
            {
                if (_pool.TryGetValue(value, out int index))
                {
                    return index;
                }

                _pool[value] = _nextIndex;
                return _nextIndex++;
            }
            
            /// <summary>
            /// Saves this pool to an array.
            /// The indices in the array correspond to the indices returned from <see cref="GetIndex"/>
            /// </summary>
            /// <returns>A one-dimensional array of the contents of this pool</returns>
            public T[] Save()
            {
                T[] result = new T[_pool.Count];
                foreach (KeyValuePair<T, int> pair in _pool)
                {
                    result[pair.Value] = pair.Key;
                }

                return result;
            }
        }

        public Pool<string> StringPool { get; } = new();
        public Pool<int> ResourcePool { get; } = new();

        /// <summary>
        /// The writer for the main section of the document, which is written to memory.
        /// The main section is written to a <see cref="MemoryStream"/>> because the string/resource pool size determines the length at the beginning of the document.
        /// Therefore it is impossible to know what we need to write in the pools without first writing the latter part of the document.
        ///
        /// This could be also done by an initial pass that adds all the strings to the pool, but this would add a lot of extra code, even though the memory usage would be more efficient.
        /// </summary>
        public BinaryWriter Writer { get; } = new(new MemoryStream());
    }
}