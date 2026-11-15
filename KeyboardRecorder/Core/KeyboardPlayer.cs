using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using ADOFAI_Auto.Core.Model;

namespace ADOFAI_Auto.Core
{
    public class KeyboardPlayer : INotifyPropertyChanged
    {
        public event EventHandler<KeyboardLogInfo> KeyEvent;
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private volatile bool IsSuspendPlay;

        private bool _IsPlaying;
        public bool IsPlaying
        {
            get => _IsPlaying;
            private set
            {
                _IsPlaying = value;
                OnPropertyChanged("IsPlaying");
            }
        }

        private int ErrorFrame = 0;

        private int BeforeInputOffset = 0;
        private int GlobalOffset = 0;

        /// <summary>마지막 재생이 정상 종료(true)인지 중단(false)인지.</summary>
        public bool LastPlayCompleted { get; private set; }

        /// <summary>프레임 카운터가 이 시간(ms) 이상 멈추면 게임이 닫히거나 멈춘 것으로 보고 재생을 중단한다.</summary>
        public int StallTimeoutMs { get; set; } = 3000;

        private Thread RunThread;

        public void Play(KeyList keyboardLogs)
        {
            if (RunThread != null)
                return;

            RunThread = new Thread(() =>
            {
                if (keyboardLogs == null || IsPlaying)
                {
                    RunThread = null;
                    return;
                }

                IsPlaying = true;
                IsSuspendPlay = false;
                LastPlayCompleted = false;
                ErrorFrame = 0;

                BeforeInputOffset = MemoryReader.GetInputOffset();
                MemoryReader.SetInputOffset(keyboardLogs.InputOffset);
                GlobalOffset = keyboardLogs.GlobalOffset;

                // 절대 목표 프레임 방식: 오차가 누적되지 않는다.
                long startFrame = MemoryReader.ReadFrameNow();
                long target = startFrame;
                bool completed = true;

                for (int i = 0; i < keyboardLogs.Count; i++)
                {
                    if (IsSuspendPlay)
                    {
                        completed = false;
                        break;
                    }

                    int delay = keyboardLogs[i].FrameDelay + GlobalOffset;
                    target += Math.Max(0, delay);

                    if (!WaitUntilFrame(target))
                    {
                        completed = false;
                        break;
                    }

                    KeyboardInput.PressKey();
                    int now = MemoryReader.ReadFrameNow();
                    ErrorFrame = (int)(now - target);
                    KeyEvent?.Invoke(null, new KeyboardLogInfo(now, -1, ErrorFrame, delay, "Auto", "Down"));
                }

                // 중단/종료 시 사용자가 쓰던 입력 오프셋으로 복원
                MemoryReader.SetInputOffset(BeforeInputOffset);

                LastPlayCompleted = completed;
                IsSuspendPlay = false;
                IsPlaying = false;
                RunThread = null;
            })
            {
                IsBackground = true,
                Priority = ThreadPriority.Highest,
                Name = "ADOFAI auto play"
            };

            RunThread.Start();
        }

        public void Stop()
        {
            IsSuspendPlay = true;
        }

        /// <summary>
        /// 목표 프레임에 도달할 때까지 기다린다. 멀리 있을 때는 Sleep(1)로 CPU를 아끼고,
        /// 목표 직전 1프레임 구간부터는 메모리를 직접 읽으며 바쁜 대기를 해서 프레임이 바뀌는 즉시 입력한다.
        /// </summary>
        private bool WaitUntilFrame(long target)
        {
            int lastFrame = MemoryReader.ReadFrameNow();
            var stall = System.Diagnostics.Stopwatch.StartNew();

            while (!IsSuspendPlay)
            {
                int frame = MemoryReader.ReadFrameNow();
                if (frame >= target)
                    return true;

                if (frame != lastFrame)
                {
                    lastFrame = frame;
                    stall.Restart();
                }
                else if (stall.ElapsedMilliseconds > StallTimeoutMs)
                    return false;

                if (target - frame >= 2)
                    Thread.Sleep(1);
                else
                    Thread.SpinWait(50);
            }
            return false;
        }
    }
}
