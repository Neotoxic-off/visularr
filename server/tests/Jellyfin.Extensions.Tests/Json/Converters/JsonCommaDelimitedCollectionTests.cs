using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Extensions.Tests.Json.Models;
using MediaBrowser.Model.Session;
using Xunit;

namespace Jellyfin.Extensions.Tests.Json.Converters
{
    public class JsonCommaDelimitedCollectionTests
    {
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions()
        {
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };

        [Fact]
        public void Deserialize_String_Null_Success()
        {
            GenericBodyArrayModel<string>? value = JsonSerializer.Deserialize<GenericBodyArrayModel<string>>(@"{ ""Value"": null }", _jsonOptions);
            Assert.Null(value?.Value);
        }

        [Fact]
        public void Deserialize_Empty_Success()
        {
            GenericBodyArrayModel<string> desiredValue = new GenericBodyArrayModel<string>
            {
                Value = Array.Empty<string>()
            };

            GenericBodyArrayModel<string>? value = JsonSerializer.Deserialize<GenericBodyArrayModel<string>>(@"{ ""Value"": """" }", _jsonOptions);
            Assert.Equal(desiredValue.Value, value?.Value);
        }

        [Fact]
        public void Deserialize_EmptyList_Success()
        {
            GenericBodyListModel<string> desiredValue = new GenericBodyListModel<string>
            {
                Value = []
            };

            Assert.Throws<InvalidOperationException>(() => JsonSerializer.Deserialize<GenericBodyListModel<string>>(@"{ ""Value"": """" }", _jsonOptions));
        }

        [Fact]
        public void Deserialize_EmptyIReadOnlyList_Success()
        {
            GenericBodyIReadOnlyListModel<string> desiredValue = new GenericBodyIReadOnlyListModel<string>
            {
                Value = []
            };

            GenericBodyIReadOnlyListModel<string>? value = JsonSerializer.Deserialize<GenericBodyIReadOnlyListModel<string>>(@"{ ""Value"": """" }", _jsonOptions);
            Assert.Equal(desiredValue.Value, value?.Value);
        }

        [Fact]
        public void Deserialize_String_Valid_Success()
        {
            GenericBodyArrayModel<string> desiredValue = new GenericBodyArrayModel<string>
            {
                Value = ["a", "b", "c"]
            };

            GenericBodyArrayModel<string>? value = JsonSerializer.Deserialize<GenericBodyArrayModel<string>>(@"{ ""Value"": ""a,b,c"" }", _jsonOptions);
            Assert.Equal(desiredValue.Value, value?.Value);
        }

        [Fact]
        public void Deserialize_StringList_Valid_Success()
        {
            GenericBodyListModel<string> desiredValue = new GenericBodyListModel<string>
            {
                Value = ["a", "b", "c"]
            };

            Assert.Throws<InvalidOperationException>(() => JsonSerializer.Deserialize<GenericBodyListModel<string>>(@"{ ""Value"": ""a,b,c"" }", _jsonOptions));
        }

        [Fact]
        public void Deserialize_String_Space_Valid_Success()
        {
            GenericBodyArrayModel<string> desiredValue = new GenericBodyArrayModel<string>
            {
                Value = ["a", "b", "c"]
            };

            GenericBodyArrayModel<string>? value = JsonSerializer.Deserialize<GenericBodyArrayModel<string>>(@"{ ""Value"": ""a, b, c"" }", _jsonOptions);
            Assert.Equal(desiredValue.Value, value?.Value);
        }

        [Fact]
        public void Deserialize_GenericCommandType_Valid_Success()
        {
            GenericBodyArrayModel<GeneralCommandType> desiredValue = new GenericBodyArrayModel<GeneralCommandType>
            {
                Value = [GeneralCommandType.MoveUp, GeneralCommandType.MoveDown]
            };

            GenericBodyArrayModel<GeneralCommandType>? value = JsonSerializer.Deserialize<GenericBodyArrayModel<GeneralCommandType>>(@"{ ""Value"": ""MoveUp,MoveDown"" }", _jsonOptions);
            Assert.Equal(desiredValue.Value, value?.Value);
        }

        [Fact]
        public void Deserialize_GenericCommandType_EmptyEntry_Success()
        {
            GenericBodyArrayModel<GeneralCommandType> desiredValue = new GenericBodyArrayModel<GeneralCommandType>
            {
                Value = [GeneralCommandType.MoveUp, GeneralCommandType.MoveDown]
            };

            GenericBodyArrayModel<GeneralCommandType>? value = JsonSerializer.Deserialize<GenericBodyArrayModel<GeneralCommandType>>(@"{ ""Value"": ""MoveUp,,MoveDown"" }", _jsonOptions);
            Assert.Equal(desiredValue.Value, value?.Value);
        }

        [Fact]
        public void Deserialize_GenericCommandType_Invalid_Success()
        {
            GenericBodyArrayModel<GeneralCommandType> desiredValue = new GenericBodyArrayModel<GeneralCommandType>
            {
                Value = [GeneralCommandType.MoveUp, GeneralCommandType.MoveDown]
            };

            GenericBodyArrayModel<GeneralCommandType>? value = JsonSerializer.Deserialize<GenericBodyArrayModel<GeneralCommandType>>(@"{ ""Value"": ""MoveUp,TotallyNotAValidCommand,MoveDown"" }", _jsonOptions);
            Assert.Equal(desiredValue.Value, value?.Value);
        }

        [Fact]
        public void Deserialize_GenericCommandType_Space_Valid_Success()
        {
            GenericBodyArrayModel<GeneralCommandType> desiredValue = new GenericBodyArrayModel<GeneralCommandType>
            {
                Value = [GeneralCommandType.MoveUp, GeneralCommandType.MoveDown]
            };

            GenericBodyArrayModel<GeneralCommandType>? value = JsonSerializer.Deserialize<GenericBodyArrayModel<GeneralCommandType>>(@"{ ""Value"": ""MoveUp, MoveDown"" }", _jsonOptions);
            Assert.Equal(desiredValue.Value, value?.Value);
        }

        [Fact]
        public void Deserialize_String_Array_Valid_Success()
        {
            GenericBodyArrayModel<string> desiredValue = new GenericBodyArrayModel<string>
            {
                Value = ["a", "b", "c"]
            };

            GenericBodyArrayModel<string>? value = JsonSerializer.Deserialize<GenericBodyArrayModel<string>>(@"{ ""Value"": [""a"",""b"",""c""] }", _jsonOptions);
            Assert.Equal(desiredValue.Value, value?.Value);
        }

        [Fact]
        public void Deserialize_GenericCommandType_Array_Valid_Success()
        {
            GenericBodyArrayModel<GeneralCommandType> desiredValue = new GenericBodyArrayModel<GeneralCommandType>
            {
                Value = [GeneralCommandType.MoveUp, GeneralCommandType.MoveDown]
            };

            GenericBodyArrayModel<GeneralCommandType>? value = JsonSerializer.Deserialize<GenericBodyArrayModel<GeneralCommandType>>(@"{ ""Value"": [""MoveUp"", ""MoveDown""] }", _jsonOptions);
            Assert.Equal(desiredValue.Value, value?.Value);
        }

        [Fact]
        public void Serialize_GenericCommandType_ReadOnlyArray_Valid_Success()
        {
            GenericBodyIReadOnlyCollectionModel<GeneralCommandType> valueToSerialize = new GenericBodyIReadOnlyCollectionModel<GeneralCommandType>
            {
                Value = new[] { GeneralCommandType.MoveUp, GeneralCommandType.MoveDown }.AsReadOnly()
            };

            string value = JsonSerializer.Serialize<GenericBodyIReadOnlyCollectionModel<GeneralCommandType>>(valueToSerialize, _jsonOptions);
            Assert.Equal(@"{""Value"":[""MoveUp"",""MoveDown""]}", value);
        }

        [Fact]
        public void Serialize_GenericCommandType_ImmutableArrayArray_Valid_Success()
        {
            GenericBodyIReadOnlyCollectionModel<GeneralCommandType> valueToSerialize = new GenericBodyIReadOnlyCollectionModel<GeneralCommandType>
            {
                Value = ImmutableArray.Create(new[] { GeneralCommandType.MoveUp, GeneralCommandType.MoveDown })
            };

            string value = JsonSerializer.Serialize<GenericBodyIReadOnlyCollectionModel<GeneralCommandType>>(valueToSerialize, _jsonOptions);
            Assert.Equal(@"{""Value"":[""MoveUp"",""MoveDown""]}", value);
        }

        [Fact]
        public void Serialize_GenericCommandType_List_Valid_Success()
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
