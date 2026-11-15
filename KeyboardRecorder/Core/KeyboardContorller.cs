using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
            }
            remove
            {
                Recorder.PropertyChanged -= value;
                Player.PropertyChanged -= value;
            }
        }
        public event EventHandler<KeyboardLogInfo> KeyEvent
        {
            add
            {
                Recorder.KeyEvent += value;
                Player.KeyEvent += value;
            }
            remove
            {
                Recorder.KeyEvent -= value;
                Player.KeyEvent -= value;
            }
        }

        private InputSource InputSource { get; }
        private KeyboardEventLogger KeyboardEventLogger { get; }
        public KeyboardRecorder Recorder { get; } = new KeyboardRecorder();
        public KeyboardPlayer Player { get; } = new KeyboardPlayer();

        public bool IsRecording => Recorder.IsRecording;
        public bool IsPlaying => Player.IsPlaying;

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
                    Player.Stop();
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
                Player.Play(Recorder.GetRecord());
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
                AdjustGlobalOffset(-1);
            else if (key == "F2")
                AdjustGlobalOffset(1);
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
    }
}
