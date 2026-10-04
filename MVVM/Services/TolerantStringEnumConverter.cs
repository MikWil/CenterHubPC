using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CenterHubNew.MVVM.Services
{
    /// <summary>
    /// Writes enums as names; an unknown name (hand edit, newer version, renamed value) reads back
    /// as an undefined value (-1) instead of failing the whole file. The settings service's
    /// sanitiser then resets it with <c>Enum.IsDefined</c> — one bad value must not cost the user
    /// every other setting through a quarantine.
    /// </summary>
    internal sealed class TolerantStringEnumConverter : StringEnumConverter
    {
        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            try
            {
                return base.ReadJson(reader, objectType, existingValue, serializer);
            }
            catch (JsonSerializationException)
            {
                var type = Nullable.GetUnderlyingType(objectType) ?? objectType;
                return Enum.ToObject(type, -1);
            }
        }
    }
}
