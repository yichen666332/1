using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FilterVisualPlatform
{
    /// <summary>
    /// 线程安全的串口通讯封装：
    /// - 提供 ConnectAsync/Disconnect 进行连接管理；
    /// - 提供同步/异步发送；
    /// - 通过事件 DataReceived, ErrorOccurred 向外部通知数据与错误；
    /// - 使用 lock 保证 SerialPort 访问的线程安全；
    /// - 使用 CancellationTokenSource 用于后续扩展（如读写循环、任务取消）。
    /// 说明：
    /// 1) SerialPort 的 DataReceived 事件在线程池线程触发；如需更新 UI，请在外部自行 Marshal 到 UI 线程（BeginInvoke/Invoke）。
    /// 2) ReadDataAsync 会在触发时将当前缓冲区可读的数据一次性读出并返回；若你的协议有分包/粘包，请在 DataReceived 事件的订阅方自行做帧拼装。
    /// 3) SendDataAsync 内部的取消仅能取消 Task.Run 的等待，不会中断底层 Write（其为同步 I/O）；如需可取消写入，需改造为串口队列线程模型。
    /// </summary>
    public class OptimizedSerialPortHandler : IDisposable
    {
        // ============================
        // 字段（私有状态）
        // ============================

        private SerialPort _serialPort;                           // 实际串口对象
        private CancellationTokenSource _cancellationTokenSource; // 预留的取消源（当前用于 Disconnect/Dispose，便于未来扩展）
        private byte[] _receiveBuffer;                            // 读取缓冲区（大小来自配置）
        private bool _disposed = false;                           // 是否已释放
        private bool _isConnected = false;                        // 用户层面的“连接状态”标志（与 SerialPort.IsOpen 共同决定 IsConnected）
        private readonly object _lockObject = new object();       // 访问 SerialPort 的互斥锁

        // ============================
        // 事件（对外通知）
        // ============================

        /// <summary>
        /// 当收到一批原始字节时触发。注意：该事件在**非 UI 线程**触发，
        /// 如需更新 UI，请在外部使用 Control.BeginInvoke/Dispatcher 等切回 UI 线程。
        /// </summary>
        public event Action<byte[]> DataReceived;

        /// <summary>
        /// 当发生错误（连接失败、发送失败、读取失败、串口错误事件等）时触发。
        /// 注意：该事件同样在**非 UI 线程**触发。
        /// </summary>
        public event Action<string> ErrorOccurred;

        // ============================
        // 属性
        // ============================

        /// <summary>
        /// 是否处于“已连接并且串口已打开”的状态。
        /// 双条件：内部标志位 _isConnected == true 且 _serialPort?.IsOpen == true。
        /// </summary>
        public bool IsConnected => _isConnected && (_serialPort?.IsOpen ?? false);

        /// <summary>
        /// 异步连接串口。
        /// - 若已存在打开的串口，先关闭；
        /// - 根据 <paramref name="config"/> 构造 SerialPort；
        /// - 订阅 DataReceived/ErrorReceived 事件；
        /// - 使用 Task.Run 打开串口，避免阻塞 UI 线程；
        /// - 连接成功后创建新的 CancellationTokenSource 并设置连接标志。
        /// </summary>
        /// <param name="config">串口配置（端口名、波特率、校验位、数据位、停止位、缓冲区大小、超时等）。</param>
        /// <returns>连接是否成功。</returns>
        public async Task<bool> ConnectAsync(SerialComConfig config)
        {
            try
            {
                // 加锁保护：避免并发修改 _serialPort
                lock (_lockObject)
                {
                    // 若已打开，先关闭并重置状态
                    if (_serialPort?.IsOpen == true)
                    {
                        _serialPort.Close();
                        _isConnected = false;
                    }

                    // 基于配置创建新的 SerialPort 实例
                    _serialPort = new SerialPort(config.PortName, config.BaudRate, config.Parity, config.DataBits, config.StopBits)
                    {
                        ReadBufferSize = config.ReadBufferSize,   // 读取缓冲区大小
                        WriteBufferSize = config.WriteBufferSize, // 写入缓冲区大小
                        ReadTimeout = config.ReadTimeout,      // 读取超时（同步 API 使用）
                        WriteTimeout = config.WriteTimeout,     // 写入超时（同步 API 使用）
                        Handshake = Handshake.None           // 不使用硬件流控（如需 RTS/CTS 请在这里调整）
                    };

                    // 初始化一次读取用的临时缓冲
                    _receiveBuffer = new byte[config.ReadBufferSize];

                    // 订阅串口事件
                    _serialPort.DataReceived += SerialPort_DataReceived;
                    _serialPort.ErrorReceived += SerialPort_ErrorReceived;
                }

                // 打开串口：放到后台线程，避免阻塞（SerialPort.Open 可能耗时）
                await Task.Run(() => _serialPort.Open());

                // 准备取消源（目前未对读写进行循环，但保留用于未来扩展）
                _cancellationTokenSource = new CancellationTokenSource();

                // 连接成功标记
                _isConnected = true;

                return true;
            }
            catch (Exception ex)
            {
                // 连接失败，确保状态为 false 并通知错误
                _isConnected = false;
                ErrorOccurred?.Invoke($"连接失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 断开串口连接。
        /// - 先将状态置为 false；
        /// - 取消可能存在的后台任务（为未来扩展保留）；
        /// - 取消事件订阅并关闭串口；
        /// - 无论是否异常，最终保证状态为 false。
        /// </summary>
        public void Disconnect()
        {
            try
            {
                // 立即置为未连接，阻止后续发送
                _isConnected = false;

                // 尝试取消后台操作（若未来有读写循环或其他任务，可生效）
                _cancellationTokenSource?.Cancel();

                // 关闭串口前需先解除事件订阅，避免关闭过程中再触发回调
                lock (_lockObject)
                {
                    if (_serialPort?.IsOpen == true)
                    {
                        _serialPort.DataReceived -= SerialPort_DataReceived;
                        _serialPort.ErrorReceived -= SerialPort_ErrorReceived;
                        _serialPort.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"断开连接时出错: {ex.Message}");
            }
            finally
            {
                // 双保险，确保标志为 false
                _isConnected = false;
            }
        }

        // ============================
        // 发送（同步 / 异步）
        // ============================

        /// <summary>
        /// 同步发送数据（调用线程将被阻塞直到写入完成或抛出异常）。
        /// 建议在非 UI 线程调用，或数据量较小的场景使用。
        /// </summary>
        /// <param name="data">待发送的字节数组。</param>
        public void SendData(byte[] data)
        {
            if (!IsConnected) return;

            try
            {
                lock (_lockObject)
                {
                    if (_serialPort.IsOpen)
                    {
                        _serialPort.Write(data, 0, data.Length);
                    }
                }
            }
            catch (TimeoutException ex)
            {
                ErrorOccurred?.Invoke($"发送超时: {ex.Message}");
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"发送失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 异步发送数据（不阻塞调用线程）。
        /// 注意：SerialPort.Write 本质为同步 I/O，这里通过 Task.Run 包装为异步；
        /// timeoutMilliseconds 仅用于取消 Task.Run 的等待，**不能**中断底层 Write。
        /// 若需真正可取消的发送，请改造为串口专用发送线程 + 队列模型。
        /// </summary>
        /// <param name="data">待发送的字节数组。</param>
        /// <param name="timeoutMilliseconds">包装任务的超时（仅影响 Task 层）。</param>
        /// <returns>发送是否成功（未超时且未抛异常）。</returns>
        public async Task<bool> SendDataAsync(byte[] data, int timeoutMilliseconds = 1000)
        {
            if (!IsConnected) return false;

            // 超时令牌（只作用于 Task.Run 的等待，并不能打断 SerialPort.Write）
            var timeoutCts = new CancellationTokenSource(timeoutMilliseconds);

            try
            {
                await Task.Run(() =>
                {
                    lock (_lockObject)
                    {
                        if (_serialPort.IsOpen)
                        {
                            _serialPort.Write(data, 0, data.Length);
                        }
                    }
                }, timeoutCts.Token);

                return true;
            }
            catch (OperationCanceledException)
            {
                ErrorOccurred?.Invoke("发送操作超时");
                return false;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"异步发送失败: {ex.Message}");
                return false;
            }
        }


        // ============================
        // 接收（事件 + 异步读取）
        // ============================

        /// <summary>
        /// 串口数据到达事件回调（在线程池线程触发）。
        /// - 使用 ReadDataAsync 异步读取尽可能多的当前可读数据；
        /// - 读取成功后，通过 DataReceived 事件向外部抛出一段字节数据。
        /// 提示：若协议存在“半包”，请在外部订阅方维护缓存并自行做帧拼接/校验。
        /// </summary>
        private async void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                // 异步读取当前已到达的数据，避免阻塞事件线程
                byte[] data = await ReadDataAsync();

                if (data.Length > 0)
                {
                    // 抛给上层：由上层决定在何线程解析/分发
                    DataReceived?.Invoke(data);
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"数据接收异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 一次性把当前缓冲区里“已到达”的数据全部读完并返回。
        /// - 使用 MemoryStream 聚合多次 ReadAsync；
        /// - 循环条件：本次读到的数据 bytesRead &gt; 0 且 BytesToRead 仍有剩余；
        /// - 不保证返回的是完整协议帧；仅保证尽量“读干净”当前可读数据。
        /// </summary>
        private async Task<byte[]> ReadDataAsync()
        {
            using (var ms = new MemoryStream())
            {
                try
                {
                    int bytesRead;
                    do
                    {
                        // 从 BaseStream 进行真正的异步读取
                        bytesRead = await _serialPort.BaseStream.ReadAsync(_receiveBuffer, 0, _receiveBuffer.Length).ConfigureAwait(false);

                        if (bytesRead > 0)
                        {
                            ms.Write(_receiveBuffer, 0, bytesRead);
                        }

                        // 若串口驱动缓冲中还有未读字节，则继续读一轮
                    } while (bytesRead > 0 && _serialPort.BytesToRead > 0);

                    return ms.ToArray();
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke($"读取数据失败: {ex.Message}");
                    return Array.Empty<byte>();
                }
            }
        }

        /// <summary>
        /// 串口底层错误事件（例如帧错误、溢出等）。
        /// 注意：该事件由系统触发，常见类型包括：Frame、Overrun、RXOver、TXFull 等。
        /// </summary>
        private void SerialPort_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            ErrorOccurred?.Invoke($"串口错误: {e.EventType}");
        }

        // ============================
        // 资源释放
        // ============================

        /// <summary>
        /// 释放托管/非托管资源（推荐 using 或在 FormClosing/应用退出时调用）。
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 受保护的释放实现。
        /// </summary>
        /// <param name="disposing">true 表示显式释放（调用 Dispose），false 表示终结器释放。</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // —— 释放托管资源 ——
                try
                {
                    // 优先取消潜在后台任务
                    _cancellationTokenSource?.Cancel();
                    _cancellationTokenSource?.Dispose();

                    // 关闭并释放串口
                    _serialPort?.Close();
                    _serialPort?.Dispose();
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke($"释放资源时出错: {ex.Message}");
                }
            }

            // —— 释放非托管资源（此处仅将缓冲置空） ——
            _receiveBuffer = null;

            _disposed = true;
        }
    }

}
