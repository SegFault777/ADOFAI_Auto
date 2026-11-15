using System;
using System.Collections.Generic;

namespace ADOFAI_Auto.Core.Memory
{
    /// <summary>스캔 대상이 될 수 있는 메모리 영역(커밋됨 + 쓰기 가능).</summary>
    public struct MemoryRegion
    {
        public long Base;
        public long Size;
        /// <summary>DLL/EXE 이미지 영역이면 true (재실행해도 "모듈+오프셋"이 유지되는 정적 영역).</summary>
        public bool IsImage;
    }

    /// <summary>프로세스 메모리 접근 추상화. 실제 구현은 Win32, 테스트에서는 가짜 메모리를 사용한다.</summary>
    public interface IMemorySource
    {
        IEnumerable<MemoryRegion> EnumerateRegions();
        bool Read(long address, byte[] buffer, int count);
        bool TryGetModule(long address, out string moduleName, out long offset);
        bool TryGetModuleBase(string moduleName, out long baseAddress);
    }
}
