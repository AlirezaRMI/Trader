using System.Reflection;
using OpenAPI.Net;

namespace Infrastructure.Maine;

public static class OpenApiCompat
{
    private static readonly Assembly ApiAsm = typeof(OpenClient).Assembly;

    private static Type? FindType(string shortName)
        => ApiAsm.GetTypes().FirstOrDefault(t => t.Name == shortName);

    private static object Create(string typeName)
    {
        var t = FindType(typeName)
                ?? throw new InvalidOperationException($"Type '{typeName}' not found in OpenAPI.Net assembly");
        return Activator.CreateInstance(t)!;
    }

    private static object? GetProp(object obj, string prop)
    {
        var p = obj.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance);
        return p?.GetValue(obj);
    }
    private static void SetProp(object obj, string prop, object? value)
    {
        var p = obj.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"Property '{prop}' not found on '{obj.GetType().Name}'");
        p.SetValue(obj, value);
    }
    
    private static readonly MethodInfo SendMessageGeneric =
        typeof(OpenClient).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m =>
            {
                if (m.Name != "SendMessage" || !m.IsGenericMethodDefinition) return false;
                var ps = m.GetParameters();
                return ps.Length == 2 && ps[1].ParameterType == typeof(string);
            });

    public static async Task<object> SendAndWaitAsync(
        OpenClient client,
        string reqTypeName,
        string resTypeName,
        string payloadName,                  
        Action<object>? init = null,
        Func<object, bool>? filter = null,
        TimeSpan? timeout = null)
    {
        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        
        var sub = client.Subscribe(
            onNext: o =>
            {
                try
                {
                    if (o.GetType().Name == resTypeName && (filter?.Invoke(o) ?? true))
                        tcs.TrySetResult(o);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            },
            onError: ex => tcs.TrySetException(ex)
        );

        try
        {
            
            var reqMsg = Create(reqTypeName);
            init?.Invoke(reqMsg);

            
            var reqType = reqMsg.GetType();
            var sendGeneric = SendMessageGeneric.MakeGenericMethod(reqType);
            var sendTask = (Task)sendGeneric.Invoke(client, [reqMsg, payloadName])!;
            await sendTask;

            if (timeout is { } to)
            {
                var done = await Task.WhenAny(tcs.Task, Task.Delay(to));
                if (done != tcs.Task)
                    throw new TimeoutException($"No '{resTypeName}' response within {to.TotalSeconds:F1}s.");
            }

            return await tcs.Task;
        }
        finally
        {
            sub.Dispose();
        }
    }

    public static long GetLong(object src, string propName)
    {
        var v = GetProp(src, propName)
                ?? throw new InvalidOperationException($"Property '{propName}' is null");
        return Convert.ToInt64(v);
    }

    public static void Set(object obj, string prop, object? value) => SetProp(obj, prop, value);
}
