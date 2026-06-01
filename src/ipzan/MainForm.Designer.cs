namespace ipzan
{
    partial class MainForm
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
            textBox1 = new TextBox();
            label1 = new Label();
            label2 = new Label();
            textBox2 = new TextBox();
            button1 = new Button();
            label5 = new Label();
            textBox3 = new TextBox();
            label3 = new Label();
            textBox4 = new TextBox();
            label4 = new Label();
            textBox5 = new TextBox();
            textBox6 = new TextBox();
            button2 = new Button();
            SuspendLayout();
            // 
            // textBox1
            // 
            textBox1.Location = new Point(154, 244);
            textBox1.Margin = new Padding(4);
            textBox1.Name = "textBox1";
            textBox1.Size = new Size(786, 30);
            textBox1.TabIndex = 0;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(116, 248);
            label1.Margin = new Padding(4, 0, 4, 0);
            label1.Name = "label1";
            label1.Size = new Size(26, 24);
            label1.TabIndex = 1;
            label1.Text = "IP";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(77, 133);
            label2.Margin = new Padding(4, 0, 4, 0);
            label2.Name = "label2";
            label2.Size = new Size(65, 24);
            label2.TabIndex = 3;
            label2.Text = "用户ID";
            // 
            // textBox2
            // 
            textBox2.Location = new Point(154, 130);
            textBox2.Margin = new Padding(4);
            textBox2.Name = "textBox2";
            textBox2.PasswordChar = '*';
            textBox2.Size = new Size(786, 30);
            textBox2.TabIndex = 2;
            textBox2.Text = "12CUEFB1GS8";
            // 
            // button1
            // 
            button1.Location = new Point(154, 281);
            button1.Margin = new Padding(4);
            button1.Name = "button1";
            button1.Size = new Size(115, 57);
            button1.TabIndex = 7;
            button1.Text = "添加";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(62, 18);
            label5.Margin = new Padding(4, 0, 4, 0);
            label5.Name = "label5";
            label5.Size = new Size(82, 24);
            label5.TabIndex = 11;
            label5.Text = "代理地址";
            // 
            // textBox3
            // 
            textBox3.Location = new Point(154, 14);
            textBox3.Margin = new Padding(4);
            textBox3.Multiline = true;
            textBox3.Name = "textBox3";
            textBox3.ScrollBars = ScrollBars.Vertical;
            textBox3.Size = new Size(786, 106);
            textBox3.TabIndex = 10;
            textBox3.Text = "https://service.ipzan.com/core-extract?num=1&no=20250819576712695526&minute=3&format=txt&repeat=1&protocol=1&pool=quality&mode=whitelist&secret=c6ooub2f39339hg";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(60, 171);
            label3.Margin = new Padding(4, 0, 4, 0);
            label3.Name = "label3";
            label3.Size = new Size(82, 24);
            label3.TabIndex = 13;
            label3.Text = "登录密码";
            // 
            // textBox4
            // 
            textBox4.Location = new Point(154, 168);
            textBox4.Margin = new Padding(4);
            textBox4.Name = "textBox4";
            textBox4.PasswordChar = '*';
            textBox4.Size = new Size(786, 30);
            textBox4.TabIndex = 12;
            textBox4.Text = "a15818511816";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(60, 209);
            label4.Margin = new Padding(4, 0, 4, 0);
            label4.Name = "label4";
            label4.Size = new Size(82, 24);
            label4.TabIndex = 15;
            label4.Text = "签名秘钥";
            // 
            // textBox5
            // 
            textBox5.Location = new Point(154, 206);
            textBox5.Margin = new Padding(4);
            textBox5.Name = "textBox5";
            textBox5.PasswordChar = '*';
            textBox5.Size = new Size(786, 30);
            textBox5.TabIndex = 14;
            textBox5.Text = "rhg593ac4kt788kn";
            // 
            // textBox6
            // 
            textBox6.Location = new Point(26, 350);
            textBox6.Margin = new Padding(4);
            textBox6.Multiline = true;
            textBox6.Name = "textBox6";
            textBox6.ScrollBars = ScrollBars.Both;
            textBox6.Size = new Size(914, 312);
            textBox6.TabIndex = 16;
            // 
            // button2
            // 
            button2.Location = new Point(825, 281);
            button2.Margin = new Padding(4);
            button2.Name = "button2";
            button2.Size = new Size(115, 57);
            button2.TabIndex = 17;
            button2.Text = "删除";
            button2.UseVisualStyleBackColor = true;
            button2.Click += button2_Click;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(120F, 120F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(978, 675);
            Controls.Add(button2);
            Controls.Add(textBox6);
            Controls.Add(label4);
            Controls.Add(textBox5);
            Controls.Add(label3);
            Controls.Add(textBox4);
            Controls.Add(label5);
            Controls.Add(textBox3);
            Controls.Add(button1);
            Controls.Add(label2);
            Controls.Add(textBox2);
            Controls.Add(label1);
            Controls.Add(textBox1);
            Margin = new Padding(4);
            Name = "MainForm";
            Text = "Form1";
            Load += MainForm_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TextBox textBox1;
        private Label label1;
        private Label label2;
        private TextBox textBox2;
        private Button button1;
        private Label label5;
        private TextBox textBox3;
        private Label label3;
        private TextBox textBox4;
        private Label label4;
        private TextBox textBox5;
        private TextBox textBox6;
        private Button button2;
    }
}
