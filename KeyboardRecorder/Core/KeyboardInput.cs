using System;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace ADOFAI_Auto.Core
{
    /// <summary>
    /// Win32 SendInput 으로 가상 키 입력을 만든다. 가상 키 코드와 스캔 코드를 함께 보내
    /// (Unity/Rewired 계열이 스캔 코드를 보는 경우에도) 게임이 입력을 놓치지 않도록 한다.
    /// </summary>
    public static class KeyboardInput
    {
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_SCANCODE = 0x0008;
        private const uint MAPVK_VK_TO_VSC = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // x64 기준 INPUT 구조체: type(4) + 패딩(4) + union(32) = 40바이트. union 은 MOUSEINPUT 크기에 맞춰 패딩.
        [StructLayout(LayoutKind.Explicit, Size = 40)]
        private struct INPUT
        {
            [FieldOffset(0)] public uint type;
            [FieldOffset(8)] public KEYBDINPUT ki;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint code, uint mapType);

        private static ushort _vk;
        private static ushort _scan;

        static KeyboardInput()
        {
            SetPlayKey(AppConfig.PlayKey);
        }

        /// <summary>키 이름(WPF Key 이름, 예: A, Space, F)을 받아 재생 키로 설정. 실패하면 false.</summary>
        public static bool SetPlayKey(string keyName)
        {
            Key key;
            if (!Enum.TryParse(keyName, true, out key) || key == Key.None)
                return false;

            int vk = KeyInterop.VirtualKeyFromKey(key);
            if (vk == 0)
                return false;

            _vk = (ushort)vk;
            _scan = (ushort)MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC);
            return true;
        }

        public static string PlayKeyName => KeyInterop.KeyFromVirtualKey(_vk == 0 ? 0x41 : _vk).ToString();

        public static void PressKey()
        {
            if (_vk == 0)
                SetPlayKey("A");

            var inputs = new INPUT[2];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].ki = new KEYBDINPUT { wVk = _vk, wScan = _scan, dwFlags = 0 };
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].ki = new KEYBDINPUT { wVk = _vk, wScan = _scan, dwFlags = KEYEVENTF_KEYUP };

            // 누르기/떼기를 한 번의 호출로 보내 두 이벤트 사이에 다른 입력이 끼어들지 않게 한다.
            SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
        }
    }
}
