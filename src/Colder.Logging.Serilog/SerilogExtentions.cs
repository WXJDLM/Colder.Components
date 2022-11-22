﻿using Colder.Logging.Abstractions;
using Confluent.Kafka;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting.Elasticsearch;
using Serilog.Sinks.Elasticsearch;
using Serilog.Sinks.Kafka;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Colder.Logging.Serilog
{
    /// <summary>
    /// 注入拓展
    /// </summary>
    public static class SerilogExtentions
    {
        /// <summary>
        /// 配置日志
        /// </summary>
        /// <param name="hostBuilder">建造者</param>
        /// <returns></returns>
        public static IHostBuilder ConfigureLoggingDefaults(this IHostBuilder hostBuilder)
        {
            var rootPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            var path = Path.Combine(rootPath, "logs", "log.txt");
            SelfLog.Enable(log =>
            {
                string msg = $"{DateTimeOffset.Now}:Serilog自己异常 {log}";
                Console.WriteLine(msg);

                var selfLogPath = Path.Combine(rootPath, "logs", "selflog.txt");
                File.WriteAllText(selfLogPath, msg);
            });

            hostBuilder.ConfigureServices((host, services) =>
            {
                services.AddOptions<LogOptions>();
                var theConfig = host.Configuration.GetChildren().Where(x => x.Key.ToLower() == "log").FirstOrDefault();
                if (theConfig == null)
                {
                    LogOptions logOption = new LogOptions();
                    services.Configure<LogOptions>(x =>
                    {
                        typeof(LogOptions).GetProperties().ToList().ForEach(aProperty =>
                        {
                            if (aProperty.CanWrite)
                            {
                                aProperty.SetValue(x, aProperty.GetValue(logOption));
                            }
                        });
                    });
                }
                else
                {
                    services.Configure<LogOptions>(theConfig);
                }

                services.AddHostedService<Bootstrapper>();
            });

            return hostBuilder.UseSerilog((hostingContext, serviceProvider, serilogConfig) =>
            {
                LogOptions logConfig = serviceProvider.GetRequiredService<IOptions<LogOptions>>().Value;

                logConfig.Overrides.ForEach(aOverride =>
                {
                    serilogConfig
                        .MinimumLevel
                        .Override(aOverride.Source, (LogEventLevel)aOverride.MinLevel);
                });
                serilogConfig.MinimumLevel.Is((LogEventLevel)logConfig.MinLevel);
                if (logConfig.Console.Enabled)
                {
                    serilogConfig.WriteTo.Console();
                }
                if (logConfig.Debug.Enabled)
                {
                    serilogConfig.WriteTo.Debug();
                }
                if (logConfig.File.Enabled)
                {
                    string template = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3} {SourceContext:l}] {Message:lj}{NewLine}{Exception}";

                    //最大日志文件10M,每天滚动,保留31天
                    serilogConfig.WriteTo.File(
                        path: path,
                        outputTemplate: template,
                        rollingInterval: RollingInterval.Day,
                        shared: true,
                        fileSizeLimitBytes: 10 * 1024 * 1024,
                        retainedFileTimeLimit: TimeSpan.FromDays(31),
                        rollOnFileSizeLimit: true,
                        retainedFileCountLimit: null
                        );
                }
                if (logConfig.Elasticsearch.Enabled)
                {
                    var uris = logConfig.Elasticsearch.Nodes.Select(x => new Uri(x)).ToList();

                    serilogConfig.WriteTo.Elasticsearch(new ElasticsearchSinkOptions(uris)
                    {
                        IndexFormat = logConfig.Elasticsearch.IndexFormat,
                        AutoRegisterTemplate = true,
                        AutoRegisterTemplateVersion = AutoRegisterTemplateVersion.ESv7
                    });
                }
                if (logConfig.Kafka.Enabled)
                {
                    serilogConfig.WriteTo.Kafka(
                        bootstrapServers: logConfig.Kafka.Brokers,
                        topic: logConfig.Kafka.Topic,
                        saslUsername: logConfig.Kafka.UserName,
                        saslPassword: logConfig.Kafka.Password,
                        securityProtocol: string.IsNullOrEmpty(logConfig.Kafka.UserName)
                            ? SecurityProtocol.Plaintext : SecurityProtocol.SaslPlaintext,
                        formatter: new ElasticsearchJsonFormatter()
                    );
                }

                //自定义属性
                serilogConfig.Enrich.WithProperty("Instance", logConfig.Instance);
                serilogConfig.Enrich.WithProperty("MachineName", Environment.MachineName);
                serilogConfig.Enrich.WithProperty("ApplicationName", Assembly.GetEntryAssembly().GetName().Name);
                serilogConfig.Enrich.WithProperty("ApplicationVersion", Assembly.GetEntryAssembly().GetName().Version);
                serilogConfig.Enrich.WithProperty("ProcessId", Process.GetCurrentProcess().Id);
                serilogConfig.Enrich.WithProperty("ThreadId", Thread.CurrentThread.ManagedThreadId);
                var httpContext = serviceProvider.GetService<IHttpContextAccessor>()?.HttpContext;
                if (httpContext != null)
                {
                    serilogConfig.Enrich.WithProperty("RequestPath", httpContext.Request.Path);
                    serilogConfig.Enrich.WithProperty("RequestIp", httpContext.Connection.RemoteIpAddress);
                }
            });
        }
    }
}
