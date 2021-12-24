using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuestPatcher.Core.Modding
{
    [JsonConverter(typeof(IMod))]
    public class ModConverter : JsonConverter<IMod>
    {
        private readonly Dictionary<string, ConfigModProvider> _modProviders = new();
        
        public void RegisterProvider(ConfigModProvider provider)
        {
            if(_modProviders.ContainsKey(provider.ConfigSaveId))
            {
                throw new InvalidOperationException(
                    $"Attempted to register config mod provider with ID {provider.ConfigSaveId}, but a provider with this ID already existed");
            }

            _modProviders[provider.ConfigSaveId] = provider;
        }

        public override IMod? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if(!typeToConvert.IsAssignableTo(typeof(IMod)))
            {
                return null;
            }
            
            Debug.Assert(reader.TokenType == JsonTokenType.StartObject);
            reader.Read();
            Debug.Assert(reader.TokenType == JsonTokenType.PropertyName);
            
            string? providerType = reader.GetString();
            if(providerType == null)
            {
                throw new NullReferenceException("Provider type was null");
            }

            ConfigModProvider provider = _modProviders[providerType];

            IMod? mod = provider.Read(ref reader, typeToConvert, options);
            reader.Read();
            Debug.Assert(reader.TokenType == JsonTokenType.EndObject);
            return mod;
        }

        public override void Write(Utf8JsonWriter writer, IMod value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            ConfigModProvider provider = (ConfigModProvider) value.Provider;
            writer.WritePropertyName(provider.ConfigSaveId);
            provider.Write(writer, value, options);
            
            writer.WriteEndObject();
        }
    }
}
