using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ADOFAI_Auto.Core;
using ADOFAI_Auto.Core.Model;

namespace ADOFAI_Auto.ViewModel
{
    public class GameStateViewModel : BaseViewModel
    {
        public GameStateViewModel()
        {
            StatusText = MemoryReader.Status;
            MemoryReader.StatusChanged += text => StatusText = text;
            KeyboardContorller.GetInstence().Notice += text => Notice = text;
            KeyboardContorller.GetInstence().PropertyChanged += Recorder_PropertyChanged;
            GameStateGetter();
        }

        private async void GameStateGetter()
        {
            //int beforeFrame;
            while (true)
            {
                IsGamePause = MemoryReader.IsPause();
                PauseText = MemoryReader.IsPauseAvailable ? (IsGamePause ? "예" : "아니오") : "알 수 없음";
                OffsetText = MemoryReader.IsInputOffsetAvailable ? MemoryReader.GetInputOffset().ToString() : "사용 안 함";
                FrameSourceText = MemoryReader.IsReady ? MemoryReader.FrameSource : "-";
                RecordName = KeyboardContorller.GetInstence().CurrentRecordName;
                //beforeFrame = GameFrame;
                GameFrame = MemoryReader.GetFrame();
                //System.Diagnostics.Debug.WriteLine($"Frame Gap: {GameFrame - beforeFrame}");
                //System.Diagnostics.Debug.WriteLine($"GameFrame: {GameFrame}");
                InputOffset = MemoryReader.GetInputOffset();
                await Task.Delay(100);
            }
        }

        private void Recorder_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var controller = KeyboardContorller.GetInstence();

            if(!controller.Recorder.IsRecording && !controller.Player.IsPlaying)
                CurrentState = "Ready";
            else if (e.PropertyName == "IsRecording" && controller.Recorder.IsRecording)
                CurrentState = "Recording";
            else if (e.PropertyName == "IsPlaying" && controller.Player.IsPlaying)
                CurrentState = "Playing";
        }

        private bool _IsGamePause = false;
        public bool IsGamePause
        {
            get => _IsGamePause;
            set
            {
                _IsGamePause = value;
                OnPropertyChanged("IsGamePause");
            }
        }

        private int _GameFrame = -1;
        public int GameFrame
        {
            get => _GameFrame;
            set
            {
                _GameFrame = value;
                OnPropertyChanged("GameFrame");
            }
        }
        
        private int _InputOffset = -1;
        public int InputOffset
        {
            get => _InputOffset;
            set
            {
                _InputOffset = value;
                OnPropertyChanged("InputOffset");
            }
        }

        private string _CurrentState = "Ready";
        public string CurrentState
        {
            get => _CurrentState;
            set
            {
                _CurrentState = value;
                OnPropertyChanged("CurrentState");
            }
        }

        private string _StatusText = "";
        public string StatusText
        {
            get => _StatusText;
            set { _StatusText = value; OnPropertyChanged("StatusText"); }
        }

        private string _Notice = "";
        public string Notice
        {
            get => _Notice;
            set { _Notice = value; OnPropertyChanged("Notice"); }
        }

        private string _PauseText = "알 수 없음";
        public string PauseText
        {
            get => _PauseText;
            set { _PauseText = value; OnPropertyChanged("PauseText"); }
        }

        private string _OffsetText = "사용 안 함";
        public string OffsetText
        {
            get => _OffsetText;
            set { _OffsetText = value; OnPropertyChanged("OffsetText"); }
        }

        private string _FrameSourceText = "-";
        public string FrameSourceText
        {
            get => _FrameSourceText;
            set { _FrameSourceText = value; OnPropertyChanged("FrameSourceText"); }
        }

        private string _RecordName = "(없음)";
        public string RecordName
        {
            get => _RecordName;
            set { _RecordName = value; OnPropertyChanged("RecordName"); }
        }

        public string HotkeyHelp =>
            "R  녹화 시작/종료\n" +
            "E  녹화한 입력 재생/중단\n" +
            "Q  현재 녹화를 Records 폴더에 저장\n" +
            "W  선택된 파일 불러오기\n" +
            "F3/F4  이전/다음 파일 불러오기\n" +
            "F1/F2  전체 프레임 오프셋 -1/+1\n" +
            "F5  프레임 카운터 재탐색\n" +
            "Delete  녹화 비우기";
    }
}
