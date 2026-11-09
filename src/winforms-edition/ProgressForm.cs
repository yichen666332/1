using System;
using System.Windows.Forms;

namespace FilterVisualPlatform
{
    public partial class ProgressForm : Form
    {
        private readonly System.Windows.Forms.Timer _timer = new();
        private readonly DateTime _start = DateTime.UtcNow;

        public ProgressForm(string title = "运行中…")
        {
            InitializeComponent();

            this.Text = title;
            this.ControlBox = false; // 不允许关闭
            this.StartPosition = FormStartPosition.CenterParent;

            // 简单 UI：一个 Label + 进度条（可在 Designer 放置）
            if (label1 == null)
            {
                label1 = new Label
                {
                    Dock = DockStyle.Top,
                    Height = 28,
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter
                };
                Controls.Add(label1);
            }
            if (progressBar1 == null)
            {
                progressBar1 = new ProgressBar
                {
                    Dock = DockStyle.Top,
                    Style = ProgressBarStyle.Marquee,
                    MarqueeAnimationSpeed = 30
                };
                Controls.Add(progressBar1);
            }

            _timer.Interval = 200;
            _timer.Tick += (_, __) =>
            {
                var sec = (DateTime.UtcNow - _start).TotalSeconds;
                label1.Text = $"已用时间：{sec:F1}s";
            };
            _timer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            base.OnFormClosed(e);
        }

        // Designer 里放置的控件占位（若你已放置，下面字段由 Designer 生成）
        private Label label1;
        private ProgressBar progressBar1;
    }
}
