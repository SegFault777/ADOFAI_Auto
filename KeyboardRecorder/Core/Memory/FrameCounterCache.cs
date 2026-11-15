using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ADOFAI_Auto.Core.Memory
{
    /// <summary>
    /// 자동 탐색한 프레임 카운터가 DLL/EXE 안의 정적 주소이면 "모듈+오프셋"을 저장해 다음 실행부터 즉시 사용한다.
    /// 게임 빌드(실행 파일 크기+수정 시각)가 바뀌면 캐시는 무시되고 다시 탐색한다.
    /// </summary>
    public static class FrameCounterCache
    {
        private static string FilePath => Path.Combine(AppConfig.BaseDirectory, "frame_counter.cache");

        /// <summary>
        /// 게임 빌드 식별자. ADOFAI는 Mono 기반 Unity 게임이라 게임 로직이 exe가 아니라
        /// UnityPlayer.dll / Managed\Assembly-CSharp.dll 에 들어 있고, 업데이트 때 exe는 그대로인 경우가 많다.
        /// 그래서 exe + UnityPlayer.dll + Assembly-CSharp.dll 의 크기/수정 시각을 함께 묶는다.
        /// </summary>
        public static string BuildKey(Process process)
        {
            try
            {
                string exe = process.MainModule.FileName;
                string dir = Path.GetDirectoryName(exe);
                var parts = new System.Collections.Generic.List<string> { Describe(exe), Describe(Path.Combine(dir, "UnityPlayer.dll")) };

                foreach (string data in Directory.GetDirectories(dir, "*_Data"))
                    parts.Add(Describe(Path.Combine(data, "Managed", "Assembly-CSharp.dll")));

                return string.Join("|", parts);
            }
            catch
            {
                return "unknown";
            }
        }

        private static string Describe(string path)
        {
            var info = new FileInfo(path);
            return info.Exists ? $"{info.Length}-{info.LastWriteTimeUtc.Ticks}" : "none";
        }

        /// <summary>게임 exe의 제품 버전 문자열 (표시용). 읽지 못하면 "알 수 없음".</summary>
        public static string GameVersion(Process process)
        {
            try
            {
                var v = FileVersionInfo.GetVersionInfo(process.MainModule.FileName);
                string text = string.IsNullOrWhiteSpace(v.ProductVersion) ? v.FileVersion : v.ProductVersion;
                return string.IsNullOrWhiteSpace(text) ? "알 수 없음" : text.Trim();
            }
            catch
            {
                return "알 수 없음";
            }
        }

        public static void Save(string buildKey, string module, long offset)
        {
            try
            {
                File.WriteAllText(FilePath, $"build={buildKey}\nmodule={module}\noffset={offset}\n", Encoding.UTF8);
            }
            catch { }
        }

        public static bool TryLoad(string buildKey, IMemorySource memory, out long address)
        {
            address = 0;
            try
            {
                if (!File.Exists(FilePath))
                    return false;

                string build = null, module = null;
                long offset = -1;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq), v = line.Substring(eq + 1).Trim();
                    if (k == "build") build = v;
                    else if (k == "module") module = v;
                    else if (k == "offset") long.TryParse(v, out offset);
                }

                long moduleBase;
                if (build != buildKey || module == null || offset < 0 || !memory.TryGetModuleBase(module, out moduleBase))
                    return false;

                address = moduleBase + offset;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void Clear()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
        }
    }
}
