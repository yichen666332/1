using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FilterVisualPlatform
{
    public sealed class RhfScorer
    {
        public sealed class Config
        {
            // 与 RobustHInfAutoTuner 对齐的目标/权重（按你的调参器改）
            public double TargetSmoothRatio = 0.65;
            public double SmoothBand = 0.30;
            public double LagScale = 1.0;         // 单位量纲
            public double WSmooth = 0.60;
            public double WLag = 0.30;
            public double WHeavyTail = 0.10;      // 重尾项权重
            public bool UseMadRatio = true;     // MAD 比更抗异常
            public int MinSamples = 150;
            // 重尾阈值（按调参器）：创新 |res| 相对 InMAD 的倍率
            public double InnovVsMadRef = 3.0;    // 3x MAD 视为明显重尾
        }

        private readonly EvalWindow _win;
        public readonly Config Cfg;

        public RhfScorer(int window = 256, Config? cfg = null)
        {
            _win = new EvalWindow(window);
            Cfg = cfg ?? new Config();
        }

        public void Clear() => _win.Clear();

        public void Feed(double raw, double filtered) => _win.Push(raw, filtered);

        public (EvalStat Stat, double Score, string Verdict) Evaluate()
        {
            var s = _win.Evaluate(Cfg.UseMadRatio, lagSearchMax: 6);
            if (s.N < Cfg.MinSamples) return (s, double.NaN, "样本不足");

            double sr = s.SmoothRatio;
            double lag = s.LagRms;

            double srTerm = Math.Max(0.0, 1.0 - Math.Abs(sr - Cfg.TargetSmoothRatio) / Cfg.SmoothBand); // 0..1
            double lagTerm = 1.0 / (1.0 + lag / Math.Max(1e-9, Cfg.LagScale));                            // 0..1

            // 重尾项：创新的绝对均值相对于输入 MAD 的归一化
            double heavy = s.InMAD > 1e-9 ? s.InnovAbsMean / s.InMAD : 0.0;
            // 使其 0..1，越小越好：ref=3 表示 3x MAD 给到 0.5 左右
            double heavyTerm = 1.0 / (1.0 + heavy / Math.Max(1e-9, Cfg.InnovVsMadRef));

            double score = 100.0 * (Cfg.WSmooth * srTerm + Cfg.WLag * lagTerm + Cfg.WHeavyTail * heavyTerm);

            string verdict =
                  sr < (Cfg.TargetSmoothRatio - 0.20) ? "过平滑/可能滞后"
                : sr > (Cfg.TargetSmoothRatio + 0.25) ? "不够平滑/重噪"
                : (heavy > 3.5 ? "重尾明显" : "平衡良好");

            return (s, score, verdict);
        }
    }

}
