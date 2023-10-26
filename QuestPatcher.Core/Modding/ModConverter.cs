using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuestPatcher.Core.Modding
{
    /// <summary>
    /// Used to load/save a config file containing mod data, and the provider of each mod.
    /// 
    /// Each mod is saved in the following format:
    /// {
    ///     "modProviderId": { /* mod data */ }
    /// }
    /// </summary>
    public class ModConverter : JsonConverter<IMod>
    {
        private readonly Dictionary<string, ConfigModProvider> _modProviders = new();

        /// <summary>
        /// Registers a provider to read/write mods with.
        /// </summary>
        /// <param name="provider">The provider to read/write mods with.</param>
        /// <exception cref="ArgumentException">If <paramref name="provider"/> had an ID of a provider that was already registered.</exception>
        public void RegisterProvider(ConfigModProvider provider)
        {
            if (_modProviders.ContainsKey(provider.ConfigSaveId))
            {
                throw new ArgumentException(
                    $"Attempted to register config mod provider with ID {provider.ConfigSaveId}, but a provider with this ID already existed");
            }

            _modProviders[provider.ConfigSaveId] = provider;
        }

        public override IMod? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // We are only responsible for reading mod files
            if (!typeToConvert.IsAssignableTo(typeof(IMod)))
            {
                return null;
            }

            // Skip past the object start and property name
            Debug.Assert(reader.TokenType == JsonTokenType.StartObject);
            reader.Read();
            Debug.Assert(reader.TokenType == JsonTokenType.PropertyName);

            // The property name itself is the mod provider ID
            string? providerType = reader.GetString();
            if (providerType == null)
            {
                throw new JsonException("Provider type was null");
            }

            // Check that the provider exists
            if (!_modProviders.TryGetValue(providerType, out var provider))
            {
                throw new JsonException($"Mod with invalid provider encountered: {providerType}");
            }

            // Finally, use the provider to read the mod
            var mod = provider.Read(ref reader, typeToConvert, options);
            reader.Read();
            Debug.Assert(reader.TokenType == JsonTokenType.EndObject);
            return mod;
        }

        public override void Write(Utf8JsonWriter writer, IMod value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            var provider = (ConfigModProvider) value.Provider;
            writer.WritePropertyName(provider.ConfigSaveId);
            provider.Write(writer, value, options);

            writer.WriteEndObject();
        }
    }
}
