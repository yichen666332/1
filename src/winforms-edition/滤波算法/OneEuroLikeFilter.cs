using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FilterVisualPlatform
{
    /// <summary>
    /// One Euro Filter（自适应低通）
    /// 参数映射：
    ///   Q     → minCutoff (Hz)   ：最低截止频率，越小越平滑
    ///   R     → dCutoff   (Hz)   ：导数低通的截止频率
    ///   Alpha → beta             ：响应度，速度越大带宽越高
    ///
    /// 改造点：
    /// - 三个参数可独立设置（SetMinCutoff/SetDCutoff/SetBeta），设置后下一次 Update 立即生效，不重置状态。
    /// - SetParameters(newQ,newR,newAlpha, resetState=false) 提供一次性设置，可选是否重置。
    /// - 线程安全：使用 Volatile.Read/Write 保障跨线程可见性。
    /// </summary>
    public class OneEuroLikeFilter
    {
        // —— 参数（Hz / 无量纲）——
        private volatile float _minCutoff; // Q
        private volatile float _dCutoff;   // R
        private volatile float _beta;      // Alpha

        private readonly float _dt;        // 采样周期（秒）

        // —— 状态 —— 
        private bool _initialized = false;
        private float _xPrev, _dxPrev;     // 原始/导数 EMA 的前一状态
        private float _xHat, _dxHat;       // 低通后的信号与导数
        private int _updateCount = 0;

        public int UpdateCount => _updateCount;

        // 数值保护下限
        private const float EPS_DT = 1e-6f;
        private const float EPS_FC = 1e-3f;  // Hz，下限避免除零

        private volatile float _lastInput;
        private volatile float _lastOutput;
        public float LastInput => Volatile.Read(ref _lastInput);
        public float LastOutput => Volatile.Read(ref _lastOutput);

        /// <param name="dt">采样周期(秒)，如 50Hz 采样则 dt=0.02</param>
        /// <param name="initialMinCutoff">最低截止频率(Hz)</param>
        /// <param name="initialDCutoff">导数低通截止频率(Hz)</param>
        /// <param name="initialBeta">响应度 beta ≥ 0</param>
        public OneEuroLikeFilter(
            float dt,
            float initialMinCutoff = 1.5f,
            float initialDCutoff = 1.0f,
            float initialBeta = 0.007f)
        {
            _dt = Math.Max(dt, EPS_DT);
            _minCutoff = Math.Max(initialMinCutoff, EPS_FC);
            _dCutoff = Math.Max(initialDCutoff, EPS_FC);
            _beta = Math.Max(initialBeta, 0f);
        }

        /// <summary>
        /// 输入一帧测量，返回滤波输出。
        /// 使用的是“当前最新参数”；若在其他线程改参，下一次调用会立即生效。
        /// </summary>
        public float Update(float measurement)
        {
            if (!_initialized)
            {
                _xPrev = measurement;
                _dxPrev = 0f;

                _xHat = measurement;
                _dxHat = 0f;

                _initialized = true;
                _updateCount = 0;
                return _xHat;
            }

            _updateCount++;

            // 读取当前参数（跨线程可见）
            float minCutoff = Volatile.Read(ref _minCutoff);
            float dCutoff = Volatile.Read(ref _dCutoff);
            float beta = Volatile.Read(ref _beta);

            // 1) 估计原始导数并低通
            float dxRaw = (measurement - _xPrev) / _dt;
            float aD = Alpha(dCutoff, _dt);
            _dxHat = _dxPrev + aD * (dxRaw - _dxPrev);

            // 2) 自适应主滤波的截止频率（随速度变化）
            float cutoff = Math.Max(minCutoff + beta * MathF.Abs(_dxHat), EPS_FC);
            float aX = Alpha(cutoff, _dt);

            // 3) 主信号低通
            _xHat = _xHat + aX * (measurement - _xHat);

            // 推进原始状态
            _xPrev = measurement;
            _dxPrev = _dxHat;

            _lastInput = measurement;
            _lastOutput = _xHat;
            return _xHat;
        }

        /// <summary>重置滤波状态（不改参数）。</summary>
        public void Reset()
        {
            _initialized = false;
            _xPrev = _dxPrev = _xHat = _dxHat = 0f;
            //_updateCount = 0;
        }

        // ---------------- 参数设置（立即生效，不重置） ----------------

        /// <summary>设置 minCutoff(Hz)，立即生效，不重置。</summary>
        public void SetMinCutoff(float hz)
        {
            Volatile.Write(ref _minCutoff, Math.Max(hz, EPS_FC));
        }

        /// <summary>设置 dCutoff(Hz)，立即生效，不重置。</summary>
        public void SetDCutoff(float hz)
        {
            Volatile.Write(ref _dCutoff, Math.Max(hz, EPS_FC));
        }

        /// <summary>设置 beta(≥0)，立即生效，不重置。</summary>
        public void SetBeta(float beta)
        {
            Volatile.Write(ref _beta, Math.Max(beta, 0f));
        }

        /// <summary>
        /// 一次性设置三个参数；默认不重置。如需“改参即重置”，传 resetState: true。
        /// newQ→minCutoff(Hz), newR→dCutoff(Hz), newAlpha→beta
        /// </summary>
        public void SetParameters(float newQ, float newR, float newAlpha, bool resetState = false)
        {
            Volatile.Write(ref _minCutoff, Math.Max(newQ, EPS_FC));
            Volatile.Write(ref _dCutoff, Math.Max(newR, EPS_FC));
            Volatile.Write(ref _beta, Math.Max(newAlpha, 0f));

            if (resetState) Reset();
        }

        /// <summary>获取当前参数 (Q=minCutoff, R=dCutoff, Alpha=beta)。</summary>
        public (float Q, float R, float Alpha) GetParameters()
        {
            return (
                Volatile.Read(ref _minCutoff),
                Volatile.Read(ref _dCutoff),
                Volatile.Read(ref _beta)
            );
        }

        // —— 工具：由截止频率(Hz)与 dt 计算 EMA 系数 α —— 
        private static float Alpha(float cutoffHz, float dt)
        {
            // τ = 1/(2π fc), α = dt / (τ + dt)
            float tau = 1.0f / (2.0f * (float)Math.PI * cutoffHz);
            return dt / (tau + dt);
        }
    }
}
