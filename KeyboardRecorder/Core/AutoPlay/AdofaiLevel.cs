using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ADOFAI_Auto.Core.AutoPlay
{
    public sealed class LevelEvent
    {
        public int Floor;
        public string Type;
        public Dictionary<string, object> Data;

        public double Number(string key, double fallback)
        {
            object v;
            if (Data != null && Data.TryGetValue(key, out v) && v is double)
                return (double)v;
            return fallback;
        }

        public string Text(string key)
        {
            object v;
            return Data != null && Data.TryGetValue(key, out v) ? v as string : null;
        }
    }

    /// <summary>.adofai 레벨 파일에서 타이밍 계산에 필요한 정보(시작 BPM, 피치, 카운트다운, 각도 데이터, 이벤트)만 읽는다.</summary>
    public sealed class AdofaiLevel
    {
        public string Path;
        public string Name;
        public double Bpm = 100;
        public double Pitch = 100;
        public int CountdownTicks = 4;
        public int Planets = 2;

        /// <summary>angleData[i] = i번 타일에서 i+1번 타일로 가는 방향(도). 999 = 미드스핀.</summary>
        public List<double> Angles = new List<double>();
        public List<LevelEvent> Events = new List<LevelEvent>();

        // 구버전 pathData 문자 → 각도
        private const string PathChars = "RpJEToUqGQHWLxNZFVDYBCMA";

        public static AdofaiLevel Load(string path)
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            var root = MiniJson.Parse(text) as Dictionary<string, object>;
            if (root == null)
                throw new FormatException("레벨 파일 형식이 올바르지 않습니다.");

            var level = new AdofaiLevel
            {
                Path = path,
                Name = System.IO.Path.GetFileNameWithoutExtension(path)
            };

            var settings = Get(root, "settings") as Dictionary<string, object>;
            if (settings != null)
            {
                level.Bpm = Num(settings, "bpm", 100);
                level.Pitch = Num(settings, "pitch", 100);
                level.CountdownTicks = (int)Num(settings, "countdownTicks", 4);
                level.Planets = (int)Num(settings, "planets", 2);
                string song = Get(settings, "song") as string;
                if (!string.IsNullOrWhiteSpace(song)) level.Name = song;
            }

            var angles = Get(root, "angleData") as List<object>;
            if (angles != null)
            {
                foreach (object o in angles)
                    level.Angles.Add(o is double ? (double)o : 0);
            }
            else
            {
                string pathData = Get(root, "pathData") as string;
                if (pathData == null)
                    throw new FormatException("angleData/pathData 를 찾을 수 없습니다.");
                foreach (char c in pathData)
                {
                    if (c == '!') { level.Angles.Add(999); continue; }
                    int idx = PathChars.IndexOf(c);
                    if (idx < 0)
                        throw new FormatException($"지원하지 않는 pathData 문자: '{c}'");
                    level.Angles.Add(idx * 15);
                }
            }

            var actions = Get(root, "actions") as List<object>;
            if (actions != null)
            {
                foreach (object o in actions)
                {
                    var d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    level.Events.Add(new LevelEvent
                    {
                        Floor = (int)Num(d, "floor", 0),
                        Type = Get(d, "eventType") as string,
                        Data = d
                    });
                }
            }
            return level;
        }

        private static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) ? v : null;
        }

        private static double Num(Dictionary<string, object> d, string key, double fallback)
        {
            object v = Get(d, key);
            return v is double ? (double)v : fallback;
        }
    }
}
