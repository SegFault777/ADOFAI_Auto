using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using ADOFAI_Auto.Core.Model;

namespace ADOFAI_Auto.Core
{
    /// <summary>Records 폴더 안의 .apmd 녹화 파일들을 관리한다 (저장 시 시각 이름으로 자동 보관, 이전/다음 선택).</summary>
    public static class RecordLibrary
    {
        public static string Folder => Path.Combine(AppConfig.BaseDirectory, "Records");

        private static string _Current;

        public static string CurrentName => _Current == null ? "(없음)" : Path.GetFileName(_Current);
        public static string CurrentPath => _Current;

        public static List<string> Files()
        {
            try
            {
                if (!Directory.Exists(Folder))
                    return new List<string>();
                return Directory.GetFiles(Folder, "*" + KeyboardLogIOManager.MAPDATA_FILE_EXTENSION)
                                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>현재 녹화를 새 파일로 저장하고 선택한다. 실패하면 null.</summary>
        public static string SaveNew(KeyList keys)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder, DateTime.Now.ToString("yyyyMMdd_HHmmss") + KeyboardLogIOManager.MAPDATA_FILE_EXTENSION);
                if (!KeyboardLogIOManager.Save(path, keys))
                    return null;
                _Current = path;
                return path;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>delta(+1/-1)만큼 선택을 옮긴다. 목록이 비어 있으면 null.</summary>
        public static string Move(int delta)
        {
            var files = Files();
            if (files.Count == 0)
                return _Current = null;

            int index = _Current == null ? -1 : files.FindIndex(f => string.Equals(f, _Current, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                index = delta > 0 ? -1 : files.Count;
            index = ((index + delta) % files.Count + files.Count) % files.Count;
            return _Current = files[index];
        }

        /// <summary>선택된 파일이 없으면 가장 최근 파일을 선택한다.</summary>
        public static string EnsureSelected()
        {
            if (_Current != null && File.Exists(_Current))
                return _Current;
            var files = Files();
            return _Current = files.Count == 0 ? null : files[files.Count - 1];
        }
    }
}
