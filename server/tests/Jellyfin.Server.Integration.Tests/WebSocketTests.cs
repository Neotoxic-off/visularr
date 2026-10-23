using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Jellyfin.Server.Integration.Tests
{
    public sealed class WebSocketTests : IClassFixture<JellyfinApplicationFactory>
    {
        private readonly JellyfinApplicationFactory _factory;

        public WebSocketTests(JellyfinApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task WebSocket_Unauthenticated_ThrowsInvalidOperationException()
        {
            TestServer server = _factory.Server;
            WebSocketClient client = server.CreateWebSocketClient();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.ConnectAsync(
                    new UriBuilder(server.BaseAddress)
                    {
                        Scheme = "ws",
                        Path = "websocket"
                    }.Uri,
                    CancellationToken.None));
        }
    }
}
