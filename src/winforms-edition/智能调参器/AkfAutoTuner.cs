using System;
using System.Threading;
using System.Threading.Tasks;

namespace FilterVisualPlatform
{
    // ===================== 调参日志事件（保持不变） =====================
    public sealed class TuningEventArgs : EventArgs
    {
        public DateTime Time { get; init; }
        public string Name { get; init; }
        public string Action { get; init; } // APPLY_LLM / APPLY_HEURISTIC / ACCEPT_BASELINE / CONTINUE / ROLLBACK / SKIP_WAITING / FINAL_PARAMS
        public (float Q, float R, float A) OldParams { get; init; }
        public (float Q, float R, float A) NewParams { get; init; }
        public float? Confidence { get; init; }
        public string Rationale { get; init; }
        public float? Score { get; init; }
        public float? Rms { get; init; }

        public override string ToString()
        {
            string conf = Confidence.HasValue ? $" conf={Confidence:0.00}" : "";
            string sc = Score.HasValue ? $" score={Score:0.0}" : "";
            string rms = Rms.HasValue ? $" rms={Rms:0.000}" : "";
            return $"[{Time:HH:mm:ss}] {Name} {Action}: " +
                   $"Q {OldParams.Q:E2}→{NewParams.Q:E2}, " +
                   $"R {OldParams.R:E2}→{NewParams.R:E2}, " +
                   $"A {OldParams.A:0.000}→{NewParams.A:0.000}{conf}{sc}{rms}" +
                   (string.IsNullOrWhiteSpace(Rationale) ? "" : $" | {Rationale}");
        }
    }

    // ===================== AKF 自动调参器（仅调 Q / Alpha；R 内部自适应） =====================
    public sealed class AkfAutoTuner
    {
        private readonly AdaptiveKalmanFilter _akf;
        private readonly string _name;
        private readonly TimeSpan _cooldown;
        private readonly int _minSamples;
        private readonly float _minScoreGain;
        private readonly float _maxRmsWorsen;
        private readonly float _maxStepScale;

        private readonly IDeepSeekClient _llm;
        private readonly string _systemPrompt;
        private readonly Func<EvalReport, string> _userPromptBuilder;
        private readonly int _llmMaxTokens;
        private readonly float _llmTemperature;

        // —— 基线与状态 —— 
        private DateTime _lastTuneTime = DateTime.MinValue;
        private (float Q, float A) _baseline;
        private float _baselineScore = float.NegativeInfinity;
        private float _baselineRms = float.PositiveInfinity;
        private float _baselineZVar = float.PositiveInfinity;
        private bool _hasBaseline = false;

        private enum Phase { Idle, AppliedWaiting }
        private Phase _phase = Phase.Idle;
        private int _applyAtUpdateCount = 0;

        private volatile bool _inFlight = false;
        private readonly CancellationTokenSource _cts = new();

        private int _acceptCount = 0;
        private int _consecutivePass = 0;

        // ============ 公开配置 ============
        public bool Enabled { get; set; } = false;

        /// <summary>目标分数，达到后停止（默认 85）。</summary>
        public float TargetScore { get; set; } = 85f;

        /// <summary>容忍窗口的分数回落（默认 3 分）。</summary>
        public float ScoreTolerance { get; set; } = 3f;

        /// <summary>达不到目标分时的兜底接受轮数上限（默认 10）。</summary>
        public int MaxAcceptRounds { get; set; } = 10;

        /// <summary>连续严格通过多少次才真正 ACCEPT（默认 2）。</summary>
        public int AcceptConsecutiveRequired { get; set; } = 2;

        /// <summary>若为 true：首次严格通过即固定并停止。</summary>
        public bool OneShot { get; set; } = false;

        public event EventHandler<TuningEventArgs> TuningEvent;


        private readonly AkfTunerConfig _cfg;


        // 低 K 救场容忍窗口预算
        private int _kRescueBudget = 0;

        public AkfAutoTuner(
            AdaptiveKalmanFilter akf,
            string name,
            IDeepSeekClient llmClient,
            string systemPrompt,
            Func<EvalReport, string> userPromptBuilder,
            AkfTunerConfig config // <<<<<< 新增，用配置驱动所有阈值/步长/目标
        )
        {
            _akf = akf ?? throw new ArgumentNullException(nameof(akf));
            _name = string.IsNullOrWhiteSpace(name) ? "AKF" : name;
            _llm = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
            _systemPrompt = string.IsNullOrWhiteSpace(systemPrompt)
                ? "You are an AKF tuning assistant. Output compact JSON only."
                : systemPrompt;
            _userPromptBuilder = userPromptBuilder
                ?? (rep => BuildDefaultUserPrompt(rep, _akf.Q, _akf.RCurrent, _akf.Alpha));

            _cfg = config ?? new AkfTunerConfig(); // 若未传则用默认配置
        }

        public (float Q, float R, float Alpha) SnapshotParams()
            => (_akf.Q, _akf.RCurrent, _akf.Alpha);

        // ===================== 前半：在评估周期内先调用（触发一次应用） =====================
        public void Step(EvalReport rep)
        {
            if (!Enabled || rep?.Stat == null) return;

            // 初次建立基线
            if (!_hasBaseline)
            {
                if (rep.Stat.N < _cfg.MinSamplesPerEval) return;
                _baseline = (_akf.Q, _akf.Alpha);
                _baselineScore = rep.Score;
                _baselineRms = rep.Stat.ResidualRms;
                _hasBaseline = true;
                _phase = Phase.Idle;
                _kRescueBudget = 0;
                return;
            }

            if (_phase == Phase.AppliedWaiting) return;

            var now = DateTime.UtcNow;
            if (now - _lastTuneTime < _cfg.Cooldown) return;
            if (rep.Stat.N < _cfg.MinSamplesPerEval) return;
            if (_inFlight) return;

            _inFlight = true;

            _ = Task.Run(async () =>
            {
                var oldParams = (_akf.Q, _akf.RCurrent, _akf.Alpha);
                float qNew = oldParams.Q, aNew = oldParams.Alpha;
                float? conf = null;
                string rationale = null;
                string action = null;

                try
                {
                    // 1) 取 LLM / 启发式建议
                    LLMSuggestion llm = null;
                    try
                    {
                        string userPrompt = _userPromptBuilder.Invoke(rep);
                        llm = await _llm.GetTuningAsync(
                                systemPrompt: _systemPrompt,
                                userPrompt: userPrompt,
                                maxTokens: _cfg.LlmMaxTokens,
                                temperature: _cfg.LlmTemperature,
                                ct: _cts.Token
                              ).ConfigureAwait(false);
                    }
                    catch { /* ignore */ }

                    if (llm != null)
                    {
                        qNew = llm.proposed_q;
                        aNew = llm.proposed_alpha;
                        conf = llm.confidence;
                        rationale = llm.rationale;
                        action = "APPLY_LLM";
                    }
                    else
                    {
                        var s = rep.Suggestion;
                        if (s != null) { qNew = s.ProposedQ; aNew = s.ProposedAlpha; }
                        action = "APPLY_HEURISTIC";
                        rationale ??= "Fallback to heuristic suggestion";
                    }

                    // 2) 合法范围
                    qNew = Math.Clamp(qNew, _cfg.QMin, _cfg.QMax);
                    aNew = Math.Clamp(aNew, _cfg.AlphaMin, _cfg.AlphaMax);

                    // 3) 相对偏差度量
                    float k = rep.Stat.MeanK;
                    float kErr = Math.Max(0f, _cfg.TargetK - k); // 低于目标的偏差
                    float z = rep.Stat.ZVar;
                    float zErr = Math.Abs(z - _cfg.TargetZVar);

                    // 4) 低 K 救场（相对化，几何混合而非直达）
                    if (k < _cfg.TargetK - _cfg.LowKBand)
                    {
                        // 把 K 偏差映射到 [RescueCoeffMin, RescueCoeffMax]
                        float t = Math.Clamp(kErr / _cfg.TargetK, 0f, 1f);
                        float coeff = _cfg.RescueCoeffMin + t * (_cfg.RescueCoeffMax - _cfg.RescueCoeffMin);

                        // 目标 Q 仍然相对 R，但只做“几何混合”靠近它，避免一次跳太大
                        float qTarget = Math.Clamp(coeff * oldParams.RCurrent, _cfg.QMin, _cfg.QMax);

                        // 计算目标倍率，并限幅（每次最多 ×1.8）
                        float mul = qTarget / Math.Max(oldParams.Q, 1e-12f);
                        mul = Math.Clamp(mul, 1f / 1.8f, 1.8f);

                        // 几何步：沿着 oldQ -> qTarget 走一小步
                        qNew = oldParams.Q * mul;

                        // Alpha 小降，促进 R 更快自适应（但也限幅）
                        float aMul = 0.98f; // 约等于 -0.02 的小步
                        aNew = Math.Clamp(oldParams.Alpha * aMul, _cfg.AlphaMin, _cfg.AlphaMax);

                        // 发放救场容忍预算
                        _kRescueBudget = Math.Max(_kRescueBudget, _cfg.RescueWindows);

                        rationale = (rationale ?? "") + $" | K-rescue geom-step: target≈{coeff:0.00}·R, clamp×≤1.8";
                    }

                    // 5) ZVar 护栏（相对）：|ZVar-1|>band 才动作
                    if (z > _cfg.TargetZVar + _cfg.ZVarBand)
                    {
                        // 噪声偏大：Alpha ↑（更稳），Q 不要继续上冲
                        aNew = Math.Min(aNew + 0.01f, _cfg.AlphaMax);
                        qNew = Math.Min(qNew, oldParams.Q * (1f + _cfg.StepBase)); // 上限小步或不增
                        rationale = (rationale ?? "") + " | ZVar>target+band => Alpha↑, limit Q↑";
                    }
                    else if (z < _cfg.TargetZVar - _cfg.ZVarBand)
                    {
                        // 噪声偏小：Alpha ↓（更快适应），Q 可适度 ↑
                        aNew = Math.Max(aNew - 0.01f, _cfg.AlphaMin);
                        qNew = Math.Max(qNew, oldParams.Q * (1f + 0.5f * _cfg.StepBase));
                        rationale = (rationale ?? "") + " | ZVar<target-band => Alpha↓, allow Q↑";
                    }

                    // 6) 步长：用“偏差量”决定放宽
                    float stepQ = _cfg.StepBase;
                    if (kErr > 0.15f || zErr > 0.4f) stepQ = Math.Max(stepQ, _cfg.StepMaxWhenSevere);

                    float absMul = qNew / Math.Max(oldParams.Q, 1e-12f);
                    absMul = Math.Clamp(absMul, 0.5f, 2.0f);
                    qNew = oldParams.Q * absMul;

                    // 若本轮不是“救场直达”，则按步长限幅
                    bool isRescue = (k < _cfg.TargetK - _cfg.LowKBand);
                    if (!isRescue)
                        qNew = ClampStep(oldParams.Q, qNew, stepQ);

                    aNew = ClampStep(oldParams.Alpha, aNew, 0.03f); // Alpha 总是小步

                    // 7) 应用
                    _akf.ApplyHyperParams(qNew, aNew, clearDiagnostics: true);
                    _applyAtUpdateCount = _akf.UpdateCount;
                    _phase = Phase.AppliedWaiting;
                    _lastTuneTime = DateTime.UtcNow;

                    var newParams = (_akf.Q, _akf.RCurrent, _akf.Alpha);
                    TuningEvent?.Invoke(this, new TuningEventArgs
                    {
                        Time = DateTime.Now,
                        Name = _name,
                        Action = action,
                        OldParams = oldParams,
                        NewParams = newParams,
                        Confidence = conf,
                        Rationale = rationale
                    });
                }
                finally { _inFlight = false; }
            }, _cts.Token);
        }


        // ===================== 后半：在评估周期内再调用=====================
        public void PostCheck(EvalReport rep)
        {
            if (!Enabled || rep?.Stat == null || !_hasBaseline) return;

            if (_phase == Phase.AppliedWaiting)
            {
                int newSamples = Math.Max(0, _akf.UpdateCount - _applyAtUpdateCount);
                if (newSamples < _cfg.MinSamplesPerEval)
                {
                    TuningEvent?.Invoke(this, new TuningEventArgs
                    {
                        Time = DateTime.Now,
                        Name = _name,
                        Action = "SKIP_WAITING",
                        OldParams = (_akf.Q, _akf.RCurrent, _akf.Alpha),
                        NewParams = (_akf.Q, _akf.RCurrent, _akf.Alpha),
                        Rationale = $"waiting newSamples={newSamples}/{_cfg.MinSamplesPerEval}"
                    });
                    return;
                }
            }
            else return;

            bool passStrict = rep.Score >= _baselineScore + _cfg.MinScoreGain;
            bool rmsOkStrict = rep.Stat.ResidualRms <= _baselineRms * (1f + _cfg.MaxRmsWorsen);
            bool passTolerant = rep.Score >= _baselineScore - _cfg.ScoreTolerance;
            bool rmsOkTolerant = rep.Stat.ResidualRms <= _baselineRms * (1f + _cfg.MaxRmsWorsen);

            float k = rep.Stat.MeanK;

            string decisionWhy =
                $"gain>={_cfg.MinScoreGain:0.0}, tol={_cfg.ScoreTolerance:0.0}, maxRmsWorsen={_cfg.MaxRmsWorsen:P0}, " +
                $"score={rep.Score:0.0} vs base={_baselineScore:0.0}, " +
                $"zv={rep.Stat.ZVar:0.00}, rms={rep.Stat.ResidualRms:0.000} vs base={_baselineRms:0.000}, k={k:0.00}";

            // 达标即停
            if (rep.Score >= _cfg.TargetScore && rmsOkStrict)
            {
                _baseline = (_akf.Q, _akf.Alpha);
                _baselineScore = rep.Score;
                _baselineRms = rep.Stat.ResidualRms;
                _phase = Phase.Idle;
                _kRescueBudget = 0;

                TuningEvent?.Invoke(this, new TuningEventArgs
                {
                    Time = DateTime.Now,
                    Name = _name,
                    Action = "ACCEPT_BASELINE",
                    OldParams = (_akf.Q, _akf.RCurrent, _akf.Alpha),
                    NewParams = (_akf.Q, _akf.RCurrent, _akf.Alpha),
                    Score = rep.Score,
                    Rms = rep.Stat.ResidualRms,
                    Rationale = $"Reach target | {decisionWhy}"
                });

                _akf.FreezeFixedParamsOnly(fixQ: true, fixAlpha: true);
                Enabled = false;

                TuningEvent?.Invoke(this, new TuningEventArgs
                {
                    Time = DateTime.Now,
                    Name = _name,
                    Action = "FINAL_PARAMS",
                    OldParams = (_akf.Q, _akf.RCurrent, _akf.Alpha),
                    NewParams = (_akf.Q, _akf.RCurrent, _akf.Alpha),
                    Score = rep.Score,
                    Rms = rep.Stat.ResidualRms,
                    Rationale = $"已达目标分数 {rep.Score:0.0} ≥ {_cfg.TargetScore:0.0}，停止。"
                });
                return;
            }

            // 低K救场容忍：在预算>0期间，优先 CONTINUE
            if (k < _cfg.TargetK - _cfg.LowKBand && _kRescueBudget > 0)
            {
                _phase = Phase.Idle;
                _kRescueBudget--;
                TuningEvent?.Invoke(this, new TuningEventArgs
                {
                    Time = DateTime.Now,
                    Name = _name,
                    Action = "CONTINUE",
                    OldParams = (_akf.Q, _akf.RCurrent, _akf.Alpha),
                    NewParams = (_akf.Q, _akf.RCurrent, _akf.Alpha),
                    Score = rep.Score,
                    Rms = rep.Stat.ResidualRms,
                    Rationale = "K-rescue window: allow settle | " + decisionWhy
                });
                return;
            }

            // 严格通过
            if (passStrict && rmsOkStrict)
            {
                _baseline = (_akf.Q, _akf.Alpha);
                _baselineScore = rep.Score;
                _baselineRms = rep.Stat.ResidualRms;
                _phase = Phase.Idle;
                _acceptCount++;
                _kRescueBudget = 0; // 接受后清零

                TuningEvent?.Invoke(this, new TuningEventArgs
                {
                    Time = DateTime.Now,
                    Name = _name,
                    Action = "ACCEPT_BASELINE",
                    OldParams = (_akf.Q, _akf.RCurrent, _akf.Alpha),
                    NewParams = (_akf.Q, _akf.RCurrent, _akf.Alpha),
                    Score = rep.Score,
                    Rms = rep.Stat.ResidualRms,
                    Rationale = "Improved vs baseline (strict) | " + decisionWhy
                });

                if (_acceptCount >= _cfg.MaxAcceptRounds || _cfg.OneShot)
                {
                    _akf.FreezeFixedParamsOnly(fixQ: true, fixAlpha: true);
                    Enabled = false;

                    TuningEvent?.Invoke(this, new TuningEventArgs
                    {
                        Time = DateTime.Now,
                        Name = _name,
                        Action = "FINAL_PARAMS",
                        OldParams = (_akf.Q, _akf.RCurrent, _akf.Alpha),
                        NewParams = (_akf.Q, _akf.RCurrent, _akf.Alpha),
                        Score = rep.Score,
                        Rms = rep.Stat.ResidualRms,
                        Rationale = _cfg.OneShot
                            ? "OneShot=true：首次改进后固定并停止。"
                            : $"已连续改进 {_cfg.MaxAcceptRounds} 轮，自动停止（未达目标分）。"
                    });
                }
                return;
            }

            // 容忍
            if (passTolerant && rmsOkTolerant)
            {
                _phase = Phase.Idle;
                TuningEvent?.Invoke(this, new TuningEventArgs
                {
                    Time = DateTime.Now,
                    Name = _name,
                    Action = "CONTINUE",
                    OldParams = (_akf.Q, _akf.RCurrent, _akf.Alpha),
                    NewParams = (_akf.Q, _akf.RCurrent, _akf.Alpha),
                    Score = rep.Score,
                    Rms = rep.Stat.ResidualRms,
                    Rationale = "Within tolerant window | " + decisionWhy
                });
                return;
            }

            // 回退
            var cur = (_akf.Q, _akf.RCurrent, _akf.Alpha);
            _akf.ApplyHyperParams(_baseline.Q, _baseline.A, clearDiagnostics: true);
            _phase = Phase.Idle;
            _kRescueBudget = 0;

            TuningEvent?.Invoke(this, new TuningEventArgs
            {
                Time = DateTime.Now,
                Name = _name,
                Action = "ROLLBACK",
                OldParams = cur,
                NewParams = (_akf.Q, _akf.RCurrent, _akf.Alpha),
                Score = rep.Score,
                Rms = rep.Stat.ResidualRms,
                Rationale = "No improvement vs baseline | " + decisionWhy
            });
        }


        // ===================== Prompt 构造（默认：成对调整 Q/Alpha） =====================
        private static string BuildDefaultUserPrompt(EvalReport rep, float q, float r, float a)
        {
            return $@"
Task:
Propose next-step Adaptive Kalman Filter (AKF) hyperparameters (Q, Alpha)
to improve the whitened residual (Z ~ N(0,1)) and maintain Kalman gain K≈0.5.

CurrentParams:
{{""Q"":{q:E6}, ""R"":[internal]{r:E6}, ""Alpha"":{a:0.###}}}

WindowStats:
{{""N"":{rep?.Stat?.N ?? 0},
  ""ZMean"":{rep?.Stat?.ZMean ?? 0},
  ""ZVar"":{rep?.Stat?.ZVar ?? 0},
  ""MeanK"":{rep?.Stat?.MeanK ?? 0},
  ""RMS"":{rep?.Stat?.ResidualRms ?? 0}
}}

Rules:
- Only adjust Q and Alpha; R is adaptive internally.
- Keep 1e-6 <= Q <= 1e-1, 0.85 <= Alpha <= 0.98.
- Prefer small steps (±10~30%) unless ZVar deviates a lot.
- When increasing Q, slightly decrease Alpha (faster R adaptation).
- When decreasing Q, slightly increase Alpha (smoother tracking).
- If ZVar > 1.2 or RMS significantly rises, moderately increase Q and slightly decrease Alpha.
- If ZVar < 0.8 and RMS falls, moderately decrease Q and slightly decrease Alpha (speed up R adaptation).
- If |ZMean| > 0.12, slightly increase Q.
- Output compact JSON only.

Return JSON:
{{""proposed_q"":float, ""proposed_alpha"":float, ""confidence"":0..1, ""rationale"":string}}";
        }

        // ===================== 小工具 =====================
        private static float ClampStep(float oldV, float newV, float maxStep)
        {
            if (oldV == 0f) return newV;
            float lo = oldV * (1f - maxStep);
            float hi = oldV * (1f + maxStep);
            if (newV < lo) return lo;
            if (newV > hi) return hi;
            return newV;
        }
    }
}

public sealed class AkfTunerConfig
{
    // 目标值
    public float TargetK { get; init; } = 0.5f;
    public float TargetZVar { get; init; } = 1.0f;

    // 合法范围
    public float QMin { get; init; } = 1e-6f;
    public float QMax { get; init; } = 5e-1f;  // 量纲无关的上限保护
    public float AlphaMin { get; init; } = 0.80f;
    public float AlphaMax { get; init; } = 0.99f;


    // 偏差带（相对定义）
    public float LowKBand { get; init; } = 0.15f;   // 低K带：K < TargetK - LowKBand 触发救场
    public float ZVarBand { get; init; } = 0.20f;   // |ZVar-1|>0.2 视为显著偏差



    // 低K救场：Q 相对 R 的系数范围 —— 收紧
    public float RescueCoeffMin { get; init; } = 0.02f;  // 原 0.15f
    public float RescueCoeffMax { get; init; } = 0.15f;  // 原 0.60f

    // 步幅
    public float StepBase { get; init; } = 0.20f;        // 原 0.25f
    public float StepMaxWhenSevere { get; init; } = 0.60f; // 原 0.80f

    // 低K救场容忍窗口预算
    public int RescueWindows { get; init; } = 1;         // 原 2

    // 评分策略
    public float TargetScore { get; init; } = 85f;
    public float ScoreTolerance { get; init; } = 5f;
    public float MinScoreGain { get; init; } = 1.5f;
    public float MaxRmsWorsen { get; init; } = 0.12f;

    // 驱动参数
    public int MinSamplesPerEval { get; init; } = 180;
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromSeconds(8);

    // 兜底
    public int MaxAcceptRounds { get; init; } = 10;
    public bool OneShot { get; init; } = false;

    // LLM
    public int LlmMaxTokens { get; init; } = 128;
    public float LlmTemperature { get; init; } = 0.2f;
}
