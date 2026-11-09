
using FilterVisualPlatform.FilterVisualPlatform; // RobustHInfinityFilter 所在命名空间
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FilterVisualPlatform
{
    /// <summary>
    /// Robust H∞（一阶标量）滤波的智能调参器（整合版，稳态增强 + “分数优先 + 稳定兜底”）。
    /// - 调参参数：Q(过程噪声方差)、R(测量噪声方差)、γ(=Alpha，鲁棒度)。
    /// - 使用方式：FeedSample → Step → PostCheck（与 OEF/AKF 一致）。
    /// - 评分：综合 OutStd/InStd（平滑度）、LagRms（滞后）、MAD（重尾）与相对改善。
    /// - 兼容 10 Hz：通过 sampleRateHz + windowSeconds 自动配置窗口；也可 minSamplesOverride。
    /// - 稳健验收：目标带“保底接受”、基线老化刷新、连续回退“脱困步”。
    /// - 去振荡增强：滞后优先门控、重尾投票、R/γ 动态硬帽、回退后禁升期、EMA 判据。
    /// - 终止判定：业界常用“分数优先 + 稳定兜底”。
    /// - γ 一直使用 RobustHInfinityFilter.Alpha 字段。
    /// </summary>
    public sealed class RobustHInfAutoTuner
    {
        // ========= 依赖 =========
        private readonly RobustHInfinityFilter _f;
        private readonly string _name;
        private readonly IDeepSeekClient? _llm;

        // ========= 可选日志 =========
        public Action<string>? Logger { get; }

        // ========= 窗口缓存 =========
        private readonly int _cap;
        private readonly float[] _inRing;
        private readonly float[] _outRing;
        private int _w = 0, _n = 0;

        // ========= 采样/窗口 =========
        private readonly float _sampleRateHz;
        private readonly float _windowSeconds;
        private readonly int _minSamplesOverride;

        // ========= 节流/判定 =========
        private readonly TimeSpan _cooldown;
        private readonly float _minScoreGain;
        private readonly float _maxLagWorsen;    // 相对基线允许的滞后劣化比例
        private readonly float _maxStepScale;    // 单步相对限幅
        private readonly CancellationTokenSource _cts = new();
        private DateTime _lastTuneUtc = DateTime.MinValue;

        private enum Phase { Idle, AppliedWaiting }
        private Phase _phase = Phase.Idle;
        private int _applyAtUpdateCount = 0;
        private volatile bool _inFlight = false;

        // ========= 基线 =========
        private (float Q, float R, float G) _baselineParams;
        private float _baselineScore = float.NegativeInfinity;
        private float _baselineLag = float.PositiveInfinity;
        private float _baselineSmooth = 1f; // OutStd/InStd
        private bool _hasBaseline = false;

        // ========= 目标/控制 =========
        public bool Enabled { get; set; } = false;
        public float TargetScore { get; set; } = 85f;
        public float ScoreTolerance { get; set; } = 5f;
        public int MaxAcceptRounds { get; set; } = 12;
        public bool OneShot { get; set; } = false;

        // ========= LLM 提示词 =========
        private readonly string _systemPrompt;
        private readonly Func<EvalStat, (float Q, float R, float G), string> _userPromptBuilder;
        private readonly int _llmMaxTokens;
        private readonly double _llmTemp;

        // ========= 事件 =========
        public event EventHandler<TuningEventArgs>? TuningEvent;

        // ========= 评估结构 =========
        public sealed class EvalStat
        {
            public int N;
            public float InStd, OutStd;
            public float SmoothRatio;   // OutStd / InStd（越小越平滑）
            public float LagRms;        // RMS(input - output)
            public float ResidualStd;   // 残差标准差
            public float ResidualMAD;   // 残差 MAD（重尾）
            public float InnovAbsMean;  // |residual| 的均值
            public int LagSamples;      // 互相关估计样本滞后(>0: 输出滞后输入)
        }

        // ========= 运行态计数 =========
        private int _acceptCount = 0;
        private int _consecutiveRollback = 0;
        private int _sinceLastAccept = 0;

        // ========= 稳态增强/门控 =========
        private float _bestLagRms = float.PositiveInfinity; // 历史最好滞后
        private float _emaLagRms = float.PositiveInfinity;  // EMA 滞后
        private float _emaSmooth = 1f;                      // EMA 平滑比
        private int _heavyTailVotes = 0;                    // 重尾投票（≥2 才允许 R↑/γ↑）
        private int _noRaiseRounds = 0;                     // 回退/滞后高后，禁止 R↑/γ↑ 的轮数
        private bool _allowRaiseRG = false;                 // 本轮是否允许 R↑/γ↑
        private readonly float _rCapFactor = 1.25f;         // R 的相对上限（基线倍数）
        private readonly float _gCap = 0.32f;               // γ 的常规上限（会被动态硬帽覆盖）

        // ========= “分数优先 + 稳定兜底”需要的状态 =========
        private int _stableCount = 0;                       // 连续稳定轮数计数
        private float _lastScore = 0f;                      // 上一次得分
        private const float StableEps = 0.01f;              // 参数相对变化阈值 (1%)
        private const int StableRounds = 3;                 // 连续稳定轮数阈值

        public RobustHInfAutoTuner(
            RobustHInfinityFilter filter,
            string name,
            IDeepSeekClient? llmClient = null,
            string? systemPrompt = null,
            Func<EvalStat, (float Q, float R, float G), string>? userPromptBuilder = null,

            // 采样/窗口参数（默认适配 ~10Hz）
            float sampleRateHz = 10f,
            float windowSeconds = 8f,
            int minSamplesOverride = 0,

            TimeSpan? cooldown = null,
            float minScoreGain = 1.5f,
            float maxLagWorsen = 0.20f,
            float maxStepScale = 0.30f,
            int llmMaxTokens = 128,
            double llmTemperature = 0.2,
            Action<string>? logger = null
        )
        {
            _f = filter ?? throw new ArgumentNullException(nameof(filter));
            _name = string.IsNullOrWhiteSpace(name) ? "RHF" : name;
            _llm = llmClient;

            Logger = logger;

            _sampleRateHz = Math.Max(1f, sampleRateHz);
            _windowSeconds = Math.Max(2f, windowSeconds);
            _minSamplesOverride = Math.Max(0, minSamplesOverride);

            // 根据采样率和窗口秒数计算容量
            int windowSamples = (int)MathF.Round(_sampleRateHz * _windowSeconds);
            _cap = Math.Max(60, windowSamples); // 至少 60
            _inRing = new float[_cap];
            _outRing = new float[_cap];

            _cooldown = cooldown ?? TimeSpan.FromSeconds(6);
            _minScoreGain = Math.Max(0f, minScoreGain);
            _maxLagWorsen = Math.Max(0f, maxLagWorsen);
            _maxStepScale = Math.Clamp(maxStepScale, 0.05f, 1.0f);

            _systemPrompt = string.IsNullOrWhiteSpace(systemPrompt)
                ? "You are a Robust H-infinity (H∞) filter tuning assistant. Return compact JSON only."
                : systemPrompt;

            _userPromptBuilder = userPromptBuilder ?? BuildDefaultUserPrompt;
            _llmMaxTokens = Math.Max(64, llmMaxTokens);
            _llmTemp = Math.Clamp(llmTemperature, 0.0, 2.0);

            _emaLagRms = float.PositiveInfinity;
            _emaSmooth = 1f;
        }

        // ======== 数据喂入（建议每帧调用）========
        public void FeedSample(float input, float output)
        {
            _inRing[_w] = input;
            _outRing[_w] = output;
            _w = (_w + 1) % _cap;
            _n = Math.Min(_n + 1, _cap);
        }

        // ======== 调参前半：尝试给出下一步参数并应用 ========
        public void Step()
        {
            if (!Enabled) return;

            var stat = EvaluateWindow();
            int minSamples = _minSamplesOverride > 0 ? _minSamplesOverride : Math.Max(30, (int)MathF.Round(_sampleRateHz * _windowSeconds));
            if (stat.N < minSamples) return;

            // —— 初始化基线 —— 
            if (!_hasBaseline)
            {
                var cur = _f.GetParameters();
                _baselineParams = (cur.Q, cur.R, cur.Alpha); // γ 使用 Alpha 字段
                _baselineLag = stat.LagRms;
                _baselineSmooth = Math.Max(0.01f, stat.SmoothRatio);
                _baselineScore = Score(stat, (cur.Q, cur.R, cur.Alpha));
                _hasBaseline = true;
                _phase = Phase.Idle;

                _bestLagRms = Math.Min(_bestLagRms, stat.LagRms);
                _heavyTailVotes = 0;

                Log($"RHF-Raw ACCEPT_BASELINE: Q {cur.Q:E3}, R {cur.R:E3}, γ {cur.Alpha:0.###} | score={_baselineScore:0.0}, lag={_baselineLag:0.###}, smooth={_baselineSmooth:0.###}");
                return;
            }

            if (_phase == Phase.AppliedWaiting) return;
            var now = DateTime.UtcNow;
            if (now - _lastTuneUtc < _cooldown) return;
            if (_inFlight) return;

            // ===== EMA 更新（平滑判据，减少一窗毛刺）=====
            float emaA = 0.30f;
            if (float.IsInfinity(_emaLagRms)) _emaLagRms = stat.LagRms; else _emaLagRms = emaA * stat.LagRms + (1 - emaA) * _emaLagRms;
            _emaSmooth = emaA * stat.SmoothRatio + (1 - emaA) * _emaSmooth;

            // ===== 重尾“投票”更新（仅在滞后不高时计票）=====
            bool heavyTailThis = (stat.ResidualMAD / Math.Max(1e-6f, stat.InStd)) >= 0.22f;
            bool lagHigh = (_emaLagRms > _baselineLag * 1.10f) || (Math.Abs(stat.LagSamples) >= 1);
            if (!lagHigh && heavyTailThis) _heavyTailVotes = Math.Min(_heavyTailVotes + 1, 3);
            else if (!heavyTailThis) _heavyTailVotes = Math.Max(_heavyTailVotes - 1, 0);

            // ===== R/γ 提升的“硬门”=====
            bool lagGate = (_emaLagRms > Math.Min(_baselineLag, _bestLagRms) * 1.10f) || (Math.Abs(stat.LagSamples) >= 1);
            _allowRaiseRG = (!lagGate) && (_noRaiseRounds <= 0) && (_heavyTailVotes >= 2);
            if (lagGate) _noRaiseRounds = Math.Max(_noRaiseRounds, 2); // 检出滞后高 → 锁两轮禁止 R↑/γ↑

            _inFlight = true;

            _ = Task.Run(async () =>
            {
                var oldParams = _f.GetParameters(); // (Q,R,Alpha==gamma)
                float qNew = oldParams.Q, rNew = oldParams.R, gNew = oldParams.Alpha;
                float? conf = null;
                string? rationale = null;
                string action = "APPLY_HEURISTIC"; // 先默认启发式

                try
                {
                    bool usedLLM = false;

                    // 1) LLM（可选）
                    if (_llm != null)
                    {
                        try
                        {
                            var promptUser = _userPromptBuilder(stat, (oldParams.Q, oldParams.R, oldParams.Alpha));
                            var llm = await _llm.GetTuningAsync(
                                systemPrompt: _systemPrompt,
                                userPrompt: promptUser,
                                maxTokens: _llmMaxTokens,
                                temperature: _llmTemp,
                                ct: _cts.Token
                            ).ConfigureAwait(false);

                            if (llm != null)
                            {
                                qNew = SanitizePos(llm.proposed_q, oldParams.Q, 1e-12f, 1e6f);
                                rNew = SanitizePos(llm.proposed_r, oldParams.R, 1e-12f, 1e6f);

                                var alphaAsGamma = llm.proposed_alpha;
                                if (float.IsNaN(alphaAsGamma) || alphaAsGamma <= 0f)
                                    alphaAsGamma = oldParams.Alpha;
                                gNew = Clamp(alphaAsGamma, 0.01f, 0.99f);

                                conf = float.IsNaN(llm.confidence) ? (float?)null : Math.Clamp(llm.confidence, 0f, 1f);
                                rationale = string.IsNullOrWhiteSpace(llm.rationale) ? "LLM" : llm.rationale + " (use proposed_alpha as gamma)";
                                action = "APPLY_LLM";
                                usedLLM = true;

                                Log($"RHF-Raw APPLY_LLM: Q {oldParams.Q:E3}→{qNew:E3}, R {oldParams.R:E3}→{rNew:E3}, γ {oldParams.Alpha:0.###}→{gNew:0.###}");
                            }
                        }
                        catch
                        {
                            // 忽略 LLM 失败
                        }
                    }

                    // 2) 启发式兜底/参考
                    if (!usedLLM)
                    {
                        (qNew, rNew, gNew, rationale) = HeuristicSuggest(stat, (oldParams.Q, oldParams.R, oldParams.Alpha));
                        action = "APPLY_HEURISTIC";
                        Log($"RHF-Raw {action}: {rationale}");
                    }

                    // 2.1) “重尾加成”最终门控（只在允许时才实际提高 R/γ）
                    bool wantTailBoost = rationale != null && rationale.Contains("heavy-tail candidate", StringComparison.OrdinalIgnoreCase);
                    if (wantTailBoost)
                    {
                        if (_allowRaiseRG)
                        {
                            rNew *= 1f + 0.06f;
                            gNew = Clamp(gNew * 1.10f, 0.01f, 0.99f);
                            rationale += " | tail-boost applied (votes ok)";
                        }
                        else
                        {
                            rationale += " | tail-boost blocked (lag/high or cooldown)";
                        }
                    }

                    // 2.5) 自适应安全下限（先于 clamp）
                    var floors = EstimateSafeFloors(stat);
                    qNew = MathF.Max(qNew, floors.qMin);
                    rNew = MathF.Max(rNew, floors.rMin);

                    // 3) 合法化 + 单步限幅
                    float qBefore = qNew, rBefore = rNew, gBefore = gNew;

                    qNew = Clamp(qNew, 1e-12f, 1e6f);
                    rNew = Clamp(rNew, 1e-12f, 1e6f);
                    gNew = Clamp(gNew, 0.01f, 0.99f);

                    qNew = ClampStep(oldParams.Q, qNew, _maxStepScale);
                    rNew = ClampStep(oldParams.R, rNew, _maxStepScale);
                    gNew = ClampStep(oldParams.Alpha, gNew, 0.30f);

                    // 护栏：限制 Q/R 比例
                    qNew = GuardQR(qNew, rNew);

                    // R/γ 常规相对上限
                    float rCap = _baselineParams.R * _rCapFactor;
                    if (rNew > rCap && stat.LagRms > _baselineLag * 0.95f) rNew = rCap;
                    if (gNew > _gCap && stat.LagRms > _baselineLag * 0.95f) gNew = _gCap;

                    // 动态硬帽：滞后样本越大，γ 上限越低；R 在未优于历史最佳时限高
                    float gHardCap = (Math.Abs(stat.LagSamples) >= 2) ? 0.22f : (Math.Abs(stat.LagSamples) >= 1 ? 0.28f : _gCap);
                    if (gNew > gHardCap) gNew = gHardCap;
                    if (_emaLagRms > _bestLagRms * 0.98f)
                    {
                        float rHardCap = _baselineParams.R * 1.30f;
                        if (rNew > rHardCap) rNew = rHardCap;
                    }

                    // 若当前被判定 lagGate，再做微反向“去滞后”微调
                    if (lagHigh)
                    {
                        qNew *= 1.06f;
                        rNew *= 0.98f;
                        gNew = Clamp(gNew * 0.96f, 0.01f, 0.99f);
                    }

                    if (qNew != qBefore || rNew != rBefore || gNew != gBefore)
                    {
                        Log($"RHF-Raw STEP_CLAMP: Q {qBefore:E3}→{qNew:E3}, R {rBefore:E3}→{rNew:E3}, γ {gBefore:0.###}→{gNew:0.###} | maxStep={_maxStepScale:0.##}");
                    }

                    // 4) 应用并等待
                    _f.SetParameters(qNew, rNew, gNew, resetState: false);
                    ClearWindow();
                    _applyAtUpdateCount = _f.UpdateCount;
                    _phase = Phase.AppliedWaiting;
                    _lastTuneUtc = DateTime.UtcNow;

                    TuningEvent?.Invoke(this, new TuningEventArgs
                    {
                        Time = DateTime.Now,
                        Name = _name,
                        Action = action,
                        OldParams = (oldParams.Q, oldParams.R, oldParams.Alpha),
                        NewParams = (qNew, rNew, gNew),
                        Confidence = conf,
                        Rationale = rationale
                    });

                    Log($"RHF-Raw APPLY_CANDIDATE: {action} | Q {oldParams.Q:E3}→{qNew:E3}, R {oldParams.R:E3}→{rNew:E3}, γ {oldParams.Alpha:0.###}→{gNew:0.###}");
                }
                finally
                {
                    // 每次尝试后，禁升轮数回退一轮
                    if (_noRaiseRounds > 0) _noRaiseRounds--;
                    _inFlight = false;
                }
            }, _cts.Token);
        }

        // ======== 调参后半：接受/容忍/回退/停止 ========
        public void PostCheck()
        {
            if (!Enabled || !_hasBaseline) return;
            if (_phase != Phase.AppliedWaiting) return;

            int newSamples = Math.Max(0, _f.UpdateCount - _applyAtUpdateCount);
            int minSamples = _minSamplesOverride > 0 ? _minSamplesOverride : Math.Max(30, (int)MathF.Round(_sampleRateHz * _windowSeconds));
            if (newSamples < minSamples)
            {
                TuningEvent?.Invoke(this, new TuningEventArgs
                {
                    Time = DateTime.Now,
                    Name = _name,
                    Action = "SKIP_WAITING",
                    OldParams = _f.GetParameters(),
                    NewParams = _f.GetParameters(),
                    Rationale = $"waiting newSamples={newSamples}/{minSamples}"
                });

                var cur = _f.GetParameters();
                Log($"[ {DateTime.Now:HH:mm:ss} ] RHF-Raw SKIP_WAITING: Q {cur.Q:E3}→{cur.Q:E3}, R {cur.R:E3}→{cur.R:E3}, γ {cur.Alpha:0.###}→{cur.Alpha:0.###} | waiting newSamples={newSamples}/{minSamples}");
                return;
            }

            var stat = EvaluateWindow();
            var nowParams = _f.GetParameters();
            float curScore = Score(stat, (nowParams.Q, nowParams.R, nowParams.Alpha));

            bool passStrict = curScore >= _baselineScore + _minScoreGain;
            bool lagOkStrict = stat.LagRms <= _baselineLag * (1f + _maxLagWorsen);

            bool passTol = curScore >= _baselineScore - ScoreTolerance;
            bool lagOkTol = stat.LagRms <= _baselineLag * (1f + _maxLagWorsen);

            string why =
                $"minGain={_minScoreGain:0.0}, tol={ScoreTolerance:0.0}, maxLagWorsen={_maxLagWorsen:P0}, " +
                $"score={curScore:0.0} vs base={_baselineScore:0.0}, lag={stat.LagRms:0.000} vs base={_baselineLag:0.000}, " +
                $"smoothRatio={stat.SmoothRatio:0.00}, mad={stat.ResidualMAD:0.000}, |res|={stat.InnovAbsMean:0.000}";

            // ★ 绝对目标-保底接受：进入目标平滑带且滞后未明显变差（≤ +10%）
            bool inTargetSmooth = stat.SmoothRatio >= 0.60f && stat.SmoothRatio <= 0.85f;
            bool lagNotWorse10 = stat.LagRms <= _baselineLag * 1.10f;
            bool heavyTailNow = stat.ResidualMAD >= 0.25f * Math.Max(1e-6f, stat.InStd);

            if (inTargetSmooth && lagNotWorse10 && (heavyTailNow || curScore >= _baselineScore - ScoreTolerance))
            {
                _baselineParams = (nowParams.Q, nowParams.R, nowParams.Alpha);
                _baselineScore = curScore;
                _baselineLag = stat.LagRms;
                _baselineSmooth = Math.Max(0.01f, stat.SmoothRatio);
                _phase = Phase.Idle;
                _acceptCount++;
                _consecutiveRollback = 0;
                _sinceLastAccept = 0;

                _bestLagRms = Math.Min(_bestLagRms, stat.LagRms);
                _heavyTailVotes = 0;

                Log($"RHF-Raw ACCEPT_BASELINE(SAFE): target-band & lag<=+10% | score={curScore:0.0}, lag={stat.LagRms:0.###}, smooth={stat.SmoothRatio:0.###}");
                CheckStopCondition(curScore); // <—— 分数优先 + 稳定兜底
                return;
            }

            // 宽限：显著提升分数且进入目标带，但滞后还偏大 → 给一轮观察
            bool graceCandidate =
                curScore >= _baselineScore + 10f &&
                stat.SmoothRatio >= 0.58f && stat.SmoothRatio <= 0.82f;

            if (graceCandidate && stat.LagRms <= _baselineLag * 1.80f)
            {
                _phase = Phase.Idle; // 不回退，继续观察
                TuningEvent?.Invoke(this, new TuningEventArgs
                {
                    Time = DateTime.Now,
                    Name = _name,
                    Action = "CONTINUE_GRACE",
                    OldParams = (nowParams.Q, nowParams.R, nowParams.Alpha),
                    NewParams = (nowParams.Q, nowParams.R, nowParams.Alpha),
                    Score = curScore,
                    Rms = stat.LagRms,
                    Rationale = "Grace once: big score gain & good smoothness; allow larger lag transient for one window"
                });

                Log($"RHF-Raw CONTINUE_GRACE: score={curScore:0.0}, lag={stat.LagRms:0.###}, smooth={stat.SmoothRatio:0.###} | {why}");
                CheckStopCondition(curScore); // 也检查一次是否稳定达标
                return;
            }

            if (passStrict && lagOkStrict)
            {
                _baselineParams = (nowParams.Q, nowParams.R, nowParams.Alpha);
                _baselineScore = curScore;
                _baselineLag = stat.LagRms;
                _baselineSmooth = Math.Max(0.01f, stat.SmoothRatio);
                _phase = Phase.Idle;
                _acceptCount++;
                _consecutiveRollback = 0;
                _sinceLastAccept = 0;

                _bestLagRms = Math.Min(_bestLagRms, stat.LagRms);
                _heavyTailVotes = 0;

                TuningEvent?.Invoke(this, new TuningEventArgs
                {
                    Time = DateTime.Now,
                    Name = _name,
                    Action = "ACCEPT_BASELINE",
                    OldParams = (nowParams.Q, nowParams.R, nowParams.Alpha),
                    NewParams = (nowParams.Q, nowParams.R, nowParams.Alpha),
                    Score = curScore,
                    Rms = stat.LagRms,
                    Rationale = "Improved vs baseline (strict) | " + why
                });

                Log($"RHF-Raw ACCEPT_BASELINE: Q {nowParams.Q:E3}, R {nowParams.R:E3}, γ {nowParams.Alpha:0.###} | score={curScore:0.0}, lag={stat.LagRms:0.###}, smooth={stat.SmoothRatio:0.###}");

                bool reachTarget = curScore >= TargetScore;
                bool reachRounds = _acceptCount >= MaxAcceptRounds;
                if (OneShot || reachTarget || reachRounds)
                {
                    Enabled = false;
                    TuningEvent?.Invoke(this, new TuningEventArgs
                    {
                        Time = DateTime.Now,
                        Name = _name,
                        Action = "FINAL_PARAMS",
                        OldParams = (nowParams.Q, nowParams.R, nowParams.Alpha),
                        NewParams = (nowParams.Q, nowParams.R, nowParams.Alpha),
                        Score = curScore,
                        Rms = stat.LagRms,
                        Rationale = OneShot
                            ? "OneShot=true：首次严格改进后固定并停止。"
                            : (reachTarget
                                ? $"已达目标分数 {curScore:0.0} ≥ {TargetScore:0.0}，停止。"
                                : $"已连续改进 {MaxAcceptRounds} 轮，自动停止（未达目标分）。")
                    });

                    Log($"RHF-Raw FINAL_PARAMS: score={curScore:0.0} (target {TargetScore:0.0}), rounds={_acceptCount}");
                }
                else
                {
                    CheckStopCondition(curScore); // <—— 统一的停机逻辑
                }
            }
            else if (passTol && lagOkTol)
            {
                _phase = Phase.Idle;
                _consecutiveRollback = 0;
                _sinceLastAccept++;

                TuningEvent?.Invoke(this, new TuningEventArgs
                {
                    Time = DateTime.Now,
                    Name = _name,
                    Action = "CONTINUE",
                    OldParams = (nowParams.Q, nowParams.R, nowParams.Alpha),
                    NewParams = (nowParams.Q, nowParams.R, nowParams.Alpha),
                    Score = curScore,
                    Rms = stat.LagRms,
                    Rationale = "Within tolerant window, keep current params | " + why
                });

                Log($"RHF-Raw CONTINUE: score={curScore:0.0}, lag={stat.LagRms:0.###}, smooth={stat.SmoothRatio:0.###} | {why}");
                CheckStopCondition(curScore);
            }
            else
            {
                var curParams = (nowParams.Q, nowParams.R, nowParams.Alpha);
                _f.SetParameters(_baselineParams.Q, _baselineParams.R, _baselineParams.G, resetState: false);
                ClearWindow();
                _phase = Phase.Idle;

                TuningEvent?.Invoke(this, new TuningEventArgs
                {
                    Time = DateTime.Now,
                    Name = _name,
                    Action = "ROLLBACK",
                    OldParams = curParams,
                    NewParams = (_baselineParams.Q, _baselineParams.R, _baselineParams.G),
                    Score = curScore,
                    Rms = stat.LagRms,
                    Rationale = "No improvement vs baseline | " + why
                });

                Log($"RHF-Raw ROLLBACK: Q {curParams.Q:E3}→{_baselineParams.Q:E3}, R {curParams.R:E3}→{_baselineParams.R:E3}, γ {curParams.Alpha:0.###}→{_baselineParams.G:0.###} | {why}");

                // 连续回退计数 + 基线老化刷新 + 脱困步
                _consecutiveRollback++;
                _sinceLastAccept++;

                // 回退后两轮禁止 R↑/γ↑ 且清空重尾投票
                _noRaiseRounds = Math.Max(_noRaiseRounds, 2);
                _heavyTailVotes = 0;

                if (_consecutiveRollback == 3)
                {
                    var p = _f.GetParameters();
                    float q = p.Q * 1.50f;
                    float r = p.R * 0.92f;
                    float g = Clamp(p.Alpha * 0.90f, 0.01f, 0.99f);
                    _f.SetParameters(q, r, g, resetState: false);
                    ClearWindow();
                    _applyAtUpdateCount = _f.UpdateCount;
                    _phase = Phase.AppliedWaiting;
                    Log("RHF-Raw ESCAPE_STEP: applied anti-lag strong step after 3 consecutive rollbacks");
                }

                if (_consecutiveRollback >= 5 || _sinceLastAccept >= 12)
                {
                    // 基线老化刷新：避免被早期“虚高”基线长期牵制
                    var p = _f.GetParameters(); // ← 先取一次
                    _baselineParams = (p.Q, p.R, p.Alpha);     // γ 使用 Alpha
                    _baselineLag = stat.LagRms;
                    _baselineSmooth = Math.Max(0.01f, stat.SmoothRatio);
                    _baselineScore = Score(stat, (p.Q, p.R, p.Alpha));
                    _consecutiveRollback = 0;
                    _sinceLastAccept = 0;

                    _bestLagRms = Math.Min(_bestLagRms, stat.LagRms);
                    _heavyTailVotes = 0;

                    Log($"RHF-Raw BASELINE_REFRESH: score={_baselineScore:0.0}, lag={_baselineLag:0.###}, smooth={_baselineSmooth:0.###}");
                }

                CheckStopCondition(curScore); // 失败路径也检查是否长期无改善
            }
        }

        // ======== 启发式（先消滞后，后谈重尾；并提供空操作兜底） ========
        private static (float Q, float R, float G, string why) HeuristicSuggest(EvalStat s, (float Q, float R, float G) cur)
        {
            const float lo = 0.60f, hi = 0.85f; // 目标带
            float q = cur.Q, r = cur.R, g = cur.G;
            var sb = new StringBuilder("Heuristic: ");

            // 偏差量化
            float smoothErr = 0f;
            if (s.SmoothRatio < lo) smoothErr = (lo - s.SmoothRatio) / lo;           // 过平滑
            else if (s.SmoothRatio > hi) smoothErr = -(s.SmoothRatio - hi) / hi;     // 不够平滑
            smoothErr = Math.Clamp(smoothErr, -1f, 1f);

            float lagSamp = Math.Abs(s.LagSamples);
            float lagPress = Math.Clamp(lagSamp / 6f, 0f, 1f);                        // 0..1
            float tailPress = Math.Clamp((s.ResidualMAD / Math.Max(1e-6f, s.InStd) - 0.12f) / 0.35f, 0f, 1f);

            // —— 先处理滞后（只要 lagPress > 0.25 或 |lagSamples| >= 1 就认为“滞后偏高”）
            if (lagPress > 0.25f || lagSamp >= 1)
            {
                float k = Math.Clamp(Math.Max(smoothErr, 0f) * 0.4f + 0.6f * lagPress, 0.06f, 0.35f);
                q *= (1f + 1.0f * k);
                r *= (1f - 0.6f * k);
                g = Clamp(g * (1f - 0.35f * k), 0.01f, 0.99f);
                sb.Append($"lag-high -> Q↑ R↓ γ↓ (k={k:0.00}); ");
                return (q, r, g, sb.ToString()); // 直接返回，不再叠加“重尾加成”
            }

            // —— 正常情况再看平滑度
            float step = 0.15f;
            if (smoothErr < 0f) // 不够平滑（输出抖）
            {
                float k = Math.Clamp(-smoothErr * step + 0.10f * tailPress, 0.05f, 0.30f);
                q *= (1f - 0.7f * k);
                r *= (1f + 1.0f * k);
                g = Clamp(g * (1f + 0.35f * k), 0.01f, 0.99f);
                sb.Append($"not-smooth -> q×(1-{0.7f * k:0.00}), r×(1+{1.0f * k:0.00}), γ↑; ");
            }
            else if (smoothErr > 0f) // 过平滑（太粘）
            {
                float k = Math.Clamp(smoothErr * step, 0.05f, 0.30f);
                q *= (1f + 1.0f * k);
                r *= (1f - 0.6f * k);
                g = Clamp(g * (1f - 0.30f * k), 0.01f, 0.99f);
                sb.Append($"over-smooth -> q×(1+{1.0f * k:0.00}), r×(1-{0.6f * k:0.00}), γ↓; ");
            }

            // —— 重尾加成：只标记，由 Step() 的门控决定是否真正加成
            sb.Append("heavy-tail candidate;");

            // —— 兜底：如果没有任何动作，做一个保守微步，避免空操作
            string txt = sb.ToString();
            if (txt.Trim().Equals("Heuristic: heavy-tail candidate;", StringComparison.OrdinalIgnoreCase)
                || txt.Trim().Equals("Heuristic:", StringComparison.OrdinalIgnoreCase))
            {
                q *= 1.04f; r *= 0.99f; g = Clamp(g * 0.99f, 0.01f, 0.99f);
                sb.Append(" fallback anti-lag micro-step;");
            }

            return (q, r, g, sb.ToString());
        }

        // ======== 自适应安全下限（避免将 Q/R 拉到不合理区间） ========
        private (float qMin, float rMin) EstimateSafeFloors(EvalStat s)
        {
            float resStd2 = s.ResidualStd * s.ResidualStd;
            float qFloor = 0.05f * MathF.Max(1e-12f, resStd2);  // 过程噪声不要过小
            float rFloor = 0.25f * MathF.Max(1e-12f, resStd2);  // 测量噪声也给下限
            return (qFloor, rFloor);
        }

        // ======== 评分 ========
        private float Score(EvalStat s, (float Q, float R, float G) cur)
        {
            // A) 相对改善（主导）：平滑比、滞后RMS相对基线的改善
            float smoothImprove = Clamp01((_baselineSmooth - s.SmoothRatio) / Math.Max(0.05f, _baselineSmooth * 0.6f));
            float lagImproveRms = Clamp01((_baselineLag - s.LagRms) / Math.Max(1e-6f, _baselineLag));
            float relScore = 100f * (0.65f * smoothImprove + 0.35f * lagImproveRms);

            // B) 绝对项（目标带 + 样本滞后惩罚）
            float absSmooth = 1f - Clamp01(MathF.Abs(s.SmoothRatio - 0.75f) / 0.40f); // 0.55..0.95 宽带
            float lagPenalty = Clamp01(MathF.Abs(s.LagSamples) / 6f);                  // 超过 2 样本开始扣
            float absScore = 100f * (0.75f * absSmooth + 0.25f * (1f - lagPenalty));

            // C) 重尾项（温和）
            float madRatio = s.ResidualMAD / Math.Max(1e-6f, s.InStd);
            float tail = Clamp01((madRatio - 0.12f) / 0.35f);
            float gammaOk = cur.G >= 0.12f ? 1f : cur.G / 0.12f;
            float tailAdj = 8f * (tail * (2f * gammaOk - 1f)); // -8..+8

            float score = 0.7f * relScore + 0.3f * absScore + tailAdj;
            return MathF.Max(0f, MathF.Min(100f, score));
        }

        // ======== 互相关估计滞后 ========
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

            float inStd = Std(ins);
            float outStd = Std(outs);
            float smoothRatio = (inStd > 1e-6f) ? outStd / inStd : 1f;

            // 残差（用于重尾与幅差）
            var residual = new float[n];
            for (int i = 0; i < n; i++) residual[i] = ins[i] - outs[i];

            float lagRms = Rms(residual);
            float resStd = Std(residual);
            float resMad = Mad(residual);
            float resAbsMean = MeanAbs(residual);

            // 互相关估计滞后（样本）
            int lagSamples = EstimateLagSamplesByXCorr(ins, outs, Math.Min(20, n / 5));

            return new EvalStat
            {
                N = n,
                InStd = inStd,
                OutStd = outStd,
                SmoothRatio = smoothRatio,
                LagRms = lagRms,
                ResidualStd = resStd,
                ResidualMAD = resMad,
                InnovAbsMean = resAbsMean,
                LagSamples = lagSamples
            };
        }

        private static int EstimateLagSamplesByXCorr(float[] x, float[] y, int maxLag)
        {
            int n = Math.Min(x.Length, y.Length);
            if (n <= 4) return 0;
            maxLag = Math.Min(maxLag, n - 2);
            double best = double.NegativeInfinity; int bestLag = 0;

            // 去均值
            double mx = 0, my = 0;
            for (int i = 0; i < n; i++) { mx += x[i]; my += y[i]; }
            mx /= n; my /= n;

            for (int lag = -maxLag; lag <= maxLag; lag++)
            {
                int i0 = Math.Max(0, -lag);
                int i1 = Math.Min(n, n - lag);
                double s = 0, sx = 0, sy = 0;
                int m = i1 - i0;
                if (m < 4) continue;
                for (int i = i0; i < i1; i++)
                {
                    double xv = x[i] - mx;
                    double yv = y[i + lag] - my;
                    s += xv * yv; sx += xv * xv; sy += yv * yv;
                }
                double denom = Math.Sqrt(Math.Max(1e-12, sx * sy));
                double corr = (denom > 0) ? (s / denom) : 0;
                if (corr > best) { best = corr; bestLag = lag; }
            }
            return bestLag; // >0: y滞后x
        }

        private void ClearWindow() { _w = 0; _n = 0; }

        // ======== Prompt（默认，要求 proposed_alpha 作为 gamma=Alpha）========
        private static string BuildDefaultUserPrompt(EvalStat s, (float Q, float R, float G) cur)
        {
            return $@"
Task: Suggest next-step parameters for a Robust H-infinity (H∞) scalar filter to balance smoothness, latency, and robustness.
Current: {{""Q"":{cur.Q:E4},""R"":{cur.R:E4},""gamma"":{cur.G:F3}}}
Stats: {{""N"":{s.N},""InStd"":{s.InStd:F3},""OutStd"":{s.OutStd:F3},""SmoothRatio"":{s.SmoothRatio:F3},""LagRms"":{s.LagRms:F3},""ResidualMAD"":{s.ResidualMAD:F3},""AbsResidualMean"":{s.InnovAbsMean:F3}}}
Rules:
- Return JSON only: {{""proposed_q"":float,""proposed_r"":float,""proposed_alpha"":float,""confidence"":0..1,""rationale"":string}}
- Bounds: 1e-12<=proposed_q<=1e6, 1e-12<=proposed_r<=1e6, 0.01<=proposed_alpha<=0.99
- Prefer ±10~30% relative steps unless stats are extreme.
- Aim SmoothRatio in ~[0.60,0.85] and reduce LagRms; when heavy-tail (MAD large), prefer slightly higher proposed_alpha and R.
";
        }

        // ======== 工具 ========
        private static float Std(float[] a)
        {
            int n = a.Length; if (n == 0) return 0;
            double sum = 0, sumsq = 0;
            for (int i = 0; i < n; i++) { double v = a[i]; sum += v; sumsq += v * v; }
            double mean = sum / n;
            double var = Math.Max(0.0, sumsq / n - mean * mean);
            return (float)Math.Sqrt(var);
        }

        private static float Rms(float[] a)
        {
            int n = a.Length; if (n == 0) return 0;
            double s = 0; for (int i = 0; i < n; i++) s += (double)a[i] * a[i];
            return (float)Math.Sqrt(s / n);
        }

        private static float MeanAbs(float[] a)
        {
            int n = a.Length; if (n == 0) return 0;
            double s = 0; for (int i = 0; i < n; i++) s += Math.Abs(a[i]);
            return (float)(s / n);
        }

        private static float Mad(float[] a)
        {
            int n = a.Length; if (n == 0) return 0;
            var tmp = (float[])a.Clone();
            Array.Sort(tmp);
            float median = tmp[n / 2];
            for (int i = 0; i < n; i++) tmp[i] = MathF.Abs(a[i] - median);
            Array.Sort(tmp);
            return tmp[n / 2];
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
        private static float Clamp01(float x) => x < 0 ? 0 : (x > 1 ? 1 : x);

        private static float ClampStep(float oldV, float newV, float maxStep)
        {
            if (oldV == 0f) return newV;
            float lo = oldV * (1f - maxStep);
            float hi = oldV * (1f + maxStep);
            if (newV < lo) return lo;
            if (newV > hi) return hi;
            return newV;
        }

        private static float SanitizePos(float v, float fallback, float lo, float hi)
        {
            if (float.IsNaN(v) || v <= 0f) return fallback;
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        private void Log(string msg)
        {
            try { Logger?.Invoke(msg); } catch { /* swallow */ }
        }

        // ======== “分数优先 + 稳定兜底” 终止判定 ========
        private void CheckStopCondition(float curScore)
        {
            var p = _f.GetParameters();

            bool tinyMove =
                Math.Abs(p.Q - _baselineParams.Q) <= StableEps * Math.Max(1e-12f, _baselineParams.Q) &&
                Math.Abs(p.R - _baselineParams.R) <= StableEps * Math.Max(1e-12f, _baselineParams.R) &&
                Math.Abs(p.Alpha - _baselineParams.G) <= StableEps * Math.Max(1e-6f, _baselineParams.G);

            bool inSmoothBand = (_baselineSmooth >= 0.60f && _baselineSmooth <= 0.85f);

            // Case 1: 分数已达标，需稳定验证
            if (curScore >= TargetScore)
            {
                _stableCount = tinyMove ? _stableCount + 1 : 0;

                if (_stableCount >= StableRounds)
                {
                    Enabled = false;
                    Log($"RHF-Raw FINAL: 达到目标分 {curScore:F1} 且稳定 {StableRounds} 轮，停止调参。");
                    TuningEvent?.Invoke(this, new TuningEventArgs
                    {
                        Time = DateTime.Now,
                        Name = _name,
                        Action = "FINAL_PARAMS",
                        OldParams = (p.Q, p.R, p.Alpha),
                        NewParams = (p.Q, p.R, p.Alpha),
                        Score = curScore,
                        Rms = _baselineLag,
                        Rationale = $"Target reached & stable ({StableRounds} rounds)."
                    });
                    return;
                }

                Log($"RHF-Raw SCORE_OK: {curScore:F1} 达标，但继续验证稳定性 ({_stableCount}/{StableRounds})。");
                _lastScore = curScore;
                return;
            }

            // Case 2: 未达标但已稳定在目标带（兜底收敛）
            if (tinyMove && inSmoothBand)
            {
                _stableCount++;
                if (_stableCount >= StableRounds)
                {
                    Enabled = false;
                    Log($"RHF-Raw FINAL: 虽未达 {TargetScore:F1}，但参数连续 {StableRounds} 轮变化 <1%，判定收敛。");
                    TuningEvent?.Invoke(this, new TuningEventArgs
                    {
                        Time = DateTime.Now,
                        Name = _name,
                        Action = "FINAL_PARAMS",
                        OldParams = (p.Q, p.R, p.Alpha),
                        NewParams = (p.Q, p.R, p.Alpha),
                        Score = curScore,
                        Rms = _baselineLag,
                        Rationale = $"Stable fallback: {StableRounds} rounds tiny change in target band."
                    });
                    return;
                }
            }
            else
            {
                _stableCount = 0;
            }

            // Case 3: 长期无改进（防止无意义微调）
            if (Math.Abs(curScore - _lastScore) < 0.5f && _acceptCount >= 10)
            {
                Enabled = false;
                Log($"RHF-Raw FINAL: 分数长期无明显改善 (Δ<0.5) 且已多轮接受，停止。");
                TuningEvent?.Invoke(this, new TuningEventArgs
                {
                    Time = DateTime.Now,
                    Name = _name,
                    Action = "FINAL_PARAMS",
                    OldParams = (p.Q, p.R, p.Alpha),
                    NewParams = (p.Q, p.R, p.Alpha),
                    Score = curScore,
                    Rms = _baselineLag,
                    Rationale = "Plateau stop: no meaningful score improvement."
                });
                return;
            }

            _lastScore = curScore;
        }

        // ======== 护栏（Q/R 比例）========
        private readonly (float min, float max) _snrGuard = (0.02f, 50f); // Q/R 的允许范围
        private float GuardQR(float q, float r)
        {
            float snr = q / Math.Max(1e-12f, r);
            if (snr < _snrGuard.min) { q = r * _snrGuard.min; }
            else if (snr > _snrGuard.max) { q = r * _snrGuard.max; }
            return q;
        }

        // === 供 UI 复用评分：返回当前窗口统计 + 与调参器一致的 Score ===
        public (EvalStat Stat, float Score) EvaluateCurrentScore()
        {
            var s = EvaluateWindow();                  // 复用内部窗口统计（含 MAD/Std/互相关滞后等）
            var p = _f.GetParameters();                // (Q, R, G=gamma/Alpha)
            float sc = Score(s, (p.Q, p.R, p.Alpha));  // 与调参器相同的评分函数
            return (s, sc);
        }

    }
}
