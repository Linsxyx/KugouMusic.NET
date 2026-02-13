using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace TestMusic.Converters
{
    public class SecondsToMinutesSecondsConverter : IValueConverter
    {
        // 将double秒数转换为"分:秒"格式
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 检查值是否为double类型
            if (value is double seconds)
            {
                // 处理负数、NaN、无穷大等情况
                if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                    return "00:00";
                
                // 转换为整数秒（向下取整）
                int totalSeconds = (int)Math.Floor(seconds);
                
                // 计算分钟和秒
                int minutes = totalSeconds / 60;
                int secs = totalSeconds % 60;
                
                // 格式化为两位数
                return $"{minutes:D2}:{secs:D2}";
            }
            
            return "00:00";
        }

        // 不需要反向转换，直接抛出异常
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}