using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FilterVisualPlatform
{
    namespace FilterVisualPlatform
    {
        public class RobustHInfinityFilter
        {
            // --- 状态 ---
            private float _xEst;     // 当前估计值
            private float _pEst;     // 当前估计误差协方差
            private bool _initialized;
            private int _updateCount;

            // --- 噪声与参数（改为 volatile，保证跨线程可见） ---
            private volatile float _q;      // 过程噪声协方差
            private volatile float _r;      // 测量噪声协方差
            private volatile float _gamma;  // H∞ 鲁棒度（0<gamma<1，越大越保守）

            // --- 鲁棒残差（Huber）阈值倍数（默认 2.5σ）---
            private float _huberC;

            // --- 数值下限 ---
            private const float EPS_R = 1e-6f;
            private const float EPS_Q = 1e-9f;
            private const float EPS_P = 1e-9f;

            public int UpdateCount => _updateCount;

            public float LastInput { get; private set; }
            public float LastOutput { get; private set; }

            /// <param name="initialQ">过程噪声方差</param>
            /// <param name="initialR">测量噪声方差</param>
            /// <param name="gamma">H∞ 鲁棒度（0,1），越大越保守</param>
            /// <param name="huberC">Huber 鲁棒阈值倍数（单位：sigma），默认 2.5</param>
            public RobustHInfinityFilter(float initialQ = 0.01f, float initialR = 1.0f,
                                         float gamma = 0.1f, float huberC = 2.5f)
            {
                Volatile.Write(ref _q, Math.Max(initialQ, EPS_Q));
                Volatile.Write(ref _r, Math.Max(initialR, EPS_R));
                Volatile.Write(ref _gamma, Clamp(gamma, 1e-4f, 0.99f));
                _huberC = Math.Max(1.0f, huberC); // 至少 1σ
            }

            /// <summary>处理一帧测量值（使用当前“最新参数”，改参后下次 Update 立即生效）。</summary>
            public float Update(float measurement)
            {
                // 读取当前参数（跨线程可见）
                float q = Volatile.Read(ref _q);
                float r = Volatile.Read(ref _r);
                float gamma = Volatile.Read(ref _gamma);

                LastInput = measurement;

                if (!_initialized)
                {
                    _xEst = measurement;
                    _pEst = Math.Max(1.0f, r);
                    _initialized = true;
                    _updateCount = 0;
                    return _xEst;
                }

                _updateCount++;

                // 预测（H∞ 放大不确定性）
                float pPred = (1.0f + gamma) * (_pEst + q);   // γ↑ → 更保守
                pPred = Math.Max(pPred, EPS_P);

                // 量测更新
                float s = pPred + r;                 // 创新方差
                s = Math.Max(s, EPS_R + EPS_P);
                float k = pPred / s;                 // 增益

                // 残差 + Huber 鲁棒
                float residual = measurement - _xEst;
                float sigma = MathF.Sqrt(s);
                float thr = _huberC * sigma;
                if (MathF.Abs(residual) > thr)
                    residual = MathF.Sign(residual) * thr;

                // 状态更新
                _xEst = _xEst + k * residual;

                // Joseph 形式协方差更新（标量）
                float oneMinusK = 1.0f - k;
                _pEst = oneMinusK * oneMinusK * pPred + k * k * r;
                _pEst = Math.Max(_pEst, EPS_P);

                LastOutput = _xEst;

                return _xEst;
            }

            /// <summary>重置滤波器状态（不改变参数）。</summary>
            public void Reset()
            {
                _initialized = false;
                //_updateCount = 0;
                _xEst = 0f;
                _pEst = 0f;
            }

            // ---------------- 参数设置（独立设置，立即生效，不重置） ----------------

            /// <summary>设置过程噪声方差 Q，立即生效不重置。</summary>
            public void SetQ(float newQ) => Volatile.Write(ref _q, Math.Max(newQ, EPS_Q));

            /// <summary>设置测量噪声方差 R，立即生效不重置。</summary>
            public void SetR(float newR) => Volatile.Write(ref _r, Math.Max(newR, EPS_R));

            /// <summary>设置鲁棒度 gamma（0,1），立即生效不重置。</summary>
            public void SetGamma(float newGamma) =>
                Volatile.Write(ref _gamma, Clamp(newGamma, 1e-4f, 0.99f));

            /// <summary>（可选）设置 Huber 阈值倍数，默认 2.5。</summary>
            public void SetHuberC(float huberC) => _huberC = Math.Max(1.0f, huberC);

            /// <summary>
            /// 兼容外部统一接口：SetParameters(q, r, alpha)；默认不重置。
            /// 如需“改参即重置”，用重载 SetParameters(q,r,alpha, resetState:true)。
            /// </summary>
            public void SetParameters(float newQ, float newR, float newAlpha)
                => SetParameters(newQ, newR, newAlpha, resetState: false);

            /// <summary>
            /// 一次性设置参数；alpha 作为 gamma 使用；可选是否重置状态。
            /// </summary>
            public void SetParameters(float newQ, float newR, float newAlpha, bool resetState)
            {
                Volatile.Write(ref _q, Math.Max(newQ, EPS_Q));
                Volatile.Write(ref _r, Math.Max(newR, EPS_R));
                Volatile.Write(ref _gamma, Clamp(newAlpha, 1e-4f, 0.99f));
                if (resetState) Reset();
            }

            /// <summary>返回 (Q, R, Alpha)；其中 Alpha 实际等于 gamma。</summary>
            public (float Q, float R, float Alpha) GetParameters()
                => (Volatile.Read(ref _q), Volatile.Read(ref _r), Volatile.Read(ref _gamma));

            // --- 工具 ---
            private static float Clamp(float v, float lo, float hi)
            {
                if (v < lo) return lo;
                if (v > hi) return hi;
                return v;
            }
        }
    }
}
