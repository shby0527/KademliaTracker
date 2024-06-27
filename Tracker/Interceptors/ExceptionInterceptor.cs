using Castle.DynamicProxy;
using Microsoft.Extensions.Logging;

namespace Umi.Dht.Client.Interceptors;

public class ExceptionInterceptor(ILogger<ExceptionInterceptor> logger) : IInterceptor
{
    public void Intercept(IInvocation invocation)
    {
        try
        {
            invocation.Proceed();
        }
        catch (Exception e)
        {
            logger.LogError(e, "{fullName}.{name} running throws an Exception, message {message}",
                invocation.TargetType.FullName,
                invocation.MethodInvocationTarget.Name,
                e.Message);
            throw;
        }
    }
}