using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SeanShell.Plugins;

internal sealed class SuspendedBrokerProcess : IDisposable
{
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateNoWindow = 0x08000000;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint StartfUseStdHandles = 0x00000100;
    private const uint HandleFlagInherit = 0x00000001;
    private const nuint ProcThreadAttributeHandleList = 0x00020002;
    private const uint ResumeThreadFailed = uint.MaxValue;

    private readonly SafeProcessHandle _nativeProcessHandle;
    private readonly NativeProcessWaitHandle _waitHandle;
    private readonly int _processId;

    private SuspendedBrokerProcess(
        SafeProcessHandle nativeProcessHandle,
        int processId,
        StreamWriter input,
        StreamReader output,
        StreamReader error)
    {
        _nativeProcessHandle = nativeProcessHandle;
        _waitHandle = new NativeProcessWaitHandle(nativeProcessHandle);
        _processId = processId;
        Input = input;
        Output = output;
        Error = error;
    }

    public int Id => _processId;

    public int ExitCode
    {
        get
        {
            if (!GetExitCodeProcess(_nativeProcessHandle, out var exitCode))
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    "Unable to read the plugin broker exit code.");
            }

            return checked((int)exitCode);
        }
    }

    public bool HasExited => WaitForSingleObject(_nativeProcessHandle, 0) == 0;

    public StreamWriter Input { get; }

    public StreamReader Output { get; }

    public StreamReader Error { get; }

    public static SuspendedBrokerProcess Start(
        string executablePath,
        BrokerProcessSandbox sandbox)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(sandbox);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Suspended plugin broker launch requires Windows.");
        }

        var fullPath = Path.GetFullPath(executablePath);
        PipePair? stdin = null;
        PipePair? stdout = null;
        PipePair? stderr = null;
        SafeProcessHandle? nativeProcessHandle = null;
        SafeWaitHandle? primaryThreadHandle = null;
        StreamWriter? input = null;
        StreamReader? output = null;
        StreamReader? error = null;
        var processCreated = false;

        try
        {
            stdin = CreatePipe(parentReads: false);
            stdout = CreatePipe(parentReads: true);
            stderr = CreatePipe(parentReads: true);
            using var attributes = ProcessThreadAttributeList.Create(
                stdin.Child,
                stdout.Child,
                stderr.Child);
            var startupInfo = new StartupInfoEx
            {
                StartupInfo = new StartupInfo
                {
                    Size = Marshal.SizeOf<StartupInfoEx>(),
                    Flags = StartfUseStdHandles,
                    StandardInput = stdin.Child.DangerousGetHandle(),
                    StandardOutput = stdout.Child.DangerousGetHandle(),
                    StandardError = stderr.Child.DangerousGetHandle(),
                },
                AttributeList = attributes.Handle,
            };
            var commandLine = new StringBuilder($"\"{fullPath}\"");
            if (!CreateProcess(
                    fullPath,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    inheritHandles: true,
                    CreateSuspended | CreateNoWindow | ExtendedStartupInfoPresent,
                    IntPtr.Zero,
                    Path.GetDirectoryName(fullPath),
                    ref startupInfo,
                    out var processInformation))
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    "Unable to create the plugin broker in a suspended state.");
            }

            processCreated = true;
            nativeProcessHandle = new SafeProcessHandle(
                processInformation.ProcessHandle,
                ownsHandle: true);
            primaryThreadHandle = new SafeWaitHandle(
                processInformation.ThreadHandle,
                ownsHandle: true);
            stdin.Child.Dispose();
            stdout.Child.Dispose();
            stderr.Child.Dispose();

            sandbox.Assign(nativeProcessHandle);
            if (ResumeThread(primaryThreadHandle) == ResumeThreadFailed)
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    "Unable to resume the sandboxed plugin broker.");
            }

            input = CreateInputWriter(stdin);
            output = CreateOutputReader(stdout);
            error = CreateOutputReader(stderr);

            return new SuspendedBrokerProcess(
                nativeProcessHandle,
                checked((int)processInformation.ProcessId),
                input,
                output,
                error);
        }
        catch
        {
            if (processCreated && nativeProcessHandle is not null &&
                !nativeProcessHandle.IsInvalid)
            {
                _ = TerminateProcess(nativeProcessHandle, 1);
            }

            input?.Dispose();
            output?.Dispose();
            error?.Dispose();
            nativeProcessHandle?.Dispose();
            throw;
        }
        finally
        {
            primaryThreadHandle?.Dispose();
            stdin?.DisposeUnused();
            stdout?.DisposeUnused();
            stderr?.DisposeUnused();
        }
    }

    public async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (HasExited)
        {
            return;
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = ThreadPool.RegisterWaitForSingleObject(
            _waitHandle,
            static (state, _) => ((TaskCompletionSource)state!).TrySetResult(),
            completion,
            Timeout.Infinite,
            executeOnlyOnce: true);
        using var cancellationRegistration = cancellationToken.Register(
            static state =>
            {
                var (source, token) =
                    ((TaskCompletionSource Source, CancellationToken Token))state!;
                source.TrySetCanceled(token);
            },
            (completion, cancellationToken));
        try
        {
            await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _ = registration.Unregister(null);
        }
    }

    public void Terminate()
    {
        if (!HasExited)
        {
            _ = TerminateProcess(_nativeProcessHandle, 1);
        }
    }

    public void Dispose()
    {
        Input.Dispose();
        Output.Dispose();
        Error.Dispose();
        _waitHandle.Dispose();
        _nativeProcessHandle.Dispose();
    }

    private static PipePair CreatePipe(bool parentReads)
    {
        var securityAttributes = new SecurityAttributes
        {
            Length = Marshal.SizeOf<SecurityAttributes>(),
            InheritHandle = 1,
        };
        if (!CreatePipe(
                out var readHandle,
                out var writeHandle,
                ref securityAttributes,
                size: 0))
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "Unable to create redirected plugin broker pipes.");
        }

        var read = new SafeFileHandle(readHandle, ownsHandle: true);
        var write = new SafeFileHandle(writeHandle, ownsHandle: true);
        var parent = parentReads ? read : write;
        var child = parentReads ? write : read;
        if (!SetHandleInformation(parent, HandleFlagInherit, flags: 0))
        {
            var error = Marshal.GetLastPInvokeError();
            read.Dispose();
            write.Dispose();
            throw new Win32Exception(
                error,
                "Unable to restrict plugin broker pipe inheritance.");
        }

        return new PipePair(parent, child);
    }

    private static StreamWriter CreateInputWriter(PipePair pipe)
    {
        var stream = new FileStream(pipe.Parent, FileAccess.Write, bufferSize: 4096);
        try
        {
            var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
            };
            pipe.MarkParentTransferred();
            return writer;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static StreamReader CreateOutputReader(PipePair pipe)
    {
        var stream = new FileStream(pipe.Parent, FileAccess.Read, bufferSize: 4096);
        try
        {
            var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false);
            pipe.MarkParentTransferred();
            return reader;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private sealed class PipePair(SafeFileHandle parent, SafeFileHandle child)
    {
        private bool _parentTransferred;

        public SafeFileHandle Parent { get; } = parent;

        public SafeFileHandle Child { get; } = child;

        public void MarkParentTransferred()
        {
            if (_parentTransferred)
            {
                throw new InvalidOperationException("The parent pipe handle was already transferred.");
            }

            _parentTransferred = true;
        }

        public void DisposeUnused()
        {
            if (!_parentTransferred && !Parent.IsClosed)
            {
                Parent.Dispose();
            }

            if (!Child.IsClosed)
            {
                Child.Dispose();
            }
        }
    }

    private sealed class ProcessThreadAttributeList : IDisposable
    {
        private ProcessThreadAttributeList(IntPtr handle, IntPtr handleList)
        {
            Handle = handle;
            _handleList = handleList;
        }

        private readonly IntPtr _handleList;

        public IntPtr Handle { get; }

        public static ProcessThreadAttributeList Create(
            SafeFileHandle stdin,
            SafeFileHandle stdout,
            SafeFileHandle stderr)
        {
            nuint size = 0;
            _ = InitializeProcThreadAttributeList(
                IntPtr.Zero,
                attributeCount: 1,
                flags: 0,
                ref size);
            if (size == 0)
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    "Unable to size the plugin broker process attribute list.");
            }

            var attributes = Marshal.AllocHGlobal(checked((nint)size));
            var handles = IntPtr.Zero;
            var initialized = false;
            try
            {
                handles = Marshal.AllocHGlobal(IntPtr.Size * 3);
                if (!InitializeProcThreadAttributeList(
                        attributes,
                        attributeCount: 1,
                        flags: 0,
                        ref size))
                {
                    throw new Win32Exception(
                        Marshal.GetLastPInvokeError(),
                        "Unable to initialize the plugin broker process attribute list.");
                }

                initialized = true;
                Marshal.WriteIntPtr(handles, 0, stdin.DangerousGetHandle());
                Marshal.WriteIntPtr(handles, IntPtr.Size, stdout.DangerousGetHandle());
                Marshal.WriteIntPtr(handles, IntPtr.Size * 2, stderr.DangerousGetHandle());
                if (!UpdateProcThreadAttribute(
                        attributes,
                        flags: 0,
                        ProcThreadAttributeHandleList,
                        handles,
                        checked((nuint)(IntPtr.Size * 3)),
                        IntPtr.Zero,
                        IntPtr.Zero))
                {
                    throw new Win32Exception(
                        Marshal.GetLastPInvokeError(),
                        "Unable to restrict inherited plugin broker handles.");
                }

                return new ProcessThreadAttributeList(attributes, handles);
            }
            catch
            {
                if (initialized)
                {
                    DeleteProcThreadAttributeList(attributes);
                }

                if (handles != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(handles);
                }

                Marshal.FreeHGlobal(attributes);
                throw;
            }
        }

        public void Dispose()
        {
            DeleteProcThreadAttributeList(Handle);
            Marshal.FreeHGlobal(_handleList);
            Marshal.FreeHGlobal(Handle);
        }
    }

    private sealed class NativeProcessWaitHandle : WaitHandle
    {
        public NativeProcessWaitHandle(SafeProcessHandle processHandle)
        {
            SafeWaitHandle = new SafeWaitHandle(
                processHandle.DangerousGetHandle(),
                ownsHandle: false);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        public int InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int Size;
        public IntPtr Reserved;
        public IntPtr Desktop;
        public IntPtr Title;
        public uint X;
        public uint Y;
        public uint XSize;
        public uint YSize;
        public uint XCountCharacters;
        public uint YCountCharacters;
        public uint FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort Reserved2Size;
        public IntPtr Reserved2;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr ProcessHandle;
        public IntPtr ThreadHandle;
        public uint ProcessId;
        public uint ThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(
        out IntPtr readPipe,
        out IntPtr writePipe,
        ref SecurityAttributes pipeAttributes,
        uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(
        SafeFileHandle handle,
        uint mask,
        uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr attributeList,
        int attributeCount,
        uint flags,
        ref nuint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr attributeList,
        uint flags,
        nuint attribute,
        IntPtr value,
        nuint size,
        IntPtr previousValue,
        IntPtr returnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

    [DllImport(
        "kernel32.dll",
        EntryPoint = "CreateProcessW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(
        string applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(SafeWaitHandle threadHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(
        SafeProcessHandle processHandle,
        uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(
        SafeProcessHandle processHandle,
        out uint exitCode);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(
        SafeProcessHandle handle,
        uint milliseconds);
}
