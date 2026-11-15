using System;
using System.Collections.Generic;
using System.Linq;

namespace ADOFAI_Auto.Core.AutoPlay
{
    public sealed class AutoPlayPlan
    {
        /// <summary>시작 키를 누른 시점을 0으로 한 각 타일(1번 타일부터)의 히트 시각(초). 속도 배율 1.0 기준.</summary>
        public List<double> HitTimes = new List<double>();
        public List<string> Warnings = new List<string>();
        /// <summary>null 이 아니면 이 레벨은 자동 재생할 수 없다 (이유 문구).</summary>
        public string Unsupported;

        public int TileCount => HitTimes.Count;
        public double Duration => HitTimes.Count == 0 ? 0 : HitTimes[HitTimes.Count - 1];
        public bool IsPlayable => Unsupported == null && HitTimes.Count > 0;
    }

    /// <summary>
    /// 레벨 파일의 각도/BPM/이벤트로부터 "언제 키를 눌러야 하는지"를 계산한다.
    ///  - 1박 = 180° 회전, 시간 = 회전각/180 × 60/BPM
    ///  - 피벗 타일 f 에서 이동 행성은 (angleData[f-1]+180)° 위치에서 angleData[f]° 위치로 회전 (시계 방향 기준)
    ///  - Twirl 은 회전 방향 반전, SetSpeed 는 BPM 변경, Pause 는 해당 박수만큼 대기
    ///  - 시작 키를 누른 뒤 countdownTicks 박의 카운트다운이 지나고 첫 회전이 시작된다고 가정
    /// 미드스핀(999), Hold, 다중 행성 레벨은 아직 지원하지 않는다.
    /// </summary>
    public static class AutoPlayPlanner
    {
        public static AutoPlayPlan Plan(AdofaiLevel level)
        {
            var plan = new AutoPlayPlan();
            int n = level.Angles.Count;

            if (n == 0) { plan.Unsupported = "타일 데이터가 없습니다."; return plan; }
            if (level.Planets > 2) { plan.Unsupported = "다중 행성(3개 이상) 레벨은 지원하지 않습니다."; return plan; }
            if (level.Angles.Any(a => a >= 999)) { plan.Unsupported = "미드스핀 타일이 있는 레벨은 아직 지원하지 않습니다."; return plan; }
            if (level.Events.Any(e => e.Type == "Hold")) { plan.Unsupported = "홀드 타일이 있는 레벨은 아직 지원하지 않습니다."; return plan; }

            var byFloor = new Dictionary<int, List<LevelEvent>>();
            foreach (var e in level.Events)
            {
                List<LevelEvent> list;
                if (!byFloor.TryGetValue(e.Floor, out list))
                    byFloor[e.Floor] = list = new List<LevelEvent>();
                list.Add(e);
            }

            double pitch = level.Pitch <= 0 ? 1 : level.Pitch / 100.0;
            double bpm = level.Bpm * pitch;
            if (bpm <= 0) { plan.Unsupported = "BPM 값이 올바르지 않습니다."; return plan; }

            double time = level.CountdownTicks * 60.0 / bpm;
            bool clockwise = true;
            var warned = new HashSet<string>();

            for (int f = 0; f < n; f++)
            {
                List<LevelEvent> events;
                if (byFloor.TryGetValue(f, out events))
                {
                    foreach (var e in events)
                    {
                        switch (e.Type)
                        {
                            case "SetSpeed":
                                if (e.Text("speedType") == "Multiplier")
                                    bpm *= e.Number("bpmMultiplier", 1);
                                else
                                    bpm = e.Number("beatsPerMinute", bpm / pitch) * pitch;
                                break;
                            case "Twirl":
                                clockwise = !clockwise;
                                break;
                            case "Pause":
                                time += e.Number("duration", 0) * 60.0 / bpm;
                                break;
                            case "AutoPlayTiles":
                                if (warned.Add(e.Type))
                                    plan.Warnings.Add("AutoPlayTiles 이벤트가 있습니다. 해당 구간도 키를 누르므로 판정에 영향이 있을 수 있습니다.");
                                break;
                        }
                    }
                }

                if (bpm <= 0) { plan.Unsupported = $"{f}번 타일에서 BPM이 0 이하가 되었습니다."; plan.HitTimes.Clear(); return plan; }

                double from = f == 0 ? 180 : level.Angles[f - 1] + 180;
                double to = level.Angles[f];
                double cw = Mod(from - to, 360);
                if (cw < 1e-9) cw = 360;
                double travel = clockwise ? cw : 360 - cw;
                if (travel < 1e-9) travel = 360;

                time += travel / 180.0 * 60.0 / bpm;
                plan.HitTimes.Add(time);
            }
            return plan;
        }

        private static double Mod(double a, double m)
        {
            double r = a % m;
            return r < 0 ? r + m : r;
        }
    }
}
