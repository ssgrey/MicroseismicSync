using System;
using System.Windows;

namespace MicroseismicSync
{
    public partial class MonitorSettingsWindow : Window
    {
        public MonitorSettingsWindow(int currentIntervalSeconds)
        {
            InitializeComponent();
            IntervalTextBox.Text = currentIntervalSeconds.ToString();
        }

        public int IntervalSeconds { get; private set; }

        private void OnOkButtonClick(object sender, RoutedEventArgs e)
        {
            int intervalSeconds;
            if (!int.TryParse(IntervalTextBox.Text, out intervalSeconds) || intervalSeconds <= 0)
            {
                MessageBox.Show(this, "请输入大于 0 的整数秒数。", "监控设置", MessageBoxButton.OK, MessageBoxImage.Warning);
                IntervalTextBox.Focus();
                IntervalTextBox.SelectAll();
                return;
            }

            IntervalSeconds = intervalSeconds;
            DialogResult = true;
        }
    }
}
