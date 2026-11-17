using System.Windows;
using System.Windows.Media;

namespace DesktopPet.Ui
{
    /// <summary>
    /// 自己画的那两个控件（折线、时序条）要用颜色，但颜色的正本在 BrainTheme.xaml 里。
    /// 这里不另外定义一套，而是按资源名去取，取不到才退回硬编码的同一个值 ——
    /// 退路只为了 VS 的设计器预览，那时候还没有 Application.Current。
    /// </summary>
    internal static class BrainBrushes
    {
        private static Brush? _ok;
        private static Brush? _warn;
        private static Brush? _crit;
        private static Brush? _hair;
        private static Brush? _soft;
        private static Brush? _ink;
        private static Brush? _noData;

        public static Brush Ok
        {
            get
            {
                if (_ok == null)
                {
                    _ok = Find("OkBrush", "#2E7D5B");
                }
                return _ok;
            }
        }

        public static Brush Warn
        {
            get
            {
                if (_warn == null)
                {
                    _warn = Find("WarnBrush", "#B07211");
                }
                return _warn;
            }
        }

        public static Brush Ink
        {
            get
            {
                if (_ink == null)
                {
                    _ink = Find("InkBrush", "#14161A");
                }
                return _ink;
            }
        }

        public static Brush Crit
        {
            get
            {
                if (_crit == null)
                {
                    _crit = Find("CritBrush", "#B3382C");
                }
                return _crit;
            }
        }

        public static Brush Hair
        {
            get
            {
                if (_hair == null)
                {
                    _hair = Find("HairBrush", "#E4E7EA");
                }
                return _hair;
            }
        }

        public static Brush Soft
        {
            get
            {
                if (_soft == null)
                {
                    _soft = Find("SoftBrush", "#9AA0A6");
                }
                return _soft;
            }
        }

        public static Brush NoData
        {
            get
            {
                if (_noData == null)
                {
                    _noData = Find("NoDataBrush", "#D7DBDF");
                }
                return _noData;
            }
        }

        private static Brush Find(string key, string fallback)
        {
            if (Application.Current != null)
            {
                Brush? found = Application.Current.TryFindResource(key) as Brush;
                if (found != null)
                {
                    return found;
                }
            }
            SolidColorBrush b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
            b.Freeze();
            return b;
        }
    }
}
