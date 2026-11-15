using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace ADOFAI_Auto.Core.Memory
{
    public sealed class FrameCounterCandidate
    {
        public long Address;
        public bool IsImage;
        public double Rate;          // 초당 증가량(≈FPS)
        public int Changes;          // 정밀 샘플링 중 값이 바뀐 횟수
        public double OneStepRatio;  // 변화량이 정확히 +1 이었던 비율 (프레임 카운터는 ≈1.0)
        public override string ToString() => $"0x{Address:X} rate={Rate:F1}/s oneStep={OneStepRatio:P0} image={IsImage}";
    }

    /// <summary>
    /// 게임 버전/포인터 체인과 무관하게 "프레임 카운터"를 런타임에 찾아낸다.
    /// 치트엔진의 "증가한 값 반복 스캔"을 자동화한 방식:
    ///   1) 짧은 간격으로 두 번 읽어 증가한 int 후보 수집
    ///   2) 여러 라운드 동안 계속 증가하는지 검증
    ///   3) 고속 샘플링으로 "변할 때마다 정확히 +1" (프레임 카운터) vs "ms 타이머(+수~수십)"를 구분
    /// </summary>
    public sealed class FrameCounterFinder
    {
        public FrameCounterFinder(IMemorySource memory, Action<string> log = null)
        {
            Memory = memory;
            Log = log ?? (_ => { });
        }

        private IMemorySource Memory { get; }
        private Action<string> Log { get; }

        public int ChunkSize = 16 * 1024 * 1024;
        public int FirstGapMs = 40;
        public int MaxFirstStep = 100;
        public int MaxCandidates = 3000000;
        public int Rounds = 6;
        public double MaxRate = 2000;
        public double MinFps = 20;
        public double MaxFps = 600;
        public int SampleMs = 500;
        public int MaxSampled = 300;

        private struct Cand
        {
            public long Addr;
            public int Value;
            public bool Image;
            public double RateSum;
            public double RateSqSum;
            public int N;
        }

        public List<FrameCounterCandidate> Find(CancellationToken cancel = default(CancellationToken))
        {
            var cands = CollectFirstPass(cancel);
            Log($"1차 후보 {cands.Count}개");
            if (cands.Count == 0)
                return new List<FrameCounterCandidate>();

            cands = Refine(cands, cancel);
            Log($"검증 후 후보 {cands.Count}개");
            if (cands.Count == 0)
                return new List<FrameCounterCandidate>();

            var result = Sample(cands, cancel);
            Log($"프레임 카운터 후보 {result.Count}개");
            return result;
        }

        private List<Cand> CollectFirstPass(CancellationToken cancel)
        {
            var cands = new List<Cand>();
            var a = new byte[ChunkSize];
            var b = new byte[ChunkSize];
            var ia = new int[ChunkSize / 4];
            var ib = new int[ChunkSize / 4];

            foreach (var region in Memory.EnumerateRegions())
            {
                for (long off = 0; off < region.Size; off += ChunkSize)
                {
                    if (cancel.IsCancellationRequested || cands.Count >= MaxCandidates)
                        return cands;

                    int len = (int)Math.Min(ChunkSize, region.Size - off) & ~3;
                    if (len < 4) continue;
                    long start = region.Base + off;

                    if (!Memory.Read(start, a, len)) continue;
                    Thread.Sleep(FirstGapMs);
                    if (!Memory.Read(start, b, len)) continue;

                    Buffer.BlockCopy(a, 0, ia, 0, len);
                    Buffer.BlockCopy(b, 0, ib, 0, len);
                    int n = len / 4;
                    for (int i = 0; i < n; i++)
                    {
                        int x = ia[i];
                        int d = ib[i] - x;
                        if (x >= 0 && d > 0 && d <= MaxFirstStep)
                            cands.Add(new Cand { Addr = start + (long)i * 4, Value = ib[i], Image = region.IsImage });
                    }
                }
            }
            return cands;
        }

        private List<Cand> Refine(List<Cand> cands, CancellationToken cancel)
        {
            var rnd = new Random();
            var sw = Stopwatch.StartNew();
            double prevT = sw.Elapsed.TotalSeconds;
            var page = new byte[4096];

            for (int round = 0; round < Rounds && cands.Count > 0; round++)
            {
                if (cancel.IsCancellationRequested) return new List<Cand>();
                Thread.Sleep(rnd.Next(80, 220));

                double now = sw.Elapsed.TotalSeconds;
                double dt = now - prevT;
                prevT = now;

                var next = new List<Cand>(cands.Count / 2 + 1);
                long pageBase = -1;
                bool pageOk = false;
                foreach (var c in cands)
                {
                    long pb = c.Addr & ~4095L;
                    if (pb != pageBase)
                    {
                        pageBase = pb;
                        pageOk = Memory.Read(pb, page, 4096);
                    }
                    if (!pageOk) continue;

                    int v = BitConverter.ToInt32(page, (int)(c.Addr - pb));
                    int d = v - c.Value;
                    if (d <= 0 || d > dt * MaxRate * 1.5 + 2) continue;

                    var n = c;
                    n.Value = v;
                    double r = d / dt;
                    n.RateSum += r;
                    n.RateSqSum += r * r;
                    n.N++;
                    next.Add(n);
                }
                cands = next;
                Log($"라운드 {round + 1}/{Rounds}: {cands.Count}개 남음");
            }
            return cands;
        }

        private List<FrameCounterCandidate> Sample(List<Cand> cands, CancellationToken cancel)
        {
            // 라운드 간 증가 속도가 가장 일정한 후보부터 정밀 샘플링
            cands.Sort((x, y) => Cv(x).CompareTo(Cv(y)));
            int count = Math.Min(MaxSampled, cands.Count);

            var buf = new byte[4];
            var last = new int[count];
            var changes = new int[count];
            var ones = new int[count];
            for (int i = 0; i < count; i++)
            {
                Memory.Read(cands[i].Addr, buf, 4);
                last[i] = BitConverter.ToInt32(buf, 0);
            }

            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < SampleMs && !cancel.IsCancellationRequested)
            {
                for (int i = 0; i < count; i++)
                {
                    if (!Memory.Read(cands[i].Addr, buf, 4)) continue;
                    int v = BitConverter.ToInt32(buf, 0);
                    if (v != last[i])
                    {
                        changes[i]++;
                        if (v - last[i] == 1) ones[i]++;
                        last[i] = v;
                    }
                }
                Thread.Yield();
            }
            double secs = sw.Elapsed.TotalSeconds;

            var result = new List<FrameCounterCandidate>();
            for (int i = 0; i < count; i++)
            {
                double fps = changes[i] / secs;
                double ratio = changes[i] == 0 ? 0 : (double)ones[i] / changes[i];
                if (changes[i] >= 5 && ratio >= 0.9 && fps >= MinFps && fps <= MaxFps)
                    result.Add(new FrameCounterCandidate
                    {
                        Address = cands[i].Addr,
                        IsImage = cands[i].Image,
                        Rate = fps,
                        Changes = changes[i],
                        OneStepRatio = ratio
                    });
            }

            // 정적 영역(이미지) 우선 → +1 비율 → 변화 횟수
            result.Sort((x, y) =>
            {
                int c = y.IsImage.CompareTo(x.IsImage);
                if (c != 0) return c;
                c = y.OneStepRatio.CompareTo(x.OneStepRatio);
                if (c != 0) return c;
                return y.Changes.CompareTo(x.Changes);
            });
            return result;
        }

        private static double Cv(Cand c)
        {
            if (c.N == 0) return double.MaxValue;
            double mean = c.RateSum / c.N;
            double var = Math.Max(0, c.RateSqSum / c.N - mean * mean);
            return mean <= 0 ? double.MaxValue : Math.Sqrt(var) / mean;
        }

        /// <summary>저장해 둔 주소가 여전히 프레임 카운터처럼 동작하는지 검사.</summary>
        public static bool Validate(IMemorySource memory, long address, int sampleMs = 400)
        {
            var buf = new byte[4];
            if (!memory.Read(address, buf, 4)) return false;
            int last = BitConverter.ToInt32(buf, 0);
            int changes = 0, ones = 0;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < sampleMs)
            {
                if (!memory.Read(address, buf, 4)) return false;
                int v = BitConverter.ToInt32(buf, 0);
                if (v != last)
                {
                    changes++;
                    if (v - last == 1) ones++;
                    last = v;
                }
                Thread.Yield();
            }
            double fps = changes / sw.Elapsed.TotalSeconds;
            return changes >= 3 && (double)ones / changes >= 0.9 && fps >= 15 && fps <= 600;
        }
    }
}
