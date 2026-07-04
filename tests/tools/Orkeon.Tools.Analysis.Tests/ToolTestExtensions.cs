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
}
