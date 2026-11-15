using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;

using ADOFAI_Auto.Core.Memory;
using ADOFAI_Auto.Core.Model;

namespace ADOFAI_Auto.Core.AutoPlay
{
    /// <summary>
    /// 녹화 없이 레벨 파일에서 계산한 시각표(AutoPlayPlan)대로 키를 누른다.
    /// KeyboardPlayer 와 같은 형태(IsPlaying / KeyEvent / Play / Stop)를 유지해 컨트롤러와 UI가 똑같이 다룬다.
    /// </summary>
    public class AutoPlayer : INotifyPropertyChanged
    {
        public event EventHandler<KeyboardLogInfo> KeyEvent;
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private volatile bool IsSuspend;
        private Thread RunThread;

        private bool _IsPlaying;
        public bool IsPlaying
        {
            get => _IsPlaying;
            private set { _IsPlaying = value; OnPropertyChanged("IsPlaying"); }
        }

        public bool LastPlayCompleted { get; private set; }

        /// <summary>plan 을 재생한다. speed = 속도 배율(스피드 트라이얼), offsetMs = 전체 보정(+ 늦게 / - 빠르게).</summary>
        public void Play(AutoPlayPlan plan, double speed, int offsetMs)
        {
            if (RunThread != null || IsPlaying || plan == null || !plan.IsPlayable)
                return;
            if (speed <= 0) speed = 1;

            IsSuspend = false;
            RunThread = new Thread(() =>
            {
                IsPlaying = true;
                LastPlayCompleted = false;
                try { NativeMethods.timeBeginPeriod(1); } catch { }

                long freq = Stopwatch.Frequency;
                long t0 = Stopwatch.GetTimestamp();
                bool completed = true;
                long prevPress = t0;

                for (int k = 0; k < plan.HitTimes.Count; k++)
                {
                    long target = t0 + (long)((plan.HitTimes[k] / speed + offsetMs / 1000.0) * freq);
                    if (!WaitUntil(target, freq)) { completed = false; break; }

                    KeyboardInput.PressKey();

                    long now = Stopwatch.GetTimestamp();
                    int delayMs = (int)((now - prevPress) * 1000 / freq);
                    int lateUs = (int)((now - target) * 1000000 / freq);
                    prevPress = now;
                    KeyEvent?.Invoke(null, new KeyboardLogInfo(MemoryReader.GetFrame(), -1, lateUs / 1000, delayMs, "Auto", "Down"));
                }

                try { NativeMethods.timeEndPeriod(1); } catch { }
                LastPlayCompleted = completed;
                IsSuspend = false;
                IsPlaying = false;
                RunThread = null;
            })
            {
                IsBackground = true,
                Priority = ThreadPriority.Highest,
                Name = "ADOFAI auto play (level)"
            };
            RunThread.Start();
        }

        public void Stop() => IsSuspend = true;

        /// <summary>멀리 있을 때는 Sleep(1), 목표 2.5ms 이내에서는 바쁜 대기로 정밀하게 기다린다.</summary>
        private bool WaitUntil(long target, long freq)
        {
            while (!IsSuspend)
            {
                long remain = target - Stopwatch.GetTimestamp();
                if (remain <= 0)
                    return true;
                if (remain * 1000.0 / freq > 2.5)
                    Thread.Sleep(1);
                else
                    Thread.SpinWait(30);
            }
            return false;
        }
    }
}
