using System.Data.SqlTypes;
using System.IO.Ports;

namespace FilterVisualPlatform
{
    public partial class Form1 : Form
    {

        private OptimizedSerialPortHandler _serialCom = new OptimizedSerialPortHandler();

        private UserComm userComm = null;
        private UserDatas userDatas = null;
        private UserCurv userCurv = null;

        private TunerControl _tunerForm;         // 单实例引用

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            userComm = new UserComm(_serialCom);
            userDatas = new UserDatas(userComm);
            userCurv = new UserCurv(userDatas, userComm);

            userComm.Dock = DockStyle.Fill;
            panel_display.Controls.Add(userComm);

            userDatas.Dock = DockStyle.Fill;
            panel_display.Controls.Add(userDatas);

            userCurv.Dock = DockStyle.Fill;
            panel_display.Controls.Add(userCurv);

            userComm.BringToFront();

            timer1.Start();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            if (_serialCom != null)
            {
                if (_serialCom.IsConnected)
                {
                    lb_ConnectStatus.Text = "已连接";
                    lb_ConnectStatus.ForeColor = Color.Green;

                    // 数据量统计（字节数）
                    long totalBytes = userComm.ByteRate.GetTotal();
                    double bytesPerSec = userComm.ByteRate.GetRatePerSecond(1000); // 最近1秒的平均值
                    lb_Count.Text = totalBytes.ToString("F1");

                    // 频率统计（Hz = 帧数/秒）
                    long totalFrames = userComm.FrameRate.GetTotal();
                    double fps = userComm.FrameRate.GetRatePerSecond(1000); // 最近1秒的平均频率
                    lb_Freq.Text = fps.ToString("F1");

                }
                else
                {
                    lb_ConnectStatus.Text = "未连接";
                    lb_ConnectStatus.ForeColor = Color.Red;
                }
            }
            else
            {
                lb_ConnectStatus.Text = "未连接";
                lb_ConnectStatus.ForeColor = Color.Red;
            }
        }

        private void btn_fifter_Click(object sender, EventArgs e)
        {
            userDatas.BringToFront();
        }

        private void btn_Curv_Click(object sender, EventArgs e)
        {
            userCurv.BringToFront();
        }

        private void btn_userCom_Click(object sender, EventArgs e)
        {
            userComm.BringToFront();
        }

        private void btn_TunerControl_Click(object sender, EventArgs e)
        {
            OpenTunerWindow();
        }

        private void OpenTunerWindow()
        {
            if (_tunerForm == null || _tunerForm.IsDisposed)
            {
                _tunerForm = new TunerControl(userDatas)
                {
                    StartPosition = FormStartPosition.CenterParent, // 可选：居中
                    TopMost = false                                  // 可选：置顶
                };

                // 窗体关闭时把引用清空，方便下次再开
                _tunerForm.FormClosed += (_, __) => _tunerForm = null;

                // 非模态显示，不阻塞主页面
                _tunerForm.Show(this); // 传 owner 可选
            }
            else
            {
                // 已经开过了，就把它激活到前台
                if (_tunerForm.WindowState == FormWindowState.Minimized)
                    _tunerForm.WindowState = FormWindowState.Normal;

                _tunerForm.BringToFront();
                _tunerForm.Activate();
            }
        }

        private void button5_Click(object sender, EventArgs e)
        {

        }
    }
}
