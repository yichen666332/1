using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace FilterVisualPlatform
{
    public partial class UserCurv : UserControl
    {
        private UserDatas _userDatas;
        private UserComm _userComm;

        private readonly ConcurrentQueue<(DateTime t, float raw, float filt, float tofRaw, float tofFilt,
                             float f_raw, float f_filt, float f_tofRaw, float f_tofFilt)> _queue_AKF
            = new ConcurrentQueue<(DateTime, float, float, float, float, float, float, float, float)>();
        private readonly ConcurrentQueue<(DateTime t, float raw, float filt, float tofRaw, float tofFilt,
                             float f_raw, float f_filt, float f_tofRaw, float f_tofFilt)> _queue_OEF
            = new ConcurrentQueue<(DateTime, float, float, float, float, float, float, float, float)>();
        private readonly ConcurrentQueue<(DateTime t, float raw, float filt, float tofRaw, float tofFilt,
                             float f_raw, float f_filt, float f_tofRaw, float f_tofFilt)> _queue_RHF
            = new ConcurrentQueue<(DateTime, float, float, float, float, float, float, float, float)>();
        private readonly ConcurrentQueue<(DateTime t, float raw, float filt, float tofRaw, float tofFilt,
                             float f_raw, float f_filt, float f_tofRaw, float f_tofFilt)> _queue_SWD
            = new ConcurrentQueue<(DateTime, float, float, float, float, float, float, float, float)>();

        private readonly System.Windows.Forms.Timer _uiTimer = new();
        private CancellationTokenSource _cts;

        private const int SampleIntervalMs = 20;  // 50Hz
        private const int WindowSeconds = 300;     // 1分钟窗口
        private const int UiIntervalMs = 100;     // UI刷新

        // 四组图的上下文
        private PlotGroup _akfGroup, _oefGroup, _rhfGroup, _swdGroup;

        public UserCurv(UserDatas userDatas, UserComm userComm)
        {
            InitializeComponent();
            _userDatas = userDatas;
            _userComm = userComm;
        }

        private void UserCurv_Load(object sender, EventArgs e)
        {
            InitAllPlots();

            _uiTimer.Interval = UiIntervalMs;
            _uiTimer.Tick += UiTimer_Tick;
            _uiTimer.Start();

            // ✅ 仅启动生产者与绘图；不做评估/调参
            StartProducer();
        }

        // --------------------------- 初始化绑定 ---------------------------

        private void InitAllPlots()
        {
            _akfGroup = InitOneGroup(
                plotView_AKF, treeView_AKF,
                titlePrefix: "AKF", algSuffix: "akf", resultGroupCaption: "AKF 结果"
            );

            _oefGroup = InitOneGroup(
                plotView_OEF, treeView_OEF,
                titlePrefix: "OEF", algSuffix: "oef", resultGroupCaption: "OEF 结果"
            );

            _rhfGroup = InitOneGroup(
                plotView_RHF, treeView_RHF,
                titlePrefix: "RHF", algSuffix: "rhf", resultGroupCaption: "RHF 结果"
            );

        }

        private PlotGroup InitOneGroup(PlotView view, TreeView tree, string titlePrefix, string algSuffix, string resultGroupCaption)
        {
            var g = new PlotGroup
            {
                View = view,
                Tree = tree,
                Map = new Dictionary<string, LineSeries>()
            };

            var model = new PlotModel
            {
                Title = $"{titlePrefix} 实时数据",
                PlotMargins = new OxyThickness(50, 10, 10, 40)
            };

            // 时间轴
            var timeAxis = new DateTimeAxis
            {
                Position = AxisPosition.Bottom,
                StringFormat = "HH:mm:ss",
                IntervalType = DateTimeIntervalType.Seconds,
                MinorIntervalType = DateTimeIntervalType.Seconds,
                IsZoomEnabled = true,
                IsPanEnabled = true,
                Title = "时间"
            };
            model.Axes.Add(timeAxis);

            // 纵轴
            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "距离/值",
                IsZoomEnabled = true,
                IsPanEnabled = true
            });

            // ---------- ✅ 全部使用实线，定义高对比度颜色 ----------
            g.Raw = new LineSeries { Title = "Raw", StrokeThickness = 1.8, Color = OxyColors.Red, LineStyle = LineStyle.Solid };
            g.Filt = new LineSeries { Title = "Filtered", StrokeThickness = 1.8, Color = OxyColors.Blue, LineStyle = LineStyle.Solid };
            g.TofRaw = new LineSeries { Title = "ToF Raw", StrokeThickness = 1.8, Color = OxyColors.Green, LineStyle = LineStyle.Solid };
            g.TofFilt = new LineSeries { Title = "ToF Filt", StrokeThickness = 1.8, Color = OxyColors.DarkOrange, LineStyle = LineStyle.Solid };

            g.AlgRaw = new LineSeries { Title = $"Raw_{algSuffix}", StrokeThickness = 1.8, Color = OxyColor.FromRgb(148, 0, 211), LineStyle = LineStyle.Solid };
            g.AlgFilt = new LineSeries { Title = $"Filtered_{algSuffix}", StrokeThickness = 1.8, Color = OxyColor.FromRgb(255, 69, 0), LineStyle = LineStyle.Solid };
            g.AlgTofRaw = new LineSeries { Title = $"ToF Raw_{algSuffix}", StrokeThickness = 1.8, Color = OxyColors.Teal, LineStyle = LineStyle.Solid };
            g.AlgTofFilt = new LineSeries { Title = $"ToF Filt_{algSuffix}", StrokeThickness = 1.8, Color = OxyColors.Magenta, LineStyle = LineStyle.Solid };

            // 加入模型
            model.Series.Add(g.Raw);
            model.Series.Add(g.Filt);
            model.Series.Add(g.TofRaw);
            model.Series.Add(g.TofFilt);
            model.Series.Add(g.AlgRaw);
            model.Series.Add(g.AlgFilt);
            model.Series.Add(g.AlgTofRaw);
            model.Series.Add(g.AlgTofFilt);

            // Map（供 TreeView 使用）
            g.Map["Raw"] = g.Raw;
            g.Map["Filtered"] = g.Filt;
            g.Map["ToF Raw"] = g.TofRaw;
            g.Map["ToF Filt"] = g.TofFilt;
            g.Map[$"Raw_{algSuffix}"] = g.AlgRaw;
            g.Map[$"Filtered_{algSuffix}"] = g.AlgFilt;
            g.Map[$"ToF Raw_{algSuffix}"] = g.AlgTofRaw;
            g.Map[$"ToF Filt_{algSuffix}"] = g.AlgTofFilt;

            // 绑定到 PlotView
            g.Model = model;
            g.TimeAxis = timeAxis;
            view.Model = model;

            // 绑定 TreeView
            BuildSeriesTree(tree, g, resultGroupCaption);

            return g;
        }

        private void BuildSeriesTree(TreeView tree, PlotGroup g, string resultGroupCaption)
        {
            tree.CheckBoxes = true;
            tree.AfterCheck -= Tree_AfterCheck;

            tree.BeginUpdate();
            tree.Nodes.Clear();

            // 顶层：原始值
            var nRawGroup = tree.Nodes.Add("原始值");
            AddSeriesNode(nRawGroup, g.Raw);
            AddSeriesNode(nRawGroup, g.Filt);
            AddSeriesNode(nRawGroup, g.TofRaw);
            AddSeriesNode(nRawGroup, g.TofFilt);

            // 顶层：算法结果
            var nAlgGroup = tree.Nodes.Add(resultGroupCaption);
            AddSeriesNode(nAlgGroup, g.AlgRaw);
            AddSeriesNode(nAlgGroup, g.AlgFilt);
            AddSeriesNode(nAlgGroup, g.AlgTofRaw);
            AddSeriesNode(nAlgGroup, g.AlgTofFilt);

            nRawGroup.Expand();
            nAlgGroup.Expand();

            tree.Tag = new TreeBinding { View = g.View };
            tree.EndUpdate();
            tree.AfterCheck += Tree_AfterCheck;
        }

        private void AddSeriesNode(TreeNode parent, LineSeries series)
        {
            var node = parent.Nodes.Add(series.Title);
            node.Tag = series;
            node.Checked = series.IsVisible;
        }

        // --------------------------- Tree 显隐 ---------------------------
        private bool _suppressTreeEvents = false;

        private void Tree_AfterCheck(object sender, TreeViewEventArgs e)
        {
            if (_suppressTreeEvents) return;
            _suppressTreeEvents = true;
            try
            {
                bool needRefresh = false;

                if (e.Node.Tag is LineSeries ls && ls.IsVisible != e.Node.Checked)
                {
                    ls.IsVisible = e.Node.Checked;
                    needRefresh = true;
                }

                foreach (TreeNode child in e.Node.Nodes)
                {
                    if (child.Checked != e.Node.Checked)
                        child.Checked = e.Node.Checked;

                    if (child.Tag is LineSeries cls && cls.IsVisible != child.Checked)
                    {
                        cls.IsVisible = child.Checked;
                        needRefresh = true;
                    }
                }

                UpdateAncestorsCheckState(e.Node);

                if (needRefresh && sender is TreeView tv && tv.Tag is TreeBinding bind)
                    bind.View.Model?.InvalidatePlot(false);
            }
            finally
            {
                _suppressTreeEvents = false;
            }
        }

        private void UpdateAncestorsCheckState(TreeNode node)
        {
            var p = node.Parent;
            while (p != null)
            {
                bool anyChecked = p.Nodes.Cast<TreeNode>().Any(n => n.Checked);
                if (p.Checked != anyChecked) p.Checked = anyChecked;
                p = p.Parent;
            }
        }

        // --------------------------- 渲染循环 ---------------------------
        private void UiTimer_Tick(object sender, EventArgs e)
        {
            ConsumeQueueAndRefresh(_queue_AKF, _akfGroup);
            ConsumeQueueAndRefresh(_queue_OEF, _oefGroup);
            ConsumeQueueAndRefresh(_queue_RHF, _rhfGroup);
            ConsumeQueueAndRefresh(_queue_SWD, _swdGroup);
        }

        private void ConsumeQueueAndRefresh(
            ConcurrentQueue<(DateTime t, float raw, float filt, float tofRaw, float tofFilt,
                             float f_raw, float f_filt, float f_tofRaw, float f_tofFilt)> q,
            PlotGroup g)
        {
            if (g == null || g.View?.Model == null) return;

            bool anyNew = false;
            DateTime lastTime = default;

            while (q.TryDequeue(out var item))
            {
                anyNew = true;
                lastTime = item.t;
                double x = DateTimeAxis.ToDouble(item.t);

                g.Raw.Points.Add(new DataPoint(x, item.raw));
                g.Filt.Points.Add(new DataPoint(x, item.filt));
                g.TofRaw.Points.Add(new DataPoint(x, item.tofRaw));
                g.TofFilt.Points.Add(new DataPoint(x, item.tofFilt));

                g.AlgRaw.Points.Add(new DataPoint(x, item.f_raw));
                g.AlgFilt.Points.Add(new DataPoint(x, item.f_filt));
                g.AlgTofRaw.Points.Add(new DataPoint(x, item.f_tofRaw));
                g.AlgTofFilt.Points.Add(new DataPoint(x, item.f_tofFilt));
            }

            if (!anyNew) return;

            double xMax = DateTimeAxis.ToDouble(lastTime);
            double xMin = DateTimeAxis.ToDouble(lastTime.AddSeconds(-WindowSeconds));

            TrimSeries(g.Raw, xMin);
            TrimSeries(g.Filt, xMin);
            TrimSeries(g.TofRaw, xMin);
            TrimSeries(g.TofFilt, xMin);
            TrimSeries(g.AlgRaw, xMin);
            TrimSeries(g.AlgFilt, xMin);
            TrimSeries(g.AlgTofRaw, xMin);
            TrimSeries(g.AlgTofFilt, xMin);

            if (g.TimeAxis != null)
            {
                g.TimeAxis.Minimum = xMin;
                g.TimeAxis.Maximum = xMax;
            }

            g.View.Model.InvalidatePlot(true);
        }

        private static void TrimSeries(LineSeries s, double minX)
        {
            int keepStart = 0;
            while (keepStart < s.Points.Count && s.Points[keepStart].X < minX) keepStart++;
            if (keepStart > 0) s.Points.RemoveRange(0, keepStart);
        }

        private void StartProducer()
        {
            if (_userComm == null || _userDatas?.adaptiveKalmanFilter1 == null)
                return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        DateTime t = DateTime.Now;

                        float raw = _userComm.Raw;
                        float filtered = _userComm.Filtered;
                        float tofRaw = _userComm.TofRaw;
                        float tofFilt = _userComm.TofFiltered;

                        // 自适应卡尔曼滤波
                        float raw_akf = _userDatas.adaptiveKalmanFilter1.Update(_userComm.Raw);
                        float filtered_akf = _userDatas.adaptiveKalmanFilter2.Update(_userComm.Filtered);
                        float tofRaw_akf = _userDatas.adaptiveKalmanFilter3.Update(_userComm.TofRaw);
                        float tofFilt_akf = _userDatas.adaptiveKalmanFilter4.Update(_userComm.TofFiltered);

                        // 自适应低通
                        float raw_oef = _userDatas.oneEuroLikeFilter1.Update(_userComm.Raw);
                        float filtered_oef = _userDatas.oneEuroLikeFilter2.Update(_userComm.Filtered);
                        float tofRaw_oef = _userDatas.oneEuroLikeFilter3.Update(_userComm.TofRaw);
                        float tofFilt_oef = _userDatas.oneEuroLikeFilter4.Update(_userComm.TofFiltered);

                        // H∞
                        float raw_rhf = _userDatas.robustHInfinityFilter1.Update(_userComm.Raw);
                        float filtered_rhf = _userDatas.robustHInfinityFilter2.Update(_userComm.Filtered);
                        float tofRaw_rhf = _userDatas.robustHInfinityFilter3.Update(_userComm.TofRaw);
                        float tofFilt_rhf = _userDatas.robustHInfinityFilter4.Update(_userComm.TofFiltered);

                        // 兜底
                        if (float.IsNaN(raw) || float.IsInfinity(raw)) raw = 0;
                        if (float.IsNaN(filtered) || float.IsInfinity(filtered)) filtered = 0;
                        if (float.IsNaN(tofRaw) || float.IsInfinity(tofRaw)) tofRaw = 0;
                        if (float.IsNaN(tofFilt) || float.IsInfinity(tofFilt)) tofFilt = 0;

                        if (float.IsNaN(raw_akf) || float.IsInfinity(raw_akf)) raw_akf = 0;
                        if (float.IsNaN(filtered_akf) || float.IsInfinity(filtered_akf)) filtered_akf = 0;
                        if (float.IsNaN(tofRaw_akf) || float.IsInfinity(tofRaw_akf)) tofRaw_akf = 0;
                        if (float.IsNaN(tofFilt_akf) || float.IsInfinity(tofFilt_akf)) tofFilt_akf = 0;

                        if (float.IsNaN(raw_oef) || float.IsInfinity(raw_oef)) raw_oef = 0;
                        if (float.IsNaN(filtered_oef) || float.IsInfinity(filtered_oef)) filtered_oef = 0;
                        if (float.IsNaN(tofRaw_oef) || float.IsInfinity(tofRaw_oef)) tofRaw_oef = 0;
                        if (float.IsNaN(tofFilt_oef) || float.IsInfinity(tofFilt_oef)) tofFilt_oef = 0;

                        if (float.IsNaN(raw_rhf) || float.IsInfinity(raw_rhf)) raw_rhf = 0;
                        if (float.IsNaN(filtered_rhf) || float.IsInfinity(filtered_rhf)) filtered_rhf = 0;
                        if (float.IsNaN(tofRaw_rhf) || float.IsInfinity(tofRaw_rhf)) tofRaw_rhf = 0;
                        if (float.IsNaN(tofFilt_rhf) || float.IsInfinity(tofFilt_rhf)) tofFilt_rhf = 0;


                        // 入队（生产）
                        _queue_AKF.Enqueue((t, raw, filtered, tofRaw, tofFilt, raw_akf, filtered_akf, tofRaw_akf, tofFilt_akf));
                        _queue_OEF.Enqueue((t, raw, filtered, tofRaw, tofFilt, raw_oef, filtered_oef, tofRaw_oef, tofFilt_oef));
                        _queue_RHF.Enqueue((t, raw, filtered, tofRaw, tofFilt, raw_rhf, filtered_rhf, tofRaw_rhf, tofFilt_rhf));

                        await Task.Delay(SampleIntervalMs, token);
                    }
                }
                catch (TaskCanceledException) { }
                catch (Exception ex) { Debug.WriteLine(ex); }
            }, token);
        }
    }

    // 每个图的一组上下文
    public sealed class PlotGroup
    {
        public PlotView View;
        public TreeView Tree;
        public PlotModel Model;
        public DateTimeAxis TimeAxis;

        public LineSeries Raw, Filt, TofRaw, TofFilt;
        public LineSeries AlgRaw, AlgFilt, AlgTofRaw, AlgTofFilt;
        public Dictionary<string, LineSeries> Map;
    }

    public sealed class TreeBinding
    {
        public PlotView View;
        public Dictionary<string, LineSeries> Map;
    }
}
