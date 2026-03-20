namespace Updater
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            label13 = new Label();
            comboBox_VersionList = new ComboBox();
            btnUpdate = new Button();
            progressBar1 = new ProgressBar();
            textBox_ApiUrl = new TextBox();
            label14 = new Label();
            SuspendLayout();
            // 
            // label13
            // 
            label13.AutoSize = true;
            label13.Location = new Point(25, 94);
            label13.Margin = new Padding(5, 0, 5, 0);
            label13.Name = "label13";
            label13.Size = new Size(73, 20);
            label13.TabIndex = 178;
            label13.Text = "文件列表:";
            // 
            // comboBox_VersionList
            // 
            comboBox_VersionList.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox_VersionList.FormattingEnabled = true;
            comboBox_VersionList.Location = new Point(101, 90);
            comboBox_VersionList.Margin = new Padding(3, 2, 3, 2);
            comboBox_VersionList.Name = "comboBox_VersionList";
            comboBox_VersionList.Size = new Size(355, 28);
            comboBox_VersionList.TabIndex = 177;
            // 
            // btnUpdate
            // 
            btnUpdate.Location = new Point(462, 90);
            btnUpdate.Name = "btnUpdate";
            btnUpdate.Size = new Size(87, 30);
            btnUpdate.TabIndex = 176;
            btnUpdate.Text = "更新";
            btnUpdate.UseVisualStyleBackColor = true;
            btnUpdate.Click += btnUpdate_Click;
            // 
            // progressBar1
            // 
            progressBar1.Dock = DockStyle.Bottom;
            progressBar1.Location = new Point(0, 289);
            progressBar1.Name = "progressBar1";
            progressBar1.Size = new Size(597, 29);
            progressBar1.TabIndex = 179;
            progressBar1.Visible = false;
            // 
            // textBox_ApiUrl
            // 
            textBox_ApiUrl.Location = new Point(101, 43);
            textBox_ApiUrl.Margin = new Padding(5, 4, 5, 4);
            textBox_ApiUrl.Name = "textBox_ApiUrl";
            textBox_ApiUrl.Size = new Size(448, 27);
            textBox_ApiUrl.TabIndex = 181;
            textBox_ApiUrl.Text = "http://211.154.24.179:9000/update.php";
            textBox_ApiUrl.Leave += textBox_ApiUrl_Leave;
            // 
            // label14
            // 
            label14.AutoSize = true;
            label14.Location = new Point(25, 47);
            label14.Margin = new Padding(5, 0, 5, 0);
            label14.Name = "label14";
            label14.Size = new Size(73, 20);
            label14.TabIndex = 180;
            label14.Text = "更新接口:";
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(9F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(597, 318);
            Controls.Add(textBox_ApiUrl);
            Controls.Add(label14);
            Controls.Add(progressBar1);
            Controls.Add(label13);
            Controls.Add(comboBox_VersionList);
            Controls.Add(btnUpdate);
            MaximizeBox = false;
            Name = "MainForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Updater(1.0.0.3)";
            Load += MainForm_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label13;
        private ComboBox comboBox_VersionList;
        private Button btnUpdate;
        private ProgressBar progressBar1;
        private TextBox textBox_ApiUrl;
        private Label label14;
    }
}