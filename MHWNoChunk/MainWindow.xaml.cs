using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Win32;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;

namespace MHWNoChunk
{
    public partial class MainWindow : Window
    {
        public static Stack<string> errors = new Stack<string>();
        public static bool CNMode = false;
        public static bool regexEnabled = false;
        public static string filterText = "";
        public static bool filterEnabled = false;
        public static Regex filterRegex = null;
        private string chunkfilename;
        static int MagicChunk = 0x00504D43;
        int MagicInputFile;
        BackgroundWorker analyzeworker, extractworker;
        List<FileNode> itemlist = new List<FileNode>();
        string output_directory = "";
        int extract_progress = 0;
        int total_progress = 0;
        Chunk mainChunk;
        bool CombineChecked = false;
        Dictionary<string, Chunk> chunkMap = new Dictionary<string, Chunk>();
        TexPreviewer texPreviewer = new TexPreviewer();
        MemoryStream texStream = new MemoryStream();
        bool enablePreview = false;
        static string oo2core8Md5_1 = "9b7f9e3e4931b80257da5e1b626db43a";
        static string oo2core8Md5_2 = "c1a0dd317543035221327d44f07c3d06";
        static string oo2core7Md5 = "b486c6f46a3d802966d04911a619b2ed";

        public MainWindow()
        {
            InitializeComponent();
            PreviewUnsupportedInfoLabel.Visibility = Visibility.Hidden;
            analyzeworker = new BackgroundWorker();
            analyzeworker.WorkerSupportsCancellation = true;
            analyzeworker.DoWork += new DoWorkEventHandler(DoAnalyzeHandler);
            extractworker = new BackgroundWorker();
            extractworker.WorkerSupportsCancellation = true;
            extractworker.DoWork += new DoWorkEventHandler(DoExtractHandler);
            initChinese();
            printlog("");
            checkFilesExist();
            checkDllVersion();
            printErrorInfo();
        }

        private void initChinese() {
            if (CNMode)
            {
                this.Title = "MHW部分解包器 v2.2.0 By Jodo @ 狩技MOD组";
                LogBox.Text = "拖拽chunkN.bin至上方空白区域以开始。如果想要一次性解析全部chunk0-chunkN.bin，请先勾选右侧的联合解析全部Chunk。本程序根据 WorldChunkTool by MHVuze的原理制作: https://github.com/mhvuze/WorldChunkTool";
                CombineCheckBox.Content = "联合解析全部Chunk";
                ExtractBtn.Content = "提取所选文件";
                FilterLabel.Content = "筛选:";
                RegExCheckBox.Content = "正则表达式";
                BasicInfoLabel.Content = "基本信息";
                PreviewCheckbox.Content = "启用预览";
                PreviewUnsupportedInfoLabel.Content = "暂不支持该格式文件预览";
            }
        }

        public void checkFilesExist() {
            string[] filesRequired = { "file.png", "dir.png", "chunk.key", "oo2core_8_win64.dll", "SharpGL.dll" };
            foreach (string fileRequired in filesRequired) {
                if (!File.Exists(fileRequired)) { if(!CNMode)errors.Push($"Error: {fileRequired} not found in the executable folder.");
                else errors.Push($"错误：{fileRequired}未找到");
                }
            }
        }

        public void checkDllVersion() {
            if (File.Exists("oo2core_8_win64.dll")) {
                string curMd5 = CalculateMD5("oo2core_8_win64.dll");
                if (!curMd5.Equals(oo2core8Md5_1) && !curMd5.Equals(oo2core8Md5_2) && !curMd5.Equals(oo2core7Md5))
                {
                    if (!CNMode) printlog("Error: oo2core_8_win64.dll found but version not matched. Please get this .dll file from somewhere else.");
                    else { printlog("错误：oo2core_8_win64.dll校验失败，请从另一渠道获取该文件"); }
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

        public void printErrorInfo()
        {
            while (errors.Count > 0)
            {
                printlog(errors.Pop(),false,false);
            }
        }

        private void item_SelectedItemChanged(object sender, RoutedEventArgs e)
        {
            TreeViewItem item = (TreeViewItem)sender;
        }

        private void Grid_Drop(object sender, System.Windows.DragEventArgs e)
        {
            chunkfilename = ((System.Array)e.Data.GetData(System.Windows.DataFormats.FileDrop)).GetValue(0).ToString();
            Console.WriteLine(chunkfilename);
            analyzeworker.RunWorkerAsync();

        }
        private void DoAnalyzeHandler(object sender, DoWorkEventArgs e)
        {
            analyze(chunkfilename);
        }

        private void DoExtractHandler(object sender, DoWorkEventArgs e)
        {
            extract_progress = 0;
            if (itemlist.Count > 0)
            {
                FileNode rootnode = itemlist[0];
                total_progress = rootnode.getSelectedCount();
                setProgressbarMax(total_progress);
                setProgressbar(0, total_progress);
            }
            if (total_progress == 0)
            {
                if (!CNMode) printlog("Nothing selected.");
                else printlog("未选择任何文件");
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ExtractBtn.IsEnabled = true;
                    RegExCheckBox.IsEnabled = true;
                    FilterBox.IsEnabled = true;
                }));
                return;
            }
            if (!CNMode)
            {
                printlog("Export to: " + output_directory, true);
                printlog("It may take a long time to extract all the files you selected which depends on the file size and amount you selected.");
            }
            else {
                printlog("解包至: " + output_directory, true);
                printlog("根据你所选取的文件数量和大小，这可能会花费很长时间，请耐心等待");
            }
            int failed = 0;
            if (filterText != "")
            {
                filterEnabled = true;
                if (regexEnabled) filterRegex = new Regex(filterText);
                else filterRegex = null;
            }
            else filterEnabled = false;
            if (CombineChecked) chunkMap.FirstOrDefault().Value.ExtractSelected(itemlist, output_directory, this);
            else failed = mainChunk.ExtractSelected(itemlist, output_directory, this);
            if (failed > 0) {
                if (!CNMode) printlog($"{failed} files failed to extract in total.");
                else printlog($"总计{failed}个文件提取失败");
            }
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ExtractBtn.IsEnabled = true;
                RegExCheckBox.IsEnabled = true;
                FilterBox.IsEnabled = true;
            }));
            if (!CNMode) printlog("Finished!");
            else printlog("提取完成！");
        }

        // To analyze the chunkN.bin
        private void analyze(string filename)
        {
            if (!File.Exists(filename)) {
                if (!CNMode) printlog("Error: file does not exist.");
                else printlog("错误：文件不存在");
                return; }

            try
            {
                using (BinaryReader Reader = new BinaryReader(File.OpenRead(filename))) MagicInputFile = Reader.ReadInt32();
                if (MagicInputFile == MagicChunk)
                {
                    if (!CNMode) printlog("Chunk detected，now analyzing...", true);
                    else printlog("检测到chunk文件，正在解析...", true);
                    if (CombineChecked) {
                        if (!CNMode) printlog("Merge mode on. The program will merge all the chunk files.");
                        else printlog("联合解析已开启，程序将整合所有chunkN.bin文件");
                    }
                    if (!File.Exists($"{AppDomain.CurrentDomain.BaseDirectory}\\oo2core_8_win64.dll")) {
                        if (!CNMode) printlog("Error: oo2core_8_win64.dll not found. Download the file from elsewhere to the executable folder.");
                        else printlog("错误：未找到oo2core_8_win64.dll，请从其他地方下载该文件至本程序文件夹");
                        return;
                    }
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        CombineCheckBox.IsEnabled = false;
                    }));
                    if (CombineChecked)
                    {
                        FileInfo chosenChunkFileInfo = new FileInfo(filename);
                        string[] chunkfiles = Directory.GetFiles(chosenChunkFileInfo.DirectoryName, "chunkG*.bin");
                        Array.Sort(chunkfiles, (a, b) => int.Parse(Regex.Replace(a, "[^0-9]", "")) - int.Parse(Regex.Replace(b, "[^0-9]", "")));
                        foreach (string filenameEach in chunkfiles)
                        {
                            Chunk cur_chunk = new Chunk();
                            itemlist = cur_chunk.AnalyzeChunk(filenameEach, this, itemlist);
                            chunkMap.Add(filenameEach, cur_chunk);
                        }
                    }
                    else {
                        mainChunk = new Chunk();
                        itemlist = mainChunk.AnalyzeChunk(filename, this, itemlist);
                    }
                    if (!CNMode) printlog("Analyzation finished.");
                    else printlog("解析完成");
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        this.AllowDrop = false;
                        maingrid.AllowDrop = false;
                        ExtractBtn.IsEnabled = true;
                        FileTree.ItemsSource = itemlist;
                    }));
                }
            }
            catch (Exception e)
            {
                if (!CNMode) printlog("Error info is as follows:");
                else printlog("错误信息如下：");
                printlog(e.Message);
                return;
            }
        }

        // Print log to window
        public void printlog(string log, bool clear = false, bool checkError = true)
        {
            if (checkError) {
                printErrorInfo();
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
        public void setProgressbar(int value, int total)
        {
            if (value > total) value = total;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                progressbar.Value = value * 100 / total;
            }));
        }

        public void setProgressbarMax(int maxValue)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                progressbar.Maximum = maxValue;
            }));
        }

        // Update progress bar
        public void updateExtractProgress(int updateValue = 1)
        {
            extract_progress = extract_progress + updateValue >= total_progress ? total_progress : extract_progress + updateValue;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                progressbar.Value = extract_progress;
            }));
        }

        private void ExtractBtn_Click(object sender, RoutedEventArgs e)
        {
            // Select the output directory
            if (output_directory.Equals(""))
            {
                FolderBrowserDialog fbd = new FolderBrowserDialog();
                if (!CNMode) fbd.Description = "Select the path where you want to export the files to and the program will create a chunkN directory.";
                else fbd.Description = "选择你想要解包至的文件夹，程序将在该目录自动创建一个chunkN文件夹";
                if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    if (CombineChecked) { output_directory = fbd.SelectedPath + "\\chunk"; }
                    else output_directory = fbd.SelectedPath + "\\" + System.IO.Path.GetFileNameWithoutExtension(chunkfilename);
                }
                else
                {
                    if (!CNMode) printlog("Canceled.");
                    else printlog("已取消");
                    return;
                }
            }
            ExtractBtn.IsEnabled = false;
            RegExCheckBox.IsEnabled = false;
            FilterBox.IsEnabled = false;
            extractworker.RunWorkerAsync();
        }

        private void CombineCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            CombineChecked = true;
        }

        private void CombineCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            CombineChecked = false;
        }

        private void RegExCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            regexEnabled = (bool)RegExCheckBox.IsChecked;
        }

        private void FilterBox_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            filterText = FilterBox.Text;
        }

        private void FileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            FileNode selectedNode = (FileNode)FileTree.SelectedItem;
            BasicInfoBox.Text = (selectedNode).getPreviewInfo();
            if (enablePreview) {
                if (selectedNode.IsFile && selectedNode.Name.EndsWith(".tex")&& previewTex(selectedNode))
                {
                    setAllPreviewInvisible();
                    PreviewUnsupportedInfoLabel.Visibility = Visibility.Hidden;
                    PreviewImage.Visibility = Visibility.Visible;
                }
                else {
                    setAllPreviewInvisible();
                }
            }
            
        }

        public bool previewTex(FileNode texNode) {
            try
            {
                byte[] texdata = null;
                if (CombineChecked) texdata = ((Chunk)chunkMap.First().Value).getFileData(texNode);
                else texdata = mainChunk.getFileData(texNode);
                Bitmap pic = texPreviewer.getPic(texdata);
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
                if (!CNMode) printlog("Error occured while previewing.");
                else printlog("预览时发生错误");
                Console.WriteLine(ex);
                texPreviewer = new TexPreviewer();
                return false;
            }
        }

        private void setAllPreviewInvisible() {
            PreviewImage.Visibility = Visibility.Hidden;
            PreviewUnsupportedInfoLabel.Visibility = Visibility.Visible;
        }

        private void PreviewCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            enablePreview = PreviewCheckbox.IsChecked == null? false:(bool)PreviewCheckbox.IsChecked;
            if (!enablePreview) setAllPreviewInvisible();
            PreviewUnsupportedInfoLabel.Visibility = Visibility.Hidden;
        }

        public Chunk getChunk(string chunkfile) {
            if(chunkMap.ContainsKey(chunkfile))return chunkMap[chunkfile];
            return mainChunk;
        }
    }
}
