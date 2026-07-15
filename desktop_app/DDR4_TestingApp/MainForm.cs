using ScottPlot;
using ScottPlot.WinForms;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using Color = System.Drawing.Color;

namespace DDR4_TestingApp
{
    public partial class MainForm : Form
    {

        // Data viewer settings
        const uint DATA_VIEWER_ROW_SIZE = 16;
        const uint DATA_VIEWER_RENDER_SIZE = 112;

        float dyn_trigger_threshold = 0.5f;

        // Keep the collection alive for the lifespan of the Form
        private PrivateFontCollection privateFonts = new PrivateFontCollection();
        private Font customFont;


        private readonly System.Windows.Forms.Timer _uiTimer = new();
        private readonly System.Windows.Forms.Timer _statusTimer = new();

        // Parameters
        private bool captureDiff = false;
        private bool enableSEFI = false;
        private bool enableBeamTrigger = false;

        private DateTime startTime = DateTime.Now;
        private DateTime endTime = DateTime.Now;

        //Data arrays for dynamic plot
        private readonly List<float> dataX = new();
        private readonly List<float> dataY = new();

        enum BeamState
        {
            Manual,
            Armed,
            Waiting,
            Triggered,
            SEFI,
            Finished
        };

        private BeamState bms = BeamState.Manual;


        public MainForm()
        {

            _statusTimer.Interval = 20;          // milliseconds — 4x per second
            _statusTimer.Tick += statusUpdate_Tick;
            _statusTimer.Start();

            _uiTimer.Interval = 100;          // milliseconds — 4x per second
            _uiTimer.Tick += UiTimer_Tick;
            _uiTimer.Start();

            InitializeComponent();

            //Setup scaling for dynamic test plot

            var sp = dynPlot.Plot.Add.Scatter(dataX, dataY);
            sp.LineWidth = 2;              // connecting line thickness in pixels
            sp.LineColor = Colors.Blue;    // optional
            //dynPlot.Plot.XLabel("Time [ms]");
            //dynPlot.Plot.YLabel("Error [%]");

            // Just one axis
            dynPlot.Plot.Axes.SetLimitsX(0, 60);
            dynPlot.Plot.Axes.SetLimitsY(0, 1);    // e.g. error rate pinned to 0–100%
            dynPlot.Plot.Axes.Title.Label.IsVisible = false;
            //dynPlot.Plot.Axes.Bottom.Label.IsVisible = false;
            //dynPlot.Plot.Axes.Left.Label.IsVisible = false;
            dynPlot.Dock = DockStyle.Fill;


            dynPlot.Refresh();          // always call Refresh() after changing the plot

        }






        private void UpdateDramPanels(uint ramOrg, uint selectedChip)
        {
            var panels = new[] { dram0, dram1, dram2, dram3, dram4, dram5, dram6, dram7 };

            int enabledCount = ramOrg switch
            {
                8 => 8,    // x8  chips: all 8 active
                16 => 4,    // x16 chips: first 4 active
                _ => 0,    // anything else: none active
            };

            for (int i = 0; i < panels.Length; i++)
            {
                panels[i].BackColor =
                    i >= enabledCount ? Color.Gray :
                    i == selectedChip ? Color.Green :
                                          Color.Red;
            }

            if (enabledCount == 8)
            {
                sideA.Enabled = true;
                sideB.Enabled = true;
            }

            if (enabledCount == 4)
            {
                sideA.Enabled = true;
                sideB.Enabled = false;
            }
        }

        private async void UiTimer_Tick(object? sender, EventArgs e)
        {
            // --- Status indicator ---
            bool connected = TcpManager.Status == TcpManager.ConnectionStatus.Connected;

            //Attempt to update information
            Info.update();

            //Update dynamic buttons
            if (captureDiff)
            {
                captureModeRaw.BackColor = Color.Black;
                captureModeDiff.BackColor = Color.ForestGreen;
            }
            else
            {
                captureModeDiff.BackColor = Color.Black;
                captureModeRaw.BackColor = Color.ForestGreen;
            }

            // Connection button
            if (connected) { connect_btn.Text = "Disconnect"; }
            else { connect_btn.Text = "Connect"; }

            // Update DRAM panels
            if (Config.sys.EnableChipSelect)
            {

                //Update panel
                UpdateDramPanels(Convert.ToUInt32(Config.sys.BusBytesPerChip * 8), Config.sys.ChipIndex);
            }
            else
            {
                //Disable panels
                sideA.Enabled = false;
                sideB.Enabled = false;
                UpdateDramPanels(0, 0);
            }

            // Attempt to Generate table
            uint? addr = Tools.ParseHex(viewerAddress.Text);

            //Check to see if an address was provided
            if (addr.HasValue)
            {

                Stopwatch stopwatch = Stopwatch.StartNew();

                DumpTableFormatter.WriteDumpHexTableAsync(
                  dataViewer,
                  addr.Value,
                  DATA_VIEWER_RENDER_SIZE,
                  1,
                  DATA_VIEWER_ROW_SIZE).GetAwaiter();

                stopwatch.Stop();
                TimeSpan timeSpan = stopwatch.Elapsed;

                // 5. Print the results
                dataViewerStats.Text = $"Fetched {DATA_VIEWER_RENDER_SIZE} bytes in {timeSpan.TotalMilliseconds} ms";


                



            }

            update_addrInfo();

        }


        public void statusUpdate_Tick(object? sender, EventArgs e)
        {
            //Attempt to update task indicator
            taskProgress.Value = (int)Program.taskProgress;
            //taskInfo.Text = Program.taskInfo;
            taskName.Text = Program.taskName;


            if (TcpManager.Status == TcpManager.ConnectionStatus.Connected) { onlineInd.BackColor = Color.Green; }
            else { onlineInd.BackColor = Color.Red; }


            if (Info.sys.HasValue)
            {

                //Update status indicators


                if (Info.sys.Value.BeamSignal) { beamInd.BackColor = Color.Green; }
                else { beamInd.BackColor = Color.Red; }

                if (Info.sys.Value.ControllerCalibrated) { calInd.BackColor = Color.Green; }
                else { calInd.BackColor = Color.Red; }

                if (Info.sys.Value.UiClock) { uiInd.BackColor = Color.Green; }
                else { uiInd.BackColor = Color.Red; }

                if (Info.sys.Value.PlClock) { plInd.BackColor = Color.Green; }
                else { plInd.BackColor = Color.Red; }

                if (Info.sys.Value.FpgaLoaded) { loadedInd.BackColor = Color.Green; }
                else { loadedInd.BackColor = Color.Red; }

            }
        }

        private async void button1_Click(object sender, EventArgs e)
        {

            if (TcpManager.Status == TcpManager.ConnectionStatus.Connected)
            {
                connect_btn.Text = "Disconnecting...";

                TcpManager.Disconnect();
            }
            else
            {
                connect_btn.Text = "Connecting...";

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await TcpManager.ConnectAsync(ip_address.Text, int.Parse(port.Text), cts.Token);
                }
                catch (OperationCanceledException)
                {
                    MessageBox.Show("Could not reach the VCU server within 5 seconds.");
                }
            }
        }

        private async void writeButton_Click(object sender, EventArgs e)
        {
            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                MessageBox.Show("Not connected.");
                return;
            }

            Program.taskName = "WRITE";

            uint taskDelay = 0;


            var cmd = new WriteCmd
            {
                Pattern = (byte)writeMode.SelectedIndex,           // 0 = zeros, 1 = ones, 2 = pseudorandom
                Seed = UInt32.Parse(prngSeed.Text),
            };

            // Progress<T> captures the current SynchronizationContext (the UI thread),
            // so the lambda runs on the UI thread even though ConnectAsync's progress
            // reports come from a background continuation. Safe to touch controls.
            var progress = new Progress<WriteRsp>(rsp =>
            {
                //Update beam status

                Program.taskProgress = rsp.PercentComplete;
                Program.taskInfo = $"{rsp.BytesWritten:N0} bytes  ({rsp.PercentComplete:F1}%)  {(rsp.TimeSpentMs / 1000):F0}s";

            });

            using var cts = new CancellationTokenSource();
            EventHandler cancelHandler = (_, _) => cts.Cancel();

            writeButton.Enabled = false;

            try
            {
                WriteRsp final = await TcpManager.SendWriteAsync(cmd, progress, cts.Token);
                Program.taskInfo = $"Done. {final.BytesWritten:N0} bytes in {(final.TimeSpentMs / 1000):F0} seconds";
            }
            catch (OperationCanceledException)
            {
                Program.taskInfo = "Write cancelled.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Write failed: {ex.Message}");
            }
            finally
            {
                writeButton.Enabled = true;
                bms = BeamState.Finished;
            }
        }

        private void genSeed_Click(object sender, EventArgs e)
        {
            prngSeed.Text = Random.Shared.Next(0, 100_000_000).ToString("D8");
        }

        private void sel_dram0_Click(object sender, EventArgs e)
        {
            Config.sys.ChipIndex = 0;
        }

        private void sel_dram1_Click(object sender, EventArgs e)
        {
            Config.sys.ChipIndex = 1;
        }

        private void sel_dram2_Click(object sender, EventArgs e)
        {
            Config.sys.ChipIndex = 2;
        }

        private void sel_dram3_Click(object sender, EventArgs e)
        {
            Config.sys.ChipIndex = 3;
        }

        private void sel_dram4_Click(object sender, EventArgs e)
        {
            Config.sys.ChipIndex = 4;
        }

        private void sel_dram5_Click(object sender, EventArgs e)
        {
            Config.sys.ChipIndex = 5;
        }

        private void sel_dram6_Click(object sender, EventArgs e)
        {
            Config.sys.ChipIndex = 6;
        }

        private void sel_dram7_Click(object sender, EventArgs e)
        {
            Config.sys.ChipIndex = 7;
        }

        private async void verifyButton_Click(object sender, EventArgs e)
        {
            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                MessageBox.Show("Not connected.");
                return;
            }

            Program.taskName = "VERIFY";


            uint taskDelay = 0;


            var cmd = new VerifyCmd
            {
                Pattern = (byte)verifyMode.SelectedIndex,    // 0 = zeros, 1 = ones, 2 = pseudorandom
                Seed = UInt32.Parse(prngSeed.Text),
            };
            bms = BeamState.Armed;
            bool prevBeam = false;

            var progress = new Progress<VerifyRsp>(rsp =>
            {

                // Compose summary
                ulong total = rsp.NumCorrect + rsp.NumErrors;
                double corruptedPercent = total > 0 ? (rsp.NumErrors * 100.0 / total) : 0.0;
                double seconds = rsp.TimeSpentMs / 1000.0;

                verificationResults.Text =
                    $"Finished verification in {seconds:F2} seconds!\n\n" +
                    $"Correct bits:   {rsp.NumCorrect:N0}\n" +
                    $"Incorrect bits: {rsp.NumErrors:N0}\n\n" +
                    $"{corruptedPercent:F2}% of the bits were corrupted.";


                Program.taskProgress = (int)rsp.PercentComplete;
                Program.taskInfo = $"{rsp.BytesVerified:N0} bytes  ({rsp.PercentComplete:F1}%)  {(rsp.TimeSpentMs / 1000):F0}s";

            });

            using var cts = new CancellationTokenSource();
            EventHandler cancelHandler = (_, _) => cts.Cancel();

            verifyButton.Enabled = false;

            try
            {
                VerifyRsp final = await TcpManager.SendVerifyAsync(cmd, progress, cts.Token);

                // Compose summary
                ulong total = final.NumCorrect + final.NumErrors;
                double corruptedPercent = total > 0 ? (final.NumErrors * 100.0 / total) : 0.0;
                double seconds = final.TimeSpentMs / 1000.0;

                verificationResults.Text =
                    $"Finished verification in {seconds:F2} seconds!\n\n" +
                    $"Correct bits:   {final.NumCorrect:N0}\n" +
                    $"Incorrect bits: {final.NumErrors:N0}\n\n" +
                    $"{corruptedPercent:F2}% of the bits were corrupted.";

                Program.taskInfo = $"Verify complete in {seconds:F1}s";
            }
            catch (OperationCanceledException)
            {
                Program.taskInfo = "Verify cancelled.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Verify failed: {ex.Message}");
            }
            finally
            {

                //Mark beam inactive
                if (prevBeam)
                {
                    //Test ended with active beam
                    endTime = DateTime.Now;
                }
                bms = BeamState.Finished;
                verifyButton.Enabled = true;
            }
        }

        private void update_addrInfo()
        {

            address_information.Text = $"Start Address: \t{Tools.ToHexString(0)}\nEnd Address: \t\t{Tools.ToHexString(Program.selection_size)}\nAddress Scaling: \t{Tools.ToHexString(Config.sys.AddressMultiplier)}\nPercent Sampled: \t{Math.Round(((float)Program.sample_size / Program.selection_size) * 100)}%\n";



        }

        private void selectSaveLocation_Click(object sender, EventArgs e)
        {
            dumpPath.Text = Tools.SelectFolder(dumpPath.Text);
        }

        private async void dumpButton_Click(object sender, EventArgs e)
        {
            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected || !Info.sys.HasValue)
            {
                MessageBox.Show("Not connected.");
                return;
            }

            // Parse inputs — offset accepts "0x..." hex or decimal, num pages is decimal.
            uint offset, numPages;
            try
            {
                offset = 0;
                numPages = Config.sys.ChipSizeBytes / TcpManager.PAGE_SIZE;
            }
            catch (Exception)
            {
                MessageBox.Show("Offset must be hex (0x...) or decimal; pages must be a positive integer.");
                return;
            }

            if (numPages == 0)
            {
                MessageBox.Show("Page count must be at least 1.");
                return;
            }

            Program.taskName = "DUMP";

            var cmd = new DumpCmd { OffsetStart = offset, NumPages = numPages, ComparisonMode = captureDiff };

            int pagesReceived = 0;
            var progress = new Progress<DumpPage>(page =>
            {
                pagesReceived++;
                Program.taskProgress = Math.Clamp((int)(pagesReceived * 100L / numPages), 0, 100);
                Program.taskInfo = $"Page {pagesReceived}/{numPages} @ 0x{page.Address:X8}";

            });

            using var cts = new CancellationTokenSource();
            EventHandler cancelHandler = (_, _) => cts.Cancel();

            dumpButton.Enabled = false;

            try
            {
                var pages = await TcpManager.SendDumpAsync(cmd, progress, cts.Token);

                // Write the raw bytes to disk in the workspace.
                string filename = dumpFileName.Text + ".bin";
                string path = Path.Combine(dumpPath.Text, filename);

                using (var fs = File.Create(path))
                {
                    foreach (var page in pages)
                        fs.Write(page.Data, 0, page.Data.Length);
                }

                // Summary + small hex preview of the first page.
                uint totalErrors = (uint)pages.Sum(p => (long)p.NumErrors);
                long totalBytes = pages.Sum(p => (long)p.Data.Length);

                if (pages.Count > 0)
                {
                    int previewLen = Math.Min(256, pages[0].Data.Length);
                }

                Program.taskInfo = $"Dumped {totalBytes:N0} bytes ({pages.Count} pages)";
            }
            catch (OperationCanceledException)
            {
                Program.taskInfo = "Dump cancelled.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Dump failed: {ex.Message}");
            }
            finally
            {
                dumpButton.Enabled = true;
            }
        }

        private void enableChipSelection_CheckedChanged(object sender, EventArgs e)
        {
            Config.sys.EnableChipSelect = chip_isolation.Checked;
        }

        private void chipSizeBox_TextChanged(object sender, EventArgs e)
        {

        }

        private void applyConfiguration_Click(object sender, EventArgs e)
        {
            Config.apply();
        }


        private void label3_Click(object sender, EventArgs e)
        {

        }

        private void tabPage2_Click(object sender, EventArgs e)
        {

        }

        private void captureModeRaw_Click(object sender, EventArgs e)
        {
            captureDiff = false;
        }

        private void captureModeDiff_Click(object sender, EventArgs e)
        {
            captureDiff = true;
        }

        private void setBeamArm_Click(object sender, EventArgs e)
        {
            enableBeamTrigger = true;
            bms = BeamState.Armed;
        }

        private void setBeamOff_Click(object sender, EventArgs e)
        {
            enableBeamTrigger = false;
            bms = BeamState.Manual;
        }

        private void SetSEFIArm_Click(object sender, EventArgs e)
        {
            enableSEFI = true;
        }

        private void setSEFIOff_Click(object sender, EventArgs e)
        {
            enableSEFI = false;
        }

        private void chip_capacity_SelectedIndexChanged(object sender, EventArgs e)
        {
            uint sb = 0;

            switch (selection_size.SelectedIndex)
            {
                case 0:
                    // 10 MB
                    sb = 1024 * 1024 * 10;
                    break;
                case 1:
                    // 20 MB
                    sb = 1024 * 1024 * 20;
                    break;
                case 2:
                    // 50 MB
                    sb = 1024 * 1024 * 50;
                    break;
                case 3:
                    // 100 MB
                    sb = 1024 * 1024 * 100;
                    break;
                case 4:
                    // 250 MB
                    sb = 1024 * 1024 * 250;
                    break;
                case 5:
                    // 512 MB
                    sb = 1024 * 1024 * 512;
                    break;
                case 6:
                    // 1 GB
                    sb = 1024 * 1024 * 1024;
                    break;
                default:
                    // 2 GB
                    sb = (uint)1024 * 1024 * 1024 * 2;
                    break;
            }

            Program.selection_size = sb;


        }

        private void tabPage1_Click(object sender, EventArgs e)
        {

        }

        private void formsPlot1_Load(object sender, EventArgs e)
        {

        }

        private void chipOrg_SelectedIndexChanged(object sender, EventArgs e)
        {
            uint sb = 0;

            switch (sample_size.SelectedIndex)
            {
                case 0:
                    // 10 MB
                    sb = 1024 * 1024 * 10;
                    break;
                case 1:
                    // 20 MB
                    sb = 1024 * 1024 * 20;
                    break;
                case 2:
                    // 50 MB
                    sb = 1024 * 1024 * 50;
                    break;
                case 3:
                    // 100 MB
                    sb = 1024 * 1024 * 100;
                    break;
                case 4:
                    // 250 MB
                    sb = 1024 * 1024 * 250;
                    break;
                case 5:
                    // 512 MB
                    sb = 1024 * 1024 * 512;
                    break;
                case 6:
                    // 1 GB
                    sb = 1024 * 1024 * 1024;
                    break;
                default:
                    // 2 GB
                    sb = (uint)1024 * 1024 * 1024 * 2;
                    break;
            }

            Program.sample_size = sb;

        }


        private void viewerAddress_TextChanged_1(object sender, EventArgs e)
        {
            //Attempt to parse address
            uint? addr = Tools.ParseHex(viewerAddress.Text);

            //Check to see if an address was provided
            if (addr.HasValue)
            {
                //Update field
                viewerAddress.Text = Tools.ToHexString(addr.Value);

            }
            else
            {
                //Default value
                viewerAddress.Text = Tools.ToHexString(0);
            }


        }

        private void viewerAddress_TextChanged(object sender, EventArgs e)
        {

        }

        private void DataViewerScrollUp_Click(object sender, EventArgs e)
        {
            //Attempt to parse address
            uint? addr = Tools.ParseHex(viewerAddress.Text);

            //Check to see if an address was provided
            if (addr.HasValue)
            {
                //Scroll if possible
                if (Convert.ToInt64(addr.Value) - Convert.ToInt64(DATA_VIEWER_ROW_SIZE) >= 0x0) viewerAddress.Text = Tools.ToHexString(addr.Value - DATA_VIEWER_ROW_SIZE);

            }
        }

        private void DataViewerScrollDown_Click(object sender, EventArgs e)
        {
            //Attempt to parse address
            uint? addr = Tools.ParseHex(viewerAddress.Text);

            //Check to see if an address was provided
            if (addr.HasValue)
            {
                //Scroll if possible
                if (addr.Value + DATA_VIEWER_RENDER_SIZE < 0xFFFFFFFF) viewerAddress.Text = Tools.ToHexString(addr.Value + DATA_VIEWER_ROW_SIZE);

            }
        }

        private void tabPage3_Click(object sender, EventArgs e)
        {

        }

        private void resetFPGA_Click(object sender, EventArgs e)
        {

            ResetCmd r = new ResetCmd { ControllerReset = false, FpgaReset = true };

            Program.taskName = "RESET";
            Program.taskProgress = 0;

            TcpManager.SendResetAsync(r).GetAwaiter();

            Program.taskName = "RESET";
            Program.taskProgress = 100;
        }

        private void resetController_Click(object sender, EventArgs e)
        {
            ResetCmd r = new ResetCmd { ControllerReset = true, FpgaReset = false };

            Program.taskName = "RESET";
            Program.taskProgress = 0;

            TcpManager.SendResetAsync(r).GetAwaiter();

            Program.taskName = "RESET";
            Program.taskProgress = 100;
        }

        private void chip_org_SelectedIndexChanged(object sender, EventArgs e)
        {
            switch (chip_org.SelectedIndex)
            {
                case 0:
                    Config.sys.BusBytesPerChip = 1;
                    break;
                case 1:
                    Config.sys.BusBytesPerChip = 2;
                    break;
            }
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private async void dyn_execute_Click(object sender, EventArgs e)
        {
            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                MessageBox.Show("Not connected.");
                return;
            }

            Program.taskName = "DYNAMIC";
            Program.taskProgress = 50;

            //Clear all previous data
            dataX.Clear();
            dataY.Clear();


            var cmd = new DynamicCmd
            {
                // Pattern generation
                Pattern = (byte)dyn_pattern.SelectedIndex,
                Seed = UInt32.Parse(prngSeed.Text),

                SampleSizeInBytes = UInt32.Parse(dyn_bps.Text),
                WaitForBeam = (bool)(dyn_beam.Checked),

                // SEFI threshold
                TriggerThreshold = dyn_trigger_threshold
            };


            var progress = new Progress<DynamicRsp>(rsp =>
            {

                // Update status pane
                if (!rsp.SefiDetected)
                {
                    if (!rsp.ExposureStarted) dynStage.Text = "WAITING";
                    else dynStage.Text = "RUNNING";
                }
                else
                {
                    dynStage.Text = "TRIGGERED";
                }

                //Add data to collection
                dataX.Add(rsp.ExposureTimeMs);

                if (!double.IsNaN(rsp.ErrorRatePercent) && !double.IsInfinity(rsp.ErrorRatePercent))
                    dataY.Add(rsp.ErrorRatePercent);

                double cutoff = rsp.ExposureTimeMs - 10000;
                while (dataX.Count > 0 && dataX[0] < cutoff)
                {
                    dataX.RemoveAt(0);
                    dataY.RemoveAt(0);
                }

                dynPlot.Plot.Axes.AutoScaleX();   // now fits only the retained window
                dynPlot.Refresh();
                //Calculate bit error rate per thousand
                float err_per_thousand = ((float)rsp.ErrorRate / (cmd.SampleSizeInBytes * 8)) * 1000.0f;

                dynSEFI.Text = $"{Math.Round(err_per_thousand)} per thousand";

                // Update status indicators
                {
                    if (rsp.BeamSignal) { beamInd.BackColor = Color.Green; }
                    else { beamInd.BackColor = Color.Red; }

                    if (rsp.ControllerCalibrated) { calInd.BackColor = Color.Green; }
                    else { calInd.BackColor = Color.Red; }

                    if (rsp.UiClock) { uiInd.BackColor = Color.Green; }
                    else { uiInd.BackColor = Color.Red; }

                    if (rsp.PlClock) { plInd.BackColor = Color.Green; }
                    else { plInd.BackColor = Color.Red; }

                    if (rsp.FpgaLoaded) { loadedInd.BackColor = Color.Green; }
                    else { loadedInd.BackColor = Color.Red; }
                }

                //Update status boxes
                dynTotalTime.Text = rsp.TotalTimeMs.ToString() + " ms";
                dynExposureTime.Text = rsp.ExposureTimeMs.ToString() + " ms";
                dynBytes.Text = Tools.FormatBytes(rsp.TotalBytes);

                //Check if SEFI occured
                if (!rsp.SefiDetected)
                {
                    // No SEFI
                    dynSEFI.Text = "NO FAULTS";
                    dynUntilSEFI.Text = "==========";

                }
                else
                {
                    dynSEFI.Text = "SEFI DETECTED";
                    dynUntilSEFI.Text = rsp.TimeToSefi.ToString() + " ms";
                }

                //Update rate and error stats
                dynBitErrors.Text = rsp.ErrorRate.ToString() + " bit(s)";
                dynRateTime.Text = rsp.ErrorRatePerSecond.ToString() + " errors/sec";
                dynRatePercent.Text = rsp.ErrorRatePercent.ToString();

                //Render Memory Viewer
                {
                    uint rows = 21;
                    String txt = "";

                    uint start_addr = 0;
                    uint end_addr = Config.sys.ChipSizeBytes;
                    uint step = (uint)(end_addr - start_addr) / rows;

                    //Render rows
                    for(int i = 0; i < rows; i++)
                    {

                        //Check if cursor should be rendered

                        txt += "  --->  |        | 0x00000000 ";



                    }


                    



                }

            });

            using var cts = new CancellationTokenSource();
            EventHandler cancelHandler = (_, _) => cts.Cancel();

            verifyButton.Enabled = false;

            try
            {
                await TcpManager.RunDynamicAsync(cmd, progress, cts.Token);
            }
            catch (OperationCanceledException)
            {
                Program.taskInfo = "Verify cancelled.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Verify failed: {ex.Message}");
            }
            finally
            {

                //Task complete
                Program.taskProgress = 100;

                //Fill in info
                dynStage.Text = "DONE";
                   
            }
        }

        private void textBox8_TextChanged(object sender, EventArgs e)
        {

        }

        private void dyn_trigger_bar_Scroll(object sender, EventArgs e)
        {
            //Update from bar
            dyn_trigger_threshold = ((float)dyn_trigger_bar.Value) / 100;

            //Propagate to textbox
            dyn_trigger_box.Text = dyn_trigger_threshold.ToString();
        }

        private void dyn_trigger_box_TextChanged(object sender, EventArgs e)
        {
            // Update from textbox; any non-float zeroes both
            if (!float.TryParse(dyn_trigger_box.Text, out dyn_trigger_threshold))
            {
                dyn_trigger_threshold = 0;
                dyn_trigger_bar.Value = 0;
                return;
            }

            // Clamp overflows
            if (dyn_trigger_threshold > 1)
            {
                dyn_trigger_threshold = 1;
                dyn_trigger_box.Text = "1";
            }
            else if (dyn_trigger_threshold < 0)
            {
                dyn_trigger_threshold = 0;
                dyn_trigger_box.Text = "0";
            }

            // Propagate to bar (clamped to valid range as a guard)
            dyn_trigger_bar.Value = Math.Clamp((int)(dyn_trigger_threshold * 100), 0, 100);
        }
    }
}
