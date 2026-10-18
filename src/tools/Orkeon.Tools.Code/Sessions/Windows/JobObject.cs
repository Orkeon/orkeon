using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Orkeon.Tools.Code.Sessions.Windows;

/// <summary>
/// The Job Object of a Windows shell session: every process the session starts is created inside it
/// (<c>PROC_THREAD_ATTRIBUTE_JOB_LIST</c>), and so is everything those processes start. The host
/// keeps the only handle and the job is <c>KILL_ON_JOB_CLOSE</c>: when the session ends, or the host
/// dies and the kernel closes its handles, the whole tree dies with it.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class JobObject : IDisposable
{
    private readonly SafeJobHandle _handle;

    private JobObject(SafeJobHandle handle) => _handle = handle;

    /// <summary>The handle, for the attribute list of <c>CreateProcessW</c>.</summary>
    internal SafeJobHandle Handle => _handle;

    /// <summary>A new anonymous job that kills its processes when its last handle closes.</summary>
    /// <exception cref="Win32Exception">The job could not be created or configured.</exception>
    public static JobObject Create()
    {
        var handle = NativeMethods.CreateJobObject(0, null);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error, "CreateJobObject failed");
        }

        var limits = new NativeMethods.JobObjectExtendedLimitInformationData
        {
            BasicLimitInformation = new NativeMethods.JobObjectBasicLimitInformation
            {
                // DIE_ON_UNHANDLED_EXCEPTION: a crashing child ends instead of waiting on the
                // Windows Error Reporting dialog, which nobody sees on a host.
                LimitFlags = NativeMethods.JobObjectLimitKillOnJobClose | NativeMethods.JobObjectLimitDieOnUnhandledException,
            },
        };
        if (!NativeMethods.SetInformationJobObject(
                handle, NativeMethods.JobObjectExtendedLimitInformation, &limits, (uint)sizeof(NativeMethods.JobObjectExtendedLimitInformationData)))
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error, "SetInformationJobObject failed");
        }

        return new JobObject(handle);
    }

    /// <summary>The number of processes alive in the job.</summary>
    public int ActiveProcessCount
    {
        get
        {
            NativeMethods.JobObjectBasicAccountingInformationData info;
            return NativeMethods.QueryInformationJobObject(
                _handle, NativeMethods.JobObjectBasicAccountingInformation, &info, (uint)sizeof(NativeMethods.JobObjectBasicAccountingInformationData), out _)
                ? (int)info.ActiveProcesses
                : 0;
        }
    }

    /// <summary>The ids of the processes alive in the job (up to 1024).</summary>
    public IReadOnlyList<int> ProcessIds()
    {
        // JOBOBJECT_BASIC_PROCESS_ID_LIST: two DWORDs, then ULONG_PTR ids.
        const int capacity = 1024;
        var size = (2 * sizeof(uint)) + (capacity * sizeof(nuint));
        var buffer = NativeMemory.AllocZeroed((nuint)size);
        try
        {
            if (!NativeMethods.QueryInformationJobObject(_handle, NativeMethods.JobObjectBasicProcessIdList, buffer, (uint)size, out _))
                return [];
            var count = ((uint*)buffer)[1];
            var ids = (nuint*)((byte*)buffer + (2 * sizeof(uint)));
            var result = new int[count];
            for (var i = 0; i < count; i++)
                result[i] = (int)ids[i];
            return result;
        }
        finally
        {
            NativeMemory.Free(buffer);
        }
    }

    /// <summary>Kills every process of the job; the job stays usable. Never throws.</summary>
    public void Terminate()
    {
        try
        {
            if (!_handle.IsClosed)
                _ = NativeMethods.TerminateJobObject(_handle, 1);
        }
        catch (ObjectDisposedException)
        {
            // Closed by a concurrent Dispose, which killed the processes already.
        }
    }

    /// <summary>Kills every process of the job and closes it.</summary>
    public void Dispose()
    {
        Terminate();
        _handle.Dispose();
    }
}
