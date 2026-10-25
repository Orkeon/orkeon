using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Orkeon.Tools.Code.Sessions.Windows;

/// <summary>What <see cref="WindowsProcessLauncher.Start"/> starts.</summary>
/// <param name="ApplicationPath">The program's full path (<c>lpApplicationName</c>).</param>
/// <param name="CommandLine">The whole command line, program included (<see cref="WindowsCommandLine"/>).</param>
/// <param name="EnvironmentBlock">The environment block (<see cref="WindowsEnvironmentBlock"/>).</param>
/// <param name="WorkingDirectory">The physical directory it starts in; null keeps the host's.</param>
/// <param name="Job">The job it is created in.</param>
/// <param name="ContainerSid">The AppContainer it runs in, or null to run as the host's user.</param>
/// <param name="Capabilities">The capabilities of the AppContainer (none: no network).</param>
[SupportedOSPlatform("windows")]
internal sealed record WindowsLaunch(
    string ApplicationPath,
    string CommandLine,
    string EnvironmentBlock,
    string? WorkingDirectory,
    JobObject Job,
    SecurityIdentifier? ContainerSid,
    IReadOnlyList<SecurityIdentifier> Capabilities);

/// <summary>A process <see cref="WindowsProcessLauncher.Start"/> started, with the read ends of its output.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsLaunchedProcess : IDisposable
{
    internal WindowsLaunchedProcess(SafeProcessHandle handle, int processId, Stream stdout, Stream stderr)
    {
        Handle = handle;
        ProcessId = processId;
        Stdout = stdout;
        Stderr = stderr;
    }

    /// <summary>The process handle.</summary>
    public SafeProcessHandle Handle { get; }

    /// <summary>The process id.</summary>
    public int ProcessId { get; }

    /// <summary>The read end of its standard output (asynchronous, cancellable).</summary>
    public Stream Stdout { get; }

    /// <summary>The read end of its standard error (asynchronous, cancellable).</summary>
    public Stream Stderr { get; }

    /// <summary>Its exit code, or null while it runs.</summary>
    public int? ExitCode => NativeMethods.GetExitCodeProcess(Handle, out var code) && code != NativeMethods.StillActive ? unchecked((int)code) : null;

    /// <summary>Completes when the process exits.</summary>
    public Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waitHandle = new ProcessWaitHandle(Handle);
        var registration = ThreadPool.RegisterWaitForSingleObject(
            waitHandle, static (state, _) => ((TaskCompletionSource)state!).TrySetResult(), exited, Timeout.Infinite, executeOnlyOnce: true);
        var cancellation = cancellationToken.Register(() => exited.TrySetCanceled(cancellationToken));
        return Finish();

        async Task Finish()
        {
            try
            {
                await exited.Task.ConfigureAwait(false);
            }
            finally
            {
                await cancellation.DisposeAsync().ConfigureAwait(false);
                registration.Unregister(null);
                waitHandle.Dispose();
            }
        }
    }

    /// <summary>Closes the handle and the pipes (the process itself belongs to its job).</summary>
    public void Dispose()
    {
        Stdout.Dispose();
        Stderr.Dispose();
        Handle.Dispose();
    }

    /// <summary>
    /// A wait handle over the process handle, which it does not own: the process handle is
    /// referenced (the P/Invoke marshaller's add-ref) for as long as the wait handle lives.
    /// </summary>
    private sealed class ProcessWaitHandle : WaitHandle
    {
        private SafeHandleMarshaller<SafeProcessHandle>.ManagedToUnmanagedIn _process;
        private int _freed;

        public ProcessWaitHandle(SafeProcessHandle process)
        {
            _process = new SafeHandleMarshaller<SafeProcessHandle>.ManagedToUnmanagedIn();
            _process.FromManaged(process);
            SafeWaitHandle = new SafeWaitHandle(_process.ToUnmanaged(), ownsHandle: false);
        }

        protected override void Dispose(bool explicitDisposing)
        {
            base.Dispose(explicitDisposing);
            // The marshaller's release is not idempotent: a second Dispose must not release the handle again.
            if (Interlocked.Exchange(ref _freed, 1) == 0)
                _process.Free();
        }
    }
}

/// <summary>
/// Starts a process with <c>CreateProcessW</c>: created suspended inside the session's job
/// (<c>PROC_THREAD_ATTRIBUTE_JOB_LIST</c>, so not one instruction runs outside it), in the
/// AppContainer when one is given (<c>PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES</c>), inheriting
/// exactly its three standard handles (<c>PROC_THREAD_ATTRIBUTE_HANDLE_LIST</c>), with no console
/// window and the given environment block, then resumed.
/// </summary>
/// <remarks>
/// Standard input is a pipe whose write end is closed at once: the process reads end of file.
/// Standard output and error are named pipes the host reads asynchronously, so a read still pending
/// when the host stops listening (a grandchild that kept the pipe) can be cancelled. The pipes are
/// <c>CurrentUserOnly</c> and single-instance; the child never opens them, it inherits its ends. The
/// handles are inheritable for the instant of the call: another process the host starts at that very
/// moment with inheritance on (outside a handle list) could inherit them too and hold the output open
/// until it exits; the host closes its copies right after the call.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static unsafe class WindowsProcessLauncher
{
    /// <summary>Starts <paramref name="launch"/>.</summary>
    /// <exception cref="Win32Exception">Windows refused to create the process.</exception>
    public static WindowsLaunchedProcess Start(WindowsLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        var (stdoutServer, stdoutClient) = CreateOutputPipe();
        var (stderrServer, stderrClient) = CreateOutputPipe();
        var stdin = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        try
        {
            var process = CreateProcess(launch, stdin.ClientSafePipeHandle, stdoutClient.SafePipeHandle, stderrClient.SafePipeHandle);
            return new WindowsLaunchedProcess(process.Handle, process.ProcessId, stdoutServer, stderrServer);
        }
        catch
        {
            stdoutServer.Dispose();
            stderrServer.Dispose();
            throw;
        }
        finally
        {
            // The child holds its own copies now (or there is no child): the host's go.
            stdoutClient.Dispose();
            stderrClient.Dispose();
            stdin.Dispose();
        }
    }

    private static (NamedPipeServerStream Server, NamedPipeClientStream Client) CreateOutputPipe()
    {
        var name = "orkeon-shell-" + Guid.NewGuid().ToString("N");
        var server = new NamedPipeServerStream(
            name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);
        try
        {
            var client = new NamedPipeClientStream(
                ".", name, PipeDirection.Out, PipeOptions.CurrentUserOnly, TokenImpersonationLevel.None, HandleInheritability.Inheritable);
            try
            {
                client.Connect(5_000);
                server.WaitForConnection();
                return (server, client);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
        catch
        {
            server.Dispose();
            throw;
        }
    }

    private static (SafeProcessHandle Handle, int ProcessId) CreateProcess(
        WindowsLaunch launch, SafePipeHandle stdin, SafePipeHandle stdout, SafePipeHandle stderr)
    {
        var containerSid = launch.ContainerSid;
        var attributeCount = containerSid is null ? 2 : 3;
        var natives = new List<nint>();
        var addRefs = new List<SafeHandleMarshaller<SafeHandle>.ManagedToUnmanagedIn>();
        void* attributes = null;
        var attributesInitialized = false;
        try
        {
            nuint size = 0;
            _ = NativeMethods.InitializeProcThreadAttributeList(null, attributeCount, 0, ref size);
            attributes = NativeMemory.Alloc(size);
            if (!NativeMethods.InitializeProcThreadAttributeList(attributes, attributeCount, 0, ref size))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "InitializeProcThreadAttributeList failed");
            attributesInitialized = true;

            // The handles the attributes point at must stay open until CreateProcess returns.
            var handles = (nint*)Allocate(natives, 3 * sizeof(nint));
            handles[0] = AddRef(addRefs, stdin);
            handles[1] = AddRef(addRefs, stdout);
            handles[2] = AddRef(addRefs, stderr);
            Update(attributes, NativeMethods.ProcThreadAttributeHandleList, handles, (nuint)(3 * sizeof(nint)));

            var jobs = (nint*)Allocate(natives, sizeof(nint));
            jobs[0] = AddRef(addRefs, launch.Job.Handle);
            Update(attributes, NativeMethods.ProcThreadAttributeJobList, jobs, (nuint)sizeof(nint));

            NativeMethods.SecurityCapabilities capabilities = default;
            if (containerSid is not null)
            {
                var sidAndAttributes = (NativeMethods.SidAndAttributes*)Allocate(
                    natives, Math.Max(1, launch.Capabilities.Count) * sizeof(NativeMethods.SidAndAttributes));
                for (var i = 0; i < launch.Capabilities.Count; i++)
                {
                    sidAndAttributes[i].Sid = NativeSid(natives, launch.Capabilities[i]);
                    sidAndAttributes[i].Attributes = NativeMethods.SeGroupEnabled;
                }

                capabilities.AppContainerSid = NativeSid(natives, containerSid);
                capabilities.Capabilities = launch.Capabilities.Count > 0 ? sidAndAttributes : null;
                capabilities.CapabilityCount = (uint)launch.Capabilities.Count;
                Update(attributes, NativeMethods.ProcThreadAttributeSecurityCapabilities, &capabilities, (nuint)sizeof(NativeMethods.SecurityCapabilities));
            }

            var startup = new NativeMethods.StartupInfoEx
            {
                StartupInfo = new NativeMethods.StartupInfo
                {
                    Cb = sizeof(NativeMethods.StartupInfoEx),
                    Flags = NativeMethods.StartfUseStdHandles,
                    StdInput = handles[0],
                    StdOutput = handles[1],
                    StdError = handles[2],
                },
                AttributeList = attributes,
            };

            // CreateProcessW may write into the command line: it gets a copy of its own.
            var commandLine = new char[launch.CommandLine.Length + 1];
            launch.CommandLine.CopyTo(commandLine);
            NativeMethods.ProcessInformation information;
            bool created;
            fixed (char* line = commandLine)
            fixed (char* environment = launch.EnvironmentBlock)
            {
                created = NativeMethods.CreateProcess(
                    launch.ApplicationPath, line, 0, 0, inheritHandles: true,
                    NativeMethods.CreateSuspended | NativeMethods.ExtendedStartupInfoPresent
                        | NativeMethods.CreateUnicodeEnvironment | NativeMethods.CreateNoWindow,
                    environment, string.IsNullOrEmpty(launch.WorkingDirectory) ? null : launch.WorkingDirectory,
                    &startup, &information);
            }

            if (!created)
            {
                var error = Marshal.GetLastPInvokeError();
                throw new Win32Exception(error, $"CreateProcess failed for '{Path.GetFileName(launch.ApplicationPath)}' ({new Win32Exception(error).Message})");
            }

            var process = new SafeProcessHandle(information.Process, ownsHandle: true);
            if (NativeMethods.ResumeThread(information.Thread) == uint.MaxValue)
            {
                var error = Marshal.GetLastPInvokeError();
                _ = NativeMethods.TerminateProcess(process, 1);
                _ = NativeMethods.CloseHandle(information.Thread);
                process.Dispose();
                throw new Win32Exception(error, "ResumeThread failed");
            }

            _ = NativeMethods.CloseHandle(information.Thread);
            return (process, information.ProcessId);
        }
        finally
        {
            if (attributesInitialized)
                NativeMethods.DeleteProcThreadAttributeList(attributes);
            if (attributes is not null)
                NativeMemory.Free(attributes);
            foreach (var native in natives)
                NativeMemory.Free((void*)native);
            foreach (var handle in addRefs)
                handle.Free();
        }
    }

    private static void Update(void* attributes, nuint attribute, void* value, nuint size)
    {
        if (!NativeMethods.UpdateProcThreadAttribute(attributes, 0, attribute, value, size, 0, 0))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"UpdateProcThreadAttribute(0x{attribute:X}) failed");
    }

    private static void* Allocate(List<nint> natives, int size)
    {
        var memory = NativeMemory.AllocZeroed((nuint)size);
        natives.Add((nint)memory);
        return memory;
    }

    private static nint NativeSid(List<nint> natives, SecurityIdentifier sid)
    {
        var binary = new byte[sid.BinaryLength];
        sid.GetBinaryForm(binary, 0);
        var memory = (byte*)Allocate(natives, binary.Length);
        binary.CopyTo(new Span<byte>(memory, binary.Length));
        return (nint)memory;
    }

    /// <summary>
    /// The raw value of <paramref name="handle"/>, referenced the way the P/Invoke marshaller
    /// references a <see cref="SafeHandle"/> argument, until the caller frees every entry of
    /// <paramref name="addRefs"/>.
    /// </summary>
    private static nint AddRef(List<SafeHandleMarshaller<SafeHandle>.ManagedToUnmanagedIn> addRefs, SafeHandle handle)
    {
        var marshaller = new SafeHandleMarshaller<SafeHandle>.ManagedToUnmanagedIn();
        marshaller.FromManaged(handle);
        addRefs.Add(marshaller);
        return marshaller.ToUnmanaged();
    }
}
