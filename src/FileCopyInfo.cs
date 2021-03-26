using System.Text.Json;

namespace QuestPatcher
{
    public struct FileCopyTypeInfo
    {
        public string DestinationPath { get; }
        public string? Description { get; }

        public FileCopyTypeInfo(JsonElement element)
        {
            DestinationPath = element.GetProperty("path").GetString();

            JsonElement description;
            if(element.TryGetProperty("description", out description)) {
                this.Description = description.GetString();
            }
            else
            {
                this.Description = null;
            }
        }
    }
}
