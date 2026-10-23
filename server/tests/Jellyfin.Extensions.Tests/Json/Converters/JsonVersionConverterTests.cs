using System;
using System.Text.Json;
using Jellyfin.Extensions.Json.Converters;
using Xunit;

namespace Jellyfin.Extensions.Tests.Json.Converters
{
    public class JsonVersionConverterTests
    {
        private readonly JsonSerializerOptions _options;

        public JsonVersionConverterTests()
        {
            _options = new JsonSerializerOptions();
            _options.Converters.Add(new JsonVersionConverter());
        }

        [Fact]
        public void Deserialize_Version_Success()
        {
            string input = "\"1.025.222\"";
            Version output = new Version(1, 25, 222);
            Version? deserializedInput = JsonSerializer.Deserialize<Version>(input, _options);
            Assert.Equal(output, deserializedInput);
        }

        [Fact]
        public void Serialize_Version_Success()
        {
            Version input = new Version(1, 09, 59);
            string output = "\"1.9.59\"";
            string serializedInput = JsonSerializer.Serialize(input, _options);
            Assert.Equal(output, serializedInput);
        }
    }
}
