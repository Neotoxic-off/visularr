using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Jellyfin.Api.Middleware.Tests
{
    public static class UrlDecodeQueryFeatureTests
    {
        [Theory]
        [InlineData("e0a72cb2a2c7", "e0a72cb2a2c7")] // isn't encoded
        public static void EmptyValueTest(string query, string key)
        {
            Dictionary<string, StringValues> dict = new Dictionary<string, StringValues>
            {
                { query, StringValues.Empty }
            };
            UrlDecodeQueryFeature test = new UrlDecodeQueryFeature(new QueryFeature(new QueryCollection(dict)));
            Assert.Single(test.Query);
            (string? k, StringValues v) = test.Query.First();
            Assert.Equal(key, k);
            Assert.True(StringValues.IsNullOrEmpty(v));
        }
    }
}
