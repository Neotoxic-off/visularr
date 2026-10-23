using System;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace Jellyfin.Server.Tests
{
    public class StartupTests
    {
        [Theory]
        [InlineData("/web/index.html", "index.html", "no-cache")]
        [InlineData("/web/_next/static/chunks/abc.js", "abc.js", "public, max-age=31536000, immutable")]
        [InlineData("/web/icon.svg", "icon.svg", "")]
        public void SetWebClientHeaders_SetsSecurityAndCacheHeaders(string path, string fileName, string cacheControl)
        {
            DefaultHttpContext httpContext = new DefaultHttpContext();
            httpContext.Request.Path = path;
            IFileInfo file = new NotFoundFileInfo(fileName);

            Startup.SetWebClientHeaders(new StaticFileResponseContext(httpContext, file));

            IHeaderDictionary headers = httpContext.Response.Headers;
            Assert.Equal("DENY", headers.XFrameOptions);
            Assert.Equal("nosniff", headers.XContentTypeOptions);
            Assert.Equal("same-origin", headers["Referrer-Policy"]);
            Assert.Contains("frame-ancestors 'none'", headers.ContentSecurityPolicy.ToString(), StringComparison.Ordinal);
            Assert.Equal(cacheControl, headers.CacheControl.ToString());
        }

        [Theory]
        [InlineData("203.0.113.5", "203.0.113.5")]
        [InlineData("::ffff:203.0.113.5", "203.0.113.5")]
        [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::")]
        [InlineData("2001:db8:1:2::1", "2001:db8:1:2::")]
        public void GetRateLimitPartitionKey_GroupsIPv6By64(string remoteIp, string expected)
        {
            DefaultHttpContext httpContext = new DefaultHttpContext();
            httpContext.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);

            Assert.Equal(expected, Startup.GetRateLimitPartitionKey(httpContext));
        }
    }
}
