using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Net.Sockets;
using System.Text;
using System.Threading.RateLimiting;
using Emby.Server.Implementations.EntryPoints;
using Jellyfin.Api.Constants;
using Jellyfin.Api.Helpers;
using Jellyfin.Api.Middleware;
using Jellyfin.Database.Implementations;
using Jellyfin.LiveTv.Extensions;
using Jellyfin.LiveTv.Recordings;
using Jellyfin.MediaEncoding.Hls.Extensions;
using Jellyfin.Networking;
using Jellyfin.Networking.HappyEyeballs;
using Jellyfin.Server.Extensions;
using Jellyfin.Server.HealthChecks;
using Jellyfin.Server.Implementations.Extensions;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Extensions;
using MediaBrowser.XbmcMetadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using Prometheus;

namespace Jellyfin.Server
{
    /// <summary>
    /// Startup configuration for the Kestrel webhost.
    /// </summary>
    public class Startup
    {
        private readonly CoreAppHost _serverApplicationHost;
        private readonly IConfiguration _configuration;
        private readonly IServerConfigurationManager _serverConfigurationManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="Startup" /> class.
        /// </summary>
        /// <param name="appHost">The server application host.</param>
        /// <param name="configuration">The used Configuration.</param>
        public Startup(CoreAppHost appHost, IConfiguration configuration)
        {
            _serverApplicationHost = appHost;
            _configuration = configuration;
            _serverConfigurationManager = appHost.ConfigurationManager;
        }

        /// <summary>
        /// Configures the service collection for the webhost.
        /// </summary>
        /// <param name="services">The service collection.</param>
        public void ConfigureServices(IServiceCollection services)
        {
            // visularr: brotli/gzip for api json, playlists and web assets, also over https.
            // BREACH needs the victim's browser to send attacker shaped requests that come back with a secret in the body;
            // auth is a header/query token, never a cookie, so such cross site requests are unauthenticated. Media is never listed.
            services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();
                options.MimeTypes =
                [
                    "application/json",
                    "application/javascript",
                    "application/xml",
                    "application/vnd.apple.mpegurl",
                    "application/x-mpegURL",
                    "image/svg+xml",
                    "text/plain",
                    "text/html",
                    "text/css",
                    "text/javascript",
                    "text/xml",
                    "text/vtt",
                ];
            });
            services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
            services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
            services.AddHttpContextAccessor();
            services.AddHttpsRedirection(options =>
            {
                options.HttpsPort = _serverApplicationHost.HttpsPort;
            });

            services.AddJellyfinApi(_serverApplicationHost.GetApiPluginAssemblies(), _serverConfigurationManager.GetNetworkConfiguration());
            services.AddJellyfinDbContext(_serverApplicationHost.ConfigurationManager, _configuration);
            services.AddJellyfinApiSwagger();

            // configure custom legacy authentication
            services.AddCustomAuthentication();

            services.AddJellyfinApiAuthorization();

            // visularr: per client address limits on login and password reset, so guessing is slow and lockout is not a free account DoS.
            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddPolicy(RateLimitPolicies.Authentication, context => RateLimitPartition.GetFixedWindowLimiter(
                    GetRateLimitPartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
                options.AddPolicy(RateLimitPolicies.QuickConnectPoll, context => RateLimitPartition.GetFixedWindowLimiter(
                    GetRateLimitPartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
            });

            ProductInfoHeaderValue productHeader = new ProductInfoHeaderValue(
                _serverApplicationHost.Name.Replace(' ', '-'),
                _serverApplicationHost.ApplicationVersionString);
            MediaTypeWithQualityHeaderValue acceptJsonHeader = new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json, 1.0);
            MediaTypeWithQualityHeaderValue acceptXmlHeader = new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Xml, 0.9);
            MediaTypeWithQualityHeaderValue acceptAnyHeader = new MediaTypeWithQualityHeaderValue("*/*", 0.8);
            Func<IServiceProvider, HttpMessageHandler> eyeballsHttpClientHandlerDelegate = (_) => new SocketsHttpHandler()
            {
                AutomaticDecompression = DecompressionMethods.All,
                RequestHeaderEncodingSelector = (_, _) => Encoding.UTF8,
                ConnectCallback = HttpClientExtension.OnConnect
            };

            Func<IServiceProvider, HttpMessageHandler> defaultHttpClientHandlerDelegate = (_) => new SocketsHttpHandler()
            {
                AutomaticDecompression = DecompressionMethods.All,
                RequestHeaderEncodingSelector = (_, _) => Encoding.UTF8
            };

            services.AddHttpClient(NamedClient.Default, c =>
                {
                    c.DefaultRequestHeaders.UserAgent.Add(productHeader);
                    c.DefaultRequestHeaders.Accept.Add(acceptJsonHeader);
                    c.DefaultRequestHeaders.Accept.Add(acceptXmlHeader);
                    c.DefaultRequestHeaders.Accept.Add(acceptAnyHeader);
                })
                .ConfigurePrimaryHttpMessageHandler(eyeballsHttpClientHandlerDelegate);

            services.AddHttpClient(NamedClient.MusicBrainz, c =>
                {
                    c.DefaultRequestHeaders.UserAgent.Add(productHeader);
                    c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue($"({_serverApplicationHost.ApplicationUserAgentAddress})"));
                    c.DefaultRequestHeaders.Accept.Add(acceptXmlHeader);
                    c.DefaultRequestHeaders.Accept.Add(acceptAnyHeader);
                })
                .ConfigurePrimaryHttpMessageHandler(eyeballsHttpClientHandlerDelegate);

            services.AddHttpClient(NamedClient.DirectIp, c =>
                {
                    c.DefaultRequestHeaders.UserAgent.Add(productHeader);
                    c.DefaultRequestHeaders.Accept.Add(acceptJsonHeader);
                    c.DefaultRequestHeaders.Accept.Add(acceptXmlHeader);
                    c.DefaultRequestHeaders.Accept.Add(acceptAnyHeader);
                })
                .ConfigurePrimaryHttpMessageHandler(defaultHttpClientHandlerDelegate);

            services.AddSingleton<ManagedHub>();

            services.AddHealthChecks()
                .AddCheck<DbContextFactoryHealthCheck<JellyfinDbContext>>(nameof(JellyfinDbContext));

            services.AddHlsPlaylistGenerator();
            services.AddLiveTvServices();

            services.AddHostedService<RecordingsHost>();
            services.AddHostedService<AutoDiscoveryHost>();
            services.AddHostedService<NfoUserDataSaver>();
            services.AddHostedService<LibraryChangedNotifier>();
            services.AddHostedService<UserDataChangeNotifier>();
            services.AddHostedService<RecordingNotifier>();
        }

        /// <summary>
        /// Configures the app builder for the webhost.
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <param name="env">The webhost environment.</param>
        /// <param name="appConfig">The application config.</param>
        public void Configure(
            IApplicationBuilder app,
            IWebHostEnvironment env,
            IConfiguration appConfig)
        {
            app.UseBaseUrlRedirection();

            // Wrap rest of configuration so everything only listens on BaseUrl.
            NetworkConfiguration config = _serverConfigurationManager.GetNetworkConfiguration();
            app.Map(config.BaseUrl, mainApp =>
            {
                if (env.IsDevelopment())
                {
                    mainApp.UseDeveloperExceptionPage();
                }

                mainApp.UseForwardedHeaders();
                mainApp.UseMiddleware<ExceptionMiddleware>();

                mainApp.UseMiddleware<ResponseTimeMiddleware>();

                mainApp.UseWebSockets();

                mainApp.UseResponseCompression();

                mainApp.UseCors();

                if (config.RequireHttps && _serverApplicationHost.ListenWithHttps)
                {
                    mainApp.UseHttpsRedirection();
                }

                // This must be injected before any path related middleware.
                mainApp.UsePathTrim();

                if (appConfig.HostWebClient())
                {
                    FileExtensionContentTypeProvider extensionProvider = new FileExtensionContentTypeProvider();

                    // subtitles octopus requires .data, .mem files.
                    extensionProvider.Mappings.Add(".data", MediaTypeNames.Application.Octet);
                    extensionProvider.Mappings.Add(".mem", MediaTypeNames.Application.Octet);
                    mainApp.UseDefaultFiles(new DefaultFilesOptions
                    {
                        FileProvider = new PhysicalFileProvider(_serverConfigurationManager.ApplicationPaths.WebPath),
                        RequestPath = "/web"
                    });
                    mainApp.UseStaticFiles(new StaticFileOptions
                    {
                        FileProvider = new PhysicalFileProvider(_serverConfigurationManager.ApplicationPaths.WebPath),
                        RequestPath = "/web",
                        ContentTypeProvider = extensionProvider,
                        OnPrepareResponse = SetWebClientHeaders
                    });

                    mainApp.UseRobotsRedirection();
                }

                mainApp.UseStaticFiles();
                mainApp.UseAuthentication();
                mainApp.UseJellyfinApiSwagger(_serverConfigurationManager);
                mainApp.UseQueryStringDecoding();
                mainApp.UseRouting();
                mainApp.UseRateLimiter();
                mainApp.UseAuthorization();

                mainApp.UseIPBasedAccessValidation();
                mainApp.UseWebSocketHandler();
                mainApp.UseServerStartupMessage();

                if (_serverConfigurationManager.Configuration.EnableMetrics)
                {
                    // Must be registered after any middleware that could change HTTP response codes or the data will be bad
                    mainApp.UseHttpMetrics();
                }

                mainApp.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                    if (_serverConfigurationManager.Configuration.EnableMetrics)
                    {
                        endpoints.MapMetrics();
                    }

                    endpoints.MapHealthChecks("/health");
                });
            });
        }

        /// <summary>
        /// Sets the security and caching headers of the hosted web client's static files.
        /// </summary>
        /// <param name="context">The static file response context.</param>
        internal static void SetWebClientHeaders(StaticFileResponseContext context)
        {
            IHeaderDictionary headers = context.Context.Response.Headers;
            headers.XFrameOptions = "DENY";
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "same-origin";

            // The Next.js static export bootstraps with inline <script> tags, so scripts need 'unsafe-inline'.
            headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; "
                + "img-src 'self' data: blob: https:; media-src 'self' blob:; worker-src 'self' blob:; connect-src 'self'; "
                + "font-src 'self' data:; frame-src https://www.youtube-nocookie.com; frame-ancestors 'none'";

            if (Path.GetFileName(context.File.Name).Equals("index.html", StringComparison.Ordinal))
            {
                headers.CacheControl = new StringValues("no-cache");
            }
            else if (context.Context.Request.Path.Value?.Contains("/_next/static/", StringComparison.Ordinal) == true)
            {
                // Content hashed file names, they never change.
                headers.CacheControl = new StringValues("public, max-age=31536000, immutable");
            }
        }

        /// <summary>
        /// Gets the rate limiting partition of a request: the client address, IPv6 addresses grouped by /64.
        /// </summary>
        /// <param name="context">The HTTP context.</param>
        /// <returns>The partition key.</returns>
        internal static string GetRateLimitPartitionKey(HttpContext context)
        {
            IPAddress ip = context.GetNormalizedRemoteIP();
            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                byte[] bytes = ip.GetAddressBytes();
                Array.Clear(bytes, 8, 8);
                ip = new IPAddress(bytes);
            }

            return ip.ToString();
        }
    }
}
