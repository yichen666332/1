using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FilterVisualPlatform
{
    /// <summary>
    /// One Euro Filter 智能调参器（独立于滤波器）。
    /// - 仅依赖 OneEuroLikeFilter 的 SetParameters/GetParameters/UpdateCount 即可工作。
    /// - 内置评分逻辑：平滑比 smoothRatio=OutStd/InStd 与滞后 LagRms。
    /// - 与 AKF 调参器相同的三段式流程：Step(应用) → PostCheck(接受/容忍/回退)。
    /// - DeepSeek 不可用时自动启用启发式兜底。
    /// - 事件复用 TuningEventArgs（Q=minCutoff, R=dCutoff, A=beta）。
    /// </summary>
    public sealed class OneEuroAutoTuner
    {
        // ========= 依赖 =========
        private readonly OneEuroLikeFilter _f;
        private readonly string _name;
        private readonly IDeepSeekClient _llm;

        // ========= 评估/窗口 =========
        private readonly int _cap;                  // 环形缓冲容量
        private readonly float[] _inRing;           // 输入窗口
        private readonly float[] _outRing;          // 输出窗口
        private int _w = 0, _n = 0;                 // 写指针/有效数量

        // ========= 节流/判定 =========
        private readonly TimeSpan _cooldown;
        private readonly int _minSamples;
        private readonly float _minScoreGain;
        private readonly float _maxLagWorsen;       // 允许滞后恶化比例（相对基线）
        private readonly float _maxStepScale;       // 单步限幅比例（±）
        private readonly CancellationTokenSource _cts = new();

        private DateTime _lastTuneUtc = DateTime.MinValue;
        private enum Phase { Idle, AppliedWaiting }
        private Phase _phase = Phase.Idle;
        private int _applyAtUpdateCount = 0;
        private volatile bool _inFlight = false;

        // ========= 基线 =========
        private (float Q, float R, float A) _baselineParams;
        private float _baselineScore = float.NegativeInfinity;
        private float _baselineLag = float.PositiveInfinity;
        private float _baselineSmooth = 1f;   // ← 新增：基线平滑比
        private bool _hasBaseline = false;

        // ========= 目标控制 =========
        public bool Enabled { get; set; } = false;
        public float TargetScore { get; set; } = 70f;     // 达到则停止
        public float ScoreTolerance { get; set; } = 5f;   // 容忍窗口
        public int MaxAcceptRounds { get; set; } = 10;    // 未达标时，兜底最多接受轮数
        public bool OneShot { get; set; } = false;        // 首次改进即固定并停

        // ========= LLM 提示词 =========
        private readonly string _systemPrompt;
        private readonly Func<EvalStat, (float Q, float R, float A), string> _userPromptBuilder;
        private readonly int _llmMaxTokens;
        private readonly double _llmTemp;

        // ========= 事件 =========
        public event EventHandler<TuningEventArgs>? TuningEvent;

        #region ===== 兼容性增强：稳健工具 & 配置 =====
        private static float Median(float[] a)
        {
            if (a == null || a.Length == 0) return 0f;
            var b = (float[])a.Clone();
            Array.Sort(b);
            int n = b.Length, m = n / 2;
            return (n % 2 == 1) ? b[m] : 0.5f * (b[m - 1] + b[m]);
        }
        private static float Mad(float[] a)
        {
            if (a == null || a.Length == 0) return 0f;
            float med = Median(a);
            var dev = new float[a.Length];
            for (int i = 0; i < a.Length; i++) dev[i] = MathF.Abs(a[i] - med);
            // 1.4826≈把MAD换算成~σ的尺度
            return 1.4826f * Median(dev);
        }
        private static float Percentile(float[] a, float p01to99)
        {
            if (a == null || a.Length == 0) return 0f;
            var b = (float[])a.Clone(); Array.Sort(b);
            p01to99 = Math.Clamp(p01to99, 0f, 100f);
            float pos = (b.Length - 1) * (p01to99 / 100f);
            int lo = (int)MathF.Floor(pos), hi = (int)MathF.Ceiling(pos);
            if (lo == hi) return b[lo];
            float w = pos - lo;
            return b[lo] * (1 - w) + b[hi] * w;
        }

        // 可调配置（给默认即可，不传也能跑）
        public sealed class OefTuningConfig
        {
            public float TargetSmoothPct { get; set; } = 30f;   // 目标平滑比=基线窗口 第30百分位
            public float LagRefPct { get; set; } = 70f;   // 滞后参考=基线窗口 第70百分位
            public float FirstRoundsLagRelax { get; set; } = 0.50f; // 前两轮滞后放宽+50%
            public int RelaxRounds { get; set; } = 2;     // 放宽轮数
            public float BetaStepMax { get; set; } = 0.20f; // beta 单步相对最大步长
            public float ExploreStepMin { get; set; } = 0.08f; // q/r最小相对步长（避免太小不动）
            public float ExploreStepMax { get; set; } = 0.25f; // q/r最大相对步长（避免过冲）
            public static OefTuningConfig Default => new OefTuningConfig();
        }
        private readonly OefTuningConfig _cfg = OefTuningConfig.Default;

        // 供评分使用的基线稳健参考
        private float _baselineInMad = 1f;
        private float _baselineSmoothTarget = 0.8f; // 初始化；真实值在首次建基线后用分位数覆盖
        private float _baselineLagRef = 0.0f;       // 同上
        #endregion

        // ========= 评分结构 =========
        public sealed class EvalStat
        {
            public int N;
            public float InStd, OutStd;
            public float SmoothRatio;  // OutStd / InStd（越小越平滑）
            public float LagRms;       // RMS(input - output) 归一化之前的绝对量
        }

        public OneEuroAutoTuner(
            OneEuroLikeFilter filter,
            string name,
            IDeepSeekClient llmClient,
            string systemPrompt = null,
            Func<EvalStat, (float Q, float R, float A), string> userPromptBuilder = null,
            int windowCapacity = 300,
            TimeSpan? cooldown = null,
            int minSamples = 120,
            float minScoreGain = 1.5f,
            float maxLagWorsen = 0.12f,   // 允许滞后短期上升 12%
            float maxStepScale = 0.30f,   // 单步限幅 ±30%
            int llmMaxTokens = 128,
            double llmTemperature = 0.2
        )
        {
            _f = filter ?? throw new ArgumentNullException(nameof(filter));
            _name = string.IsNullOrWhiteSpace(name) ? "OneEuro" : name;
            _llm = llmClient; // 允许为 null：表示无 LLM，用启发式

            _cap = Math.Max(60, windowCapacity);
            _inRing = new float[_cap];
            _outRing = new float[_cap];

            _cooldown = cooldown ?? TimeSpan.FromSeconds(6);
            _minSamples = Math.Max(30, minSamples);
            _minScoreGain = Math.Max(0f, minScoreGain);
            _maxLagWorsen = Math.Max(0f, maxLagWorsen);
            _maxStepScale = Math.Clamp(maxStepScale, 0.05f, 1.0f);

            _systemPrompt = string.IsNullOrWhiteSpace(systemPrompt)
                ? "You are a One-Euro filter tuning assistant. Output compact JSON only."
                : systemPrompt;

            _userPromptBuilder = userPromptBuilder ?? BuildDefaultUserPrompt;
            _llmMaxTokens = Math.Max(64, llmMaxTokens);
            _llmTemp = Math.Clamp(llmTemperature, 0.0, 2.0);
        }

        // ======== 数据馈送（在你的数据循环里调用）========
        public void FeedSample(float input, float output)
        {
            _inRing[_w] = input;
            _outRing[_w] = output;
            _w = (_w + 1) % _cap;
            _n = Math.Min(_n + 1, _cap);
        }

        // ======== 主流程前半：到评估周期时调用（尝试应用一次）========
        public void Step()
        {
            if (!Enabled) return;

            var stat = EvaluateWindow();
            if (stat.N < _minSamples) return;

            if (!_hasBaseline)
            {
                var cur = _f.GetParameters();
                _baselineParams = cur;

                // 用当前窗口构建稳健参考
                int n = _n;
                var ins = new float[n];
                var outs = new float[n];
                int start = _w - n; if (start < 0) start += _cap;
                for (int i = 0; i < n; i++)
                {
                    int idx = (start + i) % _cap;
                    ins[i] = _inRing[idx];
                    outs[i] = _outRing[idx];
                }
                float inMad = Math.Max(Mad(ins), 1e-6f);
                float outMad = Math.Max(Mad(outs), 1e-6f);

                _baselineInMad = inMad;
                _baselineSmooth = Math.Max(0.01f, outMad / inMad);

                // 目标平滑比 = 基线窗口 smoothRatio 的较“理想”分位（默认P30），避免写死0.8/0.7
                var srSamples = new float[n];
                // 这里用滑动MAD比的近似：简化处理就用单值；若有需要你可在FeedSample里存每步的局部比值
                for (int i = 0; i < n; i++) srSamples[i] = _baselineSmooth;
                _baselineSmoothTarget = MathF.Max(0.2f, Percentile(srSamples, _cfg.TargetSmoothPct));

                // 滞后参考 = 基线窗口 lag 的分位（默认P70）
                var lagSamples = new float[n];
                // 同理简化：使用当前窗口整体lagRms（你也可在FeedSample时累积每步abs差）
                float baseLag = RmsDiff(ins, outs);
                for (int i = 0; i < n; i++) lagSamples[i] = baseLag;
                _baselineLag = baseLag;
                _baselineLagRef = MathF.Max(1e-6f, Percentile(lagSamples, _cfg.LagRefPct));

                _baselineScore = Score(EvaluateWindow(), cur);
                _hasBaseline = true;
                _phase = Phase.Idle;
                return;
            }

            if (_phase == Phase.AppliedWaiting) return;
            var now = DateTime.UtcNow;
            if (now - _lastTuneUtc < _cooldown) return;
            if (_inFlight) return;

            _inFlight = true;

            _ = Task.Run(async () =>
            {
                var oldParams = _f.GetParameters(); // (Q=minCutoff, R=dCutoff, A=beta)
                float qNew = oldParams.Q, rNew = oldParams.R, aNew = oldParams.Alpha;
                float? conf = null;
                string rationale = null;
                string action = null;

                try
                {
                    // 1) 先试 LLM
                    bool usedLLM = false;
                    if (_llm != null)
                    {
                        try
                        {
                            var promptUser = _userPromptBuilder(stat, oldParams);
                            var llm = await _llm.GetTuningAsync(
                                systemPrompt: _systemPrompt,
                                userPrompt: promptUser,
                                maxTokens: _llmMaxTokens,
                                temperature: _llmTemp,
                                ct: _cts.Token
                            ).ConfigureAwait(false);

                            if (llm != null)
                            {
                                qNew = llm.proposed_q;   // map: proposed_q → minCutoff
                                rNew = llm.proposed_r;   // map: proposed_r → dCutoff
                                aNew = llm.proposed_alpha; // map: proposed_alpha → beta
                                conf = llm.confidence;
                                rationale = llm.rationale;
                                action = "APPLY_LLM";
                                usedLLM = true;
                            }
                        }
                        catch { /* ignore & fallback */ }
                    }

                    // 2) 启发式兜底
                    if (!usedLLM)
                    {
                        (qNew, rNew, aNew, rationale) = HeuristicSuggest(stat, oldParams);
                        action = "APPLY_HEURISTIC";
                    }

                    // 3) 合法化 + 单步限幅
                    qNew = Clamp(qNew, 0.10f, 10f);        // minCutoff ∈ [0.1, 10] Hz
                    rNew = Clamp(rNew, 0.10f, 10f);        // dCutoff   ∈ [0.1, 10] Hz
                    aNew = Clamp(aNew, 0.000f, 0.080f);    // beta      ∈ [0.000, 0.080]


                    qNew = ClampStep(oldParams.Q, qNew, _maxStepScale);
                    rNew = ClampStep(oldParams.R, rNew, _maxStepScale);
                    //aNew = ClampStep(oldParams.Alpha, aNew, 0.20f); // beta 允许±20% 相对步
                    aNew = ClampStep(oldParams.Alpha, aNew, _cfg.BetaStepMax); // 由配置控制，默认±20%

                    // 4) 应用并清窗口，转入等待
                    _f.SetParameters(qNew, rNew, aNew, resetState: false);
                    ClearWindow(); // 评估窗口清零，开始新段统计

                    _applyAtUpdateCount = _f.UpdateCount;
                    _phase = Phase.AppliedWaiting;
                    _lastTuneUtc = DateTime.UtcNow;

                    TuningEvent?.Invoke(this, new TuningEventArgs
                    {
                        Time = DateTime.Now,
                        Name = _name,
                        Action = action,
                        OldParams = oldParams,
                        NewParams = (qNew, rNew, aNew),
                        Confidence = conf,
                        Rationale = rationale
                    });
                }
                finally
                {
                    _inFlight = false;
                }
            }, _cts.Token);
        }

        // ======== 主流程后半：到评估周期时调用（接受/容忍/回退/停止）========
        private int _acceptCount = 0;
        public void PostCheck()
        {
            if (!Enabled || !_hasBaseline) return;
            if (_phase != Phase.AppliedWaiting) return;

            int newSamples = Math.Max(0, _f.UpdateCount - _applyAtUpdateCount);
            if (newSamples < _minSamples)
            {
                TuningEvent?.Invoke(this, new TuningEventArgs
                {
                    Time = DateTime.Now,
                    Name = _name,
                    Action = "SKIP_WAITING",
                    OldParams = _f.GetParameters(),
                    NewParams = _f.GetParameters(),
                    Rationale = $"waiting newSamples={newSamples}/{_minSamples}"
                });
                return;
            }

            var stat = EvaluateWindow();
            var curParams = _f.GetParameters();
            float curScore = Score(stat, curParams);

            // 前两轮：滞后放宽到 +50%，之后用配置的 _maxLagWorsen（默认 12%）
            float lagRelax = (_acceptCount < _cfg.RelaxRounds) ? _cfg.FirstRoundsLagRelax : _maxLagWorsen;

            bool passStrict = curScore >= _baselineScore + _minScoreGain;
            bool lagOkStrict = stat.LagRms <= _baselineLag * (1f + lagRelax);

            bool passTolerant = curScore >= _baselineScore - ScoreTolerance;
            bool lagOkTolerant = stat.LagRms <= _baselineLag * (1f + lagRelax);

            string why = $"minGain={_minScoreGain:0.0}, tol={ScoreTolerance:0.0}, " +
                         $"maxLagWorsen={lagRelax:P0}, score={curScore:0.0} vs base={_baselineScore:0.0}, " +
                         $"lag={stat.LagRms:0.000} vs base={_baselineLag:0.000}, smoothRatio={stat.SmoothRatio:0.00}";

            if (passStrict && lagOkStrict)
            {
                _baselineParams = curParams;
                _baselineScore = curScore;
                _baselineLag = stat.LagRms;
                _phase = Phase.Idle;
                _acceptCount++;

                TuningEvent?.Invoke(this, new TuningEventArgs
                {
                    Time = DateTime.Now,
                    Name = _name,
                    Action = "ACCEPT_BASELINE",
                    OldParams = curParams,
                    NewParams = curParams,
                    Score = curScore,
                    Rms = stat.LagRms,
                    Rationale = "Improved vs baseline (strict) | " + why
                });

                bool reachTarget = curScore >= TargetScore;
                bool reachMaxRounds = _acceptCount >= MaxAcceptRounds;
                if (OneShot || reachTarget || reachMaxRounds)
                {
                    Enabled = false;
                    TuningEvent?.Invoke(this, new TuningEventArgs
                    {
                        Time = DateTime.Now,
                        Name = _name,
                        Action = "FINAL_PARAMS",
                        OldParams = curParams,
                        NewParams = curParams,
                        Score = curScore,
                        Rms = stat.LagRms,
                        Rationale = OneShot
                            ? "OneShot=true：首次改进后固定并停止。"
                            : (reachTarget
                                ? $"已达目标分数 {curScore:0.0} ≥ {TargetScore:0.0}，停止。"
                                : $"已连续改进 {MaxAcceptRounds} 轮，自动停止（未达目标分）。")
                    });
                }
            }
            else if (passTolerant && lagOkTolerant)
            {
                _phase = Phase.Idle;
                TuningEvent?.Invoke(this, new TuningEventArgs
                {
                    Time = DateTime.Now,
                    Name = _name,
                    Action = "CONTINUE",
                    OldParams = curParams,
                    NewParams = curParams,
                    Score = curScore,
                    Rms = stat.LagRms,
                    Rationale = "Within tolerant window, keep current params | " + why
                });
            }
            else
            {
                var cur = curParams;
                _f.SetParameters(_baselineParams.Q, _baselineParams.R, _baselineParams.A, resetState: false);
                ClearWindow();
                _phase = Phase.Idle;

                TuningEvent?.Invoke(this, new TuningEventArgs
                {
                    Time = DateTime.Now,
                    Name = _name,
                    Action = "ROLLBACK",
                    OldParams = cur,
                    NewParams = _baselineParams,
                    Score = curScore,
                    Rms = stat.LagRms,
                    Rationale = "No improvement vs baseline | " + why
                });
            }
        }

        // ======== 评分逻辑（V4：稳健尺度、分位目标、自适应容忍）========
        private float Score(EvalStat s, (float Q, float R, float A) cur)
        {
            // 1) 相对项：以基线 smoothRatio 与滞后参考为基准，看改进程度
            float smoothDen = Math.Max(0.05f, _baselineSmooth - _baselineSmoothTarget);
            float smoothImprove = Clamp01((_baselineSmooth - s.SmoothRatio) / smoothDen);

            float lagDen = Math.Max(1e-6f, _baselineLagRef);
            float lagImprove = Clamp01((_baselineLagRef - s.LagRms) / lagDen);

            float relScore = 100f * (0.85f * smoothImprove + 0.15f * lagImprove);

            // 2) 绝对项：靠近目标更高分；滞后只在明显超过参考时扣
            float absSmooth = 1f - Clamp01(MathF.Abs(s.SmoothRatio - _baselineSmoothTarget) / (_baselineSmoothTarget * 0.5f));
            float absLag = 1f - Clamp01((s.LagRms - _baselineLagRef) / (_baselineLagRef * 0.5f)); // 低于参考不扣

            float absScore = 100f * (0.7f * absSmooth + 0.3f * MathF.Max(0f, absLag));

            float score = 0.7f * relScore + 0.3f * absScore;

            // 轻惩极端 beta（防抖）
            if (cur.A > 0.05f) score -= 5f * Clamp01((cur.A - 0.05f) / 0.03f);

            return MathF.Max(0f, MathF.Min(100f, score));
        }

        // ======== 评估窗口（稳健统计版）========
        private EvalStat EvaluateWindow()
        {
            int n = _n;
            if (n <= 0) return new EvalStat { N = 0 };

            var ins = new float[n];
            var outs = new float[n];
            int start = _w - n; if (start < 0) start += _cap;
            for (int i = 0; i < n; i++)
            {
                int idx = (start + i) % _cap;
                ins[i] = _inRing[idx];
                outs[i] = _outRing[idx];
            }

            // 稳健尺度：MAD 与分位
            float inStd = Std(ins);       // 保留原σ（可做参考）
            float outStd = Std(outs);
            float inMad = Math.Max(Mad(ins), 1e-6f);
            float outMad = Math.Max(Mad(outs), 1e-6f);

            // 平滑比用MAD版本更稳健（但依然提供σ版用于日志）
            float smoothRatio = outMad / inMad;
            float lagRms = RmsDiff(ins, outs);

            return new EvalStat
            {
                N = n,
                InStd = inStd,
                OutStd = outStd,
                SmoothRatio = smoothRatio, // 这里是MAD比，更抗异常
                LagRms = lagRms
            };
        }

        private void ClearWindow()
        {
            _w = 0; _n = 0;
        }

        // ======== 启发式兜底（V4：数据无关、单调合理、步长自适应）========
        private (float Q, float R, float A, string why) HeuristicSuggest(
            EvalStat s, (float Q, float R, float A) cur)
        {
            float q = cur.Q, r = cur.R, a = cur.A;
            var sb = new StringBuilder("Heuristic:");

            // 根据当前窗口的波动程度自适应相对步长（波动大→步小）
            float vol = Clamp01(s.SmoothRatio); // 0..1 视作粗糙度（越接近1越不平滑）
            float stepLo = _cfg.ExploreStepMin;
            float stepHi = _cfg.ExploreStepMax;
            float step = stepHi + (stepLo - stepHi) * Clamp01(vol);

            // 目标与死区：使用自适应目标
            float target = _baselineSmoothTarget;
            float band = 0.05f;

            if (s.SmoothRatio > target + band)
            {
                // 不够平滑：q↓、a↓，r↑（让导数更灵敏，减少滞后）
                q *= (1f - step * 0.7f);
                a *= (1f - MathF.Min(_cfg.BetaStepMax, step * 0.6f));
                r *= (1f + step * 0.4f);
                sb.Append($" not-smooth -> q×{1f - step * 0.7f:0.00}, a×{1f - MathF.Min(_cfg.BetaStepMax, step * 0.6f):0.00}, r×{1f + step * 0.4f:0.00};");
            }
            else if (s.SmoothRatio < target - band)
            {
                // 过度平滑：q↑、a↑，r↑（一起变灵）
                q *= (1f + step * 0.7f);
                a *= (1f + MathF.Min(_cfg.BetaStepMax, step * 0.5f));
                r *= (1f + step * 0.3f);
                sb.Append($" over-smooth -> q×{1f + step * 0.7f:0.00}, a×{1f + MathF.Min(_cfg.BetaStepMax, step * 0.5f):0.00}, r×{1f + step * 0.3f:0.00};");
            }

            // 滞后保护（基于分位）：
            if (s.LagRms > _baselineLagRef * 1.0f)
            {
                q *= 1f + step * 0.3f;
                r *= 1f + step * 0.2f;
                a *= 1f - MathF.Min(_cfg.BetaStepMax, step * 0.3f);
                sb.Append(" lag-high -> q↑, r↑, a↓;");
            }

            return (q, r, a, sb.ToString());
        }

        // ======== Prompt 构造（默认）========
        private static string BuildDefaultUserPrompt(EvalStat s, (float Q, float R, float A) cur)
        {
            return $@"
Task: Suggest next-step One Euro Filter parameters to balance smoothness and latency.
Current: {{""minCutoff"":{cur.Q:F4}, ""dCutoff"":{cur.R:F4}, ""beta"":{cur.A:F5}}}
Stats: {{""N"":{s.N}, ""InStd"":{s.InStd:F3}, ""OutStd"":{s.OutStd:F3}, ""SmoothRatio"":{s.SmoothRatio:F3}, ""LagRms"":{s.LagRms:F3}}}
Rules:
- Return JSON only: {{""proposed_q"":float,""proposed_r"":float,""proposed_alpha"":float,""confidence"":0..1,""rationale"":string}}
- Bounds: 0.1<=proposed_q<=10, 0.1<=proposed_r<=10, 0.000<=proposed_alpha<=0.080
- Prefer ±10~30% relative steps unless the stats are extreme.
- If SmoothRatio>0.8 (not smooth), decrease proposed_q and proposed_alpha slightly.
- If LagRms is large, increase proposed_q and proposed_alpha slightly.
";
        }

        // ======== 小工具 ========
        private static float Std(float[] arr)
        {
            int n = arr.Length;
            if (n == 0) return 0;
            double sum = 0, sumsq = 0;
            for (int i = 0; i < n; i++)
            {
                double v = arr[i];
                sum += v; sumsq += v * v;
            }
            double mean = sum / n;
            double var = Math.Max(0.0, sumsq / n - mean * mean);
            return (float)Math.Sqrt(var);
        }

        private static float RmsDiff(float[] a, float[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            if (n == 0) return 0;
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                double d = a[i] - b[i];
                sum += d * d;
            }
            return (float)Math.Sqrt(sum / n);
        }

        private static float Clamp(float v, float lo, float hi)
            => v < lo ? lo : (v > hi ? hi : v);

        private static float ClampStep(float oldV, float newV, float maxStep)
        {
            if (oldV == 0f) return newV;
            float lo = oldV * (1f - maxStep);
            float hi = oldV * (1f + maxStep);
            if (newV < lo) return lo;
            if (newV > hi) return hi;
            return newV;
        }

        private static float Clamp01(float x) => x < 0 ? 0 : (x > 1 ? 1 : x);

        // === 供 UI 复用评分：返回当前窗口统计 + 与调参器一致的 Score ===
        public (EvalStat Stat, float Score) EvaluateCurrentScore()
        {
            var s = EvaluateWindow();                  // 复用内部窗口统计（含 MAD 版 smoothRatio）
            var p = _f.GetParameters();                // (Q=minCutoff, R=dCutoff, A=beta)
            float sc = Score(s, p);                    // 与调参器相同的评分函数
            return (s, sc);
        }

    }
}

