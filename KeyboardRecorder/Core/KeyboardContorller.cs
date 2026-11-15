using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ADOFAI_Auto.Core.AutoPlay;
using ADOFAI_Auto.Core.Model;

using NeatInput.Windows;
using NeatInput.Windows.Processing.Keyboard.Enums;

namespace ADOFAI_Auto.Core
{
    public class KeyboardContorller : INotifyPropertyChanged
    {
        private KeyboardContorller()
        {
            KeyboardEventLogger = new KeyboardEventLogger();
            InputSource = new InputSource(KeyboardEventLogger);
            KeyboardEventLogger.KeyboardEvent += KeyboardEventLogger_KeyboardEvent;
            InputSource.Listen();
        }

        private static KeyboardContorller Instence;
        public static KeyboardContorller GetInstence()
        {
            if (Instence == null)
                Instence = new KeyboardContorller();
            return Instence;
        }

        public event PropertyChangedEventHandler PropertyChanged
        {
            add
            {
                Recorder.PropertyChanged += value;
                Player.PropertyChanged += value;
                AutoPlayer.PropertyChanged += value;
            }
            remove
            {
                Recorder.PropertyChanged -= value;
                Player.PropertyChanged -= value;
                AutoPlayer.PropertyChanged -= value;
            }
        }
        public event EventHandler<KeyboardLogInfo> KeyEvent
        {
            add
            {
                Recorder.KeyEvent += value;
                Player.KeyEvent += value;
                AutoPlayer.KeyEvent += value;
            }
            remove
            {
                Recorder.KeyEvent -= value;
                Player.KeyEvent -= value;
                AutoPlayer.KeyEvent -= value;
            }
        }

        private InputSource InputSource { get; }
        private KeyboardEventLogger KeyboardEventLogger { get; }
        public KeyboardRecorder Recorder { get; } = new KeyboardRecorder();
        public KeyboardPlayer Player { get; } = new KeyboardPlayer();

        public bool IsRecording => Recorder.IsRecording;
        public AutoPlayer AutoPlayer { get; } = new AutoPlayer();
        public bool IsPlaying => Player.IsPlaying || AutoPlayer.IsPlaying;

        // ───────────── 오토플레이 모드 (녹화 없이 레벨 파일로 자동 연주) ─────────────
        /// <summary>true 면 E 키가 녹화 재생 대신 레벨 파일 기반 오토플레이를 시작한다 (F6로 전환).</summary>
        public bool AutoMode { get; private set; }
        public AdofaiLevel Level { get; private set; }
        public AutoPlayPlan Plan { get; private set; }
        public double SpeedMultiplier { get; private set; } = AppConfig.SpeedMultiplier;
        public int AutoOffsetMs { get; private set; } = AppConfig.AutoOffsetMs;

        public string ModeText => AutoMode ? "오토플레이 (레벨 파일)" : "녹화/재생";
        public string LevelSummary
        {
            get
            {
                if (Level == null) return "(레벨 없음 - L 키로 .adofai 선택)";
                if (Plan == null || !Plan.IsPlayable) return $"{Level.Name}: {Plan?.Unsupported ?? "계산 실패"}";
                return $"{Level.Name} / 타일 {Plan.TileCount}개 / {Plan.Duration:F1}초 / 속도 {SpeedMultiplier:0.0#}x / 보정 {AutoOffsetMs:+#;-#;0}ms";
            }
        }

        /// <summary>UI에 보여 줄 짧은 안내 메시지 (저장/불러오기 결과, 오프셋 변경 등).</summary>
        public event Action<string> Notice;
        private void Say(string message)
        {
            Debug.WriteLine(message);
            Notice?.Invoke(message);
        }

        public string CurrentRecordName => RecordLibrary.CurrentName;

        private void KeyboardEventLogger_KeyboardEvent(object sender, NeatInput.Windows.Events.KeyboardEvent e)
        {
            if (e.State != KeyStates.Down)
                return;

            if (IsPlaying)
            {
                if (e.Key == Keys.E)
                {
                    Player.Stop();
                    AutoPlayer.Stop();
                }
                return;
            }
            if (IsRecording)
            {
                if (e.Key == Keys.R)
                    Recorder.StopRecord();
                return;
            }

            // 열거형 멤버 이름에 의존하지 않도록 F 키는 문자열로 비교한다.
            string key = e.Key.ToString();

            if (e.Key == Keys.R)
                Recorder.StartRecord(KeyboardEventLogger);
            else if (e.Key == Keys.E)
            {
                if (AutoMode) StartAuto();
                else Player.Play(Recorder.GetRecord());
            }
            else if (e.Key == Keys.Delete)
            {
                Recorder.RecordClear();
                Say("녹화 데이터를 비웠습니다.");
            }
            else if (e.Key == Keys.Q)
                SaveCurrent();
            else if (e.Key == Keys.W)
                LoadSelected();
            else if (key == "F1")
            {
                if (AutoMode) AdjustAutoOffset(-5); else AdjustGlobalOffset(-1);
            }
            else if (key == "F2")
            {
                if (AutoMode) AdjustAutoOffset(5); else AdjustGlobalOffset(1);
            }
            else if (key == "F6")
                ToggleMode();
            else if (key == "F7")
                AdjustSpeed(-0.1);
            else if (key == "F8")
                AdjustSpeed(0.1);
            else if (key == "L")
                PickLevel();
            else if (key == "F3")
                SelectRecord(-1);
            else if (key == "F4")
                SelectRecord(1);
            else if (key == "F5")
            {
                MemoryReader.Rescan();
                Say("프레임 카운터를 다시 탐색합니다.");
            }
        }

        private void SaveCurrent()
        {
            if (Recorder.GetRecord().Count == 0)
            {
                Say("저장할 녹화가 없습니다.");
                return;
            }
            string path = RecordLibrary.SaveNew(Recorder.GetRecord());
            Say(path != null ? $"저장 완료: {System.IO.Path.GetFileName(path)}" : "저장 실패");
        }

        private void LoadSelected()
        {
            string path = RecordLibrary.EnsureSelected();
            if (path == null)
            {
                Say("Records 폴더에 불러올 파일이 없습니다.");
                return;
            }
            Say(KeyboardLogIOManager.Load(path, Recorder.GetRecord())
                ? $"불러오기 완료: {RecordLibrary.CurrentName} ({Recorder.GetRecord().Count}개 입력, 오프셋 {Recorder.GlobalOffset:+#;-#;0})"
                : "불러오기 실패");
        }

        private void SelectRecord(int delta)
        {
            if (RecordLibrary.Move(delta) == null)
            {
                Say("Records 폴더에 파일이 없습니다.");
                return;
            }
            LoadSelected();
        }

        private void AdjustGlobalOffset(int delta)
        {
            Recorder.GlobalOffset += delta;
            Say($"전체 프레임 오프셋: {Recorder.GlobalOffset:+#;-#;0} (타일마다 적용)");
        }

        // ───────────── 오토플레이 동작 ─────────────
        private void ToggleMode()
        {
            AutoMode = !AutoMode;
            if (AutoMode && Level == null && !string.IsNullOrWhiteSpace(AppConfig.LevelPath))
                LoadLevel(AppConfig.LevelPath);
            Say(AutoMode
                ? "오토플레이 모드: L=레벨 선택, E=시작, F1/F2=±5ms 보정, F7/F8=속도 ∓0.1"
                : "녹화/재생 모드로 전환했습니다.");
        }

        private void PickLevel()
        {
            if (!AutoMode)
            {
                Say("F6 으로 오토플레이 모드를 켠 뒤 L 키를 누르세요.");
                return;
            }

            string path = null;
            var app = System.Windows.Application.Current;
            if (app == null) return;
            app.Dispatcher.Invoke(() =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "오토플레이할 레벨 선택",
                    Filter = "ADOFAI 레벨 (*.adofai)|*.adofai|모든 파일 (*.*)|*.*"
                };
                if (dialog.ShowDialog() == true)
                    path = dialog.FileName;
            });
            if (path != null)
                LoadLevel(path);
        }

        private void LoadLevel(string path)
        {
            try
            {
                Level = AdofaiLevel.Load(path);
                Plan = AutoPlayPlanner.Plan(Level);
                if (!Plan.IsPlayable)
                    Say("불러왔지만 재생할 수 없습니다: " + Plan.Unsupported);
                else
                    Say($"레벨 불러오기 완료: {Level.Name} ({Plan.TileCount}타일)" +
                        (Plan.Warnings.Count > 0 ? " - 주의: " + string.Join(" ", Plan.Warnings) : ""));
            }
            catch (Exception ex)
            {
                Level = null;
                Plan = null;
                Say("레벨 불러오기 실패: " + ex.Message);
            }
        }

        private void StartAuto()
        {
            if (Plan == null || !Plan.IsPlayable)
            {
                Say("재생할 수 있는 레벨이 없습니다. L 키로 .adofai 파일을 먼저 선택하세요.");
                return;
            }
            AutoPlayer.Play(Plan, SpeedMultiplier, AutoOffsetMs);
        }

        private void AdjustAutoOffset(int deltaMs)
        {
            AutoOffsetMs += deltaMs;
            Say($"오토플레이 보정: {AutoOffsetMs:+#;-#;0}ms (+는 늦게, -는 빠르게)");
        }

        private void AdjustSpeed(double delta)
        {
            if (!AutoMode) return;
            SpeedMultiplier = Math.Max(0.1, Math.Round(SpeedMultiplier + delta, 2));
            Say($"오토플레이 속도 배율: {SpeedMultiplier:0.0#}x (게임의 속도 배율과 같아야 합니다)");
        }
    }
}
