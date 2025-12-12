using System.Reflection;
using Autofac;
using Autofac.Core;
using Autofac.Extensions.DependencyInjection;
using Autofac.Extras.DynamicProxy;
using Castle.DynamicProxy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Http.Logging;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using Umi.Dht.Client.Attributes;
using Umi.Dht.Client.Configurations;
using Umi.Dht.Client.Interceptors;
using LogLevel = NLog.LogLevel;

namespace Umi.Dht.Client;

public static class Program
{
    // 主入口，主要程序
    public static void Main(string[] args)
    {
        // 创建默认的主机
        // 配制相关的目录和文件
        var fullPathConfig = Path.Combine(Directory.GetCurrentDirectory(), "nlog.config");
        var logFactory = LogManager.Setup()
            .LoadConfigurationFromFile(fullPathConfig)
            .LogFactory;
        var rootLogger = logFactory.GetLogger(typeof(Program).FullName!);
        rootLogger.Log(LogLevel.Debug, "Umi Distributed Hash Table Tracker Starting, version: 1.0.0");
        // 开始创建 Host
        var host = new HostBuilder()
            .UseEnvironment(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production")
            .UseContentRoot(Directory.GetCurrentDirectory())
            .ConfigureHostConfiguration(ConfigureHostConfigurationBuilder(args))
            .ConfigureAppConfiguration(ConfigureAppConfigurationBuilder(args))
            .ConfigureContainer(LoadAssemblies(rootLogger))
            .UseServiceProviderFactory(_ => new AutofacServiceProviderFactory())
            .ConfigureLogging(ConfigurationLoggingBuilder(logFactory))
            .ConfigureServices(ConfigurationServices)
            .UseConsoleLifetime();

        host.Build()
            .Run();
    }


    private static void ConfigurationServices(HostBuilderContext context, IServiceCollection services)
    {
        //加入日志
        services.AddLogging();

        //注册配置文件字段
        services.AddOptions();

        services.AddHttpClient("default",
                client => client.DefaultRequestHeaders.Add("User-Agent", "Umi Dht Tracker"))
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler()
            {
                UseProxy = false,
                Proxy = null
            });

        services.Configure<KademliaConfig>(context.Configuration.GetSection("Kademlia"));
    }

    private static Action<HostBuilderContext, ILoggingBuilder> ConfigurationLoggingBuilder(LogFactory factory)
    {
        return (ctx, builder) =>
        {
            builder.AddConfiguration(ctx.Configuration.GetSection("Logging"));
            builder.ClearProviders();
            builder.AddNLog(_ => factory);
        };
    }

    private static Action<HostBuilderContext, ContainerBuilder> LoadAssemblies(Logger logger)
    {
        return (ctx, builder) =>
        {
            logger.Log(LogLevel.Debug, "begin register system services");

            builder.RegisterType<TimeLoggerInterceptor>()
                .As<IInterceptor>()
                .Named("TimeLoggerInterceptor", typeof(IInterceptor))
                .PropertiesAutowired();
            builder.RegisterType<ExceptionInterceptor>()
                .As<IInterceptor>()
                .Named("ExceptionInterceptor", typeof(IInterceptor))
                .PropertiesAutowired();
            var environment = ctx.HostingEnvironment;
            var contents = environment.ContentRootFileProvider.GetDirectoryContents("plugins");
            List<Assembly> allAssembly = [typeof(Program).Assembly];
            List<Type> totalTypes = [];
            if (contents is { Exists: true })
            {
                foreach (var file in contents)
                {
                    if (file is not { Exists: true, IsDirectory: false, PhysicalPath: not null or "" }) continue;
                    try
                    {
                        allAssembly.Add(Assembly.LoadFrom(file.PhysicalPath));
                    }
                    catch (Exception e)
                    {
                        logger.Log(LogLevel.Error, e, "Error loading assembly {physicalPath}", file.PhysicalPath);
                        continue;
                    }
                }
            }

            builder.RegisterAssemblyModules(allAssembly.ToArray());
            allAssembly.ForEach(p => totalTypes.AddRange(from pt in p.GetTypes()
                where pt is { IsAbstract: false, IsInterface: false, IsPublic: true }
                select pt));
            foreach (var type in totalTypes)
            {
                var attribute = type.GetCustomAttribute<ServiceAttribute>();
                if (attribute == null)
                {
                    continue;
                }

                logger.Log(LogLevel.Debug, $"registing type {type.FullName ?? ""}");

                var register = builder.RegisterType(type)
                    .AsImplementedInterfaces();
                register = attribute.Scoped switch
                {
                    ServiceScope.Prototype => register.InstancePerDependency(),
                    ServiceScope.Scoped => register.InstancePerLifetimeScope(),
                    ServiceScope.Singleton => register.SingleInstance(),
                    _ => register.InstancePerLifetimeScope()
                };
                var interfaces = type.GetInterfaces();
                if (!string.IsNullOrEmpty(attribute.Name))
                {
                    register = interfaces.Where(@interface => @interface.IsPublic)
                        .Aggregate(register,
                            (current, item) => current.Keyed(attribute.Name, item));
                }

                register = register.PropertiesAutowired(new DefaultPropertySelector(false), true);
                string[] interceptors =
                [
                    ..attribute.Interceptors,
                    "TimeLoggerInterceptor",
                    "ExceptionInterceptor"
                ];
                if (interfaces.Length > 0)
                {
                    register.EnableInterfaceInterceptors()
                        .InterceptedBy(interceptors);
                }
                else
                {
                    register.EnableClassInterceptors()
                        .InterceptedBy(interceptors);
                }
            }
        };
    }

    private static Action<IConfigurationBuilder> ConfigureHostConfigurationBuilder(string[] args)
    {
        return builder =>
        {
            builder.AddEnvironmentVariables();
            if (args.Length > 0)
            {
                builder.AddCommandLine(args);
            }
        };
    }

    private static Action<HostBuilderContext, IConfigurationBuilder> ConfigureAppConfigurationBuilder(string[] args)
    {
        return (ctx, builder) =>
        {
            var env = ctx.HostingEnvironment;
            builder.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true);

            if (env.IsDevelopment() && !string.IsNullOrEmpty(env.ApplicationName))
            {
                var appAssembly = Assembly.Load(new AssemblyName(env.ApplicationName));
                builder.AddUserSecrets(appAssembly, optional: true);
            }

            builder.AddEnvironmentVariables();
            if (args.Length > 0)
            {
                builder.AddCommandLine(args);
            }
        };
    }
}