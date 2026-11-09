using FilterVisualPlatform.FilterVisualPlatform;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FilterVisualPlatform
{
    public partial class TunerControl : Form
    {
        #region ===== 依赖注入 / 数据源 =====
        private readonly UserDatas _userDatas;
        // 仅用于检测的 chat 客户端
        private IDeepSeekClient _deepSeekChat;
        // 真正用于调参的 reasoner 客户端
        private IDeepSeekClient _deepSeekReasoner;
        #endregion

        #region ===== AKF：四路实例引用（Raw/Filt/TofRaw/TofFilt）=====
        private AdaptiveKalmanFilter _akfRaw, _akfFilt, _akfTofRaw, _akfTofFilt;
        #endregion

        #region ===== 通道枚举 与 调参器容器（为后续其他滤波器预留）=====
        public enum Channel { Raw, Filt, TofRaw, TofFilt }
        private readonly Dictionary<Channel, AkfAutoTuner> _akfTuners = new();
        #endregion

        #region ===== OEF：四路实例引用与调参器 =====
        private OneEuroLikeFilter _oefRaw, _oefFilt, _oefTofRaw, _oefTofFilt;

        private readonly Dictionary<Channel, OneEuroAutoTuner> _oefTuners = new();

        // OEF 目标通道是否在运行
        private Channel? _runningOefChannel = null;
        #endregion

        #region ===== OEF: 数据喂样定时器 & 去重记号 =====
        private System.Windows.Forms.Timer _oefFeedTimer;
        private readonly Dictionary<Channel, int> _oefLastFedUpdateCount = new();
        #endregion

        #region ===== RHF：四路实例与调参器 =====
        private RobustHInfinityFilter _rhfRaw, _rhfFilt, _rhfTofRaw, _rhfTofFilt;

        // 四通道调参器
        private readonly Dictionary<Channel, RobustHInfAutoTuner> _rhfTuners = new();

        // 正在运行的 RHF 目标通道
        private Channel? _runningRhfChannel = null;
        #endregion

        #region ===== RHF: 数据喂样定时器 & 去重记号 =====
        private System.Windows.Forms.Timer _rhfFeedTimer;
        private readonly Dictionary<Channel, int> _rhfLastFedUpdateCount = new();
        #endregion

        #region ===== 运行时状态 / 计时器 / 进度窗 =====
        private System.Windows.Forms.Timer _evalTimer;
        private bool _busy = false;
        private Channel? _runningChannel = null;
        private TaskCompletionSource<bool> _tuneDoneTcs;
        private DateTime _tuneStartUtc;
        private ProgressForm _progress; // 你项目中已有；如无可注释相关三行
        #endregion

        private DateTime _nextDueAkf = DateTime.MinValue;
        private DateTime _nextDueOef = DateTime.MinValue;
        private DateTime _nextDueRhf = DateTime.MinValue;

        private static readonly TimeSpan _akfPeriod = TimeSpan.FromSeconds(3);     // AKF 2 Hz
        private static readonly TimeSpan _oefPeriod = TimeSpan.FromMilliseconds(100); // OEF 10 Hz
        private static readonly TimeSpan _rhfPeriod = TimeSpan.FromSeconds(1);     // RHF 1 Hz


        // 若项目里还没有，就加上这个简单枚举
        private enum Algo { AKF, OEF, RHF }

        // “改参后延迟评估”的记录
        private struct PendingEval
        {
            public Algo Algo;            // 哪组滤波器
            public Channel Ch;           // 你的通道枚举
            public int ApplyAtCount;     // 改参时的 UpdateCount
            public DateTime ApplyAtUtc;  // 改参时间
            public int MinNewSamples;    // 至少新增多少样本再评估
            public TimeSpan MinElapsed;  // 至少等多久再评估
        }

        // 三组共享的“待评估池”
        private readonly Dictionary<(Algo, Channel), PendingEval> _pending = new();

        // 统一阈值（可根据需要调整或做成 UI 配置）
        private const int kMinNewSamples = 500;
        private static readonly TimeSpan kMinElapsed = TimeSpan.FromSeconds(10);

        // —— 初始化/批量赋值暂停（你之前已有，可直接复用）
        private int _suspendManual = 0;
        private bool IsManualSuspended => _suspendManual > 0;
        private void SuspendManual() => _suspendManual++;
        private void ResumeManual() { if (_suspendManual > 0) _suspendManual--; }

        // —— 仅用户操作才响应：为 NumericUpDown 做“用户输入哨兵”
        private readonly Dictionary<NumericUpDown, long> _userEditTicks = new();
        private const int UserEditGraceMs = 1200; // 用户动作后，在此时间窗口内的 ValueChanged 才算“用户触发”

        private void MarkUserEdit(NumericUpDown nud)
        {
            _userEditTicks[nud] = Environment.TickCount64;
        }
        private bool IsUserEditRecent(NumericUpDown nud)
        {
            if (!_userEditTicks.TryGetValue(nud, out var ts)) return nud.Focused; // 有焦点也视作用户在编辑
            return (Environment.TickCount64 - ts) <= UserEditGraceMs || nud.Focused;
        }

        // 给一个 NUD 绑定“用户动作”事件，设置哨兵；只需调用一次
        private void WireUserEditGuard(NumericUpDown nud)
        {
            nud.MouseDown += (_, __) => MarkUserEdit(nud);
            nud.KeyDown += (_, __) => MarkUserEdit(nud);
            nud.Enter += (_, __) => MarkUserEdit(nud);
            // MouseWheel 需要显式绑定到控件或窗体
            nud.MouseWheel += (_, __) => MarkUserEdit(nud);
        }

        // 方便用 using 包裹一段代码
        private sealed class ManualSuspendScope : IDisposable
        {
            private readonly Action _onDispose;
            public ManualSuspendScope(Action onEnter, Action onDispose)
            {
                onEnter();
                _onDispose = onDispose;
            }
            public void Dispose() => _onDispose();
        }

        // 快捷创建 scope： using (SuspendManualScope()) { /* 批量设值 */ }
        private ManualSuspendScope SuspendManualScope()
            => new ManualSuspendScope(SuspendManual, ResumeManual);

        #region ===== 构造 / Load / 关闭释放 =====
        public TunerControl(UserDatas userDatas)
        {
            InitializeComponent();
            _userDatas = userDatas;
        }

        private async void TunerControl_Load(object sender, EventArgs e)
        {

            using (SuspendManualScope()) // 整个初始化阶段暂停
            {
                InitNumericDefaults();
                InitNumeric_OEF();
                InitNumeric_RHF();
                // 其他默认赋值、从配置恢复等……
            }



            // 0) DeepSeek 连通性检测（chat），成功后实例化 reasoner 供调参使用
            bool llmReady = await CheckDeepSeekConnectivityAsync();

            // 1) 绑定四个 AKF（必须存在）
            _akfRaw = _userDatas.adaptiveKalmanFilter1 ?? throw new InvalidOperationException("adaptiveKalmanFilter1 is null");
            _akfFilt = _userDatas.adaptiveKalmanFilter2 ?? throw new InvalidOperationException("adaptiveKalmanFilter2 is null");
            _akfTofRaw = _userDatas.adaptiveKalmanFilter3 ?? throw new InvalidOperationException("adaptiveKalmanFilter3 is null");
            _akfTofFilt = _userDatas.adaptiveKalmanFilter4 ?? throw new InvalidOperationException("adaptiveKalmanFilter4 is null");

            // 3) 目标通道下拉框
            comboTarget.DropDownStyle = ComboBoxStyle.DropDownList;
            comboTarget.Items.Clear();
            comboTarget.Items.AddRange(new object[] { Channel.Raw, Channel.Filt, Channel.TofRaw, Channel.TofFilt });
            comboTarget.SelectedIndexChanged += ComboTarget_SelectedIndexChanged;
            comboTarget.SelectedIndex = 0; // 默认 Raw

            // 4) 按钮
            btnAutoTune.Enabled = llmReady; // LLM 不可用就禁用自动调参
            btnAutoTune.Click += (_, __) => StartAutoTune(GetSelectedChannel());
            btnRestart.Click += (_, __) => RestartCurrentAkf();

            // 5) 调参驱动定时器（1Hz）
            _evalTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _evalTimer.Tick += EvalTimer_Tick;

            // 6) 初始化 UI
            var initCh = GetSelectedChannel();
            WriteParamsToControls(initCh, GetAkf(initCh));
            SetRowEnabled(initCh);
            HookManualHandlers();

            //txtLog.Clear();


            //OEF
            // ===== OEF: 绑定四个滤波实例 =====
            _oefRaw = _userDatas.oneEuroLikeFilter1 ?? throw new InvalidOperationException("oneEuroLikeFilter1 is null");
            _oefFilt = _userDatas.oneEuroLikeFilter2 ?? throw new InvalidOperationException("oneEuroLikeFilter2 is null");
            _oefTofRaw = _userDatas.oneEuroLikeFilter3 ?? throw new InvalidOperationException("oneEuroLikeFilter3 is null");
            _oefTofFilt = _userDatas.oneEuroLikeFilter4 ?? throw new InvalidOperationException("oneEuroLikeFilter4 is null");

            // ===== OEF: NumericUpDown 初始化（范围/步长）=====

            // ===== OEF: 目标下拉 =====
            comboTarget_oef.DropDownStyle = ComboBoxStyle.DropDownList;
            comboTarget_oef.Items.Clear();
            comboTarget_oef.Items.AddRange(new object[] { Channel.Raw, Channel.Filt, Channel.TofRaw, Channel.TofFilt });
            comboTarget_oef.SelectedIndexChanged += ComboTarget_Oef_SelectedIndexChanged;
            comboTarget_oef.SelectedIndex = 0;

            // ===== OEF: 按钮 =====
            btnAutoTune_oef.Enabled = (_deepSeekReasoner != null); // chat 检测成功后 reasoner 已创建
            btnAutoTune_oef.Click += (_, __) => StartAutoTune_Oef(GetSelectedOefChannel());
            btnRestart_oef.Click += (_, __) => RestartCurrentOef();

            // ===== OEF: 初始化 UI 值并仅使选中行可编辑 =====
            var initOef = GetSelectedOefChannel();
            WriteParamsToControls_Oef(initOef, GetOef(initOef));
            SetRowEnabled_Oef(initOef);

            // ===== OEF: 喂样定时器（把实时流快照喂给调参器）=====
            _oefFeedTimer = new System.Windows.Forms.Timer { Interval = 10 }; // 10 Hz 采样喂给窗口
            _oefFeedTimer.Tick += (s, ev) =>
            {
                // 仅当存在已创建的 OEF 调参器时才喂样
                foreach (var kv in _oefTuners)
                {
                    var ch = kv.Key;
                    var tuner = kv.Value;
                    var f = GetOef(ch);

                    // 用 UpdateCount 去重：只在滤波器有新样本时才喂一次当前快照
                    int uc = f.UpdateCount;
                    if (!_oefLastFedUpdateCount.TryGetValue(ch, out var fed) || uc > fed)
                    {
                        float input = f.LastInput;   // 来自 OneEuroLikeFilter.Update 记录的最近输入
                        float output = f.LastOutput;  // 最近输出
                        tuner.FeedSample(input, output);
                        _oefLastFedUpdateCount[ch] = uc;
                    }
                }
            };
            _oefFeedTimer.Start();

            //RHF
            // ===== RHF: 绑定四个滤波实例 =====
            _rhfRaw = _userDatas.robustHInfinityFilter1 ?? throw new InvalidOperationException("robustHInfinityFilter1 is null");
            _rhfFilt = _userDatas.robustHInfinityFilter2 ?? throw new InvalidOperationException("robustHInfinityFilter2 is null");
            _rhfTofRaw = _userDatas.robustHInfinityFilter3 ?? throw new InvalidOperationException("robustHInfinityFilter3 is null");
            _rhfTofFilt = _userDatas.robustHInfinityFilter4 ?? throw new InvalidOperationException("robustHInfinityFilter4 is null");

            // ===== RHF: NumericUpDown 初始化（Q/R 为方差; γ∈(0,1)）=====

            // ===== RHF: 目标下拉 =====
            comboTarget_rhf.DropDownStyle = ComboBoxStyle.DropDownList;
            comboTarget_rhf.Items.Clear();
            comboTarget_rhf.Items.AddRange(new object[] { Channel.Raw, Channel.Filt, Channel.TofRaw, Channel.TofFilt });
            comboTarget_rhf.SelectedIndexChanged += ComboTarget_Rhf_SelectedIndexChanged;
            comboTarget_rhf.SelectedIndex = 0;

            // ===== RHF: 按钮 =====
            btnAutoTune_rhf.Enabled = (_deepSeekReasoner != null);
            btnAutoTune_rhf.Click += (_, __) => StartAutoTune_Rhf(GetSelectedRhfChannel());
            btnRestart_rhf.Click += (_, __) => RestartCurrentRHF();

            // ===== RHF: 初始化 UI 值并仅使选中行可编辑 =====
            var initRhf = GetSelectedRhfChannel();
            WriteParamsToControls_Rhf(initRhf, GetRHF(initRhf));
            SetRowEnabled_Rhf(initRhf);

            // ===== RHF: 喂样定时器（把实时流快照喂给调参器）=====
            _rhfFeedTimer = new System.Windows.Forms.Timer { Interval = 10 }; // 与 OEF 一致：10ms tick
            _rhfFeedTimer.Tick += (s, ev) =>
            {
                foreach (var kv in _rhfTuners)
                {
                    var ch = kv.Key;
                    var tuner = kv.Value;
                    var f = GetRHF(ch);

                    int uc = f.UpdateCount;
                    if (!_rhfLastFedUpdateCount.TryGetValue(ch, out var fed) || uc > fed)
                    {
                        float input = f.LastInput;   // 需在 RobustHInfinityFilter.Update 内记录并暴露
                        float output = f.LastOutput;
                        tuner.FeedSample(input, output);
                        _rhfLastFedUpdateCount[ch] = uc;
                    }
                }
            };
            _rhfFeedTimer.Start();

            StartScoreFeedTimers();

        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);

            StopScoreFeedTimers();

            // 避免资源泄露
            (_deepSeekChat as IDisposable)?.Dispose();
            (_deepSeekReasoner as IDisposable)?.Dispose();
        }
        #endregion

        #region ===== DeepSeek 连通性检测（chat）并准备 reasoner =====
        private async Task<bool> CheckDeepSeekConnectivityAsync()
        {
            AppendLog("🔌 正在检测 DeepSeek 连通性（chat）…");
            AppendLogOef("🔌 正在检测 DeepSeek 连通性（chat）…");
            AppendLogRHF("🔌 正在检测 DeepSeek 连通性（chat）…");

            string key = GetDeepSeekKey();
            if (string.IsNullOrWhiteSpace(key))
            {
                AppendLog("❌ DeepSeekApiKey 未配置（App.config: <appSettings>）。");
                AppendLogOef("❌ DeepSeekApiKey 未配置（App.config: <appSettings>）。");
                AppendLogRHF("❌ DeepSeekApiKey 未配置（App.config: <appSettings>）。");


                MessageBox.Show(this, "未检测到 DeepSeekApiKey，AI 调参将不可用（可使用启发式/手动调参）。",
                    "DeepSeek 未配置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            try
            {
                _deepSeekChat = new DeepSeekClient(key, model: "deepseek-chat");
                var (ok, detail) = await _deepSeekChat.PingAsync().ConfigureAwait(true);
                if (!ok)
                {
                    AppendLog("❌ Chat Ping 失败：" + detail);
                    AppendLogOef("❌ Chat Ping 失败：" + detail);
                    AppendLogRHF("❌ Chat Ping 失败：" + detail);
                    MessageBox.Show(this, $"DeepSeek Chat 模型不可用：{detail}\n将使用启发式/手动调参。",
                        "AI 调参不可用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                AppendLog("✅ Chat Ping 通过。");
                AppendLogOef("✅ Chat Ping 通过。");
                AppendLogRHF("✅ Chat Ping 通过。");

                // 成功后创建 reasoner 用于调参（常驻）
                _deepSeekReasoner = new DeepSeekClient(key, model: "deepseek-reasoner");
                AppendLog("🧠 已切换 reasoner 用于智能调参。");
                AppendLogOef("🧠 已切换 reasoner 用于智能调参。");
                AppendLogRHF("🧠 已切换 reasoner 用于智能调参。");
                return true;
            }
            catch (Exception ex)
            {
                AppendLog("❌ DeepSeek 检测异常：" + ex.Message);
                AppendLogOef("❌ DeepSeek 检测异常：" + ex.Message);
                AppendLogRHF("❌ DeepSeek 检测异常：" + ex.Message);



                MessageBox.Show(this, "DeepSeek 检测异常，AI 调参不可用（可使用启发式/手动调参）。",
                    "AI 调参不可用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private static string GetDeepSeekKey()
        {
            string key = ConfigurationManager.AppSettings["DeepSeekApiKey"];
            return string.IsNullOrWhiteSpace(key) ? null : key;
        }

        private void AppendLog(string line)
        {
            if (txtLog == null) return;
            if (txtLog.InvokeRequired)
                txtLog.BeginInvoke(new Action(() => txtLog.AppendText(line + Environment.NewLine)));
            else
                txtLog.AppendText(line + Environment.NewLine);
        }
        #endregion

        #region ===== UI：通道选择 Only Row Enabled =====
        private void ComboTarget_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_busy)
            {
                // 正在调参，禁止切换，回滚选择
                if (_runningChannel.HasValue)
                {
                    var idx = comboTarget.Items.IndexOf(_runningChannel.Value);
                    if (idx >= 0) comboTarget.SelectedIndex = idx;
                }
                MessageBox.Show(this, "正在进行 AI 调参，暂不可切换目标组。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var ch = GetSelectedChannel();
            WriteParamsToControls(ch, GetAkf(ch));
            SetRowEnabled(ch);
        }

        private void SetRowEnabled(Channel ch)
        {
            // 全禁
            nud_Raw_Q.Enabled = nud_Raw_A.Enabled = false;
            nud_Filt_Q.Enabled = nud_Filt_A.Enabled = false;
            nud_TofRaw_Q.Enabled = nud_TofRaw_A.Enabled = false;
            nud_TofFilt_Q.Enabled = nud_TofFilt_A.Enabled = false;

            // 仅选中行可编辑
            switch (ch)
            {
                case Channel.Raw:
                    nud_Raw_Q.Enabled = true; nud_Raw_A.Enabled = true; break;
                case Channel.Filt:
                    nud_Filt_Q.Enabled = true; nud_Filt_A.Enabled = true; break;
                case Channel.TofRaw:
                    nud_TofRaw_Q.Enabled = true; nud_TofRaw_A.Enabled = true; break;
                case Channel.TofFilt:
                    nud_TofFilt_Q.Enabled = true; nud_TofFilt_A.Enabled = true; break;
            }
        }
        #endregion

        #region ===== 公共：获取/写入 控件参数 与 评估输出 =====
        private Channel GetSelectedChannel()
            => comboTarget.SelectedItem is Channel ch ? ch : Channel.Raw;

        private AdaptiveKalmanFilter GetAkf(Channel ch) => ch switch
        {
            Channel.Raw => _akfRaw,
            Channel.Filt => _akfFilt,
            Channel.TofRaw => _akfTofRaw,
            Channel.TofFilt => _akfTofFilt,
            _ => throw new ArgumentOutOfRangeException(nameof(ch))
        };

        private void InitNumericDefaults()
        {
            using (SuspendManualScope()) // ← 新增：整个函数期间暂停手动调参
            {
                InitNumeric(nud_Raw_Q, 6, 0.000001M, 0, 1000000);
                InitNumeric(nud_Raw_A, 3, 0.010M, 0.700M, 0.995M);

                InitNumeric(nud_Filt_Q, 6, 0.000001M, 0, 1000000);
                InitNumeric(nud_Filt_A, 3, 0.010M, 0.700M, 0.995M);

                InitNumeric(nud_TofRaw_Q, 6, 0.000001M, 0, 1000000);
                InitNumeric(nud_TofRaw_A, 3, 0.010M, 0.700M, 0.995M);

                InitNumeric(nud_TofFilt_Q, 6, 0.000001M, 0, 1000000);
                InitNumeric(nud_TofFilt_A, 3, 0.010M, 0.700M, 0.995M);
            }
        }

        private static void InitNumeric(NumericUpDown n, int dp, decimal inc, decimal min, decimal max)
        {
            n.DecimalPlaces = dp;
            n.Increment = inc;
            n.Minimum = min; n.Maximum = max;
        }

        private void WriteParamsToControls(Channel ch, AdaptiveKalmanFilter akf)
        {
            switch (ch)
            {
                case Channel.Raw:
                    nud_Raw_Q.Value = (decimal)akf.Q; nud_Raw_A.Value = (decimal)akf.Alpha; break;
                case Channel.Filt:
                    nud_Filt_Q.Value = (decimal)akf.Q; nud_Filt_A.Value = (decimal)akf.Alpha; break;
                case Channel.TofRaw:
                    nud_TofRaw_Q.Value = (decimal)akf.Q; nud_TofRaw_A.Value = (decimal)akf.Alpha; break;
                case Channel.TofFilt:
                    nud_TofFilt_Q.Value = (decimal)akf.Q; nud_TofFilt_A.Value = (decimal)akf.Alpha; break;
            }
        }

        private void ShowEval(Channel ch, AdaptiveKalmanFilter akf)
        {
            var rep = akf.Evaluate();
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] {ch} 评估：");
            sb.AppendLine($"  Q={akf.Q:E6}, Alpha={akf.Alpha:0.000}");
            if (rep?.Stat != null)
            {
                sb.AppendLine($"  Score={rep.Score:0.0}（{rep.Verdict}）  N={rep.Stat.N}");
                sb.AppendLine($"  ZMean={rep.Stat.ZMean:+0.000;-0.000}  ZVar={rep.Stat.ZVar:0.00}  " +
                              $"K={rep.Stat.MeanK:0.00}  RMS={rep.Stat.ResidualRms:0.000}");
                if (rep.Issues != null && rep.Issues.Count > 0)
                {
                    sb.AppendLine("  提示：");
                    foreach (var s in rep.Issues) sb.AppendLine("    - " + s);
                }
            }
            txtLog.AppendText(sb.ToString() + Environment.NewLine);
        }
        #endregion

        #region ===== 定时驱动：只驱动当前已启用的单个 Tuner（AKF/OEF/RHF 三选一）=====
        #region ===== 定时驱动（带节拍门控）：AKF/RHF 1Hz，OEF 10Hz =====
        private void EvalTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                var now = DateTime.UtcNow;

                // 只会有一种在跑；分别用独立门控保障各自节拍

                // ---- RHF 在跑？（1 Hz）----
                if (_runningRhfChannel.HasValue)
                {
                    // 首次 Tick 自动立即执行
                    if (_nextDueRhf == DateTime.MinValue) _nextDueRhf = now;

                    if (now >= _nextDueRhf)
                    {
                        var ch = _runningRhfChannel.Value;
                        if (_rhfTuners.TryGetValue(ch, out var tuner) && tuner != null)
                        {
                            tuner.Step();
                            tuner.PostCheck();
                        }
                        // 下次时间：严格按 1Hz 排程，避免漂移
                        _nextDueRhf = now + _rhfPeriod;
                    }
                    return; // 保证“三选一”只跑一个体系
                }

                // ---- OEF 在跑？（10 Hz）----
                if (_runningOefChannel.HasValue)
                {
                    if (_nextDueOef == DateTime.MinValue) _nextDueOef = now;

                    if (now >= _nextDueOef)
                    {
                        var ch = _runningOefChannel.Value;
                        if (_oefTuners.TryGetValue(ch, out var tuner) && tuner != null)
                        {
                            tuner.Step();      // OEF 内部自取窗口
                            tuner.PostCheck(); // OEF 内部自取窗口
                        }
                        _nextDueOef = now + _oefPeriod; // 10Hz
                    }
                    return;
                }

                // ---- AKF 在跑？（1 Hz）----
                if (_runningChannel.HasValue)
                {
                    if (_nextDueAkf == DateTime.MinValue) _nextDueAkf = now;

                    if (now >= _nextDueAkf)
                    {
                        var ch = _runningChannel.Value;
                        if (_akfTuners.TryGetValue(ch, out var tuner) && tuner != null)
                        {
                            // AKF 需要外部 Evaluate()
                            var rep = GetAkf(ch).Evaluate();
                            tuner.Step(rep);
                            tuner.PostCheck(rep);
                        }
                        _nextDueAkf = now + _akfPeriod; // 1Hz
                    }
                    return;
                }

                OnEvalTimerPendingCheck();

            }
            catch (Exception ex)
            {
                AppendLog("⛔ 调参定时循环异常：" + ex.Message);
            }
        }

        // 在 EvalTimer_Tick 的**末尾**调用：OnEvalTimerPendingCheck();

        private readonly Dictionary<(Algo, Channel), DateTime> _lastPendingLog = new();
        private void OnEvalTimerPendingCheck()
        {
            if (_pending.Count == 0) return;

            var now = DateTime.UtcNow;
            var done = new List<(Algo, Channel)>(_pending.Count);

            // 拍快照以防遍历时集合被修改
            foreach (var kv in _pending.ToArray())
            {
                var key = kv.Key;    // (Algo, Channel)
                var pv = kv.Value;

                // 读取对应滤波器的 UpdateCount
                int curCount = key.Item1 switch
                {
                    Algo.AKF => GetAkf(key.Item2).UpdateCount,
                    Algo.OEF => GetOef(key.Item2).UpdateCount,
                    Algo.RHF => GetRHF(key.Item2).UpdateCount,   // 若你的方法名是 GetRHF，请改这里
                    _ => 0
                };

                // 计算新增样本；若出现回卷/清零，做一次自愈
                int d = curCount - pv.ApplyAtCount;
                if (d < 0)
                {
                    // 自愈：把起点同步为当前 UC，避免一直“ΔN=0”
                    pv.ApplyAtCount = curCount;
                    _pending[key] = pv;
                    d = 0;
                }

                bool enoughSamples = d >= pv.MinNewSamples;
                bool enoughTime = (now - pv.ApplyAtUtc) >= pv.MinElapsed;

                if (enoughSamples && enoughTime)
                {
                    // 达标：真正评估 + 日志
                    switch (key.Item1)
                    {
                        case Algo.AKF:
                            ShowEval(key.Item2, GetAkf(key.Item2));
                            ShowStatusAKF(key.Item2, $"评估完成：新增样本 {d}，Δt={(now - pv.ApplyAtUtc).TotalSeconds:F1}s");
                            break;

                        case Algo.OEF:
                            ShowEval_Oef(key.Item2, GetOef(key.Item2));   // 若方法名是 ShowEval_OEF，请改这里
                            ShowStatusOEF(key.Item2, $"评估完成：新增样本 {d}，Δt={(now - pv.ApplyAtUtc).TotalSeconds:F1}s");
                            break;

                        case Algo.RHF:
                            ShowEval_Rhf(key.Item2, GetRHF(key.Item2));   // 若方法名是 ShowEval_RHF，请改这里
                            ShowStatusRHF(key.Item2, $"评估完成：新增样本 {d}，Δt={(now - pv.ApplyAtUtc).TotalSeconds:F1}s");
                            break;
                    }

                    done.Add(key);
                    _lastPendingLog.Remove(key); // 清节流痕迹
                }
                else
                {
                    // 节流输出“等待评估”
                    if (!_lastPendingLog.TryGetValue(key, out var last) || (now - last).TotalSeconds >= 1.0)
                    {
                        int remainN = Math.Max(0, pv.MinNewSamples - d);
                        double remainS = Math.Max(0, (pv.MinElapsed - (now - pv.ApplyAtUtc)).TotalSeconds);

                        switch (key.Item1)
                        {
                            case Algo.AKF: ShowStatusAKF(key.Item2, $"等待评估：剩余样本 {remainN}，剩余 {remainS:F1}s"); break;
                            case Algo.OEF: ShowStatusOEF(key.Item2, $"等待评估：剩余样本 {remainN}，剩余 {remainS:F1}s"); break;
                            case Algo.RHF: ShowStatusRHF(key.Item2, $"等待评估：剩余样本 {remainN}，剩余 {remainS:F1}s"); break;
                        }

                        _lastPendingLog[key] = now;
                    }
                }
            }

            // 移除已完成项
            foreach (var k in done) _pending.Remove(k);
        }



        #endregion
        #endregion

        #region ===== 自动调参（AKF：单通道，一次性）=====
        private async void StartAutoTune(Channel ch)
        {
            if (_busy)
            {
                MessageBox.Show(this, "已有调参任务在进行中，请稍候。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_deepSeekReasoner == null)
            {
                MessageBox.Show(this, "DeepSeek 未就绪，无法进行 AI 调参。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _busy = true;
            _runningChannel = ch;
            comboTarget.Enabled = false;

            // 构建或复用该通道的 tuner
            if (!_akfTuners.ContainsKey(ch))
            {
                var akf = GetAkf(ch);
                var name = $"AKF-{ch}";

                // —— LLM 提示词（按通道动态拼装）——
                string systemPrompt =
                    "You are an AKF tuning assistant. Output compact JSON only. " +
                    "Schema:{\"proposed_q\":float,\"proposed_alpha\":float,\"confidence\":0..1,\"rationale\":string}";

                Func<EvalReport, string> userPromptBuilder = rep =>
                    $"Filter={name}; Cur: Q={akf.Q:E6}, A={akf.Alpha:0.###}. " +
                    $"Win: N={rep?.Stat?.N ?? 0}, Zm={rep?.Stat?.ZMean ?? 0:0.###}, " +
                    $"Zv={rep?.Stat?.ZVar ?? 0:0.###}, K={rep?.Stat?.MeanK ?? 0:0.###}, RMS={rep?.Stat?.ResidualRms ?? 0:0.###}. " +
                    "Constraints: Q>=1e-12, 0.70<=A<=0.995; small steps unless Zv very off; |Zm|>0.12 => +Q.";

                // —— 构建调参器 —— 
                var cfg = new AkfTunerConfig
                {
                    TargetK = 0.5f,
                    TargetZVar = 1.0f,
                    QMin = 1e-6f,
                    QMax = 5e-1f,
                    AlphaMin = 0.80f,
                    AlphaMax = 0.99f,
                    StepBase = 0.25f,
                    StepMaxWhenSevere = 0.80f,
                    LowKBand = 0.15f,
                    ZVarBand = 0.20f,
                    RescueCoeffMin = 0.15f,
                    RescueCoeffMax = 0.60f,
                    RescueWindows = 2,
                    TargetScore = 72f,
                    ScoreTolerance = 5f,
                    MinScoreGain = 1.5f,
                    MaxRmsWorsen = 0.12f,
                    MinSamplesPerEval = 80,
                    Cooldown = TimeSpan.FromSeconds(5),
                    MaxAcceptRounds = 10,
                    OneShot = false,
                    LlmMaxTokens = 128,
                    LlmTemperature = 0.2f
                };

                var tuner = new AkfAutoTuner(
                    akf: akf,
                    name: name,
                    llmClient: _deepSeekReasoner,
                    systemPrompt: systemPrompt,
                    userPromptBuilder: userPromptBuilder,
                    config: cfg
                )
                {
                    Enabled = true
                };

                // 输出所有关键事件，便于观察过程
                tuner.TuningEvent += (_, args) =>
                {
                    // 过程日志（建议都打出来，便于诊断卡在哪）
                    if (args.Action is "APPLY_LLM" or "APPLY_HEURISTIC" or "CONTINUE" or "ROLLBACK" or "ACCEPT_BASELINE" or "FINAL_PARAMS" or "SKIP_WAITING")
                        AppendLog(args.ToString());

                    if (args.Action == "FINAL_PARAMS" && ch == _runningChannel)
                        _tuneDoneTcs?.TrySetResult(true);
                };

                _akfTuners[ch] = tuner;
            }

            // 进度窗 & 计时启动（如无 ProgressForm，可注释下3行）
            _tuneStartUtc = DateTime.UtcNow;
            _tuneDoneTcs = new TaskCompletionSource<bool>();
            _progress = new ProgressForm($"{ch} 智能调参中…");
            BeginInvoke(new Action(() => _progress.Show(this)));

            _evalTimer.Start();

            // 最长 5 分钟
            var timeout = Task.Delay(TimeSpan.FromMinutes(5));
            var finished = await Task.WhenAny(_tuneDoneTcs.Task, timeout);

            // 结束：停驱动 & 关闭进度窗
            _evalTimer.Stop();
            CloseProgress();

            // —— 直接把参数写回控件 + 立即评估输出（无二次确认）——
            var akfCur = GetAkf(ch);
            WriteParamsToControls(ch, akfCur);
            ShowEval(ch, akfCur);

            var used = (DateTime.UtcNow - _tuneStartUtc).TotalSeconds;
            if (finished == timeout)
            {
                MessageBox.Show(this, $"{ch} 智能调参超时（>300s）。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                MessageBox.Show(this, $"{ch} 智能调参完毕，所用时间为：{used:F1}s", "完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            // 恢复 UI
            comboTarget.Enabled = true;
            _runningChannel = null;
            _busy = false;
        }
        #endregion

        #region ===== 手动：重启当前 AKF（不改参数），并输出一次评估 =====
        private void RestartCurrentAkf()
        {
            var ch = GetSelectedChannel();
            var akf = GetAkf(ch);

            akf.Reset(); // 仅清状态/窗口，Q/Alpha 保持，R 内部自适应继续
            ShowEval(ch, akf);

            MessageBox.Show(this, $"{ch} 滤波器已重启。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        #endregion

        #region ===== 工具：关闭进度窗 =====
        private void CloseProgress()
        {
            try
            {
                if (_progress != null && !_progress.IsDisposed)
                {
                    if (_progress.InvokeRequired)
                        _progress.BeginInvoke(new Action(() => _progress.Close()));
                    else
                        _progress.Close();
                }
            }
            catch { /* ignore */ }
            finally { _progress = null; }
        }
        #endregion


        /*************************************************低通自适应************************************************************************/
        #region ===== OEF: NumericUpDown 默认配置 =====
        private void InitNumeric_OEF()
        {
            using (SuspendManualScope()) // ← 新增
            {
                InitNumeric(nud_Raw_Q_oef, 3, 0.010M, 0.001M, 50M);
                InitNumeric(nud_Raw_R_oef, 3, 0.010M, 0.001M, 50M);
                InitNumeric(nud_Raw_A_oef, 3, 0.010M, 0.000M, 2.000M);

                InitNumeric(nud_Filt_Q_oef, 3, 0.010M, 0.001M, 50M);
                InitNumeric(nud_Filt_R_oef, 3, 0.010M, 0.001M, 50M);
                InitNumeric(nud_Filt_A_oef, 3, 0.010M, 0.000M, 2.000M);

                InitNumeric(nud_TofRaw_Q_oef, 3, 0.010M, 0.001M, 50M);
                InitNumeric(nud_TofRaw_R_oef, 3, 0.010M, 0.001M, 50M);
                InitNumeric(nud_TofRaw_A_oef, 3, 0.010M, 0.000M, 2.000M);

                InitNumeric(nud_TofFilt_Q_oef, 3, 0.010M, 0.001M, 50M);
                InitNumeric(nud_TofFilt_R_oef, 3, 0.010M, 0.001M, 50M);
                InitNumeric(nud_TofFilt_A_oef, 3, 0.010M, 0.000M, 2.000M);
            }
        }
        #endregion

        #region ===== OEF: 通道选择 & 仅选中行可编辑 =====
        private Channel GetSelectedOefChannel()
            => comboTarget_oef.SelectedItem is Channel ch ? ch : Channel.Raw;

        private OneEuroLikeFilter GetOef(Channel ch) => ch switch
        {
            Channel.Raw => _oefRaw,
            Channel.Filt => _oefFilt,
            Channel.TofRaw => _oefTofRaw,
            Channel.TofFilt => _oefTofFilt,
            _ => throw new ArgumentOutOfRangeException(nameof(ch))
        };

        private void ComboTarget_Oef_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_busy)
            {
                // 正在其他任务，禁止切换
                if (_runningOefChannel.HasValue)
                {
                    var idx = comboTarget_oef.Items.IndexOf(_runningOefChannel.Value);
                    if (idx >= 0) comboTarget_oef.SelectedIndex = idx;
                }
                MessageBox.Show(this, "正在进行调参任务，暂不可切换 OEF 目标组。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var ch = GetSelectedOefChannel();
            WriteParamsToControls_Oef(ch, GetOef(ch));
            SetRowEnabled_Oef(ch);
        }

        private void SetRowEnabled_Oef(Channel ch)
        {
            // 全禁
            nud_Raw_Q_oef.Enabled = nud_Raw_R_oef.Enabled = nud_Raw_A_oef.Enabled = false;
            nud_Filt_Q_oef.Enabled = nud_Filt_R_oef.Enabled = nud_Filt_A_oef.Enabled = false;
            nud_TofRaw_Q_oef.Enabled = nud_TofRaw_R_oef.Enabled = nud_TofRaw_A_oef.Enabled = false;
            nud_TofFilt_Q_oef.Enabled = nud_TofFilt_R_oef.Enabled = nud_TofFilt_A_oef.Enabled = false;

            // 开启选中组
            switch (ch)
            {
                case Channel.Raw:
                    nud_Raw_Q_oef.Enabled = nud_Raw_R_oef.Enabled = nud_Raw_A_oef.Enabled = true; break;
                case Channel.Filt:
                    nud_Filt_Q_oef.Enabled = nud_Filt_R_oef.Enabled = nud_Filt_A_oef.Enabled = true; break;
                case Channel.TofRaw:
                    nud_TofRaw_Q_oef.Enabled = nud_TofRaw_R_oef.Enabled = nud_TofRaw_A_oef.Enabled = true; break;
                case Channel.TofFilt:
                    nud_TofFilt_Q_oef.Enabled = nud_TofFilt_R_oef.Enabled = nud_TofFilt_A_oef.Enabled = true; break;
            }
        }
        #endregion

        #region ===== OEF: 写参数到控件 & 评估输出到日志 =====
        private void WriteParamsToControls_Oef(Channel ch, OneEuroLikeFilter f)
        {
            var (Q, R, A) = f.GetParameters(); // Q=minCutoff, R=dCutoff, A=beta
            switch (ch)
            {
                case Channel.Raw:
                    nud_Raw_Q_oef.Value = (decimal)Q; nud_Raw_R_oef.Value = (decimal)R; nud_Raw_A_oef.Value = (decimal)A; break;
                case Channel.Filt:
                    nud_Filt_Q_oef.Value = (decimal)Q; nud_Filt_R_oef.Value = (decimal)R; nud_Filt_A_oef.Value = (decimal)A; break;
                case Channel.TofRaw:
                    nud_TofRaw_Q_oef.Value = (decimal)Q; nud_TofRaw_R_oef.Value = (decimal)R; nud_TofRaw_A_oef.Value = (decimal)A; break;
                case Channel.TofFilt:
                    nud_TofFilt_Q_oef.Value = (decimal)Q; nud_TofFilt_R_oef.Value = (decimal)R; nud_TofFilt_A_oef.Value = (decimal)A; break;
            }
        }

        private void AppendLogOef(string line)
        {
            if (txtLog_oef == null) return;
            if (txtLog_oef.InvokeRequired)
                txtLog_oef.BeginInvoke(new Action(() => txtLog_oef.AppendText(line + Environment.NewLine)));
            else
                txtLog_oef.AppendText(line + Environment.NewLine);
        }

        private void ShowEval_Oef(Channel ch, OneEuroLikeFilter f)
        {
            var (stat, score, verdict) = GetOefScorer(ch).Evaluate();
            if (double.IsNaN(score))
                AppendLogOef($"[OEF-{ch}] 样本太少（N={stat.N}），暂不评分。");
            else
                AppendLogOef($"[OEF-{ch}] Score={score:F1}  {verdict}  | N={stat.N}, SmoothRatio={stat.SmoothRatio:F3}, LagRms={stat.LagRms:F3}");


            var (Q, R, A) = f.GetParameters();
            AppendLogOef($"[{DateTime.Now:HH:mm:ss}] OEF-{ch} 参数：Q(minCutoff)={Q:0.000} Hz，R(dCutoff)={R:0.000} Hz，beta={A:0.000}");
        }
        #endregion

        #region ===== OEF: 单通道自动调参 =====
        private async void StartAutoTune_Oef(Channel ch)
        {
            if (_busy)
            {
                MessageBox.Show(this, "已有调参任务在进行中，请稍候。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_deepSeekReasoner == null)
            {
                MessageBox.Show(this, "DeepSeek 未就绪，无法进行 OEF 的 AI 调参。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _busy = true;
            _runningOefChannel = ch;
            comboTarget_oef.Enabled = false;

            // 构建或复用该通道的 OEF 调参器
            if (!_oefTuners.ContainsKey(ch))
            {
                var f = GetOef(ch);
                var name = $"OEF-{ch}";

                string systemPrompt =
                    "You are a OneEuro filter tuning assistant. Output compact JSON only. " +
                    "Schema:{\"proposed_q\":float,\"proposed_r\":float,\"proposed_alpha\":float,\"confidence\":0..1,\"rationale\":string}";

                // 用户提示（简单：当前参数 + 残差要求，实际实现可用内部评分器拼更详细统计）
                Func<OneEuroAutoTuner.EvalStat, (float q, float r, float a), string> userBuilder =
                    (stat, cur) =>
                        $"Filter={name}; Cur: minCutoff={cur.q:0.###}Hz, dCutoff={cur.r:0.###}Hz, beta={cur.a:0.###}. " +
                        $"Win: N={stat.N}, InStd={stat.InStd:0.###}, OutStd={stat.OutStd:0.###}, SmoothRatio={stat.SmoothRatio:0.###}, LagRms={stat.LagRms:0.###}. " +
                        "Goal: minimize OutStd and LagRms while keeping SmoothRatio>=0.65. Small steps unless clearly beneficial.";

                var tuner = new OneEuroAutoTuner(
                    filter: f,
                    name: name,
                    llmClient: _deepSeekReasoner,
                    systemPrompt: systemPrompt,
                    userPromptBuilder: userBuilder,
                    windowCapacity: 300,
                    cooldown: TimeSpan.FromSeconds(6),
                    minSamples: 120,
                    minScoreGain: 1.5f,
                    maxLagWorsen: 0.12f,
                    maxStepScale: 0.30f,
                    llmMaxTokens: 128,
                    llmTemperature: 0.2
                )
                {
                    TargetScore = 85f,
                    ScoreTolerance = 5f,
                    MaxAcceptRounds = 10,
                    OneShot = false,
                    Enabled = true
                };

                tuner.TuningEvent += (_, args) =>
                {
                    // 只记录关键阶段
                    if (args.Action == "APPLY_HEURISTIC" || args.Action == "APPLY_LLM" ||
                        args.Action == "ACCEPT_BASELINE" || args.Action == "FINAL_PARAMS" ||
                        args.Action == "ROLLBACK" || args.Action == "CONTINUE")
                    {
                        AppendLogOef(
                            $"[{args.Time:HH:mm:ss}] {args.Name} {args.Action}: " +
                            $"Q {args.OldParams.Q:0.000}→{args.NewParams.Q:0.000}, " +
                            $"R {args.OldParams.R:0.000}→{args.NewParams.R:0.000}, " +
                            $"beta {args.OldParams.A:0.000}→{args.NewParams.A:0.000} " +
                            $"{(string.IsNullOrWhiteSpace(args.Rationale) ? "" : "| " + args.Rationale)}");
                    }
                    if (args.Action == "FINAL_PARAMS" && ch == _runningOefChannel)
                        _tuneDoneTcs?.TrySetResult(true);
                };

                _oefTuners[ch] = tuner;
            }

            // 进度窗 & 计时
            _tuneStartUtc = DateTime.UtcNow;
            _tuneDoneTcs = new TaskCompletionSource<bool>();
            _progress = new ProgressForm($"{ch} (OEF) 智能调参中…");
            BeginInvoke(new Action(() => _progress.Show(this)));

            _evalTimer.Start();

            var timeout = Task.Delay(TimeSpan.FromMinutes(5));
            var finished = await Task.WhenAny(_tuneDoneTcs.Task, timeout);

            _evalTimer.Stop();
            CloseProgress();

            // 写回控件 + 一次评估打印
            var curF = GetOef(ch);
            WriteParamsToControls_Oef(ch, curF);
            ShowEval_Oef(ch, curF);

            var used = (DateTime.UtcNow - _tuneStartUtc).TotalSeconds;
            if (finished == timeout)
                MessageBox.Show(this, $"{ch} (OEF) 智能调参超时（>300s）。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else
                MessageBox.Show(this, $"{ch} (OEF) 智能调参完毕，所用时间为：{used:F1}s", "完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

            comboTarget_oef.Enabled = true;
            _runningOefChannel = null;
            _busy = false;
        }
        #endregion

        #region ===== OEF: 重启当前滤波器 =====
        private void RestartCurrentOef()
        {
            var ch = GetSelectedOefChannel();
            var f = GetOef(ch);
            f.Reset(); // 不改参数，只清状态
            ShowEval_Oef(ch, f);
            MessageBox.Show(this, $"{ch} (OEF) 滤波器已重启。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        #endregion


        /*************************************************H∞************************************************************************/

        #region ===== RHF: NumericUpDown 默认配置 =====
        private void InitNumeric_RHF()
        {
            using (SuspendManualScope()) // ← 新增
            {
                InitNumeric(nud_Raw_Q_rhf, 6, 0.000001M, 0.000001M, 1000000M);
                InitNumeric(nud_Raw_R_rhf, 6, 0.000001M, 0.000001M, 1000000M);
                InitNumeric(nud_Raw_γ_rhf, 3, 0.001M, 0.010M, 0.990M);

                InitNumeric(nud_Filt_Q_rhf, 6, 0.000001M, 0.000001M, 1000000M);
                InitNumeric(nud_Filt_R_rhf, 6, 0.000001M, 0.000001M, 1000000M);
                InitNumeric(nud_Filt_γ_rhf, 3, 0.001M, 0.010M, 0.990M);

                InitNumeric(nud_TofRaw_Q_rhf, 6, 0.000001M, 0.000001M, 1000000M);
                InitNumeric(nud_TofRaw_R_rhf, 6, 0.000001M, 0.000001M, 1000000M);
                InitNumeric(nud_TofRaw_γ_rhf, 3, 0.001M, 0.010M, 0.990M);

                InitNumeric(nud_TofFilt_Q_rhf, 6, 0.000001M, 0.000001M, 1000000M);
                InitNumeric(nud_TofFilt_R_rhf, 6, 0.000001M, 0.000001M, 1000000M);
                InitNumeric(nud_TofFilt_γ_rhf, 3, 0.001M, 0.010M, 0.990M);
            }
        }
        #endregion

        #region ===== RHF: 通道选择 & 仅选中行可编辑 =====
        private Channel GetSelectedRhfChannel()
            => comboTarget_rhf.SelectedItem is Channel ch ? ch : Channel.Raw;

        private RobustHInfinityFilter GetRHF(Channel ch) => ch switch
        {
            Channel.Raw => _rhfRaw,
            Channel.Filt => _rhfFilt,
            Channel.TofRaw => _rhfTofRaw,
            Channel.TofFilt => _rhfTofFilt,
            _ => throw new ArgumentOutOfRangeException(nameof(ch))
        };

        private void ComboTarget_Rhf_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_busy)
            {
                if (_runningRhfChannel.HasValue)
                {
                    var idx = comboTarget_rhf.Items.IndexOf(_runningRhfChannel.Value);
                    if (idx >= 0) comboTarget_rhf.SelectedIndex = idx;
                }
                MessageBox.Show(this, "正在进行调参任务，暂不可切换 RHF 目标组。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var ch = GetSelectedRhfChannel();
            WriteParamsToControls_Rhf(ch, GetRHF(ch));
            SetRowEnabled_Rhf(ch);
        }

        private void SetRowEnabled_Rhf(Channel ch)
        {
            // 全禁
            nud_Raw_Q_rhf.Enabled = nud_Raw_R_rhf.Enabled = nud_Raw_γ_rhf.Enabled = false;
            nud_Filt_Q_rhf.Enabled = nud_Filt_R_rhf.Enabled = nud_Filt_γ_rhf.Enabled = false;
            nud_TofRaw_Q_rhf.Enabled = nud_TofRaw_R_rhf.Enabled = nud_TofRaw_γ_rhf.Enabled = false;
            nud_TofFilt_Q_rhf.Enabled = nud_TofFilt_R_rhf.Enabled = nud_TofFilt_γ_rhf.Enabled = false;

            // 仅选中组可编辑
            switch (ch)
            {
                case Channel.Raw:
                    nud_Raw_Q_rhf.Enabled = nud_Raw_R_rhf.Enabled = nud_Raw_γ_rhf.Enabled = true; break;
                case Channel.Filt:
                    nud_Filt_Q_rhf.Enabled = nud_Filt_R_rhf.Enabled = nud_Filt_γ_rhf.Enabled = true; break;
                case Channel.TofRaw:
                    nud_TofRaw_Q_rhf.Enabled = nud_TofRaw_R_rhf.Enabled = nud_TofRaw_γ_rhf.Enabled = true; break;
                case Channel.TofFilt:
                    nud_TofFilt_Q_rhf.Enabled = nud_TofFilt_R_rhf.Enabled = nud_TofFilt_γ_rhf.Enabled = true; break;
            }
        }
        #endregion

        #region ===== RHF: 写参数到控件 & 评估输出到日志 =====
        private void WriteParamsToControls_Rhf(Channel ch, RobustHInfinityFilter f)
        {
            var (Q, R, A) = f.GetParameters(); // A=gamma
            switch (ch)
            {
                case Channel.Raw:
                    nud_Raw_Q_rhf.Value = (decimal)Q; nud_Raw_R_rhf.Value = (decimal)R; nud_Raw_γ_rhf.Value = (decimal)A; break;
                case Channel.Filt:
                    nud_Filt_Q_rhf.Value = (decimal)Q; nud_Filt_R_rhf.Value = (decimal)R; nud_Filt_γ_rhf.Value = (decimal)A; break;
                case Channel.TofRaw:
                    nud_TofRaw_Q_rhf.Value = (decimal)Q; nud_TofRaw_R_rhf.Value = (decimal)R; nud_TofRaw_γ_rhf.Value = (decimal)A; break;
                case Channel.TofFilt:
                    nud_TofFilt_Q_rhf.Value = (decimal)Q; nud_TofFilt_R_rhf.Value = (decimal)R; nud_TofFilt_γ_rhf.Value = (decimal)A; break;
            }
        }

        private void AppendLogRHF(string line)
        {
            if (txtLog_rhf == null) return;
            if (txtLog_rhf.InvokeRequired)
                txtLog_rhf.BeginInvoke(new Action(() => txtLog_rhf.AppendText(line + Environment.NewLine)));
            else
                txtLog_rhf.AppendText(line + Environment.NewLine);
        }

        private void ShowEval_Rhf(Channel ch, RobustHInfinityFilter f)
        {

            var (stat, score, verdict) = GetRhfScorer(ch).Evaluate();
            if (double.IsNaN(score))
                AppendLogRHF($"[RHF-{ch}] 样本太少（N={stat.N}），暂不评分。");
            else
                AppendLogRHF($"[RHF-{ch}] Score={score:F1}  {verdict}  | N={stat.N}, SmoothRatio={stat.SmoothRatio:F3}, LagRms={stat.LagRms:F3}, MAD={stat.InMAD:F3}, |res|={stat.InnovAbsMean:F3}, LagSamples={stat.LagSamples}");

            var (Q, R, G) = f.GetParameters();
            AppendLogRHF($"[{DateTime.Now:HH:mm:ss}] RHF-{ch} 参数：Q={Q:E6}，R={R:E6}，γ={G:0.000}");
        }
        #endregion

        #region ===== RHF: 单通道自动调参 =====
        private async void StartAutoTune_Rhf(Channel ch)
        {
            if (_busy)
            {
                MessageBox.Show(this, "已有调参任务在进行中，请稍候。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_deepSeekReasoner == null)
            {
                MessageBox.Show(this, "DeepSeek 未就绪，无法进行 RHF 的 AI 调参。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _busy = true;
            _runningRhfChannel = ch;
            comboTarget_rhf.Enabled = false;

            if (!_rhfTuners.ContainsKey(ch))
            {
                var f = GetRHF(ch);
                var name = $"RHF-{ch}";

                string systemPrompt =
                    "You are a Robust H-infinity (H∞) filter tuning assistant. Return compact JSON only. " +
                    "Schema:{\"proposed_q\":float,\"proposed_r\":float,\"proposed_gamma\":float,\"confidence\":0..1,\"rationale\":string}";

                // 提示里把目标带写成 0.58~0.88，更贴合新评分
                Func<RobustHInfAutoTuner.EvalStat, (float q, float r, float g), string> userBuilder =
                    (stat, cur) =>
                        $"Filter={name}; Cur: Q={cur.q:E6}, R={cur.r:E6}, gamma={cur.g:0.###}. " +
                        $"Win: N={stat.N}, InStd={stat.InStd:0.###}, OutStd={stat.OutStd:0.###}, " +
                        $"SmoothRatio={stat.SmoothRatio:0.###}, LagRms={stat.LagRms:0.###}, " +
                        $"InnovationAbsMean={stat.InnovAbsMean:0.###}. " +
                        "Goal: reduce OutStd & LagRms, keep SmoothRatio≈0.58~0.88; if heavy tails are present, allow moderately higher gamma. " +
                        "Bounds: Q∈[1e-12,1e6], R∈[1e-12,1e6], gamma∈[0.01,0.99]. Prefer 10–35% relative steps.";

                // 关键：窗口拉到 8s、minSamples 设 80；步幅上限 0.30；相对改进门槛更宽松 0.15
                var tuner = new RobustHInfAutoTuner(
                    filter: f,
                    name: name,
                    llmClient: _deepSeekReasoner,              // 可为 null 表示只用启发式
                    systemPrompt: systemPrompt,
                    userPromptBuilder: userBuilder,

                    // ↓ 评估窗口/采样
                    sampleRateHz: 10f,                         // 你的数据频率 10 Hz
                    windowSeconds: 5f,                         // 8 秒窗口（更稳）
                    minSamplesOverride: 50,                    // 至少 80 个新样本再评（配合 8s）

                    // ↓ 其它门限
                    cooldown: TimeSpan.FromSeconds(6),
                    minScoreGain: 0.15f,                       // 由 0.20 → 0.15（长窗下单位收益更“钝化”，给点余地）
                    maxLagWorsen: 0.35f,
                    maxStepScale: 0.30f,                       // 单步上限放宽到 30%

                    // ↓ LLM
                    llmMaxTokens: 128,
                    llmTemperature: 0.2,

                    // ↓ 日志输出到 UI 控制台
                    logger: msg => Console.WriteLine(msg)
                )
                {
                    TargetScore = 72f,          // 你的目标不变
                    ScoreTolerance = 10f,
                    MaxAcceptRounds = 12,
                    OneShot = false,
                    Enabled = true
                };

                tuner.TuningEvent += (_, args) =>
                {
                    if (args.Action is "APPLY_HEURISTIC" or "APPLY_LLM" or "ACCEPT_BASELINE" or
                        "FINAL_PARAMS" or "ROLLBACK" or "CONTINUE" or "SKIP_WAITING")
                    {
                        AppendLogRHF(
                            $"[{args.Time:HH:mm:ss}] {args.Name} {args.Action}: " +
                            $"Q {args.OldParams.Q:E3}→{args.NewParams.Q:E3}, " +
                            $"R {args.OldParams.R:E3}→{args.NewParams.R:E3}, " +
                            $"γ {args.OldParams.A:0.000}→{args.NewParams.A:0.000} " +
                            $"{(string.IsNullOrWhiteSpace(args.Rationale) ? "" : "| " + args.Rationale)}");
                    }
                    if (args.Action == "FINAL_PARAMS" && ch == _runningRhfChannel)
                        _tuneDoneTcs?.TrySetResult(true);
                };

                _rhfTuners[ch] = tuner;
            }

            // 进度 & 计时
            _tuneStartUtc = DateTime.UtcNow;
            _tuneDoneTcs = new TaskCompletionSource<bool>();
            _progress = new ProgressForm($"{ch} (RHF) 智能调参中…");
            BeginInvoke(new Action(() => _progress.Show(this)));

            _evalTimer.Start();

            var timeout = Task.Delay(TimeSpan.FromMinutes(5));
            var finished = await Task.WhenAny(_tuneDoneTcs.Task, timeout);

            _evalTimer.Stop();
            CloseProgress();

            // 写回控件 & 打印
            var curF = GetRHF(ch);
            WriteParamsToControls_Rhf(ch, curF);
            ShowEval_Rhf(ch, curF);

            var used = (DateTime.UtcNow - _tuneStartUtc).TotalSeconds;
            if (finished == timeout)
                MessageBox.Show(this, $"{ch} (RHF) 智能调参超时（>300s）。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else
                MessageBox.Show(this, $"{ch} (RHF) 智能调参完毕，所用时间为：{used:F1}s", "完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

            comboTarget_rhf.Enabled = true;
            _runningRhfChannel = null;
            _busy = false;
        }
        #endregion

        #region ===== RHF: 重启当前滤波器 =====
        private void RestartCurrentRHF()
        {
            var ch = GetSelectedRhfChannel();
            var f = GetRHF(ch);
            f.Reset(); // 不改参数，只清状态
            ShowEval_Rhf(ch, f);
            MessageBox.Show(this, $"{ch} (RHF) 滤波器已重启。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        #endregion

        // 把消息路由到你现有的三个 AppendLog* 里（无需新增 TextBox）
        private void ShowStatus(Algo algo, Channel ch, string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] [{algo}-{ch}] {message}";
            switch (algo)
            {
                case Algo.AKF:
                    AppendLog(line);        // 你现有：写到 txtLog（AKF）
                    break;
                case Algo.OEF:
                    AppendLogOef(line);     // 你现有：写到 txtLog_oef（OEF）
                    break;
                case Algo.RHF:
                    AppendLogRHF(line);     // 你现有：写到 txtLog_rhf（RHF）
                    break;
            }
        }

        // 可选：三个极简包装器（方便在各自 ApplyManual_* 里调用）
        private void ShowStatusAKF(Channel ch, string msg) => ShowStatus(Algo.AKF, ch, msg);
        private void ShowStatusOEF(Channel ch, string msg) => ShowStatus(Algo.OEF, ch, msg);
        private void ShowStatusRHF(Channel ch, string msg) => ShowStatus(Algo.RHF, ch, msg);

        private void HookManualHandlers()
        {
            HookManualHandlers_AKF();
            HookManualHandlers_OEF();
            HookManualHandlers_RHF();
        }

        // AKF：Q / Alpha
        private void HookManualHandlers_AKF()
        {
            // Raw
            WireUserEditGuard(nud_Raw_Q);
            WireUserEditGuard(nud_Raw_A);
            nud_Raw_Q.ValueChanged += (_, __) => ApplyManual_AKF(Channel.Raw);
            nud_Raw_A.ValueChanged += (_, __) => ApplyManual_AKF(Channel.Raw);

            // Filt
            WireUserEditGuard(nud_Filt_Q);
            WireUserEditGuard(nud_Filt_A);
            nud_Filt_Q.ValueChanged += (_, __) => ApplyManual_AKF(Channel.Filt);
            nud_Filt_A.ValueChanged += (_, __) => ApplyManual_AKF(Channel.Filt);

            // TofRaw
            WireUserEditGuard(nud_TofRaw_Q);
            WireUserEditGuard(nud_TofRaw_A);
            nud_TofRaw_Q.ValueChanged += (_, __) => ApplyManual_AKF(Channel.TofRaw);
            nud_TofRaw_A.ValueChanged += (_, __) => ApplyManual_AKF(Channel.TofRaw);

            // TofFilt
            WireUserEditGuard(nud_TofFilt_Q);
            WireUserEditGuard(nud_TofFilt_A);
            nud_TofFilt_Q.ValueChanged += (_, __) => ApplyManual_AKF(Channel.TofFilt);
            nud_TofFilt_A.ValueChanged += (_, __) => ApplyManual_AKF(Channel.TofFilt);
        }

        private void HookManualHandlers_OEF()
        {
            // Raw
            WireUserEditGuard(nud_Raw_Q_oef);
            WireUserEditGuard(nud_Raw_R_oef);
            WireUserEditGuard(nud_Raw_A_oef);
            nud_Raw_Q_oef.ValueChanged += (_, __) => ApplyManual_OEF(Channel.Raw);
            nud_Raw_R_oef.ValueChanged += (_, __) => ApplyManual_OEF(Channel.Raw);
            nud_Raw_A_oef.ValueChanged += (_, __) => ApplyManual_OEF(Channel.Raw);

            // Filt
            WireUserEditGuard(nud_Filt_Q_oef);
            WireUserEditGuard(nud_Filt_R_oef);
            WireUserEditGuard(nud_Filt_A_oef);
            nud_Filt_Q_oef.ValueChanged += (_, __) => ApplyManual_OEF(Channel.Filt);
            nud_Filt_R_oef.ValueChanged += (_, __) => ApplyManual_OEF(Channel.Filt);
            nud_Filt_A_oef.ValueChanged += (_, __) => ApplyManual_OEF(Channel.Filt);

            // TofRaw
            WireUserEditGuard(nud_TofRaw_Q_oef);
            WireUserEditGuard(nud_TofRaw_R_oef);
            WireUserEditGuard(nud_TofRaw_A_oef);
            nud_TofRaw_Q_oef.ValueChanged += (_, __) => ApplyManual_OEF(Channel.TofRaw);
            nud_TofRaw_R_oef.ValueChanged += (_, __) => ApplyManual_OEF(Channel.TofRaw);
            nud_TofRaw_A_oef.ValueChanged += (_, __) => ApplyManual_OEF(Channel.TofRaw);

            // TofFilt
            WireUserEditGuard(nud_TofFilt_Q_oef);
            WireUserEditGuard(nud_TofFilt_R_oef);
            WireUserEditGuard(nud_TofFilt_A_oef);
            nud_TofFilt_Q_oef.ValueChanged += (_, __) => ApplyManual_OEF(Channel.TofFilt);
            nud_TofFilt_R_oef.ValueChanged += (_, __) => ApplyManual_OEF(Channel.TofFilt);
            nud_TofFilt_A_oef.ValueChanged += (_, __) => ApplyManual_OEF(Channel.TofFilt);
        }

        private void HookManualHandlers_RHF()
        {
            // Raw
            WireUserEditGuard(nud_Raw_Q_rhf);
            WireUserEditGuard(nud_Raw_R_rhf);
            WireUserEditGuard(nud_Raw_γ_rhf);
            nud_Raw_Q_rhf.ValueChanged += (_, __) => ApplyManual_RHF(Channel.Raw);
            nud_Raw_R_rhf.ValueChanged += (_, __) => ApplyManual_RHF(Channel.Raw);
            nud_Raw_γ_rhf.ValueChanged += (_, __) => ApplyManual_RHF(Channel.Raw);

            // Filt
            WireUserEditGuard(nud_Filt_Q_rhf);
            WireUserEditGuard(nud_Filt_R_rhf);
            WireUserEditGuard(nud_Filt_γ_rhf);
            nud_Filt_Q_rhf.ValueChanged += (_, __) => ApplyManual_RHF(Channel.Filt);
            nud_Filt_R_rhf.ValueChanged += (_, __) => ApplyManual_RHF(Channel.Filt);
            nud_Filt_γ_rhf.ValueChanged += (_, __) => ApplyManual_RHF(Channel.Filt);

            // TofRaw
            WireUserEditGuard(nud_TofRaw_Q_rhf);
            WireUserEditGuard(nud_TofRaw_R_rhf);
            WireUserEditGuard(nud_TofRaw_γ_rhf);
            nud_TofRaw_Q_rhf.ValueChanged += (_, __) => ApplyManual_RHF(Channel.TofRaw);
            nud_TofRaw_R_rhf.ValueChanged += (_, __) => ApplyManual_RHF(Channel.TofRaw);
            nud_TofRaw_γ_rhf.ValueChanged += (_, __) => ApplyManual_RHF(Channel.TofRaw);

            // TofFilt
            WireUserEditGuard(nud_TofFilt_Q_rhf);
            WireUserEditGuard(nud_TofFilt_R_rhf);
            WireUserEditGuard(nud_TofFilt_γ_rhf);
            nud_TofFilt_Q_rhf.ValueChanged += (_, __) => ApplyManual_RHF(Channel.TofFilt);
            nud_TofFilt_R_rhf.ValueChanged += (_, __) => ApplyManual_RHF(Channel.TofFilt);
            nud_TofFilt_γ_rhf.ValueChanged += (_, __) => ApplyManual_RHF(Channel.TofFilt);
        }

        private void ApplyManual_AKF(Channel ch)
        {

            if (IsManualSuspended) return;         // 初始化/批量赋值不响应
            if (!IsUserEditingAKF(ch)) return;     // 仅用户真实操作才响应

            if (GetSelectedChannel() != ch) return;
            var f = GetAkf(ch);

            // 从 NumericUpDown 读取（保持你的命名）
            decimal qVal, aVal;
            switch (ch)
            {
                case Channel.Raw: qVal = nud_Raw_Q.Value; aVal = nud_Raw_A.Value; break;
                case Channel.Filt: qVal = nud_Filt_Q.Value; aVal = nud_Filt_A.Value; break;
                case Channel.TofRaw: qVal = nud_TofRaw_Q.Value; aVal = nud_TofRaw_A.Value; break;
                case Channel.TofFilt: qVal = nud_TofFilt_Q.Value; aVal = nud_TofFilt_A.Value; break;
                default: return;
            }

            // 应用：只改 Q/Alpha，清诊断；不 Reset
            f.ApplyHyperParams((float)qVal, (float)aVal, clearDiagnostics: true);

            // 登记“待评估”
            _pending[(Algo.AKF, ch)] = new PendingEval
            {
                Algo = Algo.AKF,
                Ch = ch,
                ApplyAtCount = f.UpdateCount,
                ApplyAtUtc = DateTime.UtcNow,
                MinNewSamples = kMinNewSamples,
                MinElapsed = kMinElapsed
            };

            ShowStatusAKF(ch, "参数已应用，等待新样本评估…");

            EnsureEvalTimerRunning();
        }

        private void ApplyManual_OEF(Channel ch)
        {

            if (IsManualSuspended) return;
            if (!IsUserEditingOEF(ch)) return;   // 或 IsUserEditingRHF

            if (GetSelectedOefChannel() != ch) return;
            var f = GetOef(ch);

            // q=minCutoff, r=dCutoff, a=beta
            decimal q, r, a;
            switch (ch)
            {
                case Channel.Raw: q = nud_Raw_Q_oef.Value; r = nud_Raw_R_oef.Value; a = nud_Raw_A_oef.Value; break;
                case Channel.Filt: q = nud_Filt_Q_oef.Value; r = nud_Filt_R_oef.Value; a = nud_Filt_A_oef.Value; break;
                case Channel.TofRaw: q = nud_TofRaw_Q_oef.Value; r = nud_TofRaw_R_oef.Value; a = nud_TofRaw_A_oef.Value; break;
                case Channel.TofFilt: q = nud_TofFilt_Q_oef.Value; r = nud_TofFilt_R_oef.Value; a = nud_TofFilt_A_oef.Value; break;
                default: return;
            }

            // 不 Reset，只更新参数（或用你已有的 ApplyHyperParams）
            f.SetParameters((float)q, (float)r, (float)a, resetState: false);

            _pending[(Algo.OEF, ch)] = new PendingEval
            {
                Algo = Algo.OEF,
                Ch = ch,
                ApplyAtCount = f.UpdateCount,
                ApplyAtUtc = DateTime.UtcNow,
                MinNewSamples = kMinNewSamples,
                MinElapsed = kMinElapsed
            };

            ShowStatusOEF(ch, "参数已应用，等待新样本评估…");

            EnsureEvalTimerRunning();
        }

        private void ApplyManual_RHF(Channel ch)
        {

            if (IsManualSuspended) return;
            if (!IsUserEditingRHF(ch)) return;   // 只有用户操作才响应

            if (GetSelectedRhfChannel() != ch) return;
            var f = GetRHF(ch);

            decimal q, r, g; // γ
            switch (ch)
            {
                case Channel.Raw: q = nud_Raw_Q_rhf.Value; r = nud_Raw_R_rhf.Value; g = nud_Raw_γ_rhf.Value; break;
                case Channel.Filt: q = nud_Filt_Q_rhf.Value; r = nud_Filt_R_rhf.Value; g = nud_Filt_γ_rhf.Value; break;
                case Channel.TofRaw: q = nud_TofRaw_Q_rhf.Value; r = nud_TofRaw_R_rhf.Value; g = nud_TofRaw_γ_rhf.Value; break;
                case Channel.TofFilt: q = nud_TofFilt_Q_rhf.Value; r = nud_TofFilt_R_rhf.Value; g = nud_TofFilt_γ_rhf.Value; break;
                default: return;
            }

            f.SetParameters((float)q, (float)r, (float)g);

            _pending[(Algo.RHF, ch)] = new PendingEval
            {
                Algo = Algo.RHF,
                Ch = ch,
                ApplyAtCount = f.UpdateCount,
                ApplyAtUtc = DateTime.UtcNow,
                MinNewSamples = kMinNewSamples,
                MinElapsed = kMinElapsed
            };

            ShowStatusRHF(ch, "参数已应用，等待新样本评估…");

            EnsureEvalTimerRunning();
        }

        private bool IsUserEditingAKF(Channel ch) => ch switch
        {
            Channel.Raw => IsUserEditRecent(nud_Raw_Q) || IsUserEditRecent(nud_Raw_A),
            Channel.Filt => IsUserEditRecent(nud_Filt_Q) || IsUserEditRecent(nud_Filt_A),
            Channel.TofRaw => IsUserEditRecent(nud_TofRaw_Q) || IsUserEditRecent(nud_TofRaw_A),
            Channel.TofFilt => IsUserEditRecent(nud_TofFilt_Q) || IsUserEditRecent(nud_TofFilt_A),
            _ => false
        };

        // 仅当 OEF 的 NumericUpDown 存在“最近的用户操作”时返回 true
        private bool IsUserEditingOEF(Channel ch) => ch switch
        {
            Channel.Raw =>
                IsUserEditRecent(nud_Raw_Q_oef) ||
                IsUserEditRecent(nud_Raw_R_oef) ||
                IsUserEditRecent(nud_Raw_A_oef),

            Channel.Filt =>
                IsUserEditRecent(nud_Filt_Q_oef) ||
                IsUserEditRecent(nud_Filt_R_oef) ||
                IsUserEditRecent(nud_Filt_A_oef),

            Channel.TofRaw =>
                IsUserEditRecent(nud_TofRaw_Q_oef) ||
                IsUserEditRecent(nud_TofRaw_R_oef) ||
                IsUserEditRecent(nud_TofRaw_A_oef),

            Channel.TofFilt =>
                IsUserEditRecent(nud_TofFilt_Q_oef) ||
                IsUserEditRecent(nud_TofFilt_R_oef) ||
                IsUserEditRecent(nud_TofFilt_A_oef),

            _ => false
        };

        // 仅当 RHF 的 NumericUpDown 存在“最近的用户操作”时返回 true
        private bool IsUserEditingRHF(Channel ch) => ch switch
        {
            Channel.Raw =>
                IsUserEditRecent(nud_Raw_Q_rhf) ||
                IsUserEditRecent(nud_Raw_R_rhf) ||
                IsUserEditRecent(nud_Raw_γ_rhf),

            Channel.Filt =>
                IsUserEditRecent(nud_Filt_Q_rhf) ||
                IsUserEditRecent(nud_Filt_R_rhf) ||
                IsUserEditRecent(nud_Filt_γ_rhf),

            Channel.TofRaw =>
                IsUserEditRecent(nud_TofRaw_Q_rhf) ||
                IsUserEditRecent(nud_TofRaw_R_rhf) ||
                IsUserEditRecent(nud_TofRaw_γ_rhf),

            Channel.TofFilt =>
                IsUserEditRecent(nud_TofFilt_Q_rhf) ||
                IsUserEditRecent(nud_TofFilt_R_rhf) ||
                IsUserEditRecent(nud_TofFilt_γ_rhf),

            _ => false
        };

        private void EnsureEvalTimerRunning()
        {
            if (_evalTimer != null && !_evalTimer.Enabled)
                _evalTimer.Start();
        }

        //评分器==================================================
        // 成员字段：为每个通道准备独立评分器
        private readonly Dictionary<Channel, OefScorer> _oefScorers = new();
        private readonly Dictionary<Channel, RhfScorer> _rhfScorers = new();

        // —— 独立评分喂样定时器
        private System.Windows.Forms.Timer _oefScoreTimer;
        private System.Windows.Forms.Timer _rhfScoreTimer;

        // —— 每个通道上次已投喂的 UpdateCount（防重复投喂）
        private readonly Dictionary<Channel, int> _oefLastFedUc = new();
        private readonly Dictionary<Channel, int> _rhfLastFedUc = new();

        private OefScorer GetOefScorer(Channel ch)
        {
            if (!_oefScorers.TryGetValue(ch, out var s))
            {
                s = new OefScorer(window: 256, new OefScorer.Config
                {
                    // 把这里的目标/权重改成与你的 OneEuroAutoTuner 一致
                    TargetSmoothRatio = 0.70,
                    SmoothBand = 0.25,
                    LagScale = 1.0,
                    WSmooth = 0.65,
                    WLag = 0.35,
                    UseMadRatio = true,
                    MinSamples = 150
                });
                _oefScorers[ch] = s;
            }
            return s;
        }
        private RhfScorer GetRhfScorer(Channel ch)
        {
            if (!_rhfScorers.TryGetValue(ch, out var s))
            {
                s = new RhfScorer(window: 256, new RhfScorer.Config
                {
                    // 与 RobustHInfAutoTuner 保持一致
                    TargetSmoothRatio = 0.65,
                    SmoothBand = 0.30,
                    LagScale = 1.0,
                    WSmooth = 0.60,
                    WLag = 0.30,
                    WHeavyTail = 0.10,
                    UseMadRatio = true,
                    MinSamples = 150,
                    InnovVsMadRef = 3.0
                });
                _rhfScorers[ch] = s;
            }
            return s;
        }

        private void StartScoreFeedTimers()
        {
            // OEF评分喂样：默认 50ms（20Hz）。不必和采样率一致，因为我们用 UpdateCount 去重
            _oefScoreTimer = new System.Windows.Forms.Timer { Interval = 50 };
            _oefScoreTimer.Tick += OefScoreTimer_Tick;
            _oefScoreTimer.Start();

            // RHF评分喂样
            _rhfScoreTimer = new System.Windows.Forms.Timer { Interval = 50 };
            _rhfScoreTimer.Tick += RhfScoreTimer_Tick;
            _rhfScoreTimer.Start();
        }

        private void StopScoreFeedTimers()
        {
            if (_oefScoreTimer != null) { _oefScoreTimer.Stop(); _oefScoreTimer.Tick -= OefScoreTimer_Tick; _oefScoreTimer.Dispose(); _oefScoreTimer = null; }
            if (_rhfScoreTimer != null) { _rhfScoreTimer.Stop(); _rhfScoreTimer.Tick -= RhfScoreTimer_Tick; _rhfScoreTimer.Dispose(); _rhfScoreTimer = null; }
        }

        // —— OEF：独立评分器投喂
        private void OefScoreTimer_Tick(object sender, EventArgs e)
        {
            foreach (Channel ch in Enum.GetValues(typeof(Channel)))
            {
                var f = GetOef(ch);
                if (f == null) continue;

                // 只在出现新样本时投喂一次
                int uc = f.UpdateCount;
                if (!_oefLastFedUc.TryGetValue(ch, out var prev) || uc > prev)
                {
                    double input = f.LastInput;   // 你滤波器里已有 LastInput/LastOutput
                    double output = f.LastOutput;
                    GetOefScorer(ch).Feed(input, output);
                    _oefLastFedUc[ch] = uc;
                }
            }
        }

        // —— RHF：独立评分器投喂
        private void RhfScoreTimer_Tick(object sender, EventArgs e)
        {
            foreach (Channel ch in Enum.GetValues(typeof(Channel)))
            {
                var f = GetRHF(ch);
                if (f == null) continue;

                int uc = f.UpdateCount;
                if (!_rhfLastFedUc.TryGetValue(ch, out var prev) || uc > prev)
                {
                    double input = f.LastInput;
                    double output = f.LastOutput;
                    GetRhfScorer(ch).Feed(input, output);
                    _rhfLastFedUc[ch] = uc;
                }
            }
        }



    }
}
