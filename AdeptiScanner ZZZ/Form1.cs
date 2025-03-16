using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;
using Tesseract;
using InputSimulatorEx;
using System.Net.Http;

namespace AdeptiScanner_ZZZ
{
    public partial class ScannerForm : Form
    {
        public static ScannerForm INSTANCE;
        private TesseractEngine tessEngine;
        private Bitmap img_Raw;
        private Bitmap img_Filtered;
        private int[] filtered_rows;
        private Rectangle savedDiscArea = new Rectangle(0, 0, 1, 1);
        private Rectangle relativeDiscArea = new Rectangle(0, 0, 1, 1);
        private Rectangle savedGameArea = new Rectangle(0, 0, 1, 1);
        private bool pauseAuto = true;
        private bool softCancelAuto = true;
        private bool hardCancelAuto = true;
        private KeyHandler pauseHotkey; // Escape key, pause auto
        private KeyHandler readHotkey; // P key, read stats
        private DateTime soonestAllowedHotkeyUse = DateTime.MinValue; // Used to avoid spam activations and lockups caused by them
        private bool upscaleFiltered = false;

        internal bool autoRunning = false;
        private bool autoCaptureDone = false;
        internal List<Disc> scannedDiscs = new List<Disc>();
        internal List<Character> scannedCharacters = new List<Character>();
        private bool cancelOCRThreads = false;
        private const int ThreadCount = 6; //--------------------------------------------------------
        private bool[] threadRunning = new bool[ThreadCount];
        private ConcurrentQueue<Bitmap>[] threadQueues = new ConcurrentQueue<Bitmap>[ThreadCount];
        private ConcurrentQueue<Bitmap> badResults = new ConcurrentQueue<Bitmap>();
        private TesseractEngine[] threadEngines = new TesseractEngine[ThreadCount];
        private List<object>[] threadResults = new List<object>[ThreadCount];
        private bool rememberSettings = true;

        internal int minLevel = 0;
        internal int maxLevel = 15;
        internal int minRarity = 2;
        internal int maxRarity = 2;
        internal bool exportAllEquipped = true;
        internal bool useTemplate = false;
        internal string travelerName = "";
        internal string wandererName = "Wanderer";
        internal bool captureOnread = true;
        internal bool saveImagesGlobal = false;
        internal string clickSleepWait_load = "200";
        internal string scrollSleepWait_load = "1500";
        internal string scrollTestWait_load = "100";
        internal string recheckWait_load = "300";
        internal bool? updateData = null;
        internal bool? updateVersion = null;
        internal string ignoredDataVersion = "";
        internal string ignoredProgramVersion = "";
        internal string lastUpdateCheck = "";
        internal string uid = "";
        internal bool exportEquipStatus = true;

        private static InputSimulator sim = new InputSimulator();

        public ScannerForm()
        {
            ScannerForm.INSTANCE = this;
            if (Directory.Exists(Database.appdataPath) && Database.appDir != Database.appdataPath)
            {
                foreach (string filePath in Directory.EnumerateFiles(Database.appdataPath))
                {
                    string fileName = filePath.Replace(Database.appdataPath, "");
                    string localFilePath = Database.appDir + fileName;
                    if (File.Exists(localFilePath))
                        File.Delete(localFilePath);
                    File.Copy(filePath, localFilePath);
                    File.Delete(filePath);
                }
                Directory.Delete(Database.appdataPath);
            }
            loadSettings();
            InitializeComponent();
            finalizeLoadSettings();
            label_dataversion.Text = "Data: V" + Database.dataVersion;
            label_appversion.Text = "Program: V" + Database.programVersion;
            label_admin.Text = "Admin: " + IsAdministrator();
            this.Text = "AdeptiScanner_ZZZ V" + Database.programVersion;
            FormClosing += eventFormClosing;
            Activated += eventGotFocus;
            try
            {
                tessEngine = new TesseractEngine(Database.appDir + @"/tessdata", "genshin")
                {
                    DefaultPageSegMode = PageSegMode.SingleLine
                };
            }
            catch (Exception e)
            {
                MessageBox.Show("Error trying to access Tessdata file" + Environment.NewLine + Environment.NewLine +
                    "Exact error:" + Environment.NewLine + e.ToString(),
                    "Scanner could not start", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(-1);
            }

            //worker thread stuff
            for (int i = 0; i < ThreadCount; i++)
            {
                threadRunning[i] = false;
                threadQueues[i] = new ConcurrentQueue<Bitmap>();
                threadEngines[i] = new TesseractEngine(Database.appDir + @"/tessdata", "genshin")
                {
                    DefaultPageSegMode = PageSegMode.SingleLine
                };
                threadResults[i] = new List<object>();
            }

            //simple junk defaults
            img_Raw = new Bitmap(image_preview.Width, image_preview.Height);
            using (Graphics g = Graphics.FromImage(img_Raw))
            {
                g.FillRectangle(Brushes.Black, 0, 0, img_Raw.Width, img_Raw.Height);
                g.FillRectangle(Brushes.White, img_Raw.Width / 8, img_Raw.Height / 8, img_Raw.Width * 6 / 8, img_Raw.Height * 6 / 8);
            }
            img_Filtered = new Bitmap(img_Raw);
            image_preview.Image = new Bitmap(img_Raw);
            if (!updateData.HasValue || !updateVersion.HasValue)
            {
                Application.Run(new FirstStart());
            }
            else
            {
                searchForUpdates(false);
            }
        }

        private void eventFormClosing(object sender, FormClosingEventArgs e)
        {
            if (rememberSettings)
            {
                saveSettings();
            }

            unregisterPauseKey();
            unregisterReadKey();
        }

        private bool TryPauseAuto()
        {
            if (!pauseAuto && autoRunning)
            {
                pauseAuto = true;
                text_full.AppendText("Auto scanning paused, select action" + Environment.NewLine);
                button_hardCancel.Enabled = true;
                button_softCancel.Enabled = true;
                button_resume.Enabled = true;
                return true;
            }
            return false;
        }

        private void eventGotFocus(object sender, EventArgs e)
        {
            TryPauseAuto();
        }

        public void registerPauseKey()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(registerPauseKey));
                return;
            }
            if (pauseHotkey == null)
            {
                pauseHotkey = new KeyHandler(Keys.Escape, this);
            }
            pauseHotkey.Register();
        }

        public void unregisterPauseKey()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(unregisterPauseKey));
                return;
            }
            if (pauseHotkey == null)
            {
                return;
            }
            pauseHotkey.Unregister();
        }

        public void registerReadKey()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(registerReadKey));
                return;
            }
            if (readHotkey == null)
            {
                readHotkey = new KeyHandler(Keys.P, this);
            }
            readHotkey.Register();
        }

        public void unregisterReadKey()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(unregisterReadKey));
                return;
            }
            if (readHotkey == null)
            {
                return;
            }
            readHotkey.Unregister();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == KeyHandler.WM_HOTKEY_MSG_ID)
            {
                // block if last press was too recently. Exception if auto is running and not paused, to be safe
                if ((!pauseAuto && autoRunning) || DateTime.UtcNow > soonestAllowedHotkeyUse)
                {
                    if (pauseHotkey != null && pauseHotkey.GetHashCode() == m.WParam)
                    {
                        TryPauseAuto();
                    }
                    if (readHotkey != null && readHotkey.GetHashCode() == m.WParam)
                    {
                        if (btn_OCR.Enabled)
                        {
                            btn_OCR_Click(this, new HotkeyEventArgs());
                        }
                    }
                }

                soonestAllowedHotkeyUse = DateTime.UtcNow + TimeSpan.FromSeconds(0.2);
            }
            base.WndProc(ref m);
        }

        public void AppendStatusText(string value, bool setButtons)
        {
            if (InvokeRequired)
            {
                this.Invoke(new Action<string, bool>(AppendStatusText), new object[] { value, setButtons });
                return;
            }
            text_full.AppendText(value);
            if (setButtons)
            {
                btn_capture.Enabled = true;
                btn_OCR.Enabled = true;
                button_auto.Enabled = true;
                button_hardCancel.Enabled = false;
                button_softCancel.Enabled = false;
                button_resume.Enabled = false;
            }
        }


        /// <summary>
        /// Move cursor to position and simulare left click
        /// </summary>
        /// <param name="x">Cursor X position</param>
        /// <param name="y">Cursor Y position</param>
        private void clickPos(int x, int y)
        {
            System.Windows.Forms.Cursor.Position = new Point(x, y);
            sim.Mouse.LeftButtonClick();
        }

        /// <summary>
        /// Empties all text boxes
        /// </summary>
        private void resetTextBoxes()
        {
            text_full.Text = "";
        }

        private void displayInventoryItem(object item)
        {
            text_full.Text = item.ToString();
        }

        //https://stackoverflow.com/questions/11660184/c-sharp-check-if-run-as-administrator
        public static bool IsAdministrator()
        {
            return (new WindowsPrincipal(WindowsIdentity.GetCurrent()))
                      .IsInRole(WindowsBuiltInRole.Administrator);
        }

        private void runOCRThread(int threadIndex, bool weaponMode, bool localUpscaleFiltered)
        {
            Task.Run(RunOCRThreadInternal);

            async Task RunOCRThreadInternal()
            {
                threadRunning[threadIndex] = true;
                bool saveImages = false;
                while (autoRunning && !cancelOCRThreads)
                {
                    if (threadQueues[threadIndex].TryDequeue(out Bitmap img))
                    {
                        //string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ssff");
                        //if (saveImages)
                        //    img.Save(Path.Join(Database.appDir, "images", "GenshinArtifactImg_" + timestamp + ".png"));
                        Rectangle area = new Rectangle(0, 0, img.Width, img.Height);
                        Bitmap filtered = new Bitmap(img);

                        filtered = ImageProcessing.getDiscImg(filtered, area, out int[] rows, saveImages, localUpscaleFiltered);

                        Disc item = ImageProcessing.getDisc(filtered, rows, saveImages, threadEngines[threadIndex]);

                        if (Database.discInvalid(item))
                        {
                            badResults.Enqueue(img);
                        }
                        else
                        {
                            threadResults[threadIndex].Add(item);
                        }

                    }
                    else if (autoCaptureDone || softCancelAuto || hardCancelAuto)
                    {
                        threadRunning[threadIndex] = false;
                        return;
                    }
                    else
                    {
                        await Task.Delay(1000);
                    }
                }
                threadRunning[threadIndex] = false;
            }
        }

        private void discAuto(bool saveImages, int clickSleepWait = 100, int scrollSleepWait = 1500, int scrollTestWait = 100, int recheckSleepWait = 300)
        {
            text_full.Text = "Starting auto-run. ---Press ESCAPE to pause---" + Environment.NewLine;
            autoRunning = true;
            autoCaptureDone = false;
            bool localUpscaleFiltered = upscaleFiltered;
            registerPauseKey(); //activate pause auto hotkey
            //start worker threads
            for (int i = 0; i < ThreadCount; i++)
            {
                threadQueues[i] = new ConcurrentQueue<Bitmap>();
                threadResults[i] = new List<object>();
                runOCRThread(i, false, localUpscaleFiltered);
            }

            Task.Run(DiscAutoInternal);

            void DiscAutoInternal()
            {
                Stopwatch runtime = new Stopwatch();
                runtime.Start();
                System.Security.Cryptography.SHA1 sha1 = System.Security.Cryptography.SHA1.Create();
                bool running = true;
                bool firstRun = true;
                int nextThread = 0;
                Rectangle gridArea = new Rectangle(savedGameArea.X, savedGameArea.Y, savedDiscArea.X - savedGameArea.X, savedGameArea.Height);
                Point gridOffset = new Point(gridArea.X, gridArea.Y);
                List<string> foundRowHashes = new List<string>();
                List<(string Hash, Bitmap Image)> lastScrollLastRowHashes = new();


                GameVisibilityHandler.bringGameToFront();

                //make sure cursor is on the correct screen
                System.Threading.Thread.Sleep(50);
                System.Windows.Forms.Cursor.Position = new Point(savedGameArea.X, savedGameArea.Y);
                System.Threading.Thread.Sleep(50);
                System.Windows.Forms.Cursor.Position = new Point(savedGameArea.X, savedGameArea.Y);
                System.Threading.Thread.Sleep(50);

                while (running)
                {
                    //load current grid/scroll location
                    Bitmap img = ImageProcessing.CaptureScreenshot(saveImages, gridArea, true);
                    List<Point> artifactLocations = ImageProcessing.getArtifactGrid(img, saveImages, gridOffset);
                    artifactLocations = ImageProcessing.equalizeGrid(artifactLocations, gridArea.Height / 20, gridArea.Width / 20, out int rows);

                    if (artifactLocations.Count == 0)
                    {
                        break;
                    }

                    if (!firstRun)
                    {
                        while (pauseAuto)
                        {
                            if (hardCancelAuto)
                            {
                                goto hard_cancel_pos;
                            }
                            if (softCancelAuto)
                            {
                                running = false;
                                pauseAuto = false;
                                goto soft_cancel_pos;
                            }
                            System.Threading.Thread.Sleep(1000);
                        }

                        // each scroll moves a full row, so scroll as many times as there are rows
                        for (int i = 0; i < rows; i++)
                        {
                            System.Threading.Thread.Sleep(scrollTestWait);
                            sim.Mouse.VerticalScroll(-1);
                        }

                        System.Threading.Thread.Sleep(scrollSleepWait);
                        img = ImageProcessing.CaptureScreenshot(saveImages, gridArea, true);
                        artifactLocations = ImageProcessing.getArtifactGrid(img, saveImages, gridOffset);
                        artifactLocations = ImageProcessing.equalizeGrid(artifactLocations, gridArea.Height / 20, gridArea.Width / 20, out rows);
                    }


                    firstRun = false;

                    if (!running && lastScrollLastRowHashes.Count > 0)
                    {
                        var finalHash = lastScrollLastRowHashes.Last().Hash;
                        while (lastScrollLastRowHashes.Count > 1 && lastScrollLastRowHashes[^2].Hash == finalHash)
                        {
                            lastScrollLastRowHashes.RemoveAt(lastScrollLastRowHashes.Count - 1);
                        }
                    }


                    foreach (var tup in lastScrollLastRowHashes)
                    {
                        //queue up processing of artifact
                        threadQueues[nextThread].Enqueue(tup.Image);
                        nextThread = (nextThread + 1) % ThreadCount;
                    }

                    lastScrollLastRowHashes.Clear();

                    //select and OCR each artifact in list
                    int artisPerRow = artifactLocations.Count / rows;
                    int artisThisRow = 0;
                    List<(string Hash, Bitmap Image)> thisRowHashes = new();

                    for (int i = 0; i < artifactLocations.Count;)
                    {
                        if (artisThisRow == 0)
                        {
                            thisRowHashes = new();
                        }
                        artisThisRow++;

                        Point p = artifactLocations[i];
                        while (pauseAuto)
                        {
                            if (hardCancelAuto)
                            {
                                goto hard_cancel_pos;
                            }

                            if (softCancelAuto)
                            {
                                running = false;
                                pauseAuto = false;
                                goto soft_cancel_pos;
                            }
                            System.Threading.Thread.Sleep(1000);
                        }
                        clickPos(p.X, p.Y);
                        System.Threading.Thread.Sleep(clickSleepWait);

                        bool imageHasWhite = false;
                        Bitmap artifactSC = null;
                        byte[] imgBytes;
                        int tries = 0;
                        do
                        {
                            if (tries > 0)
                            {
                                AppendStatusText("Image still fading. Possibly increase ClickSleepWait in Advanced. Retry " + tries + "" + Environment.NewLine, false);
                                System.Threading.Thread.Sleep(recheckSleepWait);                                    
                            }
                            tries++;
                            artifactSC = ImageProcessing.CaptureScreenshot(saveImages, savedDiscArea, true);

                            //check if artifact already found using hash of pixels
                            int width = artifactSC.Width;
                            int height = artifactSC.Height;
                            BitmapData imgData = artifactSC.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadWrite, artifactSC.PixelFormat);
                            int numBytes = Math.Abs(imgData.Stride) * height;
                            imgBytes = new byte[numBytes];
                            Marshal.Copy(imgData.Scan0, imgBytes, 0, numBytes);
                            //int PixelSize = 4; //ARGB, reverse order
                            artifactSC.UnlockBits(imgData);

                            // check beginning of image for perfectly white pixels, to confirm fade is done
                            for (int j = 0; j < 4 * width * (height / 6); j += 4)
                            {
                                var pixel = imgBytes.AsSpan(j, 4);
                                if (ImageProcessing.PixelIsColor(pixel, ImageProcessing.GameColor.PerfectWhite))
                                {
                                    imageHasWhite = true;
                                    break;
                                }
                            }

                        } while (!imageHasWhite && tries <= 3);

                        //https://stackoverflow.com/a/800469 with some liberty
                        string hash = string.Concat(sha1.ComputeHash(imgBytes).Select(x => x.ToString("X2")));

                        thisRowHashes.Add((hash, artifactSC));

                        i++;
                        if (artisThisRow != artisPerRow)
                        {
                            continue;
                        }

                        artisThisRow = 0;

                        string thisRowHash = string.Concat(thisRowHashes.Select(x => x.Hash));

                        if (foundRowHashes.Contains(thisRowHash))
                        {
                            if (running)
                            {
                                AppendStatusText("Duplicate row found, stopping after this screen" + Environment.NewLine, false);
                            }
                            running = false;
                            continue;
                        }

                        foundRowHashes.Add(thisRowHash);

                        bool isLastRow = i == artifactLocations.Count;

                        if (isLastRow)
                        {
                            lastScrollLastRowHashes = thisRowHashes;

                            if (!running && thisRowHashes.Count > 0)
                            {
                                var finalHash = thisRowHashes.Last().Hash;
                                while (thisRowHashes.Count > 1 && thisRowHashes[^2].Hash == finalHash)
                                {
                                    thisRowHashes.RemoveAt(thisRowHashes.Count - 1);
                                }
                            }
                        }

                        if (!isLastRow || !running)
                        {
                            foreach (var tup in thisRowHashes)
                            {
                                //queue up processing of artifact
                                threadQueues[nextThread].Enqueue(tup.Image);
                                nextThread = (nextThread + 1) % ThreadCount;
                            }
                        }
                    }

                }

            soft_cancel_pos:

                autoCaptureDone = true;

                //temporarily disable "got focus" event, as that would trigger pause
                Activated -= eventGotFocus;
                GameVisibilityHandler.bringScannerToFront();
                Activated += eventGotFocus;

                AppendStatusText("Scanning complete, awaiting results" + Environment.NewLine
                    + "Time elapsed: " + runtime.ElapsedMilliseconds + "ms" + Environment.NewLine, false);
                for (int i = 0; i < ThreadCount; i++)
                {
                    while (threadRunning[i] || pauseAuto)
                    {
                        if (hardCancelAuto)
                        {
                            goto hard_cancel_pos;
                        }
                        System.Threading.Thread.Sleep(1000);
                    }
                    foreach (object item in threadResults[i])
                    {
                        if (item is Disc disc)
                        {
                            scannedDiscs.Add(disc);
                        }
                        else
                        {
                            throw new NotImplementedException((item?.GetType()?.FullName ?? "NULL") + " Type Not supported");
                        }
                    }
                }


                AppendStatusText("Auto finished" + Environment.NewLine
                    + " Good results: " + scannedDiscs.Count + ", Bad results: " + badResults.Count + Environment.NewLine
                    + "Time elapsed: " + runtime.ElapsedMilliseconds + "ms" + Environment.NewLine + Environment.NewLine, false);

                while (badResults.TryDequeue(out Bitmap img))
                {
                    Rectangle area = new Rectangle(0, 0, img.Width, img.Height);
                    Bitmap filtered = new Bitmap(img);
                    string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ssff");
                    filtered.Save(Path.Join(Database.appDir, "images", "GenshinArtifactImg_" + timestamp + ".png"));
                    filtered = ImageProcessing.getDiscImg(filtered, area, out int[] rows, true, localUpscaleFiltered);
                    Disc item = ImageProcessing.getDisc(filtered, rows, true, tessEngine);
                    AppendStatusText(item.ToString() + Environment.NewLine, false);
                }

                AppendStatusText("All bad results displayed" + Environment.NewLine, false);

            hard_cancel_pos:
                unregisterPauseKey();
                runtime.Stop();
                GameVisibilityHandler.bringScannerToFront();
                AppendStatusText("Time elapsed: " + runtime.ElapsedMilliseconds + "ms" + Environment.NewLine, true);
                autoRunning = false;
            }
        }

        enum CaptureDebugMode
        {
            Off,
            FullScreen,
            GameWindow,
            DiscArea
        };

        private void btn_capture_Click(object sender, EventArgs e)
        {
            bool saveImages = checkbox_saveImages.Checked;
            if (autoRunning)
            {
                text_full.AppendText("Ignored, auto currently running" + Environment.NewLine);
                return;
            }
            resetTextBoxes();

            //Nothing = Normal, LShift + LCtrl = Game, LShift = Disc, LCtrl = FullScreen
            CaptureDebugMode debugMode = CaptureDebugMode.Off;
            if (Keyboard.IsKeyDown(Key.LeftShift))
            {
                if (Keyboard.IsKeyDown(Key.LeftCtrl))
                {
                    debugMode = CaptureDebugMode.GameWindow;
                }
                else
                {
                    debugMode = CaptureDebugMode.DiscArea;
                }
            }
            else if (Keyboard.IsKeyDown(Key.LeftCtrl))
            {
                debugMode = CaptureDebugMode.FullScreen;
            }

            Rectangle directGameRect = Rectangle.Empty;
            if (debugMode != CaptureDebugMode.Off)
            {
                img_Raw = ImageProcessing.LoadScreenshot();
            }
            else
            {
                GameVisibilityHandler.captureGameProcess();
                GameVisibilityHandler.bringGameToFront();

                //try to get close estimate of game location, if successfull use that as base instead of the entire primary monitor
                bool areaObtained = GameVisibilityHandler.getGameLocation(out directGameRect);

                img_Raw = ImageProcessing.CaptureScreenshot(saveImages, directGameRect, areaObtained);
                GameVisibilityHandler.bringScannerToFront();
            }

            Rectangle? tmpGameArea = new Rectangle(0, 0, img_Raw.Width, img_Raw.Height);
            if (debugMode == CaptureDebugMode.Off || debugMode == CaptureDebugMode.FullScreen)
            {
                tmpGameArea = ImageProcessing.findGameArea(img_Raw);
                if (tmpGameArea == null)
                {
                    if (directGameRect != Rectangle.Empty)
                    {
                        //assume directGameRect is a close enough estimate, primarily in case the game is in fullscreen
                        tmpGameArea = new Rectangle(0, 0, directGameRect.Width, directGameRect.Height);
                        AppendStatusText("Window header not found, treating whole image area as game" + Environment.NewLine, false);
                    }
                    else
                    {
                        MessageBox.Show("Failed to find Game Area" + Environment.NewLine +
                            "Please make sure you're following the instructions properly."
                            + Environment.NewLine + "If the problem persists, please contact scanner dev", "Failed to find Game Area"
                            , MessageBoxButtons.OK, MessageBoxIcon.Warning);

                    }
                }
            }

            bool DiscAreaCaptured = tmpGameArea != null;
            Rectangle? tmpDiscArea = null;
            if (debugMode == CaptureDebugMode.DiscArea)
            {
                tmpDiscArea = new Rectangle(0, 0, img_Raw.Width, img_Raw.Height);
            }
            else if (tmpGameArea != null)
            {
                try
                {
                    tmpDiscArea = ImageProcessing.findArtifactArea(img_Raw, tmpGameArea.Value);
                    if (tmpDiscArea.Value.Width == 0 || tmpDiscArea.Value.Height == 0)
                        throw new Exception("Detected disc are has width or height 0");
                }
                catch (Exception exc)
                {
                    MessageBox.Show("Failed to find Disc Area" + Environment.NewLine +
                        "Please make sure you're following the instructions properly."
                        + Environment.NewLine + "If the problem persists, please contact scanner dev"
                        + Environment.NewLine + Environment.NewLine + "---" + Environment.NewLine + Environment.NewLine + "Exact error message: " + Environment.NewLine + exc.ToString(), "Failed to find Disc Area"
                        , MessageBoxButtons.OK, MessageBoxIcon.Error);
                    DiscAreaCaptured = false;
                }
            }

            if (DiscAreaCaptured)
            {
                savedGameArea = tmpGameArea.Value;
                savedDiscArea = tmpDiscArea.Value;
                relativeDiscArea = tmpDiscArea.Value;
                //upscaleFiltered = savedDiscArea.Width < 350; // ~600 for 1440p, ~450 for 1080p, ~375 for 1600x900, ~300 for 720p
                upscaleFiltered = true; //Unknown if upscaling is beneficial at all resolutions, but seems so
                if (upscaleFiltered)
                {
                    AppendStatusText("Using upscaling to improve reliability", false);
                }

                if (directGameRect != Rectangle.Empty)
                {
                    savedGameArea.X = savedGameArea.X + directGameRect.X;
                    savedGameArea.Y = savedGameArea.Y + directGameRect.Y;

                    savedDiscArea.X = savedDiscArea.X + directGameRect.X;
                    savedDiscArea.Y = savedDiscArea.Y + directGameRect.Y;
                }
                btn_OCR.Enabled = true;
                button_auto.Enabled = true;
            }

            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ssff");
            if (saveImages)
            {
                if (tmpGameArea != null)
                {
                    Bitmap gameImg = new Bitmap(tmpGameArea.Value.Width, tmpGameArea.Value.Height);
                    using (Graphics g = Graphics.FromImage(gameImg))
                    {
                        g.DrawImage(img_Raw, 0, 0, tmpGameArea.Value, GraphicsUnit.Pixel);
                    }
                    gameImg.Save(Path.Join(Database.appDir, "images", "GenshinGameArea_" + timestamp + ".png"));
                }

                if (DiscAreaCaptured)
                {
                    Bitmap artifactImg = new Bitmap(tmpDiscArea.Value.Width, tmpDiscArea.Value.Height);
                    using (Graphics g = Graphics.FromImage(artifactImg))
                    {
                        g.DrawImage(img_Raw, 0, 0, tmpDiscArea.Value, GraphicsUnit.Pixel);
                    }
                    artifactImg.Save(Path.Join(Database.appDir, "images", "GenshinArtifactArea_" + timestamp + ".png"));
                }
            }

            if (DiscAreaCaptured)
            {
                image_preview.Image = new Bitmap(tmpDiscArea.Value.Width, tmpDiscArea.Value.Height);
                using (Graphics g = Graphics.FromImage(image_preview.Image))
                {
                    g.DrawImage(img_Raw, 0, 0, tmpDiscArea.Value, GraphicsUnit.Pixel);
                }
            }
        }

        private void btn_OCR_Click(object sender, EventArgs e)
        {
            bool saveImages = checkbox_saveImages.Checked;
            if (autoRunning)
            {
                text_full.AppendText("Ignored, auto currently running" + Environment.NewLine);
                return;
            }

            resetTextBoxes();

            bool capture = checkbox_OCRcapture.Checked || e is HotkeyEventArgs;
            if (capture)
            {
                if (Keyboard.IsKeyDown(Key.LeftShift))
                {
                    img_Raw = ImageProcessing.LoadScreenshot();
                    savedGameArea = new Rectangle(0, 0, img_Raw.Width, img_Raw.Height);
                    savedDiscArea = new Rectangle(0, 0, img_Raw.Width, img_Raw.Height);
                    relativeDiscArea = new Rectangle(0, 0, img_Raw.Width, img_Raw.Height);
                }
                else
                {
                    bool? alreadyFocused = GameVisibilityHandler.IsGameFocused();
                    GameVisibilityHandler.bringGameToFront();
                    img_Raw = ImageProcessing.CaptureScreenshot(saveImages, savedDiscArea, GameVisibilityHandler.enabled);
                    if (alreadyFocused == false)
                    {
                        // no need to force scanner into focus if game was already focused (possible if activation is via hotkey)
                        // also no need if we don't know if the game was focused
                        GameVisibilityHandler.bringScannerToFront();
                    }
                }
            }


            img_Filtered = new Bitmap(img_Raw);

            Rectangle readArea = relativeDiscArea;
            if (GameVisibilityHandler.enabled)
            {
                //using process handle features and the image is exactly the artifact area
                if (capture)
                {
                    readArea = new Rectangle(0, 0, img_Filtered.Width, img_Filtered.Height);
                }
                else
                {
                    readArea = relativeDiscArea;
                }
            }

            img_Filtered = ImageProcessing.getDiscImg(img_Filtered, readArea, out filtered_rows, saveImages, upscaleFiltered);
            Disc disc = ImageProcessing.getDisc(img_Filtered, filtered_rows, saveImages, tessEngine);
            if (Database.discInvalid(disc))
            {
                displayInventoryItem(disc);
                text_full.AppendText(Environment.NewLine + "---This disc is invalid---" + Environment.NewLine);
            }
            else
            {
                scannedDiscs.Add(disc);
                displayInventoryItem(disc);
            }
            text_full.AppendText(Environment.NewLine + "Total stored discs:" + scannedDiscs.Count + Environment.NewLine);

            image_preview.Image = new Bitmap(img_Filtered);
        }

        private void button_auto_Click(object sender, EventArgs e)
        {
            if (!IsAdministrator() && !Keyboard.IsKeyDown(Key.LeftShift))
            {
                MessageBox.Show("Cannot automatically scroll artifacts without admin perms" + Environment.NewLine + Environment.NewLine
                + "To use auto mode, restart scanner as admin",
                "Insufficient permissions", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool saveImages = checkbox_saveImages.Checked;
            if (autoRunning)
            {
                text_full.AppendText("Ignored, auto currently running" + Environment.NewLine);
                return;
            }
            btn_OCR.Enabled = false;
            btn_capture.Enabled = false;
            button_auto.Enabled = false;
            pauseAuto = false;
            softCancelAuto = false;
            hardCancelAuto = false;

            int.TryParse(text_clickSleepWait.Text, out int clickSleepWait);
            if (clickSleepWait == 0)
                clickSleepWait = 300;
            int.TryParse(text_ScrollSleepWait.Text, out int scrollSleepWait);
            if (scrollSleepWait == 0)
                scrollSleepWait = 1500;
            int.TryParse(text_ScrollTestWait.Text, out int scrollTestWait);
            if (scrollTestWait == 0)
                scrollTestWait = 100;
            int.TryParse(text_RecheckWait.Text, out int recheckWait);
            if (recheckWait == 0)
                recheckWait = 300;
            discAuto(false, clickSleepWait, scrollSleepWait, scrollTestWait, recheckWait);
        }

        private void button_resume_Click(object sender, EventArgs e)
        {
            text_full.AppendText("Resuming auto" + Environment.NewLine);
            GameVisibilityHandler.bringGameToFront();
            pauseAuto = false;
        }

        private void button_softCancel_Click(object sender, EventArgs e)
        {
            text_full.AppendText("New scanning canceled, awaiting results" + Environment.NewLine);
            softCancelAuto = true;
        }

        private void button_hardCancel_Click(object sender, EventArgs e)
        {
            text_full.AppendText("Auto canceled" + Environment.NewLine);
            hardCancelAuto = true;
        }

        private void button_export_Click(object sender, EventArgs e)
        {
            if (autoRunning)
            {
                text_full.AppendText("Ignored, auto currently running" + Environment.NewLine);
                return;
            }
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ssff");

            JObject currData = new JObject();
            if (scannedDiscs.Count > 0)
            {
                JObject discs = Disc.listToZOD(scannedDiscs, minLevel, maxLevel, minRarity, maxRarity);
                currData.Add("discs", discs["discs"]);
            }


            if (useTemplate && !File.Exists(Path.Join(Database.appDir, "ExportTemplate.json")))
            {
                MessageBox.Show("No export template found, exporting without one" + Environment.NewLine + "To use an export template, place valid GOOD-format json in ScannerFiles and rename to \"ExportTemplate.json\"",
                    "No export template found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                useTemplate = false;
            }
            if (useTemplate)
            {
                JObject template = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(Path.Join(Database.appDir, "ExportTemplate.json")));
                if (currData.ContainsKey("discs"))
                {
                    template.Remove("discs");
                    template.Add("discs", currData["discs"]);
                }

                currData = template;
            }
            else
            {
                currData.Add("format", "ZOD");
                currData.Add("version", 1);
                currData.Add("source", "AdeptiScanner");
                //currData.Add("characters", new JArray());
                //currData.Add("weapons", new JArray());
            }
            string fileName = Path.Join(Database.appDir, @"Scan_Results", "export" + timestamp + ".ZOD.json");
            File.WriteAllText(fileName, currData.ToString());
            text_full.AppendText("Exported to \"" + fileName + "\"" + Environment.NewLine);

            Process.Start("explorer.exe", Path.Join(Database.appDir, "Scan_Results"));
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process myProcess = new Process();

            myProcess.StartInfo.UseShellExecute = true;
            myProcess.StartInfo.FileName = "https://github.com/D1firehail/AdeptiScanner-ZZZ";
            myProcess.Start();
        }

        private void loadSettings()
        {
            string fileName = Path.Join(Database.appDir, "settings.json");
            JObject settings = null;
            try
            {
                settings = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(fileName));
            }
            catch (Exception)
            {
                return;
            }

            if (settings.ContainsKey("TravelerName"))
            {
                string readTravelerName = settings["TravelerName"].ToObject<string>();
                Database.SetCharacterName(readTravelerName, "Traveler");
                travelerName = readTravelerName;
            }
            if (settings.ContainsKey("WandererName"))
            {
                string readWandererName = settings["WandererName"].ToObject<string>();
                Database.SetCharacterName(readWandererName, "Wanderer");
                wandererName = readWandererName;
            }
            if (settings.ContainsKey("FilterMinLevel"))
            {
                minLevel = Math.Clamp(settings["FilterMinLevel"].ToObject<int>(), 0, 15);
            }
            if (settings.ContainsKey("FilterMaxLevel"))
            {
                maxLevel = Math.Clamp(settings["FilterMaxLevel"].ToObject<int>(), 0, 15);
            }
            if (settings.ContainsKey("FilterMinRarity"))
            {
                minRarity = Math.Clamp(settings["FilterMinRarity"].ToObject<int>(), 0, 2);
            }
            if (settings.ContainsKey("FilterMaxRarity"))
            {
                maxRarity = Math.Clamp(settings["FilterMaxRarity"].ToObject<int>(), 0, 2);
            }
            if (settings.ContainsKey("ExportUseTemplate"))
            {
                useTemplate = settings["ExportUseTemplate"].ToObject<bool>();
            }
            if (settings.ContainsKey("ExportAllEquipped"))
            {
                exportAllEquipped = settings["ExportAllEquipped"].ToObject<bool>();
            }
            if (settings.ContainsKey("CaptureOnRead"))
            {
                captureOnread = settings["CaptureOnRead"].ToObject<bool>();
            }
            if (settings.ContainsKey("saveImagesGlobal"))
            {
                saveImagesGlobal = settings["saveImagesGlobal"].ToObject<bool>();
            }
            if (settings.ContainsKey("clickSleepWait"))
            {
                clickSleepWait_load = settings["clickSleepWait"].ToObject<string>();
            }
            if (settings.ContainsKey("scrollSleepWait"))
            {
                scrollSleepWait_load = settings["scrollSleepWait"].ToObject<string>();
            }
            if (settings.ContainsKey("scrollTestWait"))
            {
                scrollTestWait_load = settings["scrollTestWait"].ToObject<string>();
            }
            if (settings.ContainsKey("recheckWait"))
            {
                recheckWait_load = settings["recheckWait"].ToObject<string>();
            }
            if (settings.ContainsKey("updateData"))
            {
                updateData = settings["updateData"].ToObject<bool>();
            }
            if (settings.ContainsKey("updateVersion"))
            {
                updateVersion = settings["updateVersion"].ToObject<bool>();
            }
            if (settings.ContainsKey("ignoredDataVersion"))
            {
                ignoredDataVersion = settings["ignoredDataVersion"].ToObject<string>();
            }
            if (settings.ContainsKey("ignoredProgramVersion"))
            {
                ignoredProgramVersion = settings["ignoredProgramVersion"].ToObject<string>();
            }
            if (settings.ContainsKey("lastUpdateCheck"))
            {
                lastUpdateCheck = settings["lastUpdateCheck"].ToObject<string>();
            }
            if (settings.ContainsKey("processHandleInteractions"))
            {
                GameVisibilityHandler.enabled = settings["processHandleInteractions"].ToObject<bool>();
            }
            if (settings.ContainsKey("uid"))
            {
                uid = settings["uid"].ToObject<string>();
            }
            if (settings.ContainsKey("ExportEquipStatus"))
            {
                exportEquipStatus = settings["ExportEquipStatus"].ToObject<bool>();
            }
        }

        private void finalizeLoadSettings()
        {
            text_traveler.Text = travelerName;
            text_wanderer.Text = wandererName;
            checkbox_OCRcapture.Checked = captureOnread;
            checkbox_saveImages.Checked = saveImagesGlobal;
            text_clickSleepWait.Text = clickSleepWait_load;
            text_ScrollSleepWait.Text = scrollSleepWait_load;
            text_ScrollTestWait.Text = scrollTestWait_load;
            text_RecheckWait.Text = recheckWait_load;
            enkaTab.text_UID.Text = uid;
            if (updateData.HasValue)
                checkBox_updateData.Checked = updateData.Value;
            if (updateVersion.HasValue)
                checkBox_updateVersion.Checked = updateVersion.Value;
            checkBox_ProcessHandleFeatures.Checked = GameVisibilityHandler.enabled;

        }

        public void SetUpdatePreferences(bool updateData, bool updateVersion)
        {
            if (InvokeRequired)
            {
                this.Invoke(new Action<bool, bool>(SetUpdatePreferences), new object[] { updateData, updateVersion });
                return;
            }
            this.updateData = updateData;
            this.updateVersion = updateVersion;
            checkBox_updateData.Checked = updateData;
            checkBox_updateVersion.Checked = updateVersion;
        }

        public void UpdateCharacterList(List<Character> characterList)
        {
            if (InvokeRequired)
            {
                this.Invoke(new Action<List<Character>>(UpdateCharacterList), new object[] { characterList });
                return;
            }

            int beforeCount = scannedCharacters.Count;
            foreach (Character character in characterList)
            {
                //remove any old copy of the character, then add new one
                scannedCharacters = scannedCharacters.Where(c => !character.key.Equals(c.key)).ToList();
                scannedCharacters.Add(character);
            }

            int diff = scannedCharacters.Count - beforeCount;

            enkaTab.UpdateMissingChars(scannedDiscs, scannedCharacters);

            AppendStatusText("New character info: " + diff + " added, " + (characterList.Count - diff) + " updated, " + scannedCharacters.Count + " total" + Environment.NewLine, false);
        }

        private void searchForUpdates(bool isManual)
        {
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (timestamp == lastUpdateCheck && !isManual)
                return;
            lastUpdateCheck = timestamp;
            HttpClient webclient = new HttpClient();
            webclient.DefaultRequestHeaders.Add("user-agent", "AdeptiScanner");

            string programVersionTitle = "";
            string programVersionBody = "";
            bool programVersionPrerelease = false;
            bool programVersionDraft = false;

            if (isManual || (this.updateVersion.HasValue && this.updateVersion.Value))
            {
                try
                {
                    var request = webclient.GetStringAsync("https://api.github.com/repos/D1firehail/AdeptiScanner-ZZZ/releases");
                    bool requestCompleted = request.Wait(TimeSpan.FromMinutes(1));
                    if (!requestCompleted)
                    {
                        throw new TimeoutException("Version update check did not complete within 1 minute, ignoring");
                    }
                    string response = request.Result;
                    JArray releases = JsonConvert.DeserializeObject<JArray>(response);
                    if (releases.First.HasValues)
                    {
                        JObject latest = releases.First.Value<JObject>();
                        programVersionTitle = latest["tag_name"].ToObject<string>();

                        programVersionPrerelease = latest["prerelease"].ToObject<bool>();

                        programVersionDraft = latest["draft"].ToObject<bool>();

                        programVersionBody = latest["body"].ToObject<string>();

                    }
                }
                catch (Exception exc)
                {
                    Debug.WriteLine(exc.ToString());
                }
                if (programVersionPrerelease || programVersionDraft || programVersionTitle.ToLower().Equals("v" + Database.programVersion))
                {
                    programVersionTitle = "";
                }
            }

            string dataVersionString = "";
            string dataVersionJson = "";

            if (isManual || (this.updateData.HasValue && this.updateData.Value))
            {
                try
                {
                    var request = webclient.GetStringAsync("https://raw.githubusercontent.com/D1firehail/AdeptiScanner-ZZZ/master/AdeptiScanner%20ZZZ/ScannerFiles/ArtifactInfo.json");
                    bool requestCompleted = request.Wait(TimeSpan.FromMinutes(1));
                    if (!requestCompleted)
                    {
                        throw new TimeoutException("Data update check did not complete within 1 minute, ignoring");
                    }
                    string response = request.Result;
                    JObject artifactInfo = JsonConvert.DeserializeObject<JObject>(response);
                    dataVersionString = artifactInfo["DataVersion"].ToObject<string>();
                    dataVersionJson = response;
                }
                catch (Exception exc)
                {
                    Debug.WriteLine(exc.ToString());
                }
                if (dataVersionString.Equals(Database.dataVersion))
                {
                    dataVersionString = "";
                }
            }
            if ((programVersionTitle.Length > 0 && (isManual || programVersionTitle != ignoredProgramVersion))
                || (dataVersionString.Length > 0 && (isManual || dataVersionString != ignoredDataVersion)))
            {
                UpdatePrompt tmp = new UpdatePrompt(programVersionTitle, programVersionBody, dataVersionString, dataVersionJson);
                tmp.Show();
            }
            else if (isManual)
            {
                MessageBox.Show("No updates found",
                "Update checker", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

        }

        internal void executeDataUpdate(string newData)
        {
            if (InvokeRequired)
            {
                this.Invoke(new Action<string>(executeDataUpdate), new object[] { newData });
                return;
            }


            try
            {
                File.WriteAllText(Path.Join(Database.appDir, "ArtifactInfo.json"), newData);
            }
            catch (Exception exc)
            {
                MessageBox.Show("Update failed, error: " + Environment.NewLine + Environment.NewLine + exc.ToString(), "Update failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            Application.Restart();
        }

        internal void readyVersionUpdate()
        {
            if (InvokeRequired)
            {
                this.Invoke(new Action(readyVersionUpdate));
                return;
            }


            saveSettings();

            if (!Directory.Exists(Database.appdataPath))
                Directory.CreateDirectory(Database.appdataPath);

            string[] filesToCopy = { "settings.json", "ExportTemplate.json" };

            foreach (string file in filesToCopy)
            {
                string dirPath = Path.Join(Database.appDir, file);
                string appDataPath = Path.Join(Database.appdataPath, file);
                if (!File.Exists(dirPath))
                    continue;
                if (File.Exists(appDataPath))
                    File.Delete(appDataPath);
                File.Copy(dirPath, appDataPath);
            }
            Process myProcess = new Process();

            myProcess.StartInfo.UseShellExecute = true;
            myProcess.StartInfo.FileName = "https://github.com/D1firehail/AdeptiScanner-ZZZ/releases/latest";
            myProcess.Start();

            Application.Exit();
        }

        internal void setIgnoredVersions(string dataVersion, string programVersion)
        {
            if (InvokeRequired)
            {
                this.Invoke(new Action<string, string>(setIgnoredVersions), new object[] { dataVersion, programVersion });
                return;
            }
            if (dataVersion.Length > 0)
                ignoredDataVersion = dataVersion;
            if (programVersion.Length > 0)
                ignoredProgramVersion = programVersion;
        }

        private void saveSettings()
        {
            JObject settings = new JObject();
            settings["TravelerName"] = text_traveler.Text;
            settings["WandererName"] = text_wanderer.Text;
            settings["FilterMinLevel"] = minLevel;
            settings["FilterMaxLevel"] = maxLevel;
            settings["FilterMinRarity"] = minRarity;
            settings["FilterMaxRarity"] = maxRarity;
            settings["ExportUseTemplate"] = useTemplate;
            settings["ExportAllEquipped"] = exportAllEquipped;
            settings["CaptureOnRead"] = captureOnread;
            settings["saveImagesGlobal"] = saveImagesGlobal;
            settings["clickSleepWait"] = text_clickSleepWait.Text;
            settings["scrollSleepWait"] = text_ScrollSleepWait.Text;
            settings["scrollTestWait"] = text_ScrollTestWait.Text;
            settings["recheckWait"] = text_RecheckWait.Text;
            if (updateData.HasValue)
                settings["updateData"] = updateData.Value;
            if (updateVersion.HasValue)
                settings["updateVersion"] = updateVersion.Value;
            settings["ignoredDataVersion"] = ignoredDataVersion;
            settings["ignoredProgramVersion"] = ignoredProgramVersion;
            settings["lastUpdateCheck"] = lastUpdateCheck;
            settings["processHandleInteractions"] = GameVisibilityHandler.enabled;
            settings["uid"] = enkaTab.text_UID.Text;
            settings["ExportEquipStatus"] = exportEquipStatus;


            string fileName = Path.Join(Database.appDir, "settings.json");
            File.WriteAllText(fileName, settings.ToString());
        }


        private void text_traveler_TextChanged(object sender, EventArgs e)
        {
            travelerName = text_traveler.Text;
            Database.SetCharacterName(travelerName, "Traveler");
        }


        private void text_wanderer_TextChanged(object sender, EventArgs e)
        {

            wandererName = text_wanderer.Text;
            Database.SetCharacterName(wandererName, "Wanderer");
        }

        private void checkbox_OCRcapture_CheckedChanged(object sender, EventArgs e)
        {
            captureOnread = checkbox_OCRcapture.Checked;
        }

        private void checkbox_saveImages_CheckedChanged(object sender, EventArgs e)
        {
            saveImagesGlobal = checkbox_saveImages.Checked;
        }

        private void button_loadArtifacts_Click(object sender, EventArgs e)
        {
            if (autoRunning)
            {
                text_full.AppendText("Ignored, auto currently running" + Environment.NewLine);
                return;
            }
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.InitialDirectory = Database.appDir;
                openFileDialog.Filter = "All files (*.*)|*.*";
                openFileDialog.FilterIndex = 1;
                openFileDialog.RestoreDirectory = true;
                openFileDialog.Multiselect = false;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    foreach (string file in openFileDialog.FileNames)
                    {
                        try
                        {
                            int startDiscAmount = scannedDiscs.Count();
                            JObject GOODjson = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(file));
                            if (GOODjson.ContainsKey("discs"))
                            {
                                JArray discs = GOODjson["discs"].ToObject<JArray>();
                                foreach (JObject disc in discs)
                                {
                                    Disc importedDisc = null;
                                    if (importedDisc != null)
                                    {
                                        scannedDiscs.Add(importedDisc);
                                    }
                                }
                            }
                            int endDiscAmount = scannedDiscs.Count();
                            text_full.AppendText("Imported " + (endDiscAmount - startDiscAmount) + " discs (new total " + endDiscAmount + ") from file: " + file + Environment.NewLine);
                            break;
                        }
                        catch (Exception exc)
                        {
                            text_full.AppendText("Error importing from file: " + file + Environment.NewLine);
                            Debug.WriteLine(exc);
                        }
                    }
                }
            }
        }

        private void button_resetSettings_Click(object sender, EventArgs e)
        {
            DialogResult dialogResult = MessageBox.Show("This will make all settings return to default the next time the scanner is started" + Environment.NewLine + "Are you sure?", "Remove saved Settings", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (dialogResult == DialogResult.Yes)
            {
                File.Delete(Path.Join(Database.appDir, "settings.json"));
                rememberSettings = false;
            }
        }

        private void checkBox_updateData_CheckedChanged(object sender, EventArgs e)
        {
            updateData = checkBox_updateData.Checked;
        }

        private void checkBox_updateVersion_CheckedChanged(object sender, EventArgs e)
        {
            updateVersion = checkBox_updateVersion.Checked;
        }

        private void button_checkUpdateManual_Click(object sender, EventArgs e)
        {
            searchForUpdates(true);
        }

        private void checkBox_ProcessHandleFeatures_CheckedChanged(object sender, EventArgs e)
        {
            GameVisibilityHandler.enabled = checkBox_ProcessHandleFeatures.Checked;
            btn_OCR.Enabled = false;
            button_auto.Enabled = false;
        }

        private void checkbox_weaponMode_CheckedChanged(object sender, EventArgs e)
        {
            btn_OCR.Enabled = false;
            button_auto.Enabled = false;
        }

        private void checkBox_readHotkey_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox_readHotkey.Checked)
            {

                registerReadKey();
                if (!IsAdministrator())
                {
                    AppendStatusText(Environment.NewLine + "Read hotkey enabled, HOWEVER while the game is focused it only works if you RUN AS ADMIN." + Environment.NewLine, false);
                }
            }
            else
            {
                unregisterReadKey();
            }
        }
    }
}
