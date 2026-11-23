[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$server = $null
$failure = 'Could not initialize the local raid helper.'
try {
    # Unity launches this script in a dedicated process and holds the input pipe open.
    if (![Console]::IsInputRedirected -or ![Console]::IsOutputRedirected) {
        $failure = 'Start the editor raid by pressing Play in Unity.'
        throw $failure
    }
    $root = Split-Path -Parent $PSScriptRoot
    . (Join-Path $PSScriptRoot 'LocalRaidSession.ps1')
    $exe = Join-Path (Get-LocalRaidBuild $root) 'Server/HeavyPocketsServer.exe'
    if (!(Test-Path -LiteralPath $exe -PathType Leaf)) {
        $failure = 'Missing local server. Build the Windows dedicated server from the Heavy Pockets menu.'
        throw $failure
    }

    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

public static class EditorRaidLifetime
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long ProcessTime, JobTime;
        public uint Flags;
        public UIntPtr MinWorkingSet, MaxWorkingSet;
        public uint ActiveProcesses;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimits limits, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    public static Task MonitorEditor()
    {
        // The helper alone owns this non-inheritable handle. Every child it starts belongs
        // to the job from birth, so Windows cleans up even if the helper itself is killed.
        IntPtr job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) throw new Win32Exception();
        var limits = new ExtendedLimits();
        limits.Basic.Flags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        using (var helper = Process.GetCurrentProcess())
        {
            if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf(typeof(ExtendedLimits))) ||
                !AssignProcessToJobObject(job, helper.Handle))
            {
                var error = new Win32Exception();
                CloseHandle(job);
                throw error;
            }
        }
        // Leave the job handle open for the entire dedicated helper process.
        return Task.Run(() => { using (Stream input = Console.OpenStandardInput()) while (input.ReadByte() != -1) {} });
    }
}
'@
    $closed = [EditorRaidLifetime]::MonitorEditor()
    $failure = 'Could not prepare the encrypted local server session.'
    $security = New-LocalRaidSession
    if ($closed.IsCompleted) { exit 0 }

    $reservation = [Net.Sockets.UdpClient]::new([Net.IPEndPoint]::new([Net.IPAddress]::Loopback, 0))
    try { $port = $reservation.Client.LocalEndPoint.Port } finally { $reservation.Dispose() }
    $log = Join-Path ([IO.Path]::GetTempPath()) ('HeavyPockets-Editor-' + [guid]::NewGuid().ToString('N') + '.log')
    $failure = "The local raid server could not start. See $log"
    $server = Start-LocalRaidChild -Exe $exe -Arguments @('-server','-batchmode','-nographics','-port',"$port",'-logFile',$log) -Security $security -Role server
    $ready = "HEAVY POCKETS SERVER READY 127.0.0.1:$port"
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    while (!$closed.IsCompleted) {
        if ($server.HasExited) { throw $failure }
        if ((Test-Path -LiteralPath $log) -and (Select-String -LiteralPath $log -SimpleMatch $ready -Quiet)) { break }
        if ($deadline.Elapsed.TotalSeconds -ge 35) { throw $failure }
        [void]$closed.Wait(100)
    }
    if ($closed.IsCompleted) { exit 0 }

    $invitation = @{ port = $port; ca = $security.PublicCA; ticket = $security.Tickets['A'] }
    [Console]::Out.WriteLine(($invitation | ConvertTo-Json -Compress))
    [Console]::Out.Flush()
    $security = $invitation = $null
    $failure = "The local raid server stopped unexpectedly. See $log"
    while (!$closed.IsCompleted) {
        if ($server.HasExited) { throw $failure }
        [void]$closed.Wait(200)
    }
} catch {
    [Console]::Error.WriteLine($failure)
    exit 1
} finally {
    if ($null -ne $server) {
        try {
            if (!$server.HasExited) { $server.Kill(); [void]$server.WaitForExit(3000) }
        } finally { $server.Dispose() }
    }
}
