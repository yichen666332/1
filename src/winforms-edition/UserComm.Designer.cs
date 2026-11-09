namespace FilterVisualPlatform
{
    partial class UserComm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(UserComm));
            panel1 = new Panel();
            lb_Count = new Label();
            label1 = new Label();
            btn_Connect = new Button();
            cb_comPort = new ComboBox();
            cb_baud = new ComboBox();
            SuspendLayout();
            // 
            // panel1
            // 
            panel1.BackColor = Color.Black;
            panel1.BackgroundImage = (Image)resources.GetObject("panel1.BackgroundImage");
            panel1.BackgroundImageLayout = ImageLayout.Stretch;
            panel1.Location = new Point(309, 118);
            panel1.Name = "panel1";
            panel1.Size = new Size(211, 201);
            panel1.TabIndex = 0;
            // 
            // lb_Count
            // 
            lb_Count.Location = new Point(309, 345);
            lb_Count.Name = "lb_Count";
            lb_Count.Size = new Size(75, 23);
            lb_Count.TabIndex = 10;
            lb_Count.Text = "串口号：";
            lb_Count.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // label1
            // 
            label1.Location = new Point(309, 386);
            label1.Name = "label1";
            label1.Size = new Size(75, 23);
            label1.TabIndex = 11;
            label1.Text = "波特率：";
            label1.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // btn_Connect
            // 
            btn_Connect.Location = new Point(359, 439);
            btn_Connect.Name = "btn_Connect";
            btn_Connect.Size = new Size(132, 37);
            btn_Connect.TabIndex = 12;
            btn_Connect.Text = "连  接";
            btn_Connect.UseVisualStyleBackColor = true;
            btn_Connect.Click += btn_Connect_Click;
            // 
            // cb_comPort
            // 
            cb_comPort.FormattingEnabled = true;
            cb_comPort.Location = new Point(390, 345);
            cb_comPort.Name = "cb_comPort";
            cb_comPort.Size = new Size(130, 25);
            cb_comPort.TabIndex = 13;
            // 
            // cb_baud
            // 
            cb_baud.FormattingEnabled = true;
            cb_baud.Items.AddRange(new object[] { "57600", "115200", "921600" });
            cb_baud.Location = new Point(390, 386);
            cb_baud.Name = "cb_baud";
            cb_baud.Size = new Size(130, 25);
            cb_baud.TabIndex = 14;
            // 
            // UserComm
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(cb_baud);
            Controls.Add(cb_comPort);
            Controls.Add(btn_Connect);
            Controls.Add(label1);
            Controls.Add(lb_Count);
            Controls.Add(panel1);
            Name = "UserComm";
            Size = new Size(824, 605);
            Load += UserComm_Load;
            ResumeLayout(false);
        }

        #endregion

        private Panel panel1;
        private Label lb_Count;
        private Label label1;
        private Button btn_Connect;
        private ComboBox cb_comPort;
        private ComboBox cb_baud;
    }
}
