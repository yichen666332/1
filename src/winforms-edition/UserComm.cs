using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FilterVisualPlatform
{
    public partial class UserComm : UserControl
    {

        private readonly SerialPort _port = new SerialPort();
        private OptimizedSerialPortHandler _serialCom;

        //解码器
        private readonly SerialFrameParser _parser = new SerialFrameParser(2048);

        //计数器
        public readonly SlidingMsRateCounter ByteRate = new SlidingMsRateCounter(windowMs: 5000, bucketMs: 100); // 近5秒字节速率
        public readonly SlidingMsRateCounter FrameRate = new SlidingMsRateCounter(windowMs: 5000, bucketMs: 100); // 近5秒帧率

        private float raw;
        private float filtered;
        private float tofRaw;
        private float tofFiltered;

        public float Raw { get => raw; set => raw = value; }
        public float Filtered { get => filtered; set => filtered = value; }
        public float TofRaw { get => tofRaw; set => tofRaw = value; }
        public float TofFiltered { get => tofFiltered; set => tofFiltered = value; }

        public UserComm(OptimizedSerialPortHandler serialCom)
        {
            InitializeComponent();

            _serialCom = serialCom;

            // 订阅串口原始数据
            _serialCom.DataReceived += bytes =>
            {
                ByteRate.Add(bytes.Length);   // 统计字节量
                // 把零散字节喂给解析器（注意：不是 UI 线程）
                _parser.Append(bytes);
            };

            // 订阅解析后的帧
            _parser.FrameDecoded += (raw, filtered, tofRaw, tofFiltered) =>
            {
                FrameRate.Add(1); // 统计帧数

                Raw = raw;
                Filtered = filtered;
                TofRaw = tofRaw;
                TofFiltered = tofFiltered;
            };
        }

        private void UserComm_Load(object sender, EventArgs e)
        {
            var ports = SerialPort.GetPortNames();


            cb_comPort.Items.Clear();
            cb_comPort.Items.AddRange(ports);

            cb_comPort.SelectedIndex = 0;
            cb_baud.SelectedIndex = 1;
        }

        private async void btn_Connect_Click(object sender, EventArgs e)
        {

            if (_serialCom.IsConnected)
            {
                _serialCom.Disconnect();
                btn_Connect.Text = "连接";
                btn_Connect.BackColor = Color.White;
                return;
            }
            else
            {
                var cfg = new SerialComConfig
                {
                    PortName = cb_comPort.Text,
                    BaudRate = int.Parse(cb_baud.Text),
                    Parity = System.IO.Ports.Parity.None,
                    DataBits = 8,
                    StopBits = System.IO.Ports.StopBits.One,
                    ReadBufferSize = 4096,
                    WriteBufferSize = 2048,
                    ReadTimeout = 500,
                    WriteTimeout = 500
                };

                if (await _serialCom.ConnectAsync(cfg))
                {
                    btn_Connect.Text = "已连接";
                    btn_Connect.BackColor = Color.LightGreen;
                }
            }

        }
    }
}
