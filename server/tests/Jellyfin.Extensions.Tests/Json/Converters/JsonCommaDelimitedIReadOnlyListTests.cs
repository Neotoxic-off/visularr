using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Extensions.Tests.Json.Models;
using MediaBrowser.Model.Session;
using Xunit;

namespace Jellyfin.Extensions.Tests.Json.Converters
{
    public class JsonCommaDelimitedIReadOnlyListTests
    {
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions()
        {
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };

        [Fact]
        public void Deserialize_String_Valid_Success()
        {
            GenericBodyIReadOnlyListModel<string> desiredValue = new GenericBodyIReadOnlyListModel<string>
            {
                Value = new[] { "a", "b", "c" }
            };

            GenericBodyIReadOnlyListModel<string>? value = JsonSerializer.Deserialize<GenericBodyIReadOnlyListModel<string>>(@"{ ""Value"": ""a,b,c"" }", _jsonOptions);
            Assert.Equal(desiredValue.Value, value?.Value);
        }

        [Fact]
        public void Deserialize_String_Space_Valid_Success()
        {
            GenericBodyIReadOnlyListModel<string> desiredValue = new GenericBodyIReadOnlyListModel<string>
            {
                Value = new[] { "a", "b", "c" }
            };

            GenericBodyIReadOnlyListModel<string>? value = JsonSerializer.Deserialize<GenericBodyIReadOnlyListModel<string>>(@"{ ""Value"": ""a, b, c"" }", _jsonOptions);
            Assert.Equal(desiredValue.Value, value?.Value);
        }

        [Fact]
        public void Deserialize_GenericCommandType_Valid_Success()
        {
            GenericBodyIReadOnlyListModel<GeneralCommandType> desiredValue = new GenericBodyIReadOnlyListModel<GeneralCommandType>
            {
                Value = new[] { GeneralCommandType.MoveUp, GeneralCommandType.MoveDown }
            };

            GenericBodyIReadOnlyListModel<GeneralCommandType>? value = JsonSerializer.Deserialize<GenericBodyIReadOnlyListModel<GeneralCommandType>>(@"{ ""Value"": ""MoveUp,MoveDown"" }", _jsonOptions);
            Assert.Equal(desiredValue.Value, value?.Value);
        }

        [Fact]
        public void Deserialize_GenericCommandType_Space_Valid_Success()
        {
            GenericBodyIReadOnlyListModel<GeneralCommandType> desiredValue = new GenericBodyIReadOnlyListModel<GeneralCommandType>
            {
                Value = new[] { GeneralCommandType.MoveUp, GeneralCommandType.MoveDown }
            };

            GenericBodyIReadOnlyListModel<GeneralCommandType>? value = JsonSerializer.Deserialize<GenericBodyIReadOnlyListModel<GeneralCommandType>>(@"{ ""Value"": ""MoveUp, MoveDown"" }", _jsonOptions);
            Assert.Equal(desiredValue.Value, value?.Value);
        }

        [Fact]
        public void Deserialize_String_Array_Valid_Success()
        {
            GenericBodyIReadOnlyListModel<string> desiredValue = new GenericBodyIReadOnlyListModel<string>
            {
                Value = new[] { "a", "b", "c" }
            };

            GenericBodyIReadOnlyListModel<string>? value = JsonSerializer.Deserialize<GenericBodyIReadOnlyListModel<string>>(@"{ ""Value"": [""a"",""b"",""c""] }", _jsonOptions);
            Assert.Equal(desiredValue.Value, value?.Value);
        }

        [Fact]
        public void Deserialize_GenericCommandType_Array_Valid_Success()
        {
            GenericBodyIReadOnlyListModel<GeneralCommandType> desiredValue = new GenericBodyIReadOnlyListModel<GeneralCommandType>
            {
                Value = new[] { GeneralCommandType.MoveUp, GeneralCommandType.MoveDown }
            };

            GenericBodyIReadOnlyListModel<GeneralCommandType>? value = JsonSerializer.Deserialize<GenericBodyIReadOnlyListModel<GeneralCommandType>>(@"{ ""Value"": [""MoveUp"", ""MoveDown""] }", _jsonOptions);
            Assert.Equal(desiredValue.Value, value?.Value);
        }

        [Fact]
        public void Serialize_GenericCommandType_IReadOnlyList_Valid_Success()
        {
            GenericBodyIReadOnlyListModel<GeneralCommandType> valueToSerialize = new GenericBodyIReadOnlyListModel<GeneralCommandType>
            {
                Value = new List<GeneralCommandType> { GeneralCommandType.MoveUp, GeneralCommandType.MoveDown }
            };

            string value = JsonSerializer.Serialize<GenericBodyIReadOnlyListModel<GeneralCommandType>>(valueToSerialize, _jsonOptions);
            Assert.Equal(@"{""Value"":[""MoveUp"",""MoveDown""]}", value);
        }
    }
}
