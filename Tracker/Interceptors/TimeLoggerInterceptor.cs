using System.Diagnostics;
using Castle.DynamicProxy;
using Microsoft.Extensions.Logging;

namespace Umi.Dht.Client.Interceptors;

public class TimeLoggerInterceptor(ILogger<TimeLoggerInterceptor> logger) : IInterceptor
{
    public void Intercept(IInvocation invocation)
    {
        Stopwatch sw = new();
        logger.LogDebug("{fullName}.{name} starting running",
            invocation.TargetType.FullName,
            invocation.MethodInvocationTarget.Name);
        sw.Start();
        try
        {
            invocation.Proceed();
        }
        finally
        {
            sw.Stop();
            logger.LogDebug("{fullName}.{name} running finished, using {time} ",
                invocation.TargetType.FullName,
                invocation.MethodInvocationTarget.Name,
                sw.Elapsed.ToString("c"));
        }
    }
}