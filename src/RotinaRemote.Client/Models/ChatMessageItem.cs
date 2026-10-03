using System;
using System.Windows;
using System.Windows.Media;

namespace RotinaRemote.Client.Models
{
    public class ChatMessageItem
    {
        public string SenderId { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public bool IsOutgoing { get; set; }

        public string TimeFormatted => Timestamp.ToLocalTime().ToString("HH:mm");

        public HorizontalAlignment HorizontalAlignment => IsOutgoing ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        public Brush BubbleBackground => IsOutgoing
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB")) // Accent Blue
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")); // Slate 700

        public Brush HeaderColor => IsOutgoing
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BFDBFE")) // Light Blue
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A5B4FC")); // Indigo 300
    }
}
