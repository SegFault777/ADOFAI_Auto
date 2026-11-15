using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using ADOFAI_Auto.Core.Memory;

namespace ADOFAI_Auto.Core
{
    /// <summary>실행 파일 옆의 adofai_auto.ini 를 읽는 단순 설정. 파일이 없으면 기본값(전부 자동)으로 동작한다.</summary>
    public static class AppConfig
    {
        public const string FileName = "adofai_auto.ini";

        public static string ProcessName = "A Dance of Fire and Ice";
        public static string WindowTitleHint = "A Dance of Fire and Ice";
        /// <summary>자동 재생 시 눌러 줄 키 (WindowsInput 가상 키 이름이 아니라 System.Windows.Forms.Keys 와 같은 이름). 기본 A.</summary>
        public static string PlayKey = "A";

        /// <summary>시작할 때 자동으로 불러올 .adofai 레벨 파일 경로 (선택).</summary>
        public static string LevelPath = "";
        /// <summary>오토플레이 속도 배율 (스피드 트라이얼 1.2배 등).</summary>
        public static double SpeedMultiplier = 1.0;
        /// <summary>오토플레이 전체 보정 (ms, +는 늦게 / -는 빠르게).</summary>
        public static int AutoOffsetMs = 0;

        public static PointerPath FramePointer;
        public static PointerPath PausePointer;
        public static PointerPath InputOffsetPointer;

        public static string BaseDirectory => AppDomain.CurrentDomain.BaseDirectory;
        public static string FilePath => Path.Combine(BaseDirectory, FileName);

        static AppConfig()
        {
            Load();
        }

        public static void Load()
        {
            if (!File.Exists(FilePath))
            {
                WriteDefault();
                return;
            }

            try
            {
                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                        continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;

                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();
                    switch (key)
                    {
                        case "ProcessName": if (value.Length > 0) ProcessName = value; break;
                        case "WindowTitleHint": if (value.Length > 0) WindowTitleHint = value; break;
                        case "PlayKey": if (value.Length > 0) PlayKey = value; break;
                        case "LevelPath": LevelPath = value; break;
                        case "SpeedMultiplier":
                            double sm;
                            if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sm) && sm > 0)
                                SpeedMultiplier = sm;
                            break;
                        case "AutoOffsetMs":
                            int ao;
                            if (int.TryParse(value, out ao)) AutoOffsetMs = ao;
                            break;
                        case "FramePointer": FramePointer = PointerPath.Parse(value); break;
                        case "PausePointer": PausePointer = PointerPath.Parse(value); break;
                        case "InputOffsetPointer": InputOffsetPointer = PointerPath.Parse(value); break;
                    }
                }
            }
            catch { /* 설정이 깨져 있어도 기본값으로 동작 */ }
        }

        private static void WriteDefault()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# ADOFAI_Auto 설정 (모든 항목은 선택 사항입니다)");
                sb.AppendLine();
                sb.AppendLine("# 게임 프로세스 이름 / 창 제목 (프로세스 이름으로 못 찾으면 창 제목으로 찾습니다)");
                sb.AppendLine("ProcessName=A Dance of Fire and Ice");
                sb.AppendLine("WindowTitleHint=A Dance of Fire and Ice");
                sb.AppendLine();
                sb.AppendLine("# 자동 재생 때 누를 키 (게임에 설정된 키와 같아야 합니다)");
                sb.AppendLine("PlayKey=A");
                sb.AppendLine();
                sb.AppendLine("# 오토플레이 모드(F6): 불러올 .adofai 레벨 파일(비워 두면 L 키로 선택), 속도 배율, 전체 보정(ms)");
                sb.AppendLine("LevelPath=");
                sb.AppendLine("SpeedMultiplier=1.0");
                sb.AppendLine("AutoOffsetMs=0");
                sb.AppendLine();
                sb.AppendLine("# 수동 포인터 지정 (비워 두면 프레임 카운터를 자동으로 탐색합니다)");
                sb.AppendLine("# 형식: 모듈+오프셋:체인   예) UnityPlayer.dll+0x017EEDD0:0x3D8,0x0,0x578,0xE30");
                sb.AppendLine("FramePointer=");
                sb.AppendLine("# 일시정지 여부(bool) / 입력 오프셋(int) 포인터: 지정하지 않으면 해당 기능만 비활성화됩니다");
                sb.AppendLine("PausePointer=");
                sb.AppendLine("InputOffsetPointer=");
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch { /* 쓰기 권한이 없어도 무시 */ }
        }
    }
}
