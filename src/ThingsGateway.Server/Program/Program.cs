//------------------------------------------------------------------------------
//  此代码版权声明为全文件覆盖，如有原作者特别声明，会在下方手动补充
//  此代码版权（除特别声明外的代码）归作者本人Diego所有
//  源代码使用协议遵循本仓库的开源协议及附加协议
//  Gitee源代码仓库：https://gitee.com/diego2098/ThingsGateway
//  Github源代码仓库：https://github.com/kimdiego2098/ThingsGateway
//  使用文档：https://thingsgateway.cn/
//  QQ群：605534569
//------------------------------------------------------------------------------

using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;

using System.Runtime.InteropServices;
using System.Text;

using ThingsGateway.Admin.Application;
using ThingsGateway.DB;
using ThingsGateway.NewLife;
using ThingsGateway.NewLife.Json.Extension;
using ThingsGateway.NewLife.Log;
using ThingsGateway.SqlSugar;

namespace ThingsGateway.Server;

public class Program
{
    private static readonly string[] second = new[] { "application/octet-stream" };

    public static async Task Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            if (e.ExceptionObject is OutOfMemoryException)
            {
                try
                {
                    XTrace.WriteLine($"[OOM DETECTED]");
                    XTrace.WriteLine(MachineInfo.GetCurrent().ToJsonNetString());
                }
                catch
                {
                }
            }
        };

        await Task.Delay(2000).ConfigureAwait(false);
        System.IO.Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        ClaimConst.Scheme = $"{typeof(Program).Assembly.GetName().Name}{SchemeHelper.GetOrCreate()}";
        Runtime.CreateConfigOnMissing = true;

        #region 控制台输出Logo

        Console.Write(Environment.NewLine);
        Console.ForegroundColor = ConsoleColor.Yellow;
        XTrace.WriteLine(string.Empty);
        XTrace.UseConsole();
        Console.WriteLine(
            """

               _______  _      _                      _____         _
              |__   __|| |    (_)                    / ____|       | |
                 | |   | |__   _  _ __    __ _  ___ | |  __   __ _ | |_  ___ __      __ __ _  _   _
                 | |   | '_ \ | || '_ \  / _` |/ __|| | |_ | / _` || __|/ _ \\ \ /\ / // _` || | | |
                 | |   | | | || || | | || (_| |\__ \| |__| || (_| || |_|  __/ \ V  V /| (_| || |_| |
                 |_|   |_| |_||_||_| |_| \__, ||___/ \_____| \__,_| \__|\___|  \_/\_/  \__,_| \__, |
                                          __/ |                                                __/ |
                                         |___/                                                |___/

            """
         );
        Console.ResetColor();

        #endregion 控制台输出Logo

        if (WebEnableVariable.WebEnable)
        {
            await Serve.RunAsync(RunOptions.Default.ConfigureFirstActionBuilder(builder =>
            {
                ApplyIndustrialSecurityProfile(builder.Configuration);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    builder.Host.UseWindowsService();
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    builder.Host.UseSystemd();

                if (Runtime.IsLegacyWindows)
                    builder.Logging.ClearProviders();
            }).ConfigureBuilder(builder =>
            {
                if (Runtime.IsSystemd)
                {
                    builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning);
                }

                if (!builder.Environment.IsDevelopment())
                {
                    builder.Services.AddResponseCompression(
                        opts => opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(second));
                }

                builder.WebHost.UseWebRoot("wwwroot");
                builder.WebHost.UseStaticWebAssets();
                builder.WebHost.ConfigureKestrel(u =>
                {
                    u.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(30);
                    u.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(30);
                    u.Limits.MaxRequestBodySize = null;
                });
            })
            .Configure(app =>
            {
#if NET9_0_OR_GREATER
                app.MapRazorComponents<BlazorApp>()
                    .AddAdditionalAssemblies(App.RazorAssemblies.Distinct().Where(a => a != typeof(Program).Assembly).ToArray())
                    .AddInteractiveServerRenderMode();
#elif NET8_0_OR_GREATER
                app.MapRazorComponents<BlazorAppNet8>()
                    .AddAdditionalAssemblies(App.RazorAssemblies.Distinct().Where(a => a != typeof(Program).Assembly).ToArray())
                    .AddInteractiveServerRenderMode();
#else
                app.MapBlazorHub();
                app.MapFallbackToPage("/_Host");
#endif
                ReflectionInoHelper.RemoveAllCache();
                InstanceFactory.RemoveCache();
            })).ConfigureAwait(false);
        }
        else
        {
            await Serve.RunAsync(MiniRunOptions.Default.ConfigureFirstActionBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) => ApplyIndustrialSecurityProfile(configuration));

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    builder.UseWindowsService();
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    builder.UseSystemd();

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    builder.ConfigureLogging(logging =>
                    {
                        foreach (var provider in logging.Services.Where(s => s.ImplementationType?.Name == "EventLogLoggerProvider").ToList())
                        {
                            logging.Services.Remove(provider);
                        }
                    });
                }
            }).ConfigureBuilder(builder =>
            {
                if (Runtime.IsSystemd)
                {
                    builder.ConfigureLogging(a => a.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning));
                }

                builder.ConfigureKestrel(u =>
                {
                    u.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(30);
                    u.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(30);
                    u.Limits.MaxRequestBodySize = null;
                }).UseKestrel().Configure(app =>
                {
                    app.Run(context => context.Response.WriteAsync("web is disable"));
                });
            })
            .Configure(app =>
            {
                ReflectionInoHelper.RemoveAllCache();
                InstanceFactory.RemoveCache();
            })).ConfigureAwait(false);
        }

        await Task.Delay(2000).ConfigureAwait(false);
    }

    private static void ApplyIndustrialSecurityProfile(IConfigurationBuilder configuration)
    {
        var profile = Environment.GetEnvironmentVariable("INDUSTRIAL_SECURITY_PROFILE")?.Trim();
        if (string.IsNullOrWhiteSpace(profile))
            return;

        if (!profile.Equals("IamPrepare", StringComparison.OrdinalIgnoreCase)
            && !profile.Equals("Shadow", StringComparison.OrdinalIgnoreCase)
            && !profile.Equals("Centralized", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"INDUSTRIAL_SECURITY_PROFILE only supports IamPrepare, Shadow, or Centralized; received '{profile}'.");
        }

        var canonical = profile.Equals("IamPrepare", StringComparison.OrdinalIgnoreCase)
            ? "IamPrepare"
            : profile.Equals("Shadow", StringComparison.OrdinalIgnoreCase)
                ? "Shadow"
                : "Centralized";

        configuration.AddJsonFile($"appsettings.{canonical}.json", optional: false, reloadOnChange: true);
        // Deployment secrets and emergency overrides always win over profile JSON.
        configuration.AddEnvironmentVariables();
    }
}
