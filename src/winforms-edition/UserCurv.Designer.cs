namespace FilterVisualPlatform
{
    partial class UserCurv
    {
        /// <summary> 
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region 组件设计器生成的代码

        /// <summary> 
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            tabControl1 = new TabControl();
            tp_AKF = new TabPage();
            tableLayoutPanel_AKF = new TableLayoutPanel();
            panel1 = new Panel();
            plotView_AKF = new OxyPlot.WindowsForms.PlotView();
            panel2 = new Panel();
            treeView_AKF = new TreeView();
            tp_OEF = new TabPage();
            tableLayoutPanel1 = new TableLayoutPanel();
            panel3 = new Panel();
            plotView_OEF = new OxyPlot.WindowsForms.PlotView();
            panel4 = new Panel();
            treeView_OEF = new TreeView();
            tp_H = new TabPage();
            tableLayoutPanel2 = new TableLayoutPanel();
            panel5 = new Panel();
            plotView_RHF = new OxyPlot.WindowsForms.PlotView();
            panel6 = new Panel();
            treeView_RHF = new TreeView();
            tabControl1.SuspendLayout();
            tp_AKF.SuspendLayout();
            tableLayoutPanel_AKF.SuspendLayout();
            panel1.SuspendLayout();
            panel2.SuspendLayout();
            tp_OEF.SuspendLayout();
            tableLayoutPanel1.SuspendLayout();
            panel3.SuspendLayout();
            panel4.SuspendLayout();
            tp_H.SuspendLayout();
            tableLayoutPanel2.SuspendLayout();
            panel5.SuspendLayout();
            panel6.SuspendLayout();
            SuspendLayout();
            // 
            // tabControl1
            // 
            tabControl1.Controls.Add(tp_AKF);
            tabControl1.Controls.Add(tp_OEF);
            tabControl1.Controls.Add(tp_H);
            tabControl1.Dock = DockStyle.Fill;
            tabControl1.Location = new Point(0, 0);
            tabControl1.Name = "tabControl1";
            tabControl1.SelectedIndex = 0;
            tabControl1.Size = new Size(824, 605);
            tabControl1.TabIndex = 0;
            // 
            // tp_AKF
            // 
            tp_AKF.Controls.Add(tableLayoutPanel_AKF);
            tp_AKF.Location = new Point(4, 26);
            tp_AKF.Name = "tp_AKF";
            tp_AKF.Padding = new Padding(3);
            tp_AKF.Size = new Size(816, 575);
            tp_AKF.TabIndex = 0;
            tp_AKF.Text = "卡尔曼滤波";
            tp_AKF.UseVisualStyleBackColor = true;
            // 
            // tableLayoutPanel_AKF
            // 
            tableLayoutPanel_AKF.ColumnCount = 2;
            tableLayoutPanel_AKF.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.666666F));
            tableLayoutPanel_AKF.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 83.3333359F));
            tableLayoutPanel_AKF.Controls.Add(panel1, 1, 0);
            tableLayoutPanel_AKF.Controls.Add(panel2, 0, 0);
            tableLayoutPanel_AKF.Dock = DockStyle.Fill;
            tableLayoutPanel_AKF.Location = new Point(3, 3);
            tableLayoutPanel_AKF.Name = "tableLayoutPanel_AKF";
            tableLayoutPanel_AKF.RowCount = 1;
            tableLayoutPanel_AKF.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayoutPanel_AKF.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            tableLayoutPanel_AKF.Size = new Size(810, 569);
            tableLayoutPanel_AKF.TabIndex = 3;
            // 
            // panel1
            // 
            panel1.Controls.Add(plotView_AKF);
            panel1.Dock = DockStyle.Fill;
            panel1.Location = new Point(137, 3);
            panel1.Name = "panel1";
            panel1.Size = new Size(670, 563);
            panel1.TabIndex = 0;
            // 
            // plotView_AKF
            // 
            plotView_AKF.Dock = DockStyle.Fill;
            plotView_AKF.Location = new Point(0, 0);
            plotView_AKF.Name = "plotView_AKF";
            plotView_AKF.PanCursor = Cursors.Hand;
            plotView_AKF.Size = new Size(670, 563);
            plotView_AKF.TabIndex = 4;
            plotView_AKF.Text = "plotView1";
            plotView_AKF.ZoomHorizontalCursor = Cursors.SizeWE;
            plotView_AKF.ZoomRectangleCursor = Cursors.SizeNWSE;
            plotView_AKF.ZoomVerticalCursor = Cursors.SizeNS;
            // 
            // panel2
            // 
            panel2.Controls.Add(treeView_AKF);
            panel2.Dock = DockStyle.Fill;
            panel2.Location = new Point(3, 3);
            panel2.Name = "panel2";
            panel2.Size = new Size(128, 563);
            panel2.TabIndex = 1;
            // 
            // treeView_AKF
            // 
            treeView_AKF.Dock = DockStyle.Fill;
            treeView_AKF.Location = new Point(0, 0);
            treeView_AKF.Name = "treeView_AKF";
            treeView_AKF.Size = new Size(128, 563);
            treeView_AKF.TabIndex = 0;
            // 
            // tp_OEF
            // 
            tp_OEF.Controls.Add(tableLayoutPanel1);
            tp_OEF.Location = new Point(4, 26);
            tp_OEF.Name = "tp_OEF";
            tp_OEF.Padding = new Padding(3);
            tp_OEF.Size = new Size(816, 575);
            tp_OEF.TabIndex = 1;
            tp_OEF.Text = "自适应低通滤波";
            tp_OEF.UseVisualStyleBackColor = true;
            // 
            // tableLayoutPanel1
            // 
            tableLayoutPanel1.ColumnCount = 2;
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.666666F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 83.3333359F));
            tableLayoutPanel1.Controls.Add(panel3, 1, 0);
            tableLayoutPanel1.Controls.Add(panel4, 0, 0);
            tableLayoutPanel1.Dock = DockStyle.Fill;
            tableLayoutPanel1.Location = new Point(3, 3);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.RowCount = 1;
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            tableLayoutPanel1.Size = new Size(810, 569);
            tableLayoutPanel1.TabIndex = 5;
            // 
            // panel3
            // 
            panel3.Controls.Add(plotView_OEF);
            panel3.Dock = DockStyle.Fill;
            panel3.Location = new Point(137, 3);
            panel3.Name = "panel3";
            panel3.Size = new Size(670, 563);
            panel3.TabIndex = 0;
            // 
            // plotView_OEF
            // 
            plotView_OEF.Dock = DockStyle.Fill;
            plotView_OEF.Location = new Point(0, 0);
            plotView_OEF.Name = "plotView_OEF";
            plotView_OEF.PanCursor = Cursors.Hand;
            plotView_OEF.Size = new Size(670, 563);
            plotView_OEF.TabIndex = 4;
            plotView_OEF.Text = "plotView1";
            plotView_OEF.ZoomHorizontalCursor = Cursors.SizeWE;
            plotView_OEF.ZoomRectangleCursor = Cursors.SizeNWSE;
            plotView_OEF.ZoomVerticalCursor = Cursors.SizeNS;
            // 
            // panel4
            // 
            panel4.Controls.Add(treeView_OEF);
            panel4.Dock = DockStyle.Fill;
            panel4.Location = new Point(3, 3);
            panel4.Name = "panel4";
            panel4.Size = new Size(128, 563);
            panel4.TabIndex = 1;
            // 
            // treeView_OEF
            // 
            treeView_OEF.Dock = DockStyle.Fill;
            treeView_OEF.Location = new Point(0, 0);
            treeView_OEF.Name = "treeView_OEF";
            treeView_OEF.Size = new Size(128, 563);
            treeView_OEF.TabIndex = 0;
            // 
            // tp_H
            // 
            tp_H.Controls.Add(tableLayoutPanel2);
            tp_H.Location = new Point(4, 26);
            tp_H.Name = "tp_H";
            tp_H.Size = new Size(816, 575);
            tp_H.TabIndex = 2;
            tp_H.Text = "H∞滤波";
            tp_H.UseVisualStyleBackColor = true;
            // 
            // tableLayoutPanel2
            // 
            tableLayoutPanel2.ColumnCount = 2;
            tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.666666F));
            tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 83.3333359F));
            tableLayoutPanel2.Controls.Add(panel5, 1, 0);
            tableLayoutPanel2.Controls.Add(panel6, 0, 0);
            tableLayoutPanel2.Dock = DockStyle.Fill;
            tableLayoutPanel2.Location = new Point(0, 0);
            tableLayoutPanel2.Name = "tableLayoutPanel2";
            tableLayoutPanel2.RowCount = 1;
            tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            tableLayoutPanel2.Size = new Size(816, 575);
            tableLayoutPanel2.TabIndex = 7;
            // 
            // panel5
            // 
            panel5.Controls.Add(plotView_RHF);
            panel5.Dock = DockStyle.Fill;
            panel5.Location = new Point(138, 3);
            panel5.Name = "panel5";
            panel5.Size = new Size(675, 569);
            panel5.TabIndex = 0;
            // 
            // plotView_RHF
            // 
            plotView_RHF.Dock = DockStyle.Fill;
            plotView_RHF.Location = new Point(0, 0);
            plotView_RHF.Name = "plotView_RHF";
            plotView_RHF.PanCursor = Cursors.Hand;
            plotView_RHF.Size = new Size(675, 569);
            plotView_RHF.TabIndex = 4;
            plotView_RHF.Text = "plotView2";
            plotView_RHF.ZoomHorizontalCursor = Cursors.SizeWE;
            plotView_RHF.ZoomRectangleCursor = Cursors.SizeNWSE;
            plotView_RHF.ZoomVerticalCursor = Cursors.SizeNS;
            // 
            // panel6
            // 
            panel6.Controls.Add(treeView_RHF);
            panel6.Dock = DockStyle.Fill;
            panel6.Location = new Point(3, 3);
            panel6.Name = "panel6";
            panel6.Size = new Size(129, 569);
            panel6.TabIndex = 1;
            // 
            // treeView_RHF
            // 
            treeView_RHF.Dock = DockStyle.Fill;
            treeView_RHF.Location = new Point(0, 0);
            treeView_RHF.Name = "treeView_RHF";
            treeView_RHF.Size = new Size(129, 569);
            treeView_RHF.TabIndex = 0;
            // 
            // UserCurv
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(tabControl1);
            Name = "UserCurv";
            Size = new Size(824, 605);
            Load += UserCurv_Load;
            tabControl1.ResumeLayout(false);
            tp_AKF.ResumeLayout(false);
            tableLayoutPanel_AKF.ResumeLayout(false);
            panel1.ResumeLayout(false);
            panel2.ResumeLayout(false);
            tp_OEF.ResumeLayout(false);
            tableLayoutPanel1.ResumeLayout(false);
            panel3.ResumeLayout(false);
            panel4.ResumeLayout(false);
            tp_H.ResumeLayout(false);
            tableLayoutPanel2.ResumeLayout(false);
            panel5.ResumeLayout(false);
            panel6.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private TabControl tabControl1;
        private TabPage tp_OEF;
        private TabPage tp_H;
        private TableLayoutPanel tableLayoutPanel1;
        private Panel panel3;
        private OxyPlot.WindowsForms.PlotView plotView_OEF;
        private Panel panel4;
        private TreeView treeView_OEF;
        private TableLayoutPanel tableLayoutPanel2;
        private Panel panel5;
        private OxyPlot.WindowsForms.PlotView plotView_RHF;
        private Panel panel6;
        private TreeView treeView_RHF;
        private TabPage tp_AKF;
        private TableLayoutPanel tableLayoutPanel_AKF;
        private Panel panel1;
        private OxyPlot.WindowsForms.PlotView plotView_AKF;
        private Panel panel2;
        private TreeView treeView_AKF;
    }
}
