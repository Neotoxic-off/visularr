using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Jellyfin.Networking.Manager;
using Jellyfin.Server.Extensions;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using IConfigurationManager = MediaBrowser.Common.Configuration.IConfigurationManager;
using IPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace Jellyfin.Server.Tests
{
    public class ParseNetworkTests
    {
        public static TheoryData<bool, bool, string[], IPAddress[], IPNetwork[]> TestNetworks_TestData()
        {
            TheoryData<bool, bool, string[], IPAddress[], IPNetwork[]> data = new TheoryData<bool, bool, string[], IPAddress[], IPNetwork[]>();
            data.Add(
                true,
                true,
                new string[] { "192.168.t", "127.0.0.1", "::1", "1234.1232.12.1234" },
                new IPAddress[] { IPAddress.Loopback },
                new IPNetwork[] { new IPNetwork(IPAddress.IPv6Loopback, 128) });

            data.Add(
                true,
                false,
                new string[] { "192.168.x", "127.0.0.1", "1234.1232.12.1234" },
                new IPAddress[] { IPAddress.Loopback },
                Array.Empty<IPNetwork>());

            data.Add(
                true,
                true,
                new string[] { "::1" },
                Array.Empty<IPAddress>(),
                new IPNetwork[] { new IPNetwork(IPAddress.IPv6Loopback, 128) });

            data.Add(
                false,
                false,
                new string[] { "localhost" },
                Array.Empty<IPAddress>(),
                Array.Empty<IPNetwork>());

            data.Add(
                true,
                false,
                new string[] { "localhost" },
                new IPAddress[] { IPAddress.Loopback },
                Array.Empty<IPNetwork>());

            data.Add(
                false,
                true,
                new string[] { "localhost" },
                Array.Empty<IPAddress>(),
                new IPNetwork[] { new IPNetwork(IPAddress.IPv6Loopback, 128) });

            data.Add(
                true,
                true,
                new string[] { "localhost" },
                new IPAddress[] { IPAddress.Loopback },
                new IPNetwork[] { new IPNetwork(IPAddress.IPv6Loopback, 128) });
            return data;
        }

        [Theory]
        [MemberData(nameof(TestNetworks_TestData))]
        public void TestNetworks(bool ip4, bool ip6, string[] hostList, IPAddress[] knownProxies, IPNetwork[] knownNetworks)
        {
            using NetworkManager nm = CreateNetworkManager();

            NetworkConfiguration settings = new NetworkConfiguration
            {
                EnableIPv4 = ip4,
                EnableIPv6 = ip6
            };

            ForwardedHeadersOptions options = new ForwardedHeadersOptions();

            // Need this here as ::1 and 127.0.0.1 are in them by default.
            options.KnownProxies.Clear();
            options.KnownNetworks.Clear();

            ApiServiceCollectionExtensions.AddProxyAddresses(settings, hostList, options);

            Assert.Equal(knownProxies.Length, options.KnownProxies.Count);
            foreach (IPAddress item in knownProxies)
            {
                Assert.True(options.KnownProxies.Contains(item));
            }

            Assert.Equal(knownNetworks.Length, options.KnownNetworks.Count);
            foreach (IPNetwork item in knownNetworks)
            {
                Assert.NotNull(options.KnownNetworks.FirstOrDefault(x => x.Prefix.Equals(item.Prefix) && x.PrefixLength == item.PrefixLength));
            }
        }

        [Theory]
        [InlineData("10.0.0.0/8", "10.1.2.3", "203.0.113.5")]
        [InlineData("10.1.2.3", "10.1.2.3", "203.0.113.5")]
        [InlineData("10.0.0.0/8", "192.168.1.2", "192.168.1.2")]
        public async Task ForwardedHeaders_TrustOnlyConfiguredProxies(string proxy, string remoteIp, string expectedIp)
        {
            using NetworkManager nm = CreateNetworkManager();
            NetworkConfiguration settings = new NetworkConfiguration
            {
                EnableIPv4 = true,
                EnableIPv6 = true,
                KnownProxies = new[] { proxy }
            };
            ForwardedHeadersOptions options = new ForwardedHeadersOptions();
            ApiServiceCollectionExtensions.ConfigureForwardHeaders(settings, options);

            DefaultHttpContext context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
            context.Request.Headers["X-Forwarded-For"] = "203.0.113.5";
            ForwardedHeadersMiddleware middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(options));
            await middleware.Invoke(context);

            Assert.Equal(IPAddress.Parse(expectedIp), context.Connection.RemoteIpAddress);
        }

        private static IConfigurationManager GetMockConfig(NetworkConfiguration conf)
        {
            Mock<IConfigurationManager> configManager = new Mock<IConfigurationManager>
            {
                CallBase = true
            };
            configManager.Setup(x => x.GetConfiguration(It.IsAny<string>())).Returns(conf);
            return configManager.Object;
        }

        private static NetworkManager CreateNetworkManager()
        {
            NetworkConfiguration conf = new NetworkConfiguration()
            {
                EnableIPv6 = true,
                EnableIPv4 = true,
            };
            Mock<IConfiguration> startupConf = new Mock<IConfiguration>();
            return new NetworkManager(GetMockConfig(conf), startupConf.Object, new NullLogger<NetworkManager>());
        }
    }
}
