using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PhoneAudioRecorder
{
    public class MainForm : Form
    {
        private readonly string appDir;
        private readonly string toolsDir;
        private readonly string scrcpyPath;
        private readonly string adbPath;
        private readonly string ffmpegPath;
        private readonly string configPath;

        private TextBox saveFolderBox;
        private ComboBox formatBox;
        private ComboBox sourceBox;
        private Label statusLabel;
        private Label fileLabel;
        private Label hintLabel;
        private TextBox logBox;
        private Button startButton;
        private Button stopButton;
        private Button finishButton;
        private Button checkButton;
        private Button folderButton;
        private CheckBox autoStopBox;
        private NumericUpDown silenceSecondsBox;

        private Process recorderProcess;
        private string currentOutputFile;
        private volatile bool monitorRequested;
        private volatile bool monitorStopRequested;
        private Thread audioMonitorThread;
        private DateTime lastSoundAt;
        private int silenceSeconds = 10;
        private bool heardSound;
        private bool stoppingForSilence;
        private bool pendingFinish;
        private bool pendingPause;
        private bool sessionActive;
        private bool isFinishing;
        private string sessionId;
        private string finalOutputFile;
        private readonly List<string> segmentFiles = new List<string>();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

        [DllImport("kernel32.dll")]
        private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handlerRoutine, bool add);

        private delegate bool ConsoleCtrlDelegate(uint ctrlType);

        public MainForm()
        {
            appDir = Environment.GetEnvironmentVariable("PHONE_AUDIO_RECORDER_HOME");
            if (string.IsNullOrWhiteSpace(appDir))
            {
                appDir = AppDomain.CurrentDomain.BaseDirectory;
            }
            toolsDir = Path.Combine(appDir, "tools");
            scrcpyPath = Path.Combine(toolsDir, "scrcpy.exe");
            adbPath = Path.Combine(toolsDir, "adb.exe");
            ffmpegPath = Path.Combine(toolsDir, "ffmpeg.exe");
            configPath = Path.Combine(appDir, "settings.ini");

            InitializeUi();
            LoadDefaults();
            CheckTools();
        }

        private void InitializeUi()
        {
            Text = "Phone Audio Recorder";
            Width = 820;
            Height = 720;
            MinimumSize = new Size(780, 640);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10F);
            BackColor = Color.FromArgb(245, 246, 248);

            var title = new Label
            {
                Text = "Phone Audio Recorder",
                Font = new Font("Segoe UI Semibold", 24F),
                AutoSize = true,
                Location = new Point(32, 24)
            };

            var subtitle = new Label
            {
                Text = "USB로 연결한 갤럭시 소리를 오디오 파일로 바로 녹음합니다.",
                AutoSize = true,
                ForeColor = Color.FromArgb(80, 86, 96),
                Location = new Point(36, 78)
            };

            var saveLabel = new Label { Text = "저장 폴더", AutoSize = true, Location = new Point(36, 128) };
            saveFolderBox = new TextBox { Location = new Point(36, 154), Width = 590, Height = 32 };
            folderButton = new Button { Text = "선택", Location = new Point(644, 152), Width = 116, Height = 36 };
            folderButton.Click += ChooseFolder;

            var formatLabel = new Label { Text = "저장 형식", AutoSize = true, Location = new Point(36, 208) };
            formatBox = new ComboBox { Location = new Point(36, 234), Width = 270, DropDownStyle = ComboBoxStyle.DropDownList };
            formatBox.Items.AddRange(new object[]
            {
                "M4A / AAC 192K - 추천",
                "M4A / AAC 320K",
                "FLAC - 고음질",
                "WAV - 편집용"
            });

            var sourceLabel = new Label { Text = "오디오 캡처 방식", AutoSize = true, Location = new Point(336, 208) };
            sourceBox = new ComboBox { Location = new Point(336, 234), Width = 424, DropDownStyle = ComboBoxStyle.DropDownList };
            sourceBox.Items.AddRange(new object[]
            {
                "output - 기본값, 전체 폰 소리",
                "playback - Android 13+ 대안"
            });

            checkButton = new Button { Text = "폰 연결 확인", Location = new Point(36, 304), Width = 140, Height = 46 };
            checkButton.Click += (s, e) => CheckDevice();

            startButton = new Button { Text = "녹음 시작", Location = new Point(190, 304), Width = 140, Height = 46 };
            startButton.BackColor = Color.FromArgb(30, 126, 82);
            startButton.ForeColor = Color.White;
            startButton.FlatStyle = FlatStyle.Flat;
            startButton.Click += (s, e) => StartRecording();

            stopButton = new Button { Text = "일시정지", Location = new Point(344, 304), Width = 128, Height = 46 };
            stopButton.BackColor = Color.FromArgb(176, 117, 36);
            stopButton.ForeColor = Color.White;
            stopButton.FlatStyle = FlatStyle.Flat;
            stopButton.Enabled = false;
            stopButton.Click += (s, e) => PauseRecording();

            finishButton = new Button { Text = "최종 저장", Location = new Point(486, 304), Width = 128, Height = 46 };
            finishButton.BackColor = Color.FromArgb(53, 92, 162);
            finishButton.ForeColor = Color.White;
            finishButton.FlatStyle = FlatStyle.Flat;
            finishButton.Enabled = false;
            finishButton.Click += (s, e) => FinishRecording();

            var openButton = new Button { Text = "저장 폴더 열기", Location = new Point(628, 304), Width = 132, Height = 46 };
            openButton.Click += (s, e) => OpenSaveFolder();

            autoStopBox = new CheckBox
            {
                Text = "첫 소리 이후 무음이면 일시정지",
                AutoSize = true,
                Location = new Point(36, 372),
                Checked = true
            };

            var silenceLabel = new Label { Text = "무음 시간(초)", AutoSize = true, Location = new Point(312, 372) };
            silenceSecondsBox = new NumericUpDown
            {
                Location = new Point(410, 368),
                Width = 80,
                Minimum = 3,
                Maximum = 300,
                Value = 10
            };

            hintLabel = new Label
            {
                Text = "폰 볼륨은 녹음 크기, PC 볼륨은 듣기 크기입니다. 무음 일시정지 후에는 이어 녹음 또는 최종 저장을 선택하세요.",
                AutoSize = false,
                Location = new Point(36, 404),
                Width = 724,
                Height = 28,
                ForeColor = Color.FromArgb(87, 94, 105)
            };

            statusLabel = new Label
            {
                Text = "대기 중",
                AutoSize = false,
                Location = new Point(36, 446),
                Width = 724,
                Height = 32,
                ForeColor = Color.FromArgb(44, 50, 58)
            };

            fileLabel = new Label
            {
                Text = "저장 파일: 아직 없음",
                AutoSize = false,
                Location = new Point(36, 478),
                Width = 724,
                Height = 30,
                ForeColor = Color.FromArgb(80, 86, 96)
            };

            logBox = new TextBox
            {
                Location = new Point(36, 520),
                Width = 724,
                Height = 120,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.White
            };

            Controls.AddRange(new Control[]
            {
                title, subtitle, saveLabel, saveFolderBox, folderButton,
                formatLabel, formatBox, sourceLabel, sourceBox,
                checkButton, startButton, stopButton, finishButton, openButton,
                autoStopBox, silenceLabel, silenceSecondsBox,
                hintLabel, statusLabel, fileLabel, logBox
            });

            FormClosing += (s, e) =>
            {
                if (recorderProcess != null && !recorderProcess.HasExited)
                {
                    StopRecording();
                }
            };
        }

        private void LoadDefaults()
        {
            var defaultFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                "Phone Audio Recorder");
            Directory.CreateDirectory(defaultFolder);
            saveFolderBox.Text = defaultFolder;
            formatBox.SelectedIndex = 0;
            sourceBox.SelectedIndex = 0;
            autoStopBox.Checked = true;
            silenceSecondsBox.Value = 10;

            try
            {
                if (!File.Exists(configPath))
                {
                    return;
                }

                foreach (var line in File.ReadAllLines(configPath, Encoding.UTF8))
                {
                    var index = line.IndexOf('=');
                    if (index <= 0)
                    {
                        continue;
                    }

                    var key = line.Substring(0, index);
                    var value = line.Substring(index + 1);

                    if (key == "saveFolder" && Directory.Exists(value))
                    {
                        saveFolderBox.Text = value;
                    }
                    else if (key == "formatIndex")
                    {
                        SetComboIndex(formatBox, value);
                    }
                    else if (key == "sourceIndex")
                    {
                        SetComboIndex(sourceBox, value);
                    }
                    else if (key == "autoStop")
                    {
                        autoStopBox.Checked = value == "true";
                    }
                    else if (key == "silenceSeconds")
                    {
                        decimal seconds;
                        if (decimal.TryParse(value, out seconds))
                        {
                            if (seconds < silenceSecondsBox.Minimum) seconds = silenceSecondsBox.Minimum;
                            if (seconds > silenceSecondsBox.Maximum) seconds = silenceSecondsBox.Maximum;
                            silenceSecondsBox.Value = seconds;
                        }
                    }
                }
            }
            catch
            {
            }

            formatBox.SelectedIndexChanged += (s, e) => SaveSettings();
            sourceBox.SelectedIndexChanged += (s, e) => SaveSettings();
            autoStopBox.CheckedChanged += (s, e) => SaveSettings();
            silenceSecondsBox.ValueChanged += (s, e) => SaveSettings();
        }

        private void SetComboIndex(ComboBox combo, string value)
        {
            int index;
            if (int.TryParse(value, out index) && index >= 0 && index < combo.Items.Count)
            {
                combo.SelectedIndex = index;
            }
        }

        private void SaveSettings()
        {
            try
            {
                var lines = new[]
                {
                    "saveFolder=" + saveFolderBox.Text.Trim(),
                    "formatIndex=" + formatBox.SelectedIndex,
                    "sourceIndex=" + sourceBox.SelectedIndex,
                    "autoStop=" + (autoStopBox.Checked ? "true" : "false"),
                    "silenceSeconds=" + (int)silenceSecondsBox.Value
                };
                File.WriteAllLines(configPath, lines, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AppendLog("설정 저장 실패: " + ex.Message);
            }
        }

        private void CheckTools()
        {
            if (!File.Exists(scrcpyPath) || !File.Exists(adbPath))
            {
                SetStatus("tools 폴더에 scrcpy.exe 또는 adb.exe가 없습니다.");
                startButton.Enabled = false;
                checkButton.Enabled = false;
                AppendLog("앱 폴더의 tools 폴더를 확인하세요.");
                return;
            }

            AppendLog("scrcpy 준비 완료: " + scrcpyPath);
            if (File.Exists(ffmpegPath))
            {
                AppendLog("FFmpeg 준비 완료: " + ffmpegPath);
            }
            else
            {
                AppendLog("FFmpeg 없음: 여러 조각을 하나로 합치는 기능이 제한됩니다.");
            }
        }

        private void ChooseFolder(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "녹음 파일을 저장할 폴더를 선택하세요.";
                dialog.SelectedPath = Directory.Exists(saveFolderBox.Text)
                    ? saveFolderBox.Text
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    saveFolderBox.Text = dialog.SelectedPath;
                    SaveSettings();
                }
            }
        }

        private void CheckDevice()
        {
            try
            {
                var output = RunAndRead(adbPath, "devices -l", 6000);
                AppendLog(output.Trim());

                var deviceLines = output
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Skip(1)
                    .Where(line => line.Contains("\tdevice") || line.Contains(" device "))
                    .ToList();

                if (deviceLines.Count > 0)
                {
                    SetStatus("폰 연결 확인됨. 녹음을 시작할 수 있습니다.");
                }
                else if (output.Contains("unauthorized"))
                {
                    SetStatus("폰에서 USB 디버깅 허용을 눌러야 합니다.");
                }
                else
                {
                    SetStatus("연결된 폰을 찾지 못했습니다. USB 디버깅과 케이블을 확인하세요.");
                }
            }
            catch (Exception ex)
            {
                SetStatus("폰 연결 확인 실패");
                AppendLog(ex.Message);
            }
        }

        private void StartRecording()
        {
            if (recorderProcess != null && !recorderProcess.HasExited)
            {
                SetStatus("이미 녹음 중입니다.");
                return;
            }

            if (!File.Exists(scrcpyPath))
            {
                SetStatus("scrcpy.exe를 찾지 못했습니다.");
                return;
            }

            var folder = saveFolderBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(folder))
            {
                SetStatus("저장 폴더를 먼저 선택하세요.");
                return;
            }

            Directory.CreateDirectory(folder);
            SaveSettings();

            if (!sessionActive)
            {
                sessionActive = true;
                isFinishing = false;
                segmentFiles.Clear();
                sessionId = "phone_audio_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                finalOutputFile = Path.Combine(folder, sessionId + GetExtension());
                startButton.Text = "이어 녹음";
            }

            var partFolder = Path.Combine(folder, "_phone_audio_parts");
            Directory.CreateDirectory(partFolder);
            currentOutputFile = Path.Combine(partFolder, sessionId + "_part" + (segmentFiles.Count + 1).ToString("000") + GetExtension());

            var args = BuildScrcpyArgs(currentOutputFile);
            AppendLog("실행: scrcpy " + args);

            var psi = new ProcessStartInfo
            {
                FileName = scrcpyPath,
                Arguments = args,
                WorkingDirectory = toolsDir,
                UseShellExecute = false,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            recorderProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
            recorderProcess.OutputDataReceived += (s, e) => { if (e.Data != null) BeginInvoke(new Action(() => AppendLog(e.Data))); };
            recorderProcess.ErrorDataReceived += (s, e) => { if (e.Data != null) BeginInvoke(new Action(() => AppendLog(e.Data))); };
            recorderProcess.Exited += (s, e) => BeginInvoke(new Action(OnRecorderExited));

            try
            {
                recorderProcess.Start();
                recorderProcess.BeginOutputReadLine();
                recorderProcess.BeginErrorReadLine();

                startButton.Enabled = false;
                stopButton.Enabled = true;
                finishButton.Enabled = true;
                checkButton.Enabled = false;
                autoStopBox.Enabled = false;
                silenceSecondsBox.Enabled = false;
                SetStatus("녹음 중입니다. 폰에서 Gemini 음성을 재생하세요.");
                fileLabel.Text = "최종 파일: " + finalOutputFile;
                StartAudioMonitorIfNeeded();
            }
            catch (Exception ex)
            {
                SetStatus("녹음 시작 실패");
                AppendLog(ex.Message);
            }
        }

        private string BuildScrcpyArgs(string outputFile)
        {
            string codec;
            string bitRate = "";
            string format = "";

            switch (formatBox.SelectedIndex)
            {
                case 1:
                    codec = "aac";
                    bitRate = " --audio-bit-rate=320K";
                    format = " --record-format=m4a";
                    break;
                case 2:
                    codec = "flac";
                    format = " --record-format=flac";
                    break;
                case 3:
                    codec = "raw";
                    format = " --record-format=wav";
                    break;
                default:
                    codec = "aac";
                    bitRate = " --audio-bit-rate=192K";
                    format = " --record-format=m4a";
                    break;
            }

            var source = sourceBox.SelectedIndex == 1 ? "playback" : "output";
            var duplicate = source == "playback" ? " --audio-dup" : "";

            return "--no-video --require-audio --audio-source=" + source +
                   duplicate +
                   " --audio-codec=" + codec +
                   bitRate +
                   format +
                   " --record=\"" + outputFile + "\"";
        }

        private string GetExtension()
        {
            switch (formatBox.SelectedIndex)
            {
                case 2: return ".flac";
                case 3: return ".wav";
                default: return ".m4a";
            }
        }

        private void PauseRecording()
        {
            StopCurrentSegment(false, false);
        }

        private void FinishRecording()
        {
            if (recorderProcess != null && !recorderProcess.HasExited)
            {
                StopCurrentSegment(true, false);
                return;
            }

            FinalizeSession();
        }

        private void StopRecording()
        {
            StopCurrentSegment(true, false);
        }

        private void StopCurrentSegment(bool finishAfterStop, bool pauseForSilence)
        {
            if (recorderProcess == null || recorderProcess.HasExited)
            {
                if (finishAfterStop)
                {
                    FinalizeSession();
                }
                return;
            }

            SetStatus("녹음을 정리하는 중입니다...");
            stopButton.Enabled = false;
            pendingFinish = finishAfterStop;
            pendingPause = !finishAfterStop;
            stoppingForSilence = pauseForSilence;
            StopAudioMonitor();

            try
            {
                SendCtrlC(recorderProcess);
                if (!recorderProcess.WaitForExit(7000))
                {
                    recorderProcess.Kill();
                    recorderProcess.WaitForExit(3000);
                }
            }
            catch (Exception ex)
            {
                AppendLog("중지 처리 오류: " + ex.Message);
                try
                {
                    if (!recorderProcess.HasExited)
                    {
                        recorderProcess.Kill();
                    }
                }
                catch { }
            }
        }

        private void OnRecorderExited()
        {
            startButton.Enabled = true;
            stopButton.Enabled = false;
            checkButton.Enabled = true;
            autoStopBox.Enabled = true;
            silenceSecondsBox.Enabled = true;
            StopAudioMonitor();

            if (!string.IsNullOrEmpty(currentOutputFile) && File.Exists(currentOutputFile))
            {
                if (!segmentFiles.Contains(currentOutputFile))
                {
                    segmentFiles.Add(currentOutputFile);
                }

                if (pendingFinish || isFinishing)
                {
                    pendingFinish = false;
                    pendingPause = false;
                    FinalizeSession();
                    return;
                }

                SetStatus(stoppingForSilence ? ((int)silenceSecondsBox.Value).ToString() + "초 무음 감지로 일시정지됨. 이어 녹음 또는 최종 저장을 선택하세요." : "일시정지됨. 이어 녹음 또는 최종 저장을 선택하세요.");
                stoppingForSilence = false;
                pendingPause = false;
                finishButton.Enabled = true;
                startButton.Text = "이어 녹음";
                fileLabel.Text = "최종 파일: " + finalOutputFile;
            }
            else
            {
                SetStatus("녹음이 종료됐지만 저장 파일을 찾지 못했습니다.");
            }
        }

        private void SendCtrlC(Process process)
        {
            FreeConsole();
            if (AttachConsole((uint)process.Id))
            {
                SetConsoleCtrlHandler(null, true);
                GenerateConsoleCtrlEvent(0, 0);
                System.Threading.Thread.Sleep(1000);
                FreeConsole();
                SetConsoleCtrlHandler(null, false);
            }
        }

        private void StartAudioMonitorIfNeeded()
        {
            monitorStopRequested = false;
            monitorRequested = autoStopBox.Checked;
            silenceSeconds = (int)silenceSecondsBox.Value;
            heardSound = false;
            lastSoundAt = DateTime.Now;

            if (!monitorRequested)
            {
                return;
            }

            audioMonitorThread = new Thread(AudioMonitorWorker);
            audioMonitorThread.IsBackground = true;
            audioMonitorThread.Start();
            AppendLog("무음 감지 시작: 첫 소리 이후 " + silenceSeconds.ToString() + "초 무음이면 일시정지");
        }

        private void StopAudioMonitor()
        {
            monitorStopRequested = true;
            monitorRequested = false;
        }

        private void AudioMonitorWorker()
        {
            try
            {
                using (var monitor = new WasapiLoopbackMonitor())
                {
                    monitor.Start();

                    while (!monitorStopRequested)
                    {
                        var level = monitor.ReadLevel(300);
                        if (level > 0.004)
                        {
                            if (!heardSound)
                            {
                                BeginInvoke(new Action(() => AppendLog("첫 소리 감지됨. 이제부터 " + silenceSeconds.ToString() + "초 무음을 감시합니다.")));
                            }
                            heardSound = true;
                            lastSoundAt = DateTime.Now;
                        }
                        else if (heardSound && (DateTime.Now - lastSoundAt).TotalSeconds >= silenceSeconds)
                        {
                            BeginInvoke(new Action(() =>
                            {
                                if (recorderProcess != null && !recorderProcess.HasExited)
                                {
                                    stoppingForSilence = true;
                                    AppendLog(silenceSeconds.ToString() + "초 무음 감지. 녹음을 일시정지합니다.");
                                    StopCurrentSegment(false, true);
                                }
                            }));
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                BeginInvoke(new Action(() => AppendLog("무음 감지 비활성화: " + ex.Message)));
            }
        }

        private void OpenSaveFolder()
        {
            var folder = saveFolderBox.Text.Trim();
            if (Directory.Exists(folder))
            {
                Process.Start("explorer.exe", folder);
            }
            else
            {
                SetStatus("저장 폴더가 없습니다.");
            }
        }

        private void FinalizeSession()
        {
            if (!sessionActive && segmentFiles.Count == 0)
            {
                SetStatus("저장할 녹음 세션이 없습니다.");
                return;
            }

            isFinishing = true;
            SaveSettings();
            startButton.Enabled = false;
            stopButton.Enabled = false;
            finishButton.Enabled = false;
            checkButton.Enabled = false;
            autoStopBox.Enabled = false;
            silenceSecondsBox.Enabled = false;
            SetStatus("최종 파일을 저장하는 중입니다...");

            try
            {
                var validSegments = segmentFiles.Where(File.Exists).ToList();
                if (validSegments.Count == 0)
                {
                    SetStatus("저장할 녹음 조각을 찾지 못했습니다.");
                    ResetSessionUi();
                    return;
                }

                if (File.Exists(finalOutputFile))
                {
                    File.Delete(finalOutputFile);
                }

                if (validSegments.Count == 1)
                {
                    File.Copy(validSegments[0], finalOutputFile, true);
                }
                else
                {
                    MergeSegments(validSegments, finalOutputFile);
                }

                SetStatus("최종 저장 완료");
                fileLabel.Text = "저장 파일: " + finalOutputFile;
                AppendLog("최종 저장 완료: " + finalOutputFile);
                CleanupSegments(validSegments);
                ResetSessionUi();
            }
            catch (Exception ex)
            {
                SetStatus("최종 저장 실패");
                AppendLog(ex.Message);
                startButton.Enabled = true;
                finishButton.Enabled = true;
                checkButton.Enabled = true;
                autoStopBox.Enabled = true;
                silenceSecondsBox.Enabled = true;
            }
            finally
            {
                isFinishing = false;
            }
        }

        private void MergeSegments(List<string> segments, string outputFile)
        {
            var ffmpeg = File.Exists(ffmpegPath) ? ffmpegPath : "ffmpeg.exe";
            var listFile = Path.Combine(Path.GetTempPath(), "phone_audio_concat_" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllLines(listFile, segments.Select(path => "file '" + EscapeConcatPath(path) + "'").ToArray(), Encoding.ASCII);
                var args = "-y -f concat -safe 0 -i \"" + listFile + "\" -c copy \"" + outputFile + "\"";
                var result = RunProcess(ffmpeg, args, 120000);
                if (result.ExitCode == 0 && File.Exists(outputFile))
                {
                    AppendLog("조각 합치기 완료");
                    return;
                }

                AppendLog("무손실 합치기 실패. 재인코딩으로 다시 시도합니다.");
                AppendLog(result.Output);
                args = "-y -f concat -safe 0 -i \"" + listFile + "\" " + BuildFfmpegEncodeArgs() + " \"" + outputFile + "\"";
                result = RunProcess(ffmpeg, args, 180000);
                if (result.ExitCode != 0 || !File.Exists(outputFile))
                {
                    throw new Exception("FFmpeg 합치기 실패: " + result.Output);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(listFile))
                    {
                        File.Delete(listFile);
                    }
                }
                catch { }
            }
        }

        private string BuildFfmpegEncodeArgs()
        {
            switch (formatBox.SelectedIndex)
            {
                case 2:
                    return "-vn -c:a flac";
                case 3:
                    return "-vn -c:a pcm_s16le -ar 48000";
                case 1:
                    return "-vn -c:a aac -b:a 320k";
                default:
                    return "-vn -c:a aac -b:a 192k";
            }
        }

        private string EscapeConcatPath(string path)
        {
            return path.Replace("\\", "/").Replace("'", "'\\''");
        }

        private void CleanupSegments(List<string> segments)
        {
            foreach (var segment in segments)
            {
                try
                {
                    if (File.Exists(segment))
                    {
                        File.Delete(segment);
                    }
                }
                catch { }
            }
        }

        private void ResetSessionUi()
        {
            sessionActive = false;
            sessionId = null;
            currentOutputFile = null;
            finalOutputFile = null;
            segmentFiles.Clear();
            pendingFinish = false;
            pendingPause = false;
            stoppingForSilence = false;
            startButton.Text = "녹음 시작";
            startButton.Enabled = true;
            stopButton.Enabled = false;
            finishButton.Enabled = false;
            checkButton.Enabled = true;
            autoStopBox.Enabled = true;
            silenceSecondsBox.Enabled = true;
        }

        private string RunAndRead(string fileName, string arguments, int timeoutMs)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = toolsDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using (var process = Process.Start(psi))
            {
                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(timeoutMs))
                {
                    process.Kill();
                }
                return stdout + Environment.NewLine + stderr;
            }
        }

        private ProcessResult RunProcess(string fileName, string arguments, int timeoutMs)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = toolsDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using (var process = Process.Start(psi))
            {
                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(timeoutMs))
                {
                    process.Kill();
                    return new ProcessResult { ExitCode = -1, Output = stdout + Environment.NewLine + stderr + Environment.NewLine + "timeout" };
                }

                return new ProcessResult { ExitCode = process.ExitCode, Output = stdout + Environment.NewLine + stderr };
            }
        }

        private void SetStatus(string message)
        {
            statusLabel.Text = "상태: " + message;
            AppendLog("상태: " + message);
        }

        private void AppendLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);
        }

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    internal class ProcessResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; }
    }

    internal sealed class WasapiLoopbackMonitor : IDisposable
    {
        private const int AUDCLNT_SHAREMODE_SHARED = 0;
        private const int AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
        private const int AUDCLNT_BUFFERFLAGS_SILENT = 0x00000002;

        private readonly IAudioClient audioClient;
        private readonly IAudioCaptureClient captureClient;
        private readonly IntPtr waveFormatPtr;
        private readonly int channels;
        private readonly int bitsPerSample;
        private readonly int blockAlign;
        private readonly short formatTag;
        private bool started;

        public WasapiLoopbackMonitor()
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDevice device;
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 1, out device));

            object clientObject;
            var audioClientId = typeof(IAudioClient).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref audioClientId, 23, IntPtr.Zero, out clientObject));
            audioClient = (IAudioClient)clientObject;

            Marshal.ThrowExceptionForHR(audioClient.GetMixFormat(out waveFormatPtr));
            formatTag = Marshal.ReadInt16(waveFormatPtr, 0);
            channels = Marshal.ReadInt16(waveFormatPtr, 2);
            blockAlign = Marshal.ReadInt16(waveFormatPtr, 12);
            bitsPerSample = Marshal.ReadInt16(waveFormatPtr, 14);

            Marshal.ThrowExceptionForHR(audioClient.Initialize(
                AUDCLNT_SHAREMODE_SHARED,
                AUDCLNT_STREAMFLAGS_LOOPBACK,
                10000000,
                0,
                waveFormatPtr,
                IntPtr.Zero));

            object captureObject;
            var captureClientId = typeof(IAudioCaptureClient).GUID;
            Marshal.ThrowExceptionForHR(audioClient.GetService(ref captureClientId, out captureObject));
            captureClient = (IAudioCaptureClient)captureObject;
        }

        public void Start()
        {
            Marshal.ThrowExceptionForHR(audioClient.Start());
            started = true;
        }

        public double ReadLevel(int milliseconds)
        {
            var endAt = DateTime.Now.AddMilliseconds(milliseconds);
            double maxRms = 0;

            while (DateTime.Now < endAt)
            {
                int packetFrames;
                Marshal.ThrowExceptionForHR(captureClient.GetNextPacketSize(out packetFrames));

                while (packetFrames > 0)
                {
                    IntPtr data;
                    int frames;
                    int flags;
                    long devicePosition;
                    long qpcPosition;

                    Marshal.ThrowExceptionForHR(captureClient.GetBuffer(
                        out data,
                        out frames,
                        out flags,
                        out devicePosition,
                        out qpcPosition));

                    if ((flags & AUDCLNT_BUFFERFLAGS_SILENT) == 0 && frames > 0)
                    {
                        maxRms = Math.Max(maxRms, CalculateRms(data, frames));
                    }

                    Marshal.ThrowExceptionForHR(captureClient.ReleaseBuffer(frames));
                    Marshal.ThrowExceptionForHR(captureClient.GetNextPacketSize(out packetFrames));
                }

                Thread.Sleep(25);
            }

            return maxRms;
        }

        private double CalculateRms(IntPtr data, int frames)
        {
            var byteCount = frames * blockAlign;
            var buffer = new byte[byteCount];
            Marshal.Copy(data, buffer, 0, byteCount);

            double sumSquares = 0;
            int sampleCount = 0;
            var bytesPerSample = bitsPerSample / 8;

            if (bytesPerSample <= 0)
            {
                return 0;
            }

            for (int offset = 0; offset + bytesPerSample <= buffer.Length; offset += bytesPerSample)
            {
                double sample = ReadSample(buffer, offset, bytesPerSample);
                sumSquares += sample * sample;
                sampleCount++;
            }

            if (sampleCount == 0)
            {
                return 0;
            }

            return Math.Sqrt(sumSquares / sampleCount);
        }

        private double ReadSample(byte[] buffer, int offset, int bytesPerSample)
        {
            if (formatTag == 3 && bytesPerSample == 4)
            {
                return Math.Abs(BitConverter.ToSingle(buffer, offset));
            }

            if (formatTag == -2 && bytesPerSample == 4)
            {
                var subFormatKind = Marshal.ReadInt32(waveFormatPtr, 24);
                if (subFormatKind == 3)
                {
                    return Math.Abs(BitConverter.ToSingle(buffer, offset));
                }
            }

            if (bytesPerSample == 2)
            {
                return Math.Abs(BitConverter.ToInt16(buffer, offset) / 32768.0);
            }

            if (bytesPerSample == 3)
            {
                int value = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
                if ((value & 0x800000) != 0)
                {
                    value |= unchecked((int)0xFF000000);
                }
                return Math.Abs(value / 8388608.0);
            }

            if (bytesPerSample == 4)
            {
                return Math.Abs(BitConverter.ToInt32(buffer, offset) / 2147483648.0);
            }

            return 0;
        }

        public void Dispose()
        {
            try
            {
                if (started)
                {
                    audioClient.Stop();
                }
            }
            catch { }

            if (waveFormatPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(waveFormatPtr);
            }

            if (captureClient != null)
            {
                Marshal.ReleaseComObject(captureClient);
            }

            if (audioClient != null)
            {
                Marshal.ReleaseComObject(audioClient);
            }
        }
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumerator
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out object devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
    }

    [ComImport]
    [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioClient
    {
        int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr audioSessionGuid);
        int GetBufferSize(out int bufferSize);
        int GetStreamLatency(out long latency);
        int GetCurrentPadding(out int currentPadding);
        int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);
        int GetMixFormat(out IntPtr deviceFormat);
        int GetDevicePeriod(out long defaultDevicePeriod, out long minimumDevicePeriod);
        int Start();
        int Stop();
        int Reset();
        int SetEventHandle(IntPtr eventHandle);
        int GetService(ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport]
    [Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioCaptureClient
    {
        int GetBuffer(out IntPtr data, out int numFramesToRead, out int flags, out long devicePosition, out long qpcPosition);
        int ReleaseBuffer(int numFramesRead);
        int GetNextPacketSize(out int numFramesInNextPacket);
    }
}
