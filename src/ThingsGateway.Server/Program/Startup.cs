using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Industrial.Security.Abstractions;
using Industrial.Security.AspNetCore;
using ThingsGateway.Admin.Application;
using ThingsGateway.Admin.Razor;
using ThingsGateway.Common;
using ThingsGateway.DB;
using ThingsGateway.Server.IndustrialSecurity;
using ThingsGateway.VirtualFileServer;

namespace ThingsGateway.Server;

[AppStartup(-99999)]
public class Startup : AppStartup
{
    public void ConfigBlazorServer(IServiceCollection services)
    {
        // Keep ThingGetWay's existing local authentication/role engine, while
        // exposing the same Industrial.Security contract used by MOL/WCS.
        services.AddHttpContextAccessor();
        services.AddIndustrialSecurity(App.Configuration);
        services.AddScoped<ThingGetWayCurrentUser>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<ThingGetWayCurrentUser>());
        services.AddScoped<IIdentityProvider, ThingGetWayIdentityProvider>();
        services.AddScoped<ILocalPermissionSource, ThingGetWayLocalPermissionSource>();
        services.AddScoped<IPermissionCodeMapper, ThingGetWayPermissionCodeMapper>();
        services.AddScoped<IUserPermissionProvider, ThingGetWayLocalPermissionProvider>();
        services.AddScoped<IPermissionProvider, ThingGetWayLocalPermissionProvider>();
        services.AddScoped<IShadowUserResolver, ThingGetWayShadowUserResolver>();

        // 增加中文编码支持网页源码显示汉字
        services.AddSingleton(HtmlEncoder.Create(UnicodeRanges.All));
        //并发启动/停止host
        services.Configure<HostOptions>(options =>
        {
            options.ServicesStartConcurrently = true;
            options.ServicesStopConcurrently = true;
        });

        // 事件总线
        services.AddEventBus(options =>
        {

        });

        // 任务调度
        services.AddSchedule(options => options.AddPersistence<JobPersistence>());

        // 允许跨域
        services.AddCorsAccessor();

        if (!WebEnableVariable.WebEnable)
        {
            services.AddScoped<IJSRuntime, UnsupportedJavaScriptRuntime>();
            services.AddScoped<NavigationManager, UnsupportedNavigationManager>();
        }
        else
        {

            services.AddRazorPages();

            services.AddMvcFilter<RequestAuditFilter>();

            services.AddControllers()
                .AddNewtonsoftJson(options =>
                {
                    var setting = options.SerializerSettings;
                    setting.DateFormatHandling = DateFormatHandling.IsoDateFormat;
                    setting.DateTimeZoneHandling = DateTimeZoneHandling.Local;
                    // setting.Converters.AddDateTimeTypeConverters(localized: true); // 时间本地化
                    //setting.DateFormatString = "yyyy-MM-dd HH:mm:ss"; // 时间格式化
                    setting.ReferenceLoopHandling = ReferenceLoopHandling.Ignore; // 忽略循环引用

                    // setting.ContractResolver = new CamelCasePropertyNamesContractResolver(); // 解决动态对象属性名大写
                    setting.NullValueHandling = NullValueHandling.Ignore; // 忽略空值
                                                                          // setting.Converters.AddLongTypeConverters(); // long转string（防止js精度溢出） 超过17位开启
                                                                          // setting.MetadataPropertyHandling = MetadataPropertyHandling.Ignore; // 解决DateTimeOffset异常
                                                                          // setting.DateParseHandling = DateParseHandling.None; // 解决DateTimeOffset异常
                                                                          // setting.Converters.Add(new IsoDateTimeConverter { DateTimeStyles = DateTimeStyles.AssumeUniversal }); // 解决DateTimeOffset异常

                })
                //.AddXmlSerializerFormatters()
                //.AddXmlDataContractSerializerFormatters()
                .AddInjectWithUnifyResult<UnifyResultProvider>();








#if NET8_0_OR_GREATER
            services
             .AddRazorComponents(options => options.TemporaryRedirectionUrlValidityDuration = TimeSpan.FromMinutes(10))
             .AddInteractiveServerComponents(options =>
             {
                 options.RootComponents.MaxJSRootComponents = 500;
                 options.JSInteropDefaultCallTimeout = TimeSpan.FromSeconds(30);
                 options.MaxBufferedUnacknowledgedRenderBatches = 5;
                 options.DisconnectedCircuitMaxRetained = 1;
                 options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromSeconds(10);
             })
             .AddHubOptions(options =>
             {
                 //单个传入集线器消息的最大大小。默认 32 KB
                 options.MaximumReceiveMessageSize = 32 * 1024 * 1024;
                 //可为客户端上载流缓冲的最大项数。 如果达到此限制，则会阻止处理调用，直到服务器处理流项。
                 options.StreamBufferCapacity = 30;
                 options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
                 options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                 options.HandshakeTimeout = TimeSpan.FromSeconds(15);
             });

#else

                    services.AddServerSideBlazor(options =>
                     {
                         options.RootComponents.MaxJSRootComponents = 500;
                         options.JSInteropDefaultCallTimeout = TimeSpan.FromSeconds(30);
                         options.MaxBufferedUnacknowledgedRenderBatches = 5;
                         options.DisconnectedCircuitMaxRetained = 1;
                         options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromSeconds(10);
                     })
                     .AddHubOptions(options =>
                     {
                         //单个传入集线器消息的最大大小。默认 32 KB
                         options.MaximumReceiveMessageSize = 32 * 1024 * 1024;
                         //可为客户端上载流缓冲的最大项数。 如果达到此限制，则会阻止处理调用，直到服务器处理流项。
                         options.StreamBufferCapacity = 30;
                         options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
                         options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                         options.HandshakeTimeout = TimeSpan.FromSeconds(15);
                     });

#endif



            // 配置Nginx转发获取客户端真实IP
            // 注1：如果负载均衡不是在本机通过 Loopback 地址转发请求的，一定要加上options.KnownNetworks.Clear()和options.KnownProxies.Clear()
            // 注2：如果设置环境变量 ASPNETCORE_FORWARDEDHEADERS_ENABLED 为 True，则不需要下面的配置代码，注意docker内必须配置
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.All;
#if NET10_0_OR_GREATER
                options.KnownIPNetworks.Clear();
#else
                options.KnownNetworks.Clear();
#endif
                options.KnownProxies.Clear();
            });

        }



        //已添加AddOptions
        // 增加多语言支持配置信息
        services.AddRequestLocalization<IOptionsMonitor<BootstrapBlazor.Components.BootstrapBlazorOptions>>((localizerOption, blazorOption) =>
        {
            blazorOption.OnChange(op => Invoke(op));
            Invoke(blazorOption.CurrentValue);

            void Invoke(BootstrapBlazor.Components.BootstrapBlazorOptions option)
            {
                var supportedCultures = option.GetSupportedCultures();
                localizerOption.SupportedCultures = supportedCultures;
                localizerOption.SupportedUICultures = supportedCultures;
            }
        });

        services.AddScoped<BlazorAppContext>(a =>
        {
            var appContext = new BlazorAppContext(
             a.GetService<ISysResourceService>(),
             a.GetService<IUserCenterService>(),
             a.GetService<ISysUserService>());
            appContext.TitleLocalizer = a.GetRequiredService<IStringLocalizer<MainLayout>>();

            return appContext;
        });
        if (WebEnableVariable.WebEnable)
        {

            services.AddHttpContextAccessor();

            var centralizedAuthentication = App.Configuration.GetValue<string>("Security:Authentication:Mode")
                ?.Equals("Centralized", StringComparison.OrdinalIgnoreCase) == true;

            // Keep the existing cookie login for Local mode. Centralized mode
            // additionally accepts IAM bearer tokens through the standard scheme.
            var authenticationBuilder = services.AddAuthentication(ClaimConst.Scheme).AddCookie(ClaimConst.Scheme, a =>
            {
                a.AccessDeniedPath = "/Account/AccessDenied/";
                a.LogoutPath = "/Account/Logout/";
                a.LoginPath = "/Account/Login/";
            });

            var websiteOptions = App.GetConfig<WebsiteOptions>("Website");

            services.AddSingleton<CircuitHandler, ConnectionLimiterCircuitHandler>();
            if (websiteOptions.Demo)
            {
                authenticationBuilder.AddOAuth<GiteeOAuthOptions, AdminOAuthHandler<GiteeOAuthOptions>>("Gitee", "Gitee", options =>
            {
                var data = App.GetConfig<GiteeOAuthSettings>("GiteeOAuthSettings");
                options.ClientId = data.ClientId;
                options.ClientSecret = data.ClientSecret;
            });

                authenticationBuilder.AddOAuth<GitHubOAuthOptions, AdminOAuthHandler<GitHubOAuthOptions>>("Github", "Github", options =>
                {
                    var data = App.GetConfig<GithubOAuthSettings>("GithubOAuthSettings");
                    options.ClientId = data.ClientId;
                    options.ClientSecret = data.ClientSecret;
                });
            }

            if (centralizedAuthentication)
            {
                authenticationBuilder.AddIndustrialJwt(App.Configuration);
                services.AddAuthentication(options =>
                {
                    options.DefaultScheme = "ThingGetWay.Dynamic";
                    options.DefaultChallengeScheme = "ThingGetWay.Dynamic";
                }).AddPolicyScheme("ThingGetWay.Dynamic", "ThingGetWay cookie or IAM bearer", options =>
                {
                    options.ForwardDefaultSelector = context =>
                        context.Request.Headers.ContainsKey("Authorization")
                            ? Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme
                            : ClaimConst.Scheme;
                });
            }
            else
            {
                authenticationBuilder.AddJwt();
            }

            services.AddAuthorization();
#if NET8_0_OR_GREATER
            services.AddCascadingAuthenticationState();
#endif
            services.AddAuthorizationCore();
            services.AddScoped<IAuthorizationHandler, BlazorServerAuthenticationHandler>();
            services.AddScoped<AuthenticationStateProvider, BlazorServerAuthenticationStateProvider>();


            if (!NewLife.Runtime.IsLegacyWindows)
            {
#if NET9_0_OR_GREATER
                var certificate = X509CertificateLoader.LoadPkcs12FromFile("ThingsGateway.pfx", "ThingsGateway", X509KeyStorageFlags.EphemeralKeySet);
#else
                var certificate = new X509Certificate2("ThingsGateway.pfx", "ThingsGateway", X509KeyStorageFlags.EphemeralKeySet);
#endif
                services.AddDataProtection()
                    .PersistKeysToFileSystem(new DirectoryInfo("Keys"))
                    .ProtectKeysWithCertificate(certificate)
                    .UseCryptographicAlgorithms(new AuthenticatedEncryptorConfiguration
                    {
                        EncryptionAlgorithm = EncryptionAlgorithm.AES_256_CBC,
                        ValidationAlgorithm = ValidationAlgorithm.HMACSHA256
                    });
            }
        }

    }

    public void Use(IApplicationBuilder applicationBuilder, IWebHostEnvironment env)
    {

        if (WebEnableVariable.WebEnable)
        {

            var app = (WebApplication)applicationBuilder;

            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.All,
#if NET10_0_OR_GREATER
                KnownIPNetworks = { },
#else
                KnownNetworks = { },
#endif
                KnownProxies = { }
            });

            app.UseBootstrapBlazor();

            // 启用本地化
            var option = app.Services.GetService<IOptions<RequestLocalizationOptions>>();
            if (option != null)
            {
                app.UseRequestLocalization(option.Value);
            }


            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                app.UseResponseCompression();
                app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = ctx => ctx.ProcessCache(app.Configuration) });
            }

            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = (stf) =>
                {
                    stf.ProcessCache(app.Configuration);
                    stf.Context.Response.Headers.AccessControlAllowOrigin = "*";
                    stf.Context.Response.Headers.AccessControlAllowHeaders = "*";
                }
            });
            var provider = new FileExtensionContentTypeProvider();
            provider.Mappings[".properties"] = "application/octet-stream";
            provider.Mappings[".moc"] = "application/x-msdownload";
            provider.Mappings[".moc3"] = "application/x-msdownload";
            provider.Mappings[".mtn"] = "application/x-msdownload";

            app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = provider });
            app.UseStaticFiles();

            // 特定文件类型（文件后缀）处理
            var contentTypeProvider = FS.GetFileExtensionContentTypeProvider();
            // contentTypeProvider.Mappings[".文件后缀"] = "MIME 类型";
            app.UseStaticFiles(new StaticFileOptions
            {
                ContentTypeProvider = contentTypeProvider
            });

            //// 启用HTTPS
            //app.UseHttpsRedirection();

            // 添加状态码拦截中间件
            app.UseUnifyResultStatusCodes();

            // 路由注册
            app.UseRouting();

            // 启用跨域，必须在 UseRouting 和 UseAuthentication 之间注册
            app.UseCorsAccessor();

            // 启用鉴权授权
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseIndustrialSecurity();

            // 任务调度看板
            app.UseScheduleUI(options =>
            {
                options.RequestPath = "/schedule";  // 必须以 / 开头且不以 / 结尾
                options.DisableOnProduction = true; // 生产环境关闭
                options.DisplayEmptyTriggerJobs = true; // 是否显示空作业触发器的作业
                options.DisplayHead = false; // 是否显示页头
                options.DefaultExpandAllJobs = false; // 是否默认展开所有作业
            });

            app.UseInject();

#if NET8_0_OR_GREATER
            app.UseAntiforgery();
#endif


            app.MapControllers();
            app.MapIndustrialSecurityCacheInvalidation();
            app.MapIndustrialLocalUserManagementInfo();
            app.MapIndustrialEmergencyValidation();
            // V0.7 service health contract. These endpoints report the collector
            // process/configuration only; PLC/device state is intentionally kept
            // separate and is exposed by the device pages/telemetry APIs.
            app.MapGet("/health/live", () => Results.Ok(new
            {
                status = "alive",
                service = "thinggateway",
                timestamp = DateTimeOffset.UtcNow
            }));
            app.MapGet("/health/ready", () => Results.Ok(new
            {
                status = "ready",
                service = "thinggateway",
                timestamp = DateTimeOffset.UtcNow
            }));
            app.MapGet("/healthz", () => Results.Ok(new
            {
                status = "healthy",
                service = "thinggateway",
                timestamp = DateTimeOffset.UtcNow
            }));
            app.MapHubs();

        }
        //if (!options.WebPageEnable)
        //{
        //    // 中间件拦截，返回 500
        //    app.Use((context, next) =>
        //    {
        //        var path = context.Request.Path.Value;
        //        if (path?.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase) == true)
        //        {
        //            context.Response.StatusCode = 500;
        //            return Task.CompletedTask;
        //        }
        //        else
        //        {
        //            return next();
        //        }
        //    });
        //}


    }
}
