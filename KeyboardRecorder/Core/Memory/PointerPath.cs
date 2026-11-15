using System;
using System.Globalization;
using System.Linq;

namespace ADOFAI_Auto.Core.Memory
{
    /// <summary>
    /// "모듈+오프셋:체인" 형식의 수동 포인터 경로. 예) UnityPlayer.dll+0x017EEDD0:0x3D8,0x0,0x578,0xE30
    /// 체인이 없으면 모듈+오프셋 자체가 값의 주소이다. (원본 프로그램과 동일한 해석 방식)
    /// </summary>
    public sealed class PointerPath
    {
        public string Module { get; private set; }
        public long BaseOffset { get; private set; }
        public int[] Chain { get; private set; }

        public static PointerPath Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            try
            {
                string[] halves = text.Trim().Split(':');
                string[] head = halves[0].Split('+');
                if (head.Length != 2)
                    return null;

                var path = new PointerPath
                {
                    Module = head[0].Trim(),
                    BaseOffset = ParseHex(head[1])
                };
                if (halves.Length > 1 && !string.IsNullOrWhiteSpace(halves[1]))
                    path.Chain = halves[1].Split(',').Select(s => (int)ParseHex(s)).ToArray();
                return path;
            }
            catch
            {
                return null;
            }
        }

        private static long ParseHex(string s)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(2);
            return long.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        public bool TryResolve(IMemorySource memory, out long address)
        {
            address = 0;
            long moduleBase;
            if (!memory.TryGetModuleBase(Module, out moduleBase))
                return false;

            long addr = moduleBase + BaseOffset;
            if (Chain == null || Chain.Length == 0)
            {
                address = addr;
                return true;
            }

            var buf = new byte[8];
            if (!memory.Read(addr, buf, 8))
                return false;
            addr = BitConverter.ToInt64(buf, 0);

            for (int i = 0; i < Chain.Length; i++)
            {
                addr += Chain[i];
                if (i == Chain.Length - 1)
                    break;
                if (!memory.Read(addr, buf, 8))
                    return false;
                addr = BitConverter.ToInt64(buf, 0);
            }
            address = addr;
            return addr > 0x10000;
        }

        public override string ToString() =>
            $"{Module}+0x{BaseOffset:X}" + (Chain == null ? "" : ":" + string.Join(",", Chain.Select(c => "0x" + c.ToString("X"))));
    }
}
