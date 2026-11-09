namespace FilterVisualPlatform
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            tableLayoutPanel1 = new TableLayoutPanel();
            panel_menu = new Panel();
            btn_TunerControl = new Button();
            lb_Freq = new Label();
            lb_Count = new Label();
            label2 = new Label();
            label1 = new Label();
            lb_ConnectStatus = new Label();
            panel1 = new Panel();
            button5 = new Button();
            btn_Curv = new Button();
            btn_fifter = new Button();
            btn_userCom = new Button();
            panel_display = new Panel();
            timer1 = new System.Windows.Forms.Timer(components);
            tableLayoutPanel1.SuspendLayout();
            panel_menu.SuspendLayout();
            SuspendLayout();
            // 
            // tableLayoutPanel1
            // 
            tableLayoutPanel1.ColumnCount = 2;
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15.6504068F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 84.3495941F));
            tableLayoutPanel1.Controls.Add(panel_menu, 0, 0);
            tableLayoutPanel1.Controls.Add(panel_display, 1, 0);
            tableLayoutPanel1.Dock = DockStyle.Fill;
            tableLayoutPanel1.Location = new Point(0, 0);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.RowCount = 1;
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            tableLayoutPanel1.Size = new Size(984, 611);
            tableLayoutPanel1.TabIndex = 0;
            // 
            // panel_menu
            // 
            panel_menu.Controls.Add(btn_TunerControl);
            panel_menu.Controls.Add(lb_Freq);
            panel_menu.Controls.Add(lb_Count);
            panel_menu.Controls.Add(label2);
            panel_menu.Controls.Add(label1);
            panel_menu.Controls.Add(lb_ConnectStatus);
            panel_menu.Controls.Add(panel1);
            panel_menu.Controls.Add(button5);
            panel_menu.Controls.Add(btn_Curv);
            panel_menu.Controls.Add(btn_fifter);
            panel_menu.Controls.Add(btn_userCom);
            panel_menu.Dock = DockStyle.Fill;
            panel_menu.Location = new Point(3, 3);
            panel_menu.Name = "panel_menu";
            panel_menu.Size = new Size(148, 605);
            panel_menu.TabIndex = 0;
            // 
            // btn_TunerControl
            // 
            btn_TunerControl.Location = new Point(7, 304);
            btn_TunerControl.Name = "btn_TunerControl";
            btn_TunerControl.Size = new Size(132, 37);
            btn_TunerControl.TabIndex = 11;
            btn_TunerControl.Text = "智能调参";
            btn_TunerControl.UseVisualStyleBackColor = true;
            btn_TunerControl.Click += btn_TunerControl_Click;
            // 
            // lb_Freq
            // 
            lb_Freq.Location = new Point(70, 493);
            lb_Freq.Name = "lb_Freq";
            lb_Freq.Size = new Size(75, 23);
            lb_Freq.TabIndex = 10;
            lb_Freq.Text = "0";
            lb_Freq.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // lb_Count
            // 
            lb_Count.Location = new Point(70, 464);
            lb_Count.Name = "lb_Count";
            lb_Count.Size = new Size(75, 23);
            lb_Count.TabIndex = 9;
            lb_Count.Text = "0";
            lb_Count.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(9, 496);
            label2.Name = "label2";
            label2.Size = new Size(68, 17);
            label2.TabIndex = 8;
            label2.Text = "数据频率：";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(9, 466);
            label1.Name = "label1";
            label1.Size = new Size(68, 17);
            label1.TabIndex = 7;
            label1.Text = "接收数据：";
            // 
            // lb_ConnectStatus
            // 
            lb_ConnectStatus.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold, GraphicsUnit.Point, 134);
            lb_ConnectStatus.ForeColor = Color.Red;
            lb_ConnectStatus.Location = new Point(22, 415);
            lb_ConnectStatus.Name = "lb_ConnectStatus";
            lb_ConnectStatus.Size = new Size(100, 40);
            lb_ConnectStatus.TabIndex = 6;
            lb_ConnectStatus.Text = "未连接";
            lb_ConnectStatus.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // panel1
            // 
            panel1.BackgroundImage = (Image)resources.GetObject("panel1.BackgroundImage");
            panel1.BackgroundImageLayout = ImageLayout.Stretch;
            panel1.Location = new Point(9, 9);
            panel1.Name = "panel1";
            panel1.Size = new Size(130, 112);
            panel1.TabIndex = 5;
            // 
            // button5
            // 
            button5.Location = new Point(9, 559);
            button5.Name = "button5";
            button5.Size = new Size(132, 37);
            button5.TabIndex = 4;
            button5.Text = "融合滤波";
            button5.UseVisualStyleBackColor = true;
            button5.Visible = false;
            button5.Click += button5_Click;
            // 
            // btn_Curv
            // 
            btn_Curv.Location = new Point(9, 248);
            btn_Curv.Name = "btn_Curv";
            btn_Curv.Size = new Size(132, 37);
            btn_Curv.TabIndex = 2;
            btn_Curv.Text = "曲线分析";
            btn_Curv.UseVisualStyleBackColor = true;
            btn_Curv.Click += btn_Curv_Click;
            // 
            // btn_fifter
            // 
            btn_fifter.Location = new Point(7, 192);
            btn_fifter.Name = "btn_fifter";
            btn_fifter.Size = new Size(132, 37);
            btn_fifter.TabIndex = 1;
            btn_fifter.Text = "滤波数据";
            btn_fifter.UseVisualStyleBackColor = true;
            btn_fifter.Click += btn_fifter_Click;
            // 
            // btn_userCom
            // 
            btn_userCom.Location = new Point(7, 136);
            btn_userCom.Name = "btn_userCom";
            btn_userCom.Size = new Size(132, 37);
            btn_userCom.TabIndex = 0;
            btn_userCom.Text = "通讯连接";
            btn_userCom.UseVisualStyleBackColor = true;
            btn_userCom.Click += btn_userCom_Click;
            // 
            // panel_display
            // 
            panel_display.Dock = DockStyle.Fill;
            panel_display.Location = new Point(157, 3);
            panel_display.Name = "panel_display";
            panel_display.Size = new Size(824, 605);
            panel_display.TabIndex = 1;
            // 
            // timer1
            // 
            timer1.Tick += timer1_Tick;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(984, 611);
            Controls.Add(tableLayoutPanel1);
            Name = "Form1";
            Text = "滤波可视化平台";
            Load += Form1_Load;
            tableLayoutPanel1.ResumeLayout(false);
            panel_menu.ResumeLayout(false);
            panel_menu.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel tableLayoutPanel1;
        private Panel panel_menu;
        private Panel panel_display;
        private Button button5;
        private Button btn_Curv;
        private Button btn_fifter;
        private Button btn_userCom;
        private Panel panel1;
        private Label label2;
        private Label label1;
        private Label lb_ConnectStatus;
        private Label lb_Freq;
        private Label lb_Count;
        private System.Windows.Forms.Timer timer1;
        private Button btn_TunerControl;
    }
}
