namespace FilterVisualPlatform
{
    partial class UserDatas
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
            components = new System.ComponentModel.Container();
            groupBox1 = new GroupBox();
            lb_tofFilt = new Label();
            lb_filtered = new Label();
            label3 = new Label();
            label4 = new Label();
            lb_TofRaw = new Label();
            lb_Raw = new Label();
            label2 = new Label();
            label1 = new Label();
            timer1 = new System.Windows.Forms.Timer(components);
            groupBox2 = new GroupBox();
            lb_KFtofFilt = new Label();
            lb_KFfiltered = new Label();
            label7 = new Label();
            label8 = new Label();
            lb_KFtofRaw = new Label();
            lb_KFRaw = new Label();
            label11 = new Label();
            label12 = new Label();
            groupBox3 = new GroupBox();
            lb_OEtofFilt = new Label();
            lb_OEfiltered = new Label();
            label15 = new Label();
            label16 = new Label();
            lb_OEtofRaw = new Label();
            lb_OERaw = new Label();
            label19 = new Label();
            label20 = new Label();
            groupBox4 = new GroupBox();
            lb_RHtofFilt = new Label();
            lb_RHfiltered = new Label();
            label23 = new Label();
            label24 = new Label();
            lb_RHtofRaw = new Label();
            lb_RHRaw = new Label();
            label27 = new Label();
            label28 = new Label();
            groupBox1.SuspendLayout();
            groupBox2.SuspendLayout();
            groupBox3.SuspendLayout();
            groupBox4.SuspendLayout();
            SuspendLayout();
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(lb_tofFilt);
            groupBox1.Controls.Add(lb_filtered);
            groupBox1.Controls.Add(label3);
            groupBox1.Controls.Add(label4);
            groupBox1.Controls.Add(lb_TofRaw);
            groupBox1.Controls.Add(lb_Raw);
            groupBox1.Controls.Add(label2);
            groupBox1.Controls.Add(label1);
            groupBox1.Location = new Point(3, 3);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(818, 121);
            groupBox1.TabIndex = 0;
            groupBox1.TabStop = false;
            groupBox1.Text = "原始数据";
            // 
            // lb_tofFilt
            // 
            lb_tofFilt.AutoSize = true;
            lb_tofFilt.Location = new Point(604, 80);
            lb_tofFilt.Name = "lb_tofFilt";
            lb_tofFilt.Size = new Size(13, 17);
            lb_tofFilt.TabIndex = 7;
            lb_tofFilt.Text = "-";
            // 
            // lb_filtered
            // 
            lb_filtered.AutoSize = true;
            lb_filtered.Location = new Point(604, 28);
            lb_filtered.Name = "lb_filtered";
            lb_filtered.Size = new Size(13, 17);
            lb_filtered.TabIndex = 6;
            lb_filtered.Text = "-";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(425, 80);
            label3.Name = "label3";
            label3.Size = new Size(92, 17);
            label3.TabIndex = 5;
            label3.Text = "激光滤波数据：";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(413, 28);
            label4.Name = "label4";
            label4.Size = new Size(104, 17);
            label4.TabIndex = 4;
            label4.Text = "超声波滤波数据：";
            // 
            // lb_TofRaw
            // 
            lb_TofRaw.AutoSize = true;
            lb_TofRaw.Location = new Point(210, 80);
            lb_TofRaw.Name = "lb_TofRaw";
            lb_TofRaw.Size = new Size(13, 17);
            lb_TofRaw.TabIndex = 3;
            lb_TofRaw.Text = "-";
            // 
            // lb_Raw
            // 
            lb_Raw.AutoSize = true;
            lb_Raw.Location = new Point(210, 28);
            lb_Raw.Name = "lb_Raw";
            lb_Raw.Size = new Size(13, 17);
            lb_Raw.TabIndex = 2;
            lb_Raw.Text = "-";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(29, 80);
            label2.Name = "label2";
            label2.Size = new Size(92, 17);
            label2.TabIndex = 1;
            label2.Text = "激光测量数据：";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(17, 28);
            label1.Name = "label1";
            label1.Size = new Size(104, 17);
            label1.TabIndex = 0;
            label1.Text = "超声波测量数据：";
            // 
            // timer1
            // 
            timer1.Tick += timer1_Tick;
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(lb_KFtofFilt);
            groupBox2.Controls.Add(lb_KFfiltered);
            groupBox2.Controls.Add(label7);
            groupBox2.Controls.Add(label8);
            groupBox2.Controls.Add(lb_KFtofRaw);
            groupBox2.Controls.Add(lb_KFRaw);
            groupBox2.Controls.Add(label11);
            groupBox2.Controls.Add(label12);
            groupBox2.Location = new Point(3, 130);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(818, 121);
            groupBox2.TabIndex = 1;
            groupBox2.TabStop = false;
            groupBox2.Text = "卡尔曼滤波数据";
            // 
            // lb_KFtofFilt
            // 
            lb_KFtofFilt.AutoSize = true;
            lb_KFtofFilt.Location = new Point(604, 80);
            lb_KFtofFilt.Name = "lb_KFtofFilt";
            lb_KFtofFilt.Size = new Size(13, 17);
            lb_KFtofFilt.TabIndex = 7;
            lb_KFtofFilt.Text = "-";
            // 
            // lb_KFfiltered
            // 
            lb_KFfiltered.AutoSize = true;
            lb_KFfiltered.Location = new Point(604, 28);
            lb_KFfiltered.Name = "lb_KFfiltered";
            lb_KFfiltered.Size = new Size(13, 17);
            lb_KFfiltered.TabIndex = 6;
            lb_KFfiltered.Text = "-";
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Location = new Point(425, 80);
            label7.Name = "label7";
            label7.Size = new Size(92, 17);
            label7.TabIndex = 5;
            label7.Text = "激光滤波数据：";
            // 
            // label8
            // 
            label8.AutoSize = true;
            label8.Location = new Point(413, 28);
            label8.Name = "label8";
            label8.Size = new Size(104, 17);
            label8.TabIndex = 4;
            label8.Text = "超声波滤波数据：";
            // 
            // lb_KFtofRaw
            // 
            lb_KFtofRaw.AutoSize = true;
            lb_KFtofRaw.Location = new Point(210, 80);
            lb_KFtofRaw.Name = "lb_KFtofRaw";
            lb_KFtofRaw.Size = new Size(13, 17);
            lb_KFtofRaw.TabIndex = 3;
            lb_KFtofRaw.Text = "-";
            // 
            // lb_KFRaw
            // 
            lb_KFRaw.AutoSize = true;
            lb_KFRaw.Location = new Point(210, 28);
            lb_KFRaw.Name = "lb_KFRaw";
            lb_KFRaw.Size = new Size(13, 17);
            lb_KFRaw.TabIndex = 2;
            lb_KFRaw.Text = "-";
            // 
            // label11
            // 
            label11.AutoSize = true;
            label11.Location = new Point(29, 80);
            label11.Name = "label11";
            label11.Size = new Size(92, 17);
            label11.TabIndex = 1;
            label11.Text = "激光测量数据：";
            // 
            // label12
            // 
            label12.AutoSize = true;
            label12.Location = new Point(17, 28);
            label12.Name = "label12";
            label12.Size = new Size(104, 17);
            label12.TabIndex = 0;
            label12.Text = "超声波测量数据：";
            // 
            // groupBox3
            // 
            groupBox3.Controls.Add(lb_OEtofFilt);
            groupBox3.Controls.Add(lb_OEfiltered);
            groupBox3.Controls.Add(label15);
            groupBox3.Controls.Add(label16);
            groupBox3.Controls.Add(lb_OEtofRaw);
            groupBox3.Controls.Add(lb_OERaw);
            groupBox3.Controls.Add(label19);
            groupBox3.Controls.Add(label20);
            groupBox3.Location = new Point(6, 257);
            groupBox3.Name = "groupBox3";
            groupBox3.Size = new Size(818, 121);
            groupBox3.TabIndex = 2;
            groupBox3.TabStop = false;
            groupBox3.Text = "自适应低通数据";
            // 
            // lb_OEtofFilt
            // 
            lb_OEtofFilt.AutoSize = true;
            lb_OEtofFilt.Location = new Point(604, 82);
            lb_OEtofFilt.Name = "lb_OEtofFilt";
            lb_OEtofFilt.Size = new Size(13, 17);
            lb_OEtofFilt.TabIndex = 7;
            lb_OEtofFilt.Text = "-";
            // 
            // lb_OEfiltered
            // 
            lb_OEfiltered.AutoSize = true;
            lb_OEfiltered.Location = new Point(604, 28);
            lb_OEfiltered.Name = "lb_OEfiltered";
            lb_OEfiltered.Size = new Size(13, 17);
            lb_OEfiltered.TabIndex = 6;
            lb_OEfiltered.Text = "-";
            // 
            // label15
            // 
            label15.AutoSize = true;
            label15.Location = new Point(425, 82);
            label15.Name = "label15";
            label15.Size = new Size(92, 17);
            label15.TabIndex = 5;
            label15.Text = "激光滤波数据：";
            // 
            // label16
            // 
            label16.AutoSize = true;
            label16.Location = new Point(413, 28);
            label16.Name = "label16";
            label16.Size = new Size(104, 17);
            label16.TabIndex = 4;
            label16.Text = "超声波滤波数据：";
            // 
            // lb_OEtofRaw
            // 
            lb_OEtofRaw.AutoSize = true;
            lb_OEtofRaw.Location = new Point(210, 82);
            lb_OEtofRaw.Name = "lb_OEtofRaw";
            lb_OEtofRaw.Size = new Size(13, 17);
            lb_OEtofRaw.TabIndex = 3;
            lb_OEtofRaw.Text = "-";
            // 
            // lb_OERaw
            // 
            lb_OERaw.AutoSize = true;
            lb_OERaw.Location = new Point(210, 28);
            lb_OERaw.Name = "lb_OERaw";
            lb_OERaw.Size = new Size(13, 17);
            lb_OERaw.TabIndex = 2;
            lb_OERaw.Text = "-";
            // 
            // label19
            // 
            label19.AutoSize = true;
            label19.Location = new Point(29, 82);
            label19.Name = "label19";
            label19.Size = new Size(92, 17);
            label19.TabIndex = 1;
            label19.Text = "激光测量数据：";
            // 
            // label20
            // 
            label20.AutoSize = true;
            label20.Location = new Point(17, 28);
            label20.Name = "label20";
            label20.Size = new Size(104, 17);
            label20.TabIndex = 0;
            label20.Text = "超声波测量数据：";
            // 
            // groupBox4
            // 
            groupBox4.Controls.Add(lb_RHtofFilt);
            groupBox4.Controls.Add(lb_RHfiltered);
            groupBox4.Controls.Add(label23);
            groupBox4.Controls.Add(label24);
            groupBox4.Controls.Add(lb_RHtofRaw);
            groupBox4.Controls.Add(lb_RHRaw);
            groupBox4.Controls.Add(label27);
            groupBox4.Controls.Add(label28);
            groupBox4.Location = new Point(3, 384);
            groupBox4.Name = "groupBox4";
            groupBox4.Size = new Size(818, 108);
            groupBox4.TabIndex = 3;
            groupBox4.TabStop = false;
            groupBox4.Text = "H∞滤波数据";
            // 
            // lb_RHtofFilt
            // 
            lb_RHtofFilt.AutoSize = true;
            lb_RHtofFilt.Location = new Point(604, 76);
            lb_RHtofFilt.Name = "lb_RHtofFilt";
            lb_RHtofFilt.Size = new Size(13, 17);
            lb_RHtofFilt.TabIndex = 7;
            lb_RHtofFilt.Text = "-";
            // 
            // lb_RHfiltered
            // 
            lb_RHfiltered.AutoSize = true;
            lb_RHfiltered.Location = new Point(604, 28);
            lb_RHfiltered.Name = "lb_RHfiltered";
            lb_RHfiltered.Size = new Size(13, 17);
            lb_RHfiltered.TabIndex = 6;
            lb_RHfiltered.Text = "-";
            // 
            // label23
            // 
            label23.AutoSize = true;
            label23.Location = new Point(425, 76);
            label23.Name = "label23";
            label23.Size = new Size(92, 17);
            label23.TabIndex = 5;
            label23.Text = "激光滤波数据：";
            // 
            // label24
            // 
            label24.AutoSize = true;
            label24.Location = new Point(413, 28);
            label24.Name = "label24";
            label24.Size = new Size(104, 17);
            label24.TabIndex = 4;
            label24.Text = "超声波滤波数据：";
            // 
            // lb_RHtofRaw
            // 
            lb_RHtofRaw.AutoSize = true;
            lb_RHtofRaw.Location = new Point(210, 76);
            lb_RHtofRaw.Name = "lb_RHtofRaw";
            lb_RHtofRaw.Size = new Size(13, 17);
            lb_RHtofRaw.TabIndex = 3;
            lb_RHtofRaw.Text = "-";
            // 
            // lb_RHRaw
            // 
            lb_RHRaw.AutoSize = true;
            lb_RHRaw.Location = new Point(210, 28);
            lb_RHRaw.Name = "lb_RHRaw";
            lb_RHRaw.Size = new Size(13, 17);
            lb_RHRaw.TabIndex = 2;
            lb_RHRaw.Text = "-";
            // 
            // label27
            // 
            label27.AutoSize = true;
            label27.Location = new Point(29, 76);
            label27.Name = "label27";
            label27.Size = new Size(92, 17);
            label27.TabIndex = 1;
            label27.Text = "激光测量数据：";
            // 
            // label28
            // 
            label28.AutoSize = true;
            label28.Location = new Point(17, 28);
            label28.Name = "label28";
            label28.Size = new Size(104, 17);
            label28.TabIndex = 0;
            label28.Text = "超声波测量数据：";
            // 
            // UserDatas
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Controls.Add(groupBox4);
            Controls.Add(groupBox3);
            Controls.Add(groupBox2);
            Controls.Add(groupBox1);
            Name = "UserDatas";
            Size = new Size(824, 605);
            Load += UserDatas_Load;
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            groupBox3.ResumeLayout(false);
            groupBox3.PerformLayout();
            groupBox4.ResumeLayout(false);
            groupBox4.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private GroupBox groupBox1;
        private Label lb_Raw;
        private Label label2;
        private Label label1;
        private Label lb_tofFilt;
        private Label lb_filtered;
        private Label label3;
        private Label label4;
        private Label lb_TofRaw;
        private System.Windows.Forms.Timer timer1;
        private GroupBox groupBox2;
        private Label lb_KFtofFilt;
        private Label lb_KFfiltered;
        private Label label7;
        private Label label8;
        private Label lb_KFtofRaw;
        private Label lb_KFRaw;
        private Label label11;
        private Label label12;
        private GroupBox groupBox3;
        private Label lb_OEtofFilt;
        private Label lb_OEfiltered;
        private Label label15;
        private Label label16;
        private Label lb_OEtofRaw;
        private Label lb_OERaw;
        private Label label19;
        private Label label20;
        private GroupBox groupBox4;
        private Label lb_RHtofFilt;
        private Label lb_RHfiltered;
        private Label label23;
        private Label label24;
        private Label lb_RHtofRaw;
        private Label lb_RHRaw;
        private Label label27;
        private Label label28;
    }
}
