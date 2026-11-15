using System;
using System.Runtime.InteropServices;

namespace ADOFAI_Auto.Core.Memory
{
    internal static class NativeMethods
    {
        public const uint PROCESS_VM_READ = 0x0010;
        public const uint PROCESS_VM_WRITE = 0x0020;
        public const uint PROCESS_VM_OPERATION = 0x0008;
        public const uint PROCESS_QUERY_INFORMATION = 0x0400;
        public const uint PROCESS_SUSPEND_RESUME = 0x0800;

        public const uint MEM_COMMIT = 0x1000;
        public const uint MEM_PRIVATE = 0x20000;
        public const uint MEM_IMAGE = 0x1000000;
        public const uint PAGE_NOACCESS = 0x01;
        public const uint PAGE_READWRITE = 0x04;
        public const uint PAGE_WRITECOPY = 0x08;
        public const uint PAGE_EXECUTE_READWRITE = 0x40;
        public const uint PAGE_EXECUTE_WRITECOPY = 0x80;
        public const uint PAGE_GUARD = 0x100;

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public ushort PartitionId;
            public UIntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr read);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr written);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern UIntPtr VirtualQueryEx(IntPtr process, IntPtr address, out MEMORY_BASIC_INFORMATION info, UIntPtr length);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWow64Process(IntPtr process, [MarshalAs(UnmanagedType.Bool)] out bool wow64);

        [DllImport("ntdll.dll")]
        public static extern int NtSuspendProcess(IntPtr process);

        [DllImport("ntdll.dll")]
        public static extern int NtResumeProcess(IntPtr process);

        [DllImport("winmm.dll")]
        public static extern uint timeBeginPeriod(uint ms);

        [DllImport("winmm.dll")]
        public static extern uint timeEndPeriod(uint ms);
    }
}
