namespace DDR4_TestingApp
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
            captureInfoBindingSource = new BindingSource(components);
            statusStrip1 = new StatusStrip();
            cmdName = new ToolStripStatusLabel();
            progressBar = new ToolStripProgressBar();
            statusLabel = new ToolStripStatusLabel();
            connectionState = new ToolStripStatusLabel();
            ConnectAndConfigure = new TabControl();
            tabPage1 = new TabPage();
            groupBox7 = new GroupBox();
            groupBox4 = new GroupBox();
            chipSizeBox = new TextBox();
            label21 = new Label();
            applyConfiguration = new Button();
            sideB = new GroupBox();
            sel_dram7 = new Button();
            label8 = new Label();
            dram7 = new Panel();
            sel_dram6 = new Button();
            label9 = new Label();
            dram6 = new Panel();
            sel_dram5 = new Button();
            label10 = new Label();
            dram5 = new Panel();
            sel_dram4 = new Button();
            label11 = new Label();
            dram4 = new Panel();
            enableChipSelection = new CheckBox();
            sideA = new GroupBox();
            sel_dram3 = new Button();
            label7 = new Label();
            dram3 = new Panel();
            sel_dram2 = new Button();
            label6 = new Label();
            dram2 = new Panel();
            sel_dram1 = new Button();
            label5 = new Label();
            dram1 = new Panel();
            sel_dram0 = new Button();
            label4 = new Label();
            dram0 = new Panel();
            groupBox2 = new GroupBox();
            uptimeBox = new TextBox();
            label15 = new Label();
            modelBox = new TextBox();
            groupBox14 = new GroupBox();
            downlinkBox = new TextBox();
            label18 = new Label();
            uplinkBox = new TextBox();
            ramUsageBox = new TextBox();
            cpuPercentBox = new TextBox();
            ramUsageBar = new ProgressBar();
            cpuBar = new ProgressBar();
            label17 = new Label();
            label16 = new Label();
            label14 = new Label();
            manufacturerBox = new TextBox();
            label13 = new Label();
            label12 = new Label();
            groupBox3 = new GroupBox();
            groupBox15 = new GroupBox();
            label20 = new Label();
            endAddrBox = new TextBox();
            startAddrBox = new TextBox();
            selectedChipBox = new TextBox();
            label19 = new Label();
            chipOrgBox = new TextBox();
            label3 = new Label();
            groupBox1 = new GroupBox();
            connect_btn = new Button();
            port = new TextBox();
            label2 = new Label();
            ip_address = new TextBox();
            label1 = new Label();
            tabPage2 = new TabPage();
            groupBox6 = new GroupBox();
            groupBox5 = new GroupBox();
            panel1 = new Panel();
            groupBox13 = new GroupBox();
            cancelButton = new Button();
            groupBox11 = new GroupBox();
            prngSeed = new TextBox();
            genSeed = new Button();
            groupBox9 = new GroupBox();
            groupBox12 = new GroupBox();
            verificationResults = new RichTextBox();
            verifyButton = new Button();
            groupBox10 = new GroupBox();
            verifyMode = new ComboBox();
            groupBox8 = new GroupBox();
            writeButton = new Button();
            writeModeLabel = new GroupBox();
            writeMode = new ComboBox();
            ((System.ComponentModel.ISupportInitialize)captureInfoBindingSource).BeginInit();
            statusStrip1.SuspendLayout();
            ConnectAndConfigure.SuspendLayout();
            tabPage1.SuspendLayout();
            groupBox4.SuspendLayout();
            sideB.SuspendLayout();
            sideA.SuspendLayout();
            groupBox2.SuspendLayout();
            groupBox14.SuspendLayout();
            groupBox3.SuspendLayout();
            groupBox15.SuspendLayout();
            groupBox1.SuspendLayout();
            tabPage2.SuspendLayout();
            groupBox5.SuspendLayout();
            groupBox13.SuspendLayout();
            groupBox11.SuspendLayout();
            groupBox9.SuspendLayout();
            groupBox12.SuspendLayout();
            groupBox10.SuspendLayout();
            groupBox8.SuspendLayout();
            writeModeLabel.SuspendLayout();
            SuspendLayout();
            // 
            // captureInfoBindingSource
            // 
            captureInfoBindingSource.DataSource = typeof(CaptureInfo);
            // 
            // statusStrip1
            // 
            statusStrip1.Items.AddRange(new ToolStripItem[] { cmdName, progressBar, statusLabel, connectionState });
            statusStrip1.Location = new Point(0, 739);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Size = new Size(1184, 22);
            statusStrip1.TabIndex = 1;
            statusStrip1.Text = "statusStrip1";
            // 
            // cmdName
            // 
            cmdName.Name = "cmdName";
            cmdName.Size = new Size(118, 17);
            cmdName.Text = "toolStripStatusLabel1";
            // 
            // progressBar
            // 
            progressBar.AutoSize = false;
            progressBar.Name = "progressBar";
            progressBar.Size = new Size(800, 16);
            // 
            // statusLabel
            // 
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(118, 17);
            statusLabel.Text = "toolStripStatusLabel2";
            // 
            // connectionState
            // 
            connectionState.ForeColor = SystemColors.ControlLightLight;
            connectionState.Name = "connectionState";
            connectionState.Size = new Size(118, 17);
            connectionState.Text = "toolStripStatusLabel3";
            // 
            // ConnectAndConfigure
            // 
            ConnectAndConfigure.AccessibleName = "";
            ConnectAndConfigure.Controls.Add(tabPage1);
            ConnectAndConfigure.Controls.Add(tabPage2);
            ConnectAndConfigure.Location = new Point(0, 0);
            ConnectAndConfigure.Name = "ConnectAndConfigure";
            ConnectAndConfigure.SelectedIndex = 0;
            ConnectAndConfigure.Size = new Size(1184, 736);
            ConnectAndConfigure.TabIndex = 2;
            // 
            // tabPage1
            // 
            tabPage1.Controls.Add(groupBox7);
            tabPage1.Controls.Add(groupBox4);
            tabPage1.Controls.Add(groupBox2);
            tabPage1.Controls.Add(groupBox1);
            tabPage1.Location = new Point(4, 24);
            tabPage1.Name = "tabPage1";
            tabPage1.Padding = new Padding(3);
            tabPage1.Size = new Size(1176, 708);
            tabPage1.TabIndex = 0;
            tabPage1.Text = "Connection and Configuration";
            tabPage1.UseVisualStyleBackColor = true;
            // 
            // groupBox7
            // 
            groupBox7.Location = new Point(480, 162);
            groupBox7.Name = "groupBox7";
            groupBox7.Size = new Size(688, 502);
            groupBox7.TabIndex = 4;
            groupBox7.TabStop = false;
            groupBox7.Text = "Status Log";
            // 
            // groupBox4
            // 
            groupBox4.Controls.Add(chipSizeBox);
            groupBox4.Controls.Add(label21);
            groupBox4.Controls.Add(applyConfiguration);
            groupBox4.Controls.Add(sideB);
            groupBox4.Controls.Add(enableChipSelection);
            groupBox4.Controls.Add(sideA);
            groupBox4.Location = new Point(8, 162);
            groupBox4.Name = "groupBox4";
            groupBox4.Size = new Size(466, 502);
            groupBox4.TabIndex = 4;
            groupBox4.TabStop = false;
            groupBox4.Text = "SODIMM Configuration";
            // 
            // chipSizeBox
            // 
            chipSizeBox.Location = new Point(372, 20);
            chipSizeBox.Name = "chipSizeBox";
            chipSizeBox.Size = new Size(71, 23);
            chipSizeBox.TabIndex = 4;
            chipSizeBox.Text = "1";
            // 
            // label21
            // 
            label21.AutoSize = true;
            label21.Location = new Point(256, 23);
            label21.Name = "label21";
            label21.Size = new Size(110, 15);
            label21.TabIndex = 8;
            label21.Text = "Chip Capacity (GB):";
            // 
            // applyConfiguration
            // 
            applyConfiguration.Location = new Point(6, 468);
            applyConfiguration.Name = "applyConfiguration";
            applyConfiguration.Size = new Size(451, 23);
            applyConfiguration.TabIndex = 12;
            applyConfiguration.Text = "Apply Configuration";
            applyConfiguration.UseVisualStyleBackColor = true;
            applyConfiguration.Click += applyConfiguration_Click;
            // 
            // sideB
            // 
            sideB.Controls.Add(sel_dram7);
            sideB.Controls.Add(label8);
            sideB.Controls.Add(dram7);
            sideB.Controls.Add(sel_dram6);
            sideB.Controls.Add(label9);
            sideB.Controls.Add(dram6);
            sideB.Controls.Add(sel_dram5);
            sideB.Controls.Add(label10);
            sideB.Controls.Add(dram5);
            sideB.Controls.Add(sel_dram4);
            sideB.Controls.Add(label11);
            sideB.Controls.Add(dram4);
            sideB.Enabled = false;
            sideB.Location = new Point(6, 258);
            sideB.Name = "sideB";
            sideB.Size = new Size(451, 204);
            sideB.TabIndex = 12;
            sideB.TabStop = false;
            sideB.Text = "Side B";
            // 
            // sel_dram7
            // 
            sel_dram7.Location = new Point(339, 174);
            sel_dram7.Name = "sel_dram7";
            sel_dram7.Size = new Size(105, 23);
            sel_dram7.TabIndex = 11;
            sel_dram7.Text = "Select";
            sel_dram7.UseVisualStyleBackColor = true;
            sel_dram7.Click += sel_dram7_Click;
            // 
            // label8
            // 
            label8.AutoSize = true;
            label8.Location = new Point(366, 28);
            label8.Name = "label8";
            label8.Size = new Size(50, 15);
            label8.TabIndex = 10;
            label8.Text = "DRAM 7";
            label8.TextAlign = ContentAlignment.TopCenter;
            // 
            // dram7
            // 
            dram7.BackColor = Color.Silver;
            dram7.BorderStyle = BorderStyle.FixedSingle;
            dram7.Location = new Point(339, 46);
            dram7.Name = "dram7";
            dram7.Size = new Size(105, 122);
            dram7.TabIndex = 9;
            // 
            // sel_dram6
            // 
            sel_dram6.Location = new Point(228, 174);
            sel_dram6.Name = "sel_dram6";
            sel_dram6.Size = new Size(105, 23);
            sel_dram6.TabIndex = 8;
            sel_dram6.Text = "Select";
            sel_dram6.UseVisualStyleBackColor = true;
            sel_dram6.Click += sel_dram6_Click;
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.Location = new Point(255, 28);
            label9.Name = "label9";
            label9.Size = new Size(50, 15);
            label9.TabIndex = 7;
            label9.Text = "DRAM 6";
            label9.TextAlign = ContentAlignment.TopCenter;
            // 
            // dram6
            // 
            dram6.BackColor = Color.Silver;
            dram6.BorderStyle = BorderStyle.FixedSingle;
            dram6.Location = new Point(228, 46);
            dram6.Name = "dram6";
            dram6.Size = new Size(105, 122);
            dram6.TabIndex = 6;
            // 
            // sel_dram5
            // 
            sel_dram5.Location = new Point(117, 174);
            sel_dram5.Name = "sel_dram5";
            sel_dram5.Size = new Size(105, 23);
            sel_dram5.TabIndex = 5;
            sel_dram5.Text = "Select";
            sel_dram5.UseVisualStyleBackColor = true;
            sel_dram5.Click += sel_dram5_Click;
            // 
            // label10
            // 
            label10.AutoSize = true;
            label10.Location = new Point(144, 28);
            label10.Name = "label10";
            label10.Size = new Size(50, 15);
            label10.TabIndex = 4;
            label10.Text = "DRAM 5";
            label10.TextAlign = ContentAlignment.TopCenter;
            // 
            // dram5
            // 
            dram5.BackColor = Color.Silver;
            dram5.BorderStyle = BorderStyle.FixedSingle;
            dram5.Location = new Point(117, 46);
            dram5.Name = "dram5";
            dram5.Size = new Size(105, 122);
            dram5.TabIndex = 3;
            // 
            // sel_dram4
            // 
            sel_dram4.Location = new Point(6, 174);
            sel_dram4.Name = "sel_dram4";
            sel_dram4.Size = new Size(105, 23);
            sel_dram4.TabIndex = 2;
            sel_dram4.Text = "Select";
            sel_dram4.UseVisualStyleBackColor = true;
            sel_dram4.Click += sel_dram4_Click;
            // 
            // label11
            // 
            label11.AutoSize = true;
            label11.Location = new Point(33, 28);
            label11.Name = "label11";
            label11.Size = new Size(50, 15);
            label11.TabIndex = 1;
            label11.Text = "DRAM 4";
            label11.TextAlign = ContentAlignment.TopCenter;
            // 
            // dram4
            // 
            dram4.BackColor = Color.Silver;
            dram4.BorderStyle = BorderStyle.FixedSingle;
            dram4.Location = new Point(6, 46);
            dram4.Name = "dram4";
            dram4.Size = new Size(105, 122);
            dram4.TabIndex = 0;
            // 
            // enableChipSelection
            // 
            enableChipSelection.AutoSize = true;
            enableChipSelection.Location = new Point(6, 22);
            enableChipSelection.Name = "enableChipSelection";
            enableChipSelection.Size = new Size(137, 19);
            enableChipSelection.TabIndex = 1;
            enableChipSelection.Text = "Enable Chip Isolation";
            enableChipSelection.UseVisualStyleBackColor = true;
            // 
            // sideA
            // 
            sideA.Controls.Add(sel_dram3);
            sideA.Controls.Add(label7);
            sideA.Controls.Add(dram3);
            sideA.Controls.Add(sel_dram2);
            sideA.Controls.Add(label6);
            sideA.Controls.Add(dram2);
            sideA.Controls.Add(sel_dram1);
            sideA.Controls.Add(label5);
            sideA.Controls.Add(dram1);
            sideA.Controls.Add(sel_dram0);
            sideA.Controls.Add(label4);
            sideA.Controls.Add(dram0);
            sideA.Enabled = false;
            sideA.Location = new Point(6, 48);
            sideA.Name = "sideA";
            sideA.Size = new Size(451, 204);
            sideA.TabIndex = 0;
            sideA.TabStop = false;
            sideA.Text = "Side A";
            // 
            // sel_dram3
            // 
            sel_dram3.Location = new Point(339, 174);
            sel_dram3.Name = "sel_dram3";
            sel_dram3.Size = new Size(105, 23);
            sel_dram3.TabIndex = 11;
            sel_dram3.Text = "Select";
            sel_dram3.UseVisualStyleBackColor = true;
            sel_dram3.Click += sel_dram3_Click;
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Location = new Point(366, 28);
            label7.Name = "label7";
            label7.Size = new Size(50, 15);
            label7.TabIndex = 10;
            label7.Text = "DRAM 3";
            label7.TextAlign = ContentAlignment.TopCenter;
            // 
            // dram3
            // 
            dram3.BackColor = Color.Silver;
            dram3.BorderStyle = BorderStyle.FixedSingle;
            dram3.Location = new Point(339, 46);
            dram3.Name = "dram3";
            dram3.Size = new Size(105, 122);
            dram3.TabIndex = 9;
            // 
            // sel_dram2
            // 
            sel_dram2.Location = new Point(228, 174);
            sel_dram2.Name = "sel_dram2";
            sel_dram2.Size = new Size(105, 23);
            sel_dram2.TabIndex = 8;
            sel_dram2.Text = "Select";
            sel_dram2.UseVisualStyleBackColor = true;
            sel_dram2.Click += sel_dram2_Click;
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(255, 28);
            label6.Name = "label6";
            label6.Size = new Size(50, 15);
            label6.TabIndex = 7;
            label6.Text = "DRAM 2";
            label6.TextAlign = ContentAlignment.TopCenter;
            // 
            // dram2
            // 
            dram2.BackColor = Color.Silver;
            dram2.BorderStyle = BorderStyle.FixedSingle;
            dram2.Location = new Point(228, 46);
            dram2.Name = "dram2";
            dram2.Size = new Size(105, 122);
            dram2.TabIndex = 6;
            // 
            // sel_dram1
            // 
            sel_dram1.Location = new Point(117, 174);
            sel_dram1.Name = "sel_dram1";
            sel_dram1.Size = new Size(105, 23);
            sel_dram1.TabIndex = 5;
            sel_dram1.Text = "Select";
            sel_dram1.UseVisualStyleBackColor = true;
            sel_dram1.Click += sel_dram1_Click;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(144, 28);
            label5.Name = "label5";
            label5.Size = new Size(50, 15);
            label5.TabIndex = 4;
            label5.Text = "DRAM 1";
            label5.TextAlign = ContentAlignment.TopCenter;
            // 
            // dram1
            // 
            dram1.BackColor = Color.Silver;
            dram1.BorderStyle = BorderStyle.FixedSingle;
            dram1.Location = new Point(117, 46);
            dram1.Name = "dram1";
            dram1.Size = new Size(105, 122);
            dram1.TabIndex = 3;
            // 
            // sel_dram0
            // 
            sel_dram0.Location = new Point(6, 174);
            sel_dram0.Name = "sel_dram0";
            sel_dram0.Size = new Size(105, 23);
            sel_dram0.TabIndex = 2;
            sel_dram0.Text = "Select";
            sel_dram0.UseVisualStyleBackColor = true;
            sel_dram0.Click += sel_dram0_Click;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(33, 28);
            label4.Name = "label4";
            label4.Size = new Size(50, 15);
            label4.TabIndex = 1;
            label4.Text = "DRAM 0";
            label4.TextAlign = ContentAlignment.TopCenter;
            // 
            // dram0
            // 
            dram0.BackColor = Color.Silver;
            dram0.BorderStyle = BorderStyle.FixedSingle;
            dram0.Location = new Point(6, 46);
            dram0.Name = "dram0";
            dram0.Size = new Size(105, 122);
            dram0.TabIndex = 0;
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(uptimeBox);
            groupBox2.Controls.Add(label15);
            groupBox2.Controls.Add(modelBox);
            groupBox2.Controls.Add(groupBox14);
            groupBox2.Controls.Add(manufacturerBox);
            groupBox2.Controls.Add(label13);
            groupBox2.Controls.Add(label12);
            groupBox2.Controls.Add(groupBox3);
            groupBox2.Location = new Point(250, 6);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(918, 150);
            groupBox2.TabIndex = 4;
            groupBox2.TabStop = false;
            groupBox2.Text = "Board Properties";
            // 
            // uptimeBox
            // 
            uptimeBox.Enabled = false;
            uptimeBox.Location = new Point(130, 92);
            uptimeBox.Name = "uptimeBox";
            uptimeBox.Size = new Size(238, 23);
            uptimeBox.TabIndex = 7;
            uptimeBox.Text = "===================";
            // 
            // label15
            // 
            label15.AutoSize = true;
            label15.Location = new Point(19, 92);
            label15.Name = "label15";
            label15.Size = new Size(46, 15);
            label15.TabIndex = 6;
            label15.Text = "Uptime";
            // 
            // modelBox
            // 
            modelBox.Enabled = false;
            modelBox.Location = new Point(130, 61);
            modelBox.Name = "modelBox";
            modelBox.Size = new Size(238, 23);
            modelBox.TabIndex = 5;
            modelBox.Text = "===================";
            // 
            // groupBox14
            // 
            groupBox14.Controls.Add(downlinkBox);
            groupBox14.Controls.Add(label18);
            groupBox14.Controls.Add(uplinkBox);
            groupBox14.Controls.Add(ramUsageBox);
            groupBox14.Controls.Add(cpuPercentBox);
            groupBox14.Controls.Add(ramUsageBar);
            groupBox14.Controls.Add(cpuBar);
            groupBox14.Controls.Add(label17);
            groupBox14.Controls.Add(label16);
            groupBox14.Controls.Add(label14);
            groupBox14.Location = new Point(381, 13);
            groupBox14.Name = "groupBox14";
            groupBox14.Size = new Size(262, 129);
            groupBox14.TabIndex = 1;
            groupBox14.TabStop = false;
            groupBox14.Text = "SODIMM Info";
            // 
            // downlinkBox
            // 
            downlinkBox.Enabled = false;
            downlinkBox.Location = new Point(179, 88);
            downlinkBox.Name = "downlinkBox";
            downlinkBox.Size = new Size(77, 23);
            downlinkBox.TabIndex = 12;
            downlinkBox.Text = "====";
            // 
            // label18
            // 
            label18.AutoSize = true;
            label18.Location = new Point(128, 91);
            label18.Name = "label18";
            label18.Size = new Size(47, 15);
            label18.TabIndex = 11;
            label18.Text = "DOWN:";
            // 
            // uplinkBox
            // 
            uplinkBox.Enabled = false;
            uplinkBox.Location = new Point(45, 88);
            uplinkBox.Name = "uplinkBox";
            uplinkBox.Size = new Size(77, 23);
            uplinkBox.TabIndex = 10;
            uplinkBox.Text = "====";
            // 
            // ramUsageBox
            // 
            ramUsageBox.Enabled = false;
            ramUsageBox.Location = new Point(211, 54);
            ramUsageBox.Name = "ramUsageBox";
            ramUsageBox.Size = new Size(45, 23);
            ramUsageBox.TabIndex = 9;
            ramUsageBox.Text = "====";
            // 
            // cpuPercentBox
            // 
            cpuPercentBox.Enabled = false;
            cpuPercentBox.Location = new Point(211, 22);
            cpuPercentBox.Name = "cpuPercentBox";
            cpuPercentBox.Size = new Size(45, 23);
            cpuPercentBox.TabIndex = 8;
            cpuPercentBox.Text = "====";
            // 
            // ramUsageBar
            // 
            ramUsageBar.Location = new Point(45, 54);
            ramUsageBar.Name = "ramUsageBar";
            ramUsageBar.Size = new Size(160, 23);
            ramUsageBar.TabIndex = 4;
            // 
            // cpuBar
            // 
            cpuBar.Location = new Point(45, 22);
            cpuBar.Name = "cpuBar";
            cpuBar.Size = new Size(160, 23);
            cpuBar.TabIndex = 3;
            // 
            // label17
            // 
            label17.AutoSize = true;
            label17.Location = new Point(7, 91);
            label17.Name = "label17";
            label17.Size = new Size(25, 15);
            label17.TabIndex = 2;
            label17.Text = "UP:";
            // 
            // label16
            // 
            label16.AutoSize = true;
            label16.Location = new Point(6, 59);
            label16.Name = "label16";
            label16.Size = new Size(36, 15);
            label16.TabIndex = 1;
            label16.Text = "RAM:";
            // 
            // label14
            // 
            label14.AutoSize = true;
            label14.Location = new Point(6, 28);
            label14.Name = "label14";
            label14.Size = new Size(33, 15);
            label14.TabIndex = 0;
            label14.Text = "CPU:";
            // 
            // manufacturerBox
            // 
            manufacturerBox.Enabled = false;
            manufacturerBox.Location = new Point(130, 29);
            manufacturerBox.Name = "manufacturerBox";
            manufacturerBox.Size = new Size(238, 23);
            manufacturerBox.TabIndex = 4;
            manufacturerBox.Text = "===================";
            // 
            // label13
            // 
            label13.AutoSize = true;
            label13.Location = new Point(19, 61);
            label13.Name = "label13";
            label13.Size = new Size(41, 15);
            label13.TabIndex = 2;
            label13.Text = "Model";
            // 
            // label12
            // 
            label12.AutoSize = true;
            label12.Location = new Point(19, 32);
            label12.Name = "label12";
            label12.Size = new Size(79, 15);
            label12.TabIndex = 1;
            label12.Text = "Manufacturer";
            // 
            // groupBox3
            // 
            groupBox3.Controls.Add(groupBox15);
            groupBox3.Controls.Add(selectedChipBox);
            groupBox3.Controls.Add(label19);
            groupBox3.Controls.Add(chipOrgBox);
            groupBox3.Controls.Add(label3);
            groupBox3.Location = new Point(649, 13);
            groupBox3.Name = "groupBox3";
            groupBox3.Size = new Size(263, 129);
            groupBox3.TabIndex = 0;
            groupBox3.TabStop = false;
            groupBox3.Text = "SODIMM Info";
            // 
            // groupBox15
            // 
            groupBox15.Controls.Add(label20);
            groupBox15.Controls.Add(endAddrBox);
            groupBox15.Controls.Add(startAddrBox);
            groupBox15.Location = new Point(6, 79);
            groupBox15.Name = "groupBox15";
            groupBox15.Size = new Size(251, 44);
            groupBox15.TabIndex = 11;
            groupBox15.TabStop = false;
            groupBox15.Text = "Mapped Address Range";
            // 
            // label20
            // 
            label20.AutoSize = true;
            label20.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            label20.Location = new Point(106, 18);
            label20.Name = "label20";
            label20.Size = new Size(31, 15);
            label20.TabIndex = 12;
            label20.Text = "==>";
            // 
            // endAddrBox
            // 
            endAddrBox.Enabled = false;
            endAddrBox.Location = new Point(140, 15);
            endAddrBox.Name = "endAddrBox";
            endAddrBox.Size = new Size(105, 23);
            endAddrBox.TabIndex = 13;
            endAddrBox.Text = "===================";
            // 
            // startAddrBox
            // 
            startAddrBox.Enabled = false;
            startAddrBox.Location = new Point(6, 15);
            startAddrBox.Name = "startAddrBox";
            startAddrBox.Size = new Size(89, 23);
            startAddrBox.TabIndex = 12;
            startAddrBox.Text = "===================";
            // 
            // selectedChipBox
            // 
            selectedChipBox.Enabled = false;
            selectedChipBox.Location = new Point(118, 54);
            selectedChipBox.Name = "selectedChipBox";
            selectedChipBox.Size = new Size(78, 23);
            selectedChipBox.TabIndex = 10;
            selectedChipBox.Text = "===================";
            // 
            // label19
            // 
            label19.AutoSize = true;
            label19.Location = new Point(6, 54);
            label19.Name = "label19";
            label19.Size = new Size(82, 15);
            label19.TabIndex = 9;
            label19.Text = "Selected Chip:";
            // 
            // chipOrgBox
            // 
            chipOrgBox.Enabled = false;
            chipOrgBox.Location = new Point(118, 19);
            chipOrgBox.Name = "chipOrgBox";
            chipOrgBox.Size = new Size(78, 23);
            chipOrgBox.TabIndex = 8;
            chipOrgBox.Text = "===================";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(6, 24);
            label3.Name = "label3";
            label3.Size = new Size(106, 15);
            label3.TabIndex = 0;
            label3.Text = "Chip Organization:";
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(connect_btn);
            groupBox1.Controls.Add(port);
            groupBox1.Controls.Add(label2);
            groupBox1.Controls.Add(ip_address);
            groupBox1.Controls.Add(label1);
            groupBox1.Location = new Point(8, 6);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(236, 150);
            groupBox1.TabIndex = 0;
            groupBox1.TabStop = false;
            groupBox1.Text = "TCP Client Settings";
            // 
            // connect_btn
            // 
            connect_btn.Location = new Point(6, 104);
            connect_btn.Name = "connect_btn";
            connect_btn.Size = new Size(219, 38);
            connect_btn.TabIndex = 1;
            connect_btn.Text = "Connect";
            connect_btn.UseVisualStyleBackColor = true;
            connect_btn.Click += button1_Click;
            // 
            // port
            // 
            port.Location = new Point(77, 64);
            port.Name = "port";
            port.Size = new Size(148, 23);
            port.TabIndex = 3;
            port.Text = "8080";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(6, 67);
            label2.Name = "label2";
            label2.Size = new Size(32, 15);
            label2.TabIndex = 2;
            label2.Text = "Port:";
            // 
            // ip_address
            // 
            ip_address.Location = new Point(77, 32);
            ip_address.Name = "ip_address";
            ip_address.Size = new Size(148, 23);
            ip_address.TabIndex = 1;
            ip_address.Text = "172.22.189.158";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(6, 35);
            label1.Name = "label1";
            label1.Size = new Size(65, 15);
            label1.TabIndex = 0;
            label1.Text = "IP Address:";
            // 
            // tabPage2
            // 
            tabPage2.Controls.Add(groupBox6);
            tabPage2.Controls.Add(groupBox5);
            tabPage2.Controls.Add(groupBox13);
            tabPage2.Controls.Add(groupBox11);
            tabPage2.Controls.Add(groupBox9);
            tabPage2.Controls.Add(groupBox8);
            tabPage2.Location = new Point(4, 24);
            tabPage2.Name = "tabPage2";
            tabPage2.Padding = new Padding(3);
            tabPage2.Size = new Size(1176, 708);
            tabPage2.TabIndex = 1;
            tabPage2.Text = "Passive Testing Interface";
            tabPage2.UseVisualStyleBackColor = true;
            // 
            // groupBox6
            // 
            groupBox6.Location = new Point(389, 516);
            groupBox6.Name = "groupBox6";
            groupBox6.Size = new Size(779, 186);
            groupBox6.TabIndex = 5;
            groupBox6.TabStop = false;
            groupBox6.Text = "groupBox6";
            // 
            // groupBox5
            // 
            groupBox5.Controls.Add(panel1);
            groupBox5.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            groupBox5.Location = new Point(389, 6);
            groupBox5.Name = "groupBox5";
            groupBox5.Size = new Size(371, 504);
            groupBox5.TabIndex = 4;
            groupBox5.TabStop = false;
            groupBox5.Text = "Chip View";
            // 
            // panel1
            // 
            panel1.BackColor = Color.Black;
            panel1.BorderStyle = BorderStyle.Fixed3D;
            panel1.Location = new Point(15, 22);
            panel1.Name = "panel1";
            panel1.Size = new Size(339, 464);
            panel1.TabIndex = 0;
            // 
            // groupBox13
            // 
            groupBox13.Controls.Add(cancelButton);
            groupBox13.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            groupBox13.Location = new Point(6, 615);
            groupBox13.Name = "groupBox13";
            groupBox13.Size = new Size(377, 87);
            groupBox13.TabIndex = 4;
            groupBox13.TabStop = false;
            groupBox13.Text = "Overrides";
            // 
            // cancelButton
            // 
            cancelButton.Font = new Font("Segoe UI", 9F);
            cancelButton.Location = new Point(195, 22);
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new Size(176, 53);
            cancelButton.TabIndex = 2;
            cancelButton.Text = "Cancel Operation";
            cancelButton.UseVisualStyleBackColor = true;
            // 
            // groupBox11
            // 
            groupBox11.Controls.Add(prngSeed);
            groupBox11.Controls.Add(genSeed);
            groupBox11.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            groupBox11.Location = new Point(6, 6);
            groupBox11.Name = "groupBox11";
            groupBox11.Size = new Size(377, 60);
            groupBox11.TabIndex = 3;
            groupBox11.TabStop = false;
            groupBox11.Text = "PRNG Seed";
            // 
            // prngSeed
            // 
            prngSeed.Location = new Point(12, 22);
            prngSeed.Name = "prngSeed";
            prngSeed.Size = new Size(149, 23);
            prngSeed.TabIndex = 4;
            prngSeed.Text = "00000000";
            // 
            // genSeed
            // 
            genSeed.Font = new Font("Segoe UI", 9F);
            genSeed.Location = new Point(167, 16);
            genSeed.Name = "genSeed";
            genSeed.Size = new Size(204, 38);
            genSeed.TabIndex = 2;
            genSeed.Text = "Regenerate";
            genSeed.UseVisualStyleBackColor = true;
            genSeed.Click += genSeed_Click;
            // 
            // groupBox9
            // 
            groupBox9.Controls.Add(groupBox12);
            groupBox9.Controls.Add(verifyButton);
            groupBox9.Controls.Add(groupBox10);
            groupBox9.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            groupBox9.Location = new Point(6, 164);
            groupBox9.Name = "groupBox9";
            groupBox9.Size = new Size(377, 346);
            groupBox9.TabIndex = 3;
            groupBox9.TabStop = false;
            groupBox9.Text = "Verify Tool";
            // 
            // groupBox12
            // 
            groupBox12.Controls.Add(verificationResults);
            groupBox12.Font = new Font("Segoe UI", 9F);
            groupBox12.Location = new Point(6, 81);
            groupBox12.Name = "groupBox12";
            groupBox12.Size = new Size(365, 247);
            groupBox12.TabIndex = 3;
            groupBox12.TabStop = false;
            groupBox12.Text = "Verification Results";
            // 
            // verificationResults
            // 
            verificationResults.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            verificationResults.Location = new Point(6, 22);
            verificationResults.Name = "verificationResults";
            verificationResults.Size = new Size(353, 219);
            verificationResults.TabIndex = 0;
            verificationResults.Text = "";
            // 
            // verifyButton
            // 
            verifyButton.Font = new Font("Segoe UI", 9F);
            verifyButton.Location = new Point(167, 22);
            verifyButton.Name = "verifyButton";
            verifyButton.Size = new Size(204, 53);
            verifyButton.TabIndex = 2;
            verifyButton.Text = "Verify";
            verifyButton.UseVisualStyleBackColor = true;
            verifyButton.Click += verifyButton_Click;
            // 
            // groupBox10
            // 
            groupBox10.Controls.Add(verifyMode);
            groupBox10.Font = new Font("Segoe UI", 9F);
            groupBox10.Location = new Point(6, 22);
            groupBox10.Name = "groupBox10";
            groupBox10.Size = new Size(155, 53);
            groupBox10.TabIndex = 1;
            groupBox10.TabStop = false;
            groupBox10.Text = "Verify Mode";
            // 
            // verifyMode
            // 
            verifyMode.FormattingEnabled = true;
            verifyMode.Items.AddRange(new object[] { "ZEROS", "ONES", "PRNG" });
            verifyMode.Location = new Point(6, 22);
            verifyMode.Name = "verifyMode";
            verifyMode.Size = new Size(142, 23);
            verifyMode.TabIndex = 0;
            // 
            // groupBox8
            // 
            groupBox8.Controls.Add(writeButton);
            groupBox8.Controls.Add(writeModeLabel);
            groupBox8.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            groupBox8.Location = new Point(6, 72);
            groupBox8.Name = "groupBox8";
            groupBox8.Size = new Size(377, 86);
            groupBox8.TabIndex = 0;
            groupBox8.TabStop = false;
            groupBox8.Text = "Write Tool";
            // 
            // writeButton
            // 
            writeButton.Font = new Font("Segoe UI", 9F);
            writeButton.Location = new Point(167, 22);
            writeButton.Name = "writeButton";
            writeButton.Size = new Size(204, 53);
            writeButton.TabIndex = 2;
            writeButton.Text = "Write";
            writeButton.UseVisualStyleBackColor = true;
            writeButton.Click += writeButton_Click;
            // 
            // writeModeLabel
            // 
            writeModeLabel.Controls.Add(writeMode);
            writeModeLabel.Font = new Font("Segoe UI", 9F);
            writeModeLabel.Location = new Point(6, 22);
            writeModeLabel.Name = "writeModeLabel";
            writeModeLabel.Size = new Size(155, 53);
            writeModeLabel.TabIndex = 1;
            writeModeLabel.TabStop = false;
            writeModeLabel.Text = "Write Mode";
            // 
            // writeMode
            // 
            writeMode.FormattingEnabled = true;
            writeMode.Items.AddRange(new object[] { "ZEROS", "ONES", "PRNG" });
            writeMode.Location = new Point(6, 22);
            writeMode.Name = "writeMode";
            writeMode.Size = new Size(142, 23);
            writeMode.TabIndex = 0;
            writeMode.SelectedIndexChanged += writeMode_SelectedIndexChanged;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1184, 761);
            Controls.Add(ConnectAndConfigure);
            Controls.Add(statusStrip1);
            Name = "Form1";
            Text = "DDR4 Tester";
            ((System.ComponentModel.ISupportInitialize)captureInfoBindingSource).EndInit();
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            ConnectAndConfigure.ResumeLayout(false);
            tabPage1.ResumeLayout(false);
            groupBox4.ResumeLayout(false);
            groupBox4.PerformLayout();
            sideB.ResumeLayout(false);
            sideB.PerformLayout();
            sideA.ResumeLayout(false);
            sideA.PerformLayout();
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            groupBox14.ResumeLayout(false);
            groupBox14.PerformLayout();
            groupBox3.ResumeLayout(false);
            groupBox3.PerformLayout();
            groupBox15.ResumeLayout(false);
            groupBox15.PerformLayout();
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            tabPage2.ResumeLayout(false);
            groupBox5.ResumeLayout(false);
            groupBox13.ResumeLayout(false);
            groupBox11.ResumeLayout(false);
            groupBox11.PerformLayout();
            groupBox9.ResumeLayout(false);
            groupBox12.ResumeLayout(false);
            groupBox10.ResumeLayout(false);
            groupBox8.ResumeLayout(false);
            writeModeLabel.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private BindingSource captureInfoBindingSource;
        private StatusStrip statusStrip1;
        private ToolStripStatusLabel cmdName;
        private ToolStripProgressBar progressBar;
        private ToolStripStatusLabel statusLabel;
        private ToolStripStatusLabel connectionState;
        private TabControl ConnectAndConfigure;
        private TabPage tabPage1;
        private GroupBox groupBox1;
        private Label label1;
        private TabPage tabPage2;
        private Button connect_btn;
        private TextBox port;
        private Label label2;
        private TextBox ip_address;
        private GroupBox groupBox4;
        private GroupBox groupBox2;
        private GroupBox groupBox3;
        private Label label3;
        private CheckBox enableChipSelection;
        private GroupBox sideA;
        private Label label4;
        private Panel dram0;
        private GroupBox groupBox7;
        private Button applyConfiguration;
        private GroupBox sideB;
        private Button sel_dram7;
        private Label label8;
        private Panel dram7;
        private Button sel_dram6;
        private Label label9;
        private Panel dram6;
        private Button sel_dram5;
        private Label label10;
        private Panel dram5;
        private Button sel_dram4;
        private Label label11;
        private Panel dram4;
        private Button sel_dram3;
        private Label label7;
        private Panel dram3;
        private Button sel_dram2;
        private Label label6;
        private Panel dram2;
        private Button sel_dram1;
        private Label label5;
        private Panel dram1;
        private Button sel_dram0;
        private GroupBox groupBox8;
        private ComboBox writeMode;
        private GroupBox writeModeLabel;
        private Button writeButton;
        private GroupBox groupBox11;
        private Button genSeed;
        private GroupBox groupBox9;
        private Button verifyButton;
        private GroupBox groupBox10;
        private ComboBox verifyMode;
        private TextBox prngSeed;
        private GroupBox groupBox12;
        private GroupBox groupBox13;
        private Button cancelButton;
        private Label label13;
        private Label label12;
        private TextBox modelBox;
        private GroupBox groupBox14;
        private Label label14;
        private TextBox manufacturerBox;
        private TextBox uptimeBox;
        private Label label15;
        private TextBox uplinkBox;
        private TextBox ramUsageBox;
        private TextBox cpuPercentBox;
        private ProgressBar ramUsageBar;
        private ProgressBar cpuBar;
        private Label label17;
        private Label label16;
        private TextBox downlinkBox;
        private Label label18;
        private GroupBox groupBox15;
        private TextBox selectedChipBox;
        private Label label19;
        private TextBox chipOrgBox;
        private Label label20;
        private TextBox endAddrBox;
        private TextBox startAddrBox;
        private TextBox chipSizeBox;
        private Label label21;
        private GroupBox groupBox5;
        private Panel panel1;
        private GroupBox groupBox6;
        private RichTextBox verificationResults;
    }
}
