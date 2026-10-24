namespace Orkeon.Tools.Analysis.Tests;

internal static class ToolTestExtensions
{
    public static async Task<TRes> ExecuteTypedForTest<TReq, TRes>(
        this Orkeon.Tools.Abstractions.Base.ToolBase<TReq, TRes> tool,
        TReq request,
        CancellationToken ct)
        where TReq : class, new()
        where TRes : class
    {
        var method = typeof(Orkeon.Tools.Abstractions.Base.ToolBase<TReq, TRes>)
            .GetMethod("ExecuteTypedAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var task = (Task<TRes>)method!.Invoke(tool, [request, ct])!;
        return await task.ConfigureAwait(false);
    }

    public static async Task<TRes> ExecuteTypedForTest<TReq, TRes>(
        this Orkeon.Tools.Abstractions.Base.FileToolBase<TReq, TRes> tool,
        TReq request,
        CancellationToken ct)
        where TReq : class, new()
        where TRes : class
    {
        var method = typeof(Orkeon.Tools.Abstractions.Base.FileToolBase<TReq, TRes>)
            .GetMethod("ExecuteTypedAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        try
        {
            return await ((Task<TRes>)method!.Invoke(tool, [request, ct])!).ConfigureAwait(false);
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}
