using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FilterVisualPlatform
{
    /// <summary>
    /// 串口解码器
    /// </summary>
    public class SerialFrameParser
    {
        // ==== 协议常量 ====
        private const byte HEAD = 0xAA;
        private const byte TAIL = 0x55;
        private const int FRAME_SIZE = 19;   // AA + 16数据 + XOR + 55
        private const int PAYLOAD_LEN = 16;  // 4 * float

        // ==== 环形缓冲（持久化） ====
        private readonly byte[] _buf;
        private int _head = 0;   // 写入位置（下一写入索引）
        private int _tail = 0;   // 读取位置（下一读取索引）
        private int _len = 0;   // 当前有效字节数


        public SerialFrameParser(int bufferSize = 1024)
        {
            if (bufferSize < 4 * FRAME_SIZE) bufferSize = 4 * FRAME_SIZE;
            _buf = new byte[bufferSize];
        }

        /// <summary>
        /// 每解出一帧触发：raw, filtered, tofRaw, tofFiltered
        /// 注意：在串口回调线程触发，若更新 UI 请自行 BeginInvoke。
        /// </summary>
        public event Action<float, float, float, float> FrameDecoded;

        /// <summary>
        /// 追加一批新到的字节（可多可少、允许半包）。
        /// </summary>
        public void Append(byte[] data, int offset = 0, int count = -1)
        {
            if (data == null || data.Length == 0) return;
            if (count < 0) count = data.Length - offset;

            // 简单策略：若容量不够，丢弃最旧字节（保持最近数据）
            for (int i = 0; i < count; i++)
            {
                if (_len == _buf.Length)
                {
                    // 覆盖最旧的数据
                    _tail = (_tail + 1) % _buf.Length;
                    _len--;
                }
                _buf[_head] = data[offset + i];
                _head = (_head + 1) % _buf.Length;
                _len++;
            }

            // 尝试尽量多地解析
            TryParseFrames();
        }

        private void TryParseFrames()
        {
            // 只要剩余字节 >= 一帧，就尝试抽取
            while (_len >= FRAME_SIZE)
            {
                int idx = _tail;

                // 对齐到帧头 0xAA
                if (_buf[idx] != HEAD)
                {
                    // 不是帧头就丢 1 字节，继续找帧头
                    Consume(1);
                    continue;
                }

                // 帧头对齐了，但可能不满一帧（理论上这里 _len>=FRAME_SIZE 已保证足够）
                // 取出 19 字节临时帧
                byte[] frame = new byte[FRAME_SIZE];
                for (int i = 0; i < FRAME_SIZE; i++)
                {
                    frame[i] = _buf[(idx + i) % _buf.Length];
                }

                // 快速尾字节检查
                if (frame[FRAME_SIZE - 1] != TAIL)
                {
                    // 尾不对：丢弃这个头，向后错位 1 字节再继续
                    Consume(1);
                    continue;
                }

                // 校验：xor 前 17 字节（0..16）应等于 frame[17]
                byte xor = 0;
                for (int i = 0; i < 17; i++) xor ^= frame[i];
                if (xor != frame[17])
                {
                    // 校验失败：同样错位 1 字节继续同步
                    Consume(1);
                    continue;
                }

                // 解码 4 个 float（小端）
                float raw = BitConverter.ToSingle(frame, 1);
                float filtered = BitConverter.ToSingle(frame, 5);
                float tofRaw = BitConverter.ToSingle(frame, 9);
                float tofFilt = BitConverter.ToSingle(frame, 13);

                // 向外回调
                FrameDecoded?.Invoke(raw, filtered, tofRaw, tofFilt);

                // 正常消耗整帧
                Consume(FRAME_SIZE);
            }
        }

        private void Consume(int n)
        {
            _tail = (_tail + n) % _buf.Length;
            _len -= n;
            if (_len < 0) _len = 0; // 保护
        }
    }
}
