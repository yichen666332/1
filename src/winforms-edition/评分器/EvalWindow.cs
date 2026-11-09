using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FilterVisualPlatform
{
    public sealed class OefScorer
    {
        public sealed class Config
        {
            // 与 OneEuroAutoTuner 对齐的目标
            public double TargetSmoothRatio = 0.70; // 目标平滑比
            public double SmoothBand = 0.25;        // 宽容带
            public double LagScale = 1.0;           // 滞后归一化的量纲（按你的单位调）
            public double WSmooth = 0.65;           // 平滑项权重
            public double WLag = 0.35;              // 滞后项权重
            public bool UseMadRatio = true;       // 是否用 MAD 比（与自动调参保持一致）
            public int MinSamples = 150;         // 与调参器窗口要求一致
        }

        private readonly EvalWindow _win;
        public readonly Config Cfg;

        public OefScorer(int window = 256, Config? cfg = null)
        {
            _win = new EvalWindow(window);
            Cfg = cfg ?? new Config();
        }

        public void Clear() => _win.Clear();

        public void Feed(double raw, double filtered) => _win.Push(raw, filtered);

        public (EvalStat Stat, double Score, string Verdict) Evaluate()
        {
            var s = _win.Evaluate(Cfg.UseMadRatio);
            if (s.N < Cfg.MinSamples) return (s, double.NaN, "样本不足");

            double sr = s.SmoothRatio;
            double lag = s.LagRms;

            double srTerm = Math.Max(0.0, 1.0 - Math.Abs(sr - Cfg.TargetSmoothRatio) / Cfg.SmoothBand); // 0..1
            double lagTerm = 1.0 / (1.0 + lag / Math.Max(1e-9, Cfg.LagScale));                           // 0..1

            double score = 100.0 * (Cfg.WSmooth * srTerm + Cfg.WLag * lagTerm);

            string verdict =
                  sr < (Cfg.TargetSmoothRatio - 0.15) ? "过平滑（响应偏慢）"
                : sr > (Cfg.TargetSmoothRatio + 0.15) ? "不够平滑（抖动偏大）"
                : "平衡良好";

            return (s, score, verdict);
        }
    }

}
