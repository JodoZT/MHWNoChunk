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

namespace MHWNoChunk
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private string chunkfilename;
        static int MagicChunk = 0x00504D43;
        static int MagicPKG = 0x20474B50;
        int MagicInputFile;
        BackgroundWorker analyzeworker, extractworker;
        List<FileNode> itemlist;
        string output_directory = "";
        int extract_progress = 0;
        int total_progress = 0;
        
        public MainWindow()
        {
            InitializeComponent();
            analyzeworker = new BackgroundWorker();
            analyzeworker.WorkerSupportsCancellation = true;
            analyzeworker.DoWork += new DoWorkEventHandler(DoAnalyzeHandler);
            extractworker = new BackgroundWorker();
            extractworker.WorkerSupportsCancellation = true;
            extractworker.DoWork += new DoWorkEventHandler(DoExtractHandler);
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
        private void DoAnalyzeHandler(object sender, DoWorkEventArgs e) {
            analyze(chunkfilename);
        }

        private void DoExtractHandler(object sender, DoWorkEventArgs e)
        {
            extract_progress = 0;
            if (itemlist.Count > 0)
            {
                FileNode rootnode = itemlist[0];
                total_progress = rootnode.getSelectedCount();
            }
            if (total_progress == 0) { printlog("未选择任何文件");
                Dispatcher.BeginInvoke(new Action(() => {
                    ExtractBtn.IsEnabled = true;
                }));
                return;
            }

            printlog("正在输出至: " + output_directory);
            printlog("根据您的电脑状况及选择解包的文件大小，可能会出现长时间未响应的情况，请耐心等待。");
            Chunk.ExtractSelected(itemlist, output_directory,this);
            Dispatcher.BeginInvoke(new Action(() => {
                ExtractBtn.IsEnabled = true;
            }));
            printlog("解包完成");
        }

        private void analyze(string filename) {
            if (!File.Exists(filename)) { printlog("错误：文件不存在。"); return; }

            try
            {
                using (BinaryReader Reader = new BinaryReader(File.Open(filename, FileMode.Open))) MagicInputFile = Reader.ReadInt32();
                if (MagicInputFile == MagicChunk)
                {
                    printlog("已识别chunk文件，正在解析，请耐心等耐...", true);
                    if (!File.Exists($"{AppDomain.CurrentDomain.BaseDirectory}\\oo2core_5_win64.dll")) { printlog("错误：缺少oo2core_5_win64.dll"); Console.Read(); return; }

                    itemlist = Chunk.AnalyzeChunk(filename, this);
                    printlog("解析完成");
                    Dispatcher.BeginInvoke(new Action(()=> {
                        this.AllowDrop = false;
                        maingrid.AllowDrop = false;
                        ExtractBtn.IsEnabled = true;
                        FileTree.ItemsSource = itemlist;
                    }));
                }
            }
            catch (Exception e)
            {
                printlog("错误：错误内容如下");
                printlog(e.Message);
                return;
            }
        }

        public void printlog(string log, bool clear = false) {
            if (!clear) Dispatcher.BeginInvoke(new Action(() =>
            {
                LogBox.Text += (log + "\n");
                LogBox.ScrollToEnd();
            }));
            else {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    LogBox.Text = (log + "\n");
                    LogBox.ScrollToEnd();
                }));
            }
            
        }

        public void setProgressbar(int value, int total) {
            if (value > total) value = total;
            Dispatcher.BeginInvoke(new Action(()=> {
                progressbar.Value = value * 100 / total;
            }));
        }

        public void addExtractProgress() {
            extract_progress = extract_progress >= total_progress ? total_progress : extract_progress + 1;
            Dispatcher.BeginInvoke(new Action(() => {
                progressbar.Value = extract_progress * 100 / total_progress;
            }));
        }

        private void ExtractBtn_Click(object sender, RoutedEventArgs e)
        {
            if (output_directory.Equals("")) {FolderBrowserDialog fbd = new FolderBrowserDialog();
            fbd.Description = "请选择解包输出文件夹";

            if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                output_directory = fbd.SelectedPath + "\\" + System.IO.Path.GetFileNameWithoutExtension(chunkfilename);
            }
            else
            {
                printlog("已放弃解包");
                return;
            } }
            ExtractBtn.IsEnabled = false;
            extractworker.RunWorkerAsync();
        }
    }
}
