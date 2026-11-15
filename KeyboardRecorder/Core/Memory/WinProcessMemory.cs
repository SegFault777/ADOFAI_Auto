using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ADOFAI_Auto.Core.Memory
{
    /// <summary>Win32(ReadProcessMemory/VirtualQueryEx) 기반 IMemorySource. 외부 패키지에 의존하지 않는다.</summary>
    public sealed class WinProcessMemory : IMemorySource, IDisposable
    {
        private IntPtr Handle;
        private readonly Process Process;
        private readonly List<ModuleInfo> Modules = new List<ModuleInfo>();

        private struct ModuleInfo
        {
            public string Name;
            public long Base;
            public long Size;
        }

        private WinProcessMemory(Process process, IntPtr handle)
        {
            Process = process;
            Handle = handle;
            RefreshModules();
        }

        public static WinProcessMemory Open(Process process)
        {
            const uint access = NativeMethods.PROCESS_VM_READ | NativeMethods.PROCESS_VM_WRITE |
                                NativeMethods.PROCESS_VM_OPERATION | NativeMethods.PROCESS_QUERY_INFORMATION |
                                NativeMethods.PROCESS_SUSPEND_RESUME;
            IntPtr h = NativeMethods.OpenProcess(access, false, process.Id);
            if (h == IntPtr.Zero)
                return null;
            return new WinProcessMemory(process, h);
        }

        public int ProcessId => Process.Id;

        public bool IsAlive
        {
            get
            {
                try { return !Process.HasExited; }
                catch { return false; }
            }
        }

        public void RefreshModules()
        {
            Modules.Clear();
            try
            {
                foreach (ProcessModule m in Process.Modules)
                    Modules.Add(new ModuleInfo { Name = m.ModuleName, Base = m.BaseAddress.ToInt64(), Size = m.ModuleMemorySize });
            }
            catch { /* 접근 거부 등: 모듈 정보 없이 계속 */ }
        }

        public IEnumerable<MemoryRegion> EnumerateRegions()
        {
            long address = 0;
            const long max = 0x7FFFFFFEFFFFL;
            int mbiSize = Marshal.SizeOf(typeof(NativeMethods.MEMORY_BASIC_INFORMATION));

            while (address < max)
            {
                NativeMethods.MEMORY_BASIC_INFORMATION mbi;
                if (NativeMethods.VirtualQueryEx(Handle, new IntPtr(address), out mbi, new UIntPtr((uint)mbiSize)) == UIntPtr.Zero)
                    break;

                long size = (long)mbi.RegionSize.ToUInt64();
                long start = mbi.BaseAddress.ToInt64();
                if (size <= 0)
                    break;

                bool writable = (mbi.Protect & (NativeMethods.PAGE_READWRITE | NativeMethods.PAGE_WRITECOPY |
                                                NativeMethods.PAGE_EXECUTE_READWRITE | NativeMethods.PAGE_EXECUTE_WRITECOPY)) != 0;
                bool bad = (mbi.Protect & (NativeMethods.PAGE_GUARD | NativeMethods.PAGE_NOACCESS)) != 0;
                bool typeOk = mbi.Type == NativeMethods.MEM_PRIVATE || mbi.Type == NativeMethods.MEM_IMAGE;

                if (mbi.State == NativeMethods.MEM_COMMIT && writable && !bad && typeOk && size >= 4096)
                    yield return new MemoryRegion { Base = start, Size = size, IsImage = mbi.Type == NativeMethods.MEM_IMAGE };

                address = start + size;
            }
        }

        public bool Read(long address, byte[] buffer, int count)
        {
            UIntPtr read;
            return NativeMethods.ReadProcessMemory(Handle, new IntPtr(address), buffer, new UIntPtr((uint)count), out read)
                   && read.ToUInt64() == (ulong)count;
        }

        public bool Write(long address, byte[] data)
        {
            UIntPtr written;
            return NativeMethods.WriteProcessMemory(Handle, new IntPtr(address), data, new UIntPtr((uint)data.Length), out written)
                   && written.ToUInt64() == (ulong)data.Length;
        }

        public bool TryGetModule(long address, out string moduleName, out long offset)
        {
            foreach (var m in Modules)
            {
                if (address >= m.Base && address < m.Base + m.Size)
                {
                    moduleName = m.Name;
                    offset = address - m.Base;
                    return true;
                }
            }
            moduleName = null;
            offset = 0;
            return false;
        }

        public bool TryGetModuleBase(string moduleName, out long baseAddress)
        {
            foreach (var m in Modules)
            {
                if (string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase))
                {
                    baseAddress = m.Base;
                    return true;
                }
            }
            baseAddress = 0;
            return false;
        }

        public void Suspend() => NativeMethods.NtSuspendProcess(Handle);
        public void Resume() => NativeMethods.NtResumeProcess(Handle);

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(Handle);
                Handle = IntPtr.Zero;
            }
        }
    }
}
