using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;

namespace MHWNoChunk
{
    public partial class MainWindow : Window
    {
        public static Stack<string> ErrorsStack = new Stack<string>();
        public static bool CNMode = false;
        public static string[] KnownCoreMd5 = {"9b7f9e3e4931b80257da5e1b626db43a",
            "c1a0dd317543035221327d44f07c3d06",
            "224df07f2fc2bd829f3b221bb5ef1a31",
            "b486c6f46a3d802966d04911a619b2ed",
            "548800ca453904c9a892521a64d71f73"
        };

        public bool PauseFlag { get { return pauseFlag; } }
        public bool TerminateFlag { get { return terminateFlag; } }

        string chunkFileName;
        string outputDirectory = "";
        string filterText = "";
        int extractProgress = 0;
        int totalProgress = 0;
        bool mergeChecked = false;
        bool previewEnabled = false;
        bool regexEnabled = false;
        bool pauseFlag = false;
        bool terminateFlag = false;
        
        Chunk mainChunk;
        Dictionary<string, Chunk> chunkMap = new Dictionary<string, Chunk>();
        Regex filterRegex = null;
        TexPreviewer texPreviewer = new TexPreviewer();
        MemoryStream texStream = new MemoryStream();
        List<FileNode> fileNodeList = new List<FileNode>();
        BackgroundWorker analyzeWorker, extractWorker;

        public MainWindow()
        {
            InitializeComponent();
            PreviewUnsupportedInfoLabel.Visibility = Visibility.Hidden;
            analyzeWorker = new BackgroundWorker();
            analyzeWorker.WorkerSupportsCancellation = true;
            analyzeWorker.DoWork += new DoWorkEventHandler(DoAnalyzeHandler);
            extractWorker = new BackgroundWorker();
            extractWorker.WorkerSupportsCancellation = true;
            extractWorker.DoWork += new DoWorkEventHandler(DoExtractHandler);
            InitChinese();
            PrintLog("");
            CheckFilesExist();
            CheckDllVersion();
            PrintErrorInfo();
        }

        private void InitChinese() {
            if (System.Globalization.CultureInfo.InstalledUICulture.Name == "zh-CN") CNMode = true;
            if (CNMode)
            {
                Title = "MHW部分解包器 v2.3.0 By Jodo @ 狩技MOD组";
                LogBox.Text = "拖拽chunkN.bin至上方空白区域以开始。如果想要一次性解析全部chunk0-chunkN.bin，请先勾选右侧的联合解析全部Chunk。本程序根据 WorldChunkTool by MHVuze的原理制作: https://github.com/mhvuze/WorldChunkTool";
                MergeCheckBox.Content = "联合解析全部Chunk";
                ExtractBtn.Content = "提取所选文件";
                FilterLabel.Content = "筛选:";
                RegExCheckBox.Content = "正则表达式";
                BasicInfoLabel.Content = "基本信息";
                PreviewCheckbox.Content = "启用预览";
                PreviewUnsupportedInfoLabel.Content = "暂不支持该格式文件预览";
                ApplyFilterBtn.Content = "应用";
                PauseBtn.Content = "暂停";
                TerminateBtn.Content = "取消";
            }
        }

        public void CheckFilesExist() {
            string[] filesRequired = {"oo2core_8_win64.dll", "SharpGL.dll" };
            foreach (string fileRequired in filesRequired) {
                if (!File.Exists(fileRequired)) { if(!CNMode)ErrorsStack.Push($"Error: {fileRequired} not found in the executable folder.");
                else ErrorsStack.Push($"错误：{fileRequired}未找到");
                }
            }
        }

        public void CheckDllVersion() {
            if (File.Exists("oo2core_8_win64.dll")) {
                string curMd5 = CalculateMD5("oo2core_8_win64.dll");
                if (!KnownCoreMd5.Any<string>(x => x.Equals(curMd5)))
                {
                    if (!CNMode) PrintLog("Warning: oo2core_8_win64.dll found but version not matched. Please try another .dll file from somewhere else if any unknown error occurs.");
                    else { PrintLog("警告：oo2core_8_win64.dll校验失败，如果报错，请从另一渠道获取该文件"); }
                }
            }
        }

        //Copy from Jon Skeet @ stack overflow
        static string CalculateMD5(string filename)
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(filename))
                {
                    var hash = md5.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }

        public void PrintErrorInfo()
        {
            while (ErrorsStack.Count > 0)
            {
                PrintLog(ErrorsStack.Pop(),false,false);
            }
        }

        private void TreeViewItem_SelectedItemChanged(object sender, RoutedEventArgs e)
        {
            TreeViewItem item = (TreeViewItem)sender;
        }

        private void Grid_Drop(object sender, System.Windows.DragEventArgs e)
        {
            chunkFileName = ((System.Array)e.Data.GetData(System.Windows.DataFormats.FileDrop)).GetValue(0).ToString();
            Console.WriteLine(chunkFileName);
            analyzeWorker.RunWorkerAsync();

        }
        private void DoAnalyzeHandler(object sender, DoWorkEventArgs e)
        {
            analyze(chunkFileName);
        }

        private void DoExtractHandler(object sender, DoWorkEventArgs e)
        {
            extractProgress = 0;
            if (fileNodeList.Count > 0)
            {
                FileNode rootnode = fileNodeList[0];
                totalProgress = rootnode.getSelectedCount();
                SetProgressbarMax(totalProgress);
                SetProgressbar(0, totalProgress);
            }
            if (totalProgress == 0)
            {
                if (!CNMode) PrintLog("Nothing selected.");
                else PrintLog("未选择任何文件");
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ExtractBtn.IsEnabled = true;
                    RegExCheckBox.IsEnabled = true;
                    FilterBox.IsEnabled = true;
                    ApplyFilterBtn.IsEnabled = true;
                    PauseBtn.Visibility = Visibility.Hidden;
                    TerminateBtn.Visibility = Visibility.Hidden;
                }));
                return;
            }
            if (!CNMode)
            {
                PrintLog("Export to: " + outputDirectory, true);
                PrintLog("It may take a long time to extract all the files you selected which depends on the file size and amount you selected.");
            }
            else {
                PrintLog("解包至: " + outputDirectory, true);
                PrintLog("根据你所选取的文件数量和大小，这可能会花费很长时间，请耐心等待");
            }
            int failed = 0;
            if (mergeChecked) chunkMap.FirstOrDefault().Value.ExtractSelected(fileNodeList, outputDirectory, this);
            else failed = mainChunk.ExtractSelected(fileNodeList, outputDirectory, this);
            if (failed > 0) {
                if (!CNMode) PrintLog($"{failed} files failed to extract in total.");
                else PrintLog($"总计{failed}个文件提取失败");
            }
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ExtractBtn.IsEnabled = true;
                RegExCheckBox.IsEnabled = true;
                FilterBox.IsEnabled = true;
                PauseBtn.Visibility = Visibility.Hidden;
                TerminateBtn.Visibility = Visibility.Hidden;
                terminateFlag = false;
                pauseFlag = false;
            }));
            if (!CNMode) PrintLog("Finished!");
            else PrintLog("提取完成！");
        }

        // To analyze the chunkN.bin
        private void analyze(string filename)
        {
            if (!File.Exists(filename)) {
                if (!CNMode) PrintLog("Error: file does not exist.");
                else PrintLog("错误：文件不存在");
                return; }

            try
            {
                int inputFileMagic;
                using (BinaryReader Reader = new BinaryReader(File.OpenRead(filename))) inputFileMagic = Reader.ReadInt32();
                if (inputFileMagic == Chunk.MagicChunk)
                {
                    if (!CNMode) PrintLog("Chunk detected，now analyzing...", true);
                    else PrintLog("检测到chunk文件，正在解析...", true);
                    if (mergeChecked) {
                        if (!CNMode) PrintLog("Merge mode on. The program will merge all the chunk files.");
                        else PrintLog("联合解析已开启，程序将整合所有chunkN.bin文件");
                    }
                    if (!File.Exists($"{AppDomain.CurrentDomain.BaseDirectory}\\oo2core_8_win64.dll")) {
                        if (!CNMode) PrintLog("Error: oo2core_8_win64.dll not found. Download the file from elsewhere to the executable folder.");
                        else PrintLog("错误：未找到oo2core_8_win64.dll，请从其他地方下载该文件至本程序文件夹");
                        return;
                    }
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        MergeCheckBox.IsEnabled = false;
                    }));
                    if (mergeChecked)
                    {
                        FileInfo chosenChunkFileInfo = new FileInfo(filename);
                        string[] chunkFiles = Directory.GetFiles(chosenChunkFileInfo.DirectoryName, "chunkG*.bin");
                        Array.Sort(chunkFiles, (a, b) => int.Parse(Regex.Replace(a, "[^0-9]", "")) - int.Parse(Regex.Replace(b, "[^0-9]", "")));
                        foreach (string fileNameEach in chunkFiles)
                        {
                            Chunk cur_chunk = new Chunk();
                            fileNodeList = cur_chunk.AnalyzeChunk(fileNameEach, this, fileNodeList);
                            chunkMap.Add(fileNameEach, cur_chunk);
                        }
                        if (fileNodeList.Count > 0)
                        {
                            fileNodeList[0].sortChildren();
                        }
                    }
                    else {
                        mainChunk = new Chunk();
                        fileNodeList = mainChunk.AnalyzeChunk(filename, this, fileNodeList);
                    }
                    if (!CNMode) PrintLog("Analyzation finished.");
                    else PrintLog("解析完成");
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        this.AllowDrop = false;
                        maingrid.AllowDrop = false;
                        ExtractBtn.IsEnabled = true;
                        FileTree.ItemsSource = fileNodeList;
                        FilterBox.IsEnabled = true;
                        ApplyFilterBtn.IsEnabled = true;
                    }));
                }
            }
            catch (Exception e)
            {
                if (!CNMode) PrintLog("Error information is as follows:");
                else PrintLog("错误信息如下：");
                PrintLog(e.Message);
                return;
            }
        }

        // Print log to window
        public void PrintLog(string log, bool clear = false, bool checkError = true)
        {
            if (checkError) {
                PrintErrorInfo();
            }
            if (!clear) Dispatcher.BeginInvoke(new Action(() =>
            {
                LogBox.Text += (log + "\n");
                LogBox.ScrollToEnd();
            }));
            else
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    LogBox.Text = (log + "\n");
                    LogBox.ScrollToEnd();
                }));
            }
        }

        // Update progress bar
        public void SetProgressbar(int value, int total)
        {
            if (total == 0) return;
            if (value > total) value = total;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (total == 0)MainProgressBar.Value = 0;
                else MainProgressBar.Value = value * 100 / total;
            }));
        }

        public void SetProgressbarMax(int maxValue)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                MainProgressBar.Maximum = maxValue;
            }));
        }

        // Update progress bar
        public void updateExtractProgress(int updateValue = 1)
        { 
            extractProgress = extractProgress + updateValue >= totalProgress ? totalProgress : extractProgress + updateValue;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                MainProgressBar.Value = extractProgress;
            }));
        }

        private void ExtractBtn_Click(object sender, RoutedEventArgs e)
        {
            // Select the output directory
            if (outputDirectory.Equals(""))
            {
                FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
                if (!CNMode) folderBrowserDialog.Description = "Select the path where you want to export the files to and the program will create a chunkN directory.";
                else folderBrowserDialog.Description = "选择你想要解包至的文件夹，程序将在该目录自动创建一个chunkN文件夹";
                if (folderBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    if (mergeChecked) { outputDirectory = folderBrowserDialog.SelectedPath + "\\chunk"; }
                    else outputDirectory = folderBrowserDialog.SelectedPath + "\\" + System.IO.Path.GetFileNameWithoutExtension(chunkFileName);
                }
                else
                {
                    if (!CNMode) PrintLog("Canceled.");
                    else PrintLog("已取消");
                    return;
                }
            }
            ExtractBtn.IsEnabled = false;
            RegExCheckBox.IsEnabled = false;
            FilterBox.IsEnabled = false;
            ApplyFilterBtn.IsEnabled = false;
            PauseBtn.Visibility = Visibility.Visible;
            TerminateBtn.Visibility = Visibility.Visible;
            extractWorker.RunWorkerAsync();
        }

        private void MergeCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            mergeChecked = true;
        }

        private void MergeCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            mergeChecked = false;
        }

        private void RegExCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            regexEnabled = (bool)RegExCheckBox.IsChecked;
        }

        private void FilterBox_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            filterText = FilterBox.Text;
            if (e.Key == System.Windows.Input.Key.Enter) {
                ApplyFilterBtn_Click(sender, e);
            }
        }

        private void FileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            FileNode selectedNode = (FileNode)FileTree.SelectedItem;
            BasicInfoBox.Text = (selectedNode).getPreviewInfo();
            if (previewEnabled) {
                if (selectedNode.IsFile && selectedNode.Name.EndsWith(".tex")&& PreviewTex(selectedNode))
                {
                    SetAllPreviewInvisible();
                    PreviewUnsupportedInfoLabel.Visibility = Visibility.Hidden;
                    PreviewImage.Visibility = Visibility.Visible;
                }
                else {
                    SetAllPreviewInvisible();
                }
            }
        }

        public bool PreviewTex(FileNode texNode) {
            try
            {
                byte[] texdata = null;
                if (mergeChecked) texdata = ((Chunk)chunkMap.First().Value).GetFileData(texNode);
                else texdata = mainChunk.GetFileData(texNode);
                Bitmap pic = texPreviewer.GetPic(texdata);
                if (pic == null) return false;
                texStream.Seek(0, SeekOrigin.Begin);
                texStream.SetLength(0);
                pic.Save(texStream, ImageFormat.Png);
                ImageBrush imageBrush = new ImageBrush();
                ImageSourceConverter imageSourceConverter = new ImageSourceConverter();
                imageBrush.ImageSource = (ImageSource)imageSourceConverter.ConvertFrom(texStream);
                PreviewImage.Source = imageBrush.ImageSource;
                return true;
            }
            catch (Exception ex)
            {
                if (!CNMode) PrintLog("Error occured while previewing.");
                else PrintLog("预览时发生错误");
                Console.WriteLine(ex);
                texPreviewer = new TexPreviewer();
                return false;
            }
        }

        private void SetAllPreviewInvisible() {
            PreviewImage.Visibility = Visibility.Hidden;
            PreviewUnsupportedInfoLabel.Visibility = Visibility.Visible;
        }

        private void PreviewCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            previewEnabled = PreviewCheckbox.IsChecked == null? false:(bool)PreviewCheckbox.IsChecked;
            if (!previewEnabled) SetAllPreviewInvisible();
            PreviewUnsupportedInfoLabel.Visibility = Visibility.Hidden;
        }
        
        private void ApplyFilterBtn_Click(object sender, RoutedEventArgs e)
        {
            if (fileNodeList.Count > 0)
            {
                if (filterText != "")
                {
                    if (regexEnabled)
                    {
                        filterRegex = new Regex(filterText);
                        fileNodeList[0].filterChildren(filterRegex);
                    }
                    else fileNodeList[0].filterChildren(filterText);
                }
                else {
                    fileNodeList[0].resetVisibility();
                }
                FileTree.Focus();
            }
        }

        private void PauseBtn_Click(object sender, RoutedEventArgs e)
        {
            pauseFlag = !pauseFlag;
            if (PauseFlag)
            {
                PauseBtn.Background = System.Windows.Media.Brushes.Green;
                PauseBtn.Content = CNMode?"恢复":"Resume";
            }
            else {
                PauseBtn.Background = System.Windows.Media.Brushes.Orange;
                PauseBtn.Content = CNMode?"暂停":"Pause";
            }
        }

        private void TerminateBtn_Click(object sender, RoutedEventArgs e)
        {
            terminateFlag = true;
        }

        public Chunk GetChunk(string chunkfile) {
            if(chunkMap.ContainsKey(chunkfile))return chunkMap[chunkfile];
            return mainChunk;
        }
    }
}
