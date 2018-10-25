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
            }
            if (total_progress == 0)
            {
                printlog("Nothing selected.");
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ExtractBtn.IsEnabled = true;
                }));
                return;
            }

            printlog("Output to: " + output_directory);
            printlog("It may take a long time to extract according to the ability of your computer and the size of files you selected. Please wait patiently.");
            Chunk.ExtractSelected(itemlist, output_directory, this);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ExtractBtn.IsEnabled = true;
            }));
            printlog("Extract succeeded!");
        }

        // To analyze the chunkN.bin
        private void analyze(string filename)
        {
            if (!File.Exists(filename)) { printlog("Error：file not exists."); return; }

            try
            {
                using (BinaryReader Reader = new BinaryReader(File.Open(filename, FileMode.Open))) MagicInputFile = Reader.ReadInt32();
                if (MagicInputFile == MagicChunk)
                {
                    printlog("Chunk file detected. Analyzing...", true);
                    if (!File.Exists($"{AppDomain.CurrentDomain.BaseDirectory}\\oo2core_5_win64.dll")) { printlog("Error: oo2core_5_win64.dll not found."); Console.Read(); return; }

                    itemlist = Chunk.AnalyzeChunk(filename, this);
                    printlog("Analyze finished.");
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
                printlog("Error as follows:");
                printlog(e.Message);
                return;
            }
        }

        // Print log to window
        public void printlog(string log, bool clear = false)
        {
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

        // Update progress bar
        public void updateExtractProgress()
        {
            extract_progress = extract_progress >= total_progress ? total_progress : extract_progress + 1;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                progressbar.Value = extract_progress * 100 / total_progress;
            }));
        }

        private void ExtractBtn_Click(object sender, RoutedEventArgs e)
        {
            // Select the output directory
            if (output_directory.Equals(""))
            {
                FolderBrowserDialog fbd = new FolderBrowserDialog();
                fbd.Description = "Select the directory you want to export to:";

                if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    output_directory = fbd.SelectedPath + "\\" + System.IO.Path.GetFileNameWithoutExtension(chunkfilename);
                }
                else
                {
                    printlog("Aborted.");
                    return;
                }
            }
            ExtractBtn.IsEnabled = false;
            extractworker.RunWorkerAsync();
        }
    }
}
