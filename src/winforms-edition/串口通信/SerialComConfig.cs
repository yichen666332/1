using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FilterVisualPlatform
{
    public class SerialComConfig
    {
        /// <summary>
        /// 串口号
        /// </summary>
        public string PortName { get; set; }
        /// <summary>
        /// 波特率
        /// </summary>
        public int BaudRate { get; set; }
        /// <summary>
        /// 校验位
        /// </summary>
        public Parity Parity { get; set; }
        /// <summary>
        /// 数据位
        /// </summary>
        public int DataBits { get; set; }
        /// <summary>
        /// 停止位
        /// </summary>
        public StopBits StopBits { get; set; }
        /// <summary>
        /// 读取缓冲区大小
        /// </summary>
        public int ReadBufferSize { get; set; } = 4096;
        /// <summary>
        /// 写入缓冲区大小
        /// </summary>
        public int WriteBufferSize { get; set; } = 4096;
        /// <summary>
        /// 读取超时时间，单位毫秒
        /// </summary>
        public int ReadTimeout { get; set; } = 500;
        /// <summary>
        /// 写入超时时间，单位毫秒 
        /// </summary>
        public int WriteTimeout { get; set; } = 500;
    }
}
