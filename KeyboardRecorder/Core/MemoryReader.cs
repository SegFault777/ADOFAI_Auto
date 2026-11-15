using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;

using ADOFAI_Auto.Core.Memory;

namespace ADOFAI_Auto.Core
{
    /// <summary>
    /// 게임 프로세스에 붙어 프레임 카운터 등을 읽는다.
    /// 하드코딩된 UnityPlayer.dll 오프셋 대신 프레임 카운터를 런타임에 자동 탐색하므로 게임 업데이트에 영향을 받지 않는다.
    /// (우선순위: 설정 파일의 수동 포인터 → 저장된 캐시 → 자동 탐색)
    /// </summary>
    public static class MemoryReader
    {
        static MemoryReader()
        {
            try { NativeMethods.timeBeginPeriod(1); } catch { }
            var worker = new Thread(FrameSyncLoop)
            {
                IsBackground = true,
                Name = "ADOFAI frame sync",
                Priority = ThreadPriority.AboveNormal
            };
            worker.Start();
        }

        private static readonly object Sync = new object();
        private static WinProcessMemory Mem;
        private static Process Target;
        private static string BuildKey;

        private static long FrameAddress;
        private static long PauseAddress;
        private static long InputOffsetAddress;

        private static int PreloadFrame;
        private static volatile bool RescanRequested;

        public static bool HighAccuracyMode = false;

        /// <summary>UI에 표시할 현재 상태 문구.</summary>
        public static string Status { get; private set; } = "게임 대기 중...";
        public static string FrameSource { get; private set; } = "-";
        /// <summary>연결된 게임 exe의 제품 버전 (표시용).</summary>
        public static string GameVersion { get; private set; } = "-";
        public static event Action<string> StatusChanged;

        public static bool IsAttached => Mem != null && Mem.IsAlive;
        public static bool IsReady => IsAttached && FrameAddress != 0;
        public static bool IsPauseAvailable => PauseAddress != 0;
        public static bool IsInputOffsetAvailable => InputOffsetAddress != 0;

        private static void SetStatus(string text)
        {
            Status = text;
            Debug.WriteLine("[MemoryReader] " + text);
            StatusChanged?.Invoke(text);
        }

        /// <summary>프레임 카운터를 다시 탐색한다 (게임 업데이트 직후 캐시가 틀렸을 때 등).</summary>
        public static void Rescan()
        {
            FrameCounterCache.Clear();
            RescanRequested = true;
        }

        // ───────────────────────── 프로세스 연결 ─────────────────────────

        private static Process FindGameProcess()
        {
            var byName = Process.GetProcessesByName(AppConfig.ProcessName);
            if (byName.Length > 0)
                return byName[0];

            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (!string.IsNullOrEmpty(p.MainWindowTitle) &&
                        p.MainWindowTitle.IndexOf(AppConfig.WindowTitleHint, StringComparison.OrdinalIgnoreCase) >= 0)
                        return p;
                }
                catch { }
            }
            return null;
        }

        private static bool Attach()
        {
            var process = FindGameProcess();
            if (process == null)
            {
                SetStatus("게임 대기 중... (A Dance of Fire and Ice 실행 필요)");
                return false;
            }

            var mem = WinProcessMemory.Open(process);
            if (mem == null)
            {
                SetStatus("게임에 접근할 수 없습니다. 관리자 권한으로 실행해 보세요.");
                return false;
            }

            lock (Sync)
            {
                Detach();
                Target = process;
                Mem = mem;
                BuildKey = FrameCounterCache.BuildKey(process);
                GameVersion = FrameCounterCache.GameVersion(process);
                FrameAddress = PauseAddress = InputOffsetAddress = 0;
            }
            SetStatus($"게임에 연결됨 (버전 {GameVersion}). 프레임 카운터 확인 중...");
            return true;
        }

        private static void Detach()
        {
            if (Mem != null) Mem.Dispose();
            if (Target != null) Target.Dispose();
            Mem = null;
            Target = null;
            FrameAddress = PauseAddress = InputOffsetAddress = 0;
            Interlocked.Exchange(ref PreloadFrame, 0);
            FrameSource = "-";
            GameVersion = "-";
        }

        // ───────────────────────── 주소 결정 ─────────────────────────

        private static void ResolveOptionalPointers()
        {
            long a;
            if (AppConfig.PausePointer != null && AppConfig.PausePointer.TryResolve(Mem, out a))
                PauseAddress = a;
            if (AppConfig.InputOffsetPointer != null && AppConfig.InputOffsetPointer.TryResolve(Mem, out a))
                InputOffsetAddress = a;
        }

        private static bool ResolveFrameAddress()
        {
            ResolveOptionalPointers();
            long addr;

            // 1) 설정 파일의 수동 포인터
            if (AppConfig.FramePointer != null && AppConfig.FramePointer.TryResolve(Mem, out addr) &&
                FrameCounterFinder.Validate(Mem, addr))
            {
                FrameAddress = addr;
                FrameSource = "수동 포인터";
                SetStatus("준비 완료 (수동 포인터)");
                return true;
            }

            // 2) 이전에 찾아 둔 정적 주소 캐시
            if (FrameCounterCache.TryLoad(BuildKey, Mem, out addr) && FrameCounterFinder.Validate(Mem, addr))
            {
                FrameAddress = addr;
                FrameSource = "캐시";
                SetStatus("준비 완료 (저장된 주소)");
                return true;
            }

            // 3) 자동 탐색
            SetStatus("프레임 카운터 탐색 중... 게임이 화면에 표시된 상태로 두세요 (수 초 소요)");
            var finder = new FrameCounterFinder(Mem, msg => SetStatus("탐색: " + msg));
            var found = finder.Find();
            if (found.Count == 0)
            {
                SetStatus("프레임 카운터를 찾지 못했습니다. 게임 창을 띄워 둔 채 F5(재탐색)를 눌러 주세요.");
                return false;
            }

            // 탐색 도중 게임이 종료/재시작되었을 수 있으니 상위 후보부터 다시 검증
            foreach (var cand in found.Take(5))
            {
                if (!FrameCounterFinder.Validate(Mem, cand.Address))
                    continue;

                FrameAddress = cand.Address;
                FrameSource = cand.IsImage ? "자동 탐색(정적)" : "자동 탐색";

                string module; long offset;
                if (Mem.TryGetModule(cand.Address, out module, out offset))
                    FrameCounterCache.Save(BuildKey, module, offset);

                SetStatus($"준비 완료 (자동 탐색, 약 {cand.Rate:F0} FPS)");
                return true;
            }
            SetStatus("프레임 카운터 검증에 실패했습니다. F5로 다시 탐색해 주세요.");
            return false;
        }

        // ───────────────────────── 프레임 동기화 루프 ─────────────────────────

        private static void FrameSyncLoop()
        {
            var buf = new byte[4];
            int failures = 0;

            while (true)
            {
                try
                {
                    if (!IsAttached)
                    {
                        if (!Attach())
                        {
                            Thread.Sleep(1000);
                            continue;
                        }
                    }

                    if (FrameAddress == 0 || RescanRequested)
                    {
                        RescanRequested = false;
                        if (Mem != null) Mem.RefreshModules();
                        if (!ResolveFrameAddress())
                        {
                            Thread.Sleep(3000);
                            continue;
                        }
                        failures = 0;
                    }

                    if (Mem.Read(FrameAddress, buf, 4))
                    {
                        Interlocked.Exchange(ref PreloadFrame, BitConverter.ToInt32(buf, 0));
                        failures = 0;
                    }
                    else if (++failures > 50)
                    {
                        // 게임이 종료되었거나 주소가 무효해짐 → 처음부터 다시 연결
                        lock (Sync) Detach();
                        SetStatus("게임 연결이 끊어졌습니다. 다시 찾는 중...");
                        failures = 0;
                    }
                }
                catch
                {
                    Thread.Sleep(500);
                }
                Thread.Sleep(1);
            }
        }

        // ───────────────────────── 공개 API ─────────────────────────

        /// <summary>백그라운드 스레드(≈1ms 주기)가 갱신한 최신 프레임 값.</summary>
        public static int GetFrame() => Volatile.Read(ref PreloadFrame);

        /// <summary>메모리를 즉시 직접 읽은 프레임 값 (재생 시 정확한 타이밍 대기에 사용).</summary>
        public static int ReadFrameNow()
        {
            var mem = Mem;
            if (mem == null || FrameAddress == 0)
                return GetFrame();
            var buf = new byte[4];
            return mem.Read(FrameAddress, buf, 4) ? BitConverter.ToInt32(buf, 0) : GetFrame();
        }

        public static bool IsPause()
        {
            if (!IsPauseAvailable || Mem == null)
                return false;
            var buf = new byte[1];
            return Mem.Read(PauseAddress, buf, 1) && buf[0] != 0;
        }

        public static int GetInputOffset()
        {
            if (!IsInputOffsetAvailable || Mem == null)
                return 0;
            var buf = new byte[4];
            return Mem.Read(InputOffsetAddress, buf, 4) ? BitConverter.ToInt32(buf, 0) : 0;
        }

        public static void SetInputOffset(int value)
        {
            if (!IsInputOffsetAvailable || Mem == null)
                return;

            var data = BitConverter.GetBytes(value);
            if (HighAccuracyMode)
            {
                Mem.Suspend();
                Mem.Write(InputOffsetAddress, data);
                Mem.Resume();
            }
            else
                Mem.Write(InputOffsetAddress, data);
        }

        public static void Dispose()
        {
            lock (Sync) Detach();
            try { NativeMethods.timeEndPeriod(1); } catch { }
        }
    }
}
