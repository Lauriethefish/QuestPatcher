namespace QuestPatcher.Core
{
    public static class SystemSpecificValueExtensions
    {
        /// <summary>
        /// Creates a SystemSpecificValue that is set to the same for all systems.
        /// </summary>
        /// <param name="value">Value to set for all systems</param>
        /// <typeparam name="T">Type of the value</typeparam>
        /// <returns>A SystemSpecificValue that has the same value for all systems</returns>
        public static SystemSpecificValue<T> ForAllSystems<T>(this T value)
        {
            return new()
            {
                Any = value
            };
        }
    }
}