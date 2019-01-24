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
    public partial class MainWindow : Window
    {
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
                setProgressbarMax(total_progress);
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

            printlog("Export to: " + output_directory);
            printlog("It may take a long time to extract all the files you selected which depends on the file size and count you selected.");
            int failed = 0;
            if(CombineChecked) chunkMap.FirstOrDefault().Value.ExtractSelected(itemlist, output_directory, this);
            else failed = mainChunk.ExtractSelected(itemlist, output_directory, this);
            if (failed > 0) {
                printlog($"{failed} files failed to extract in total.");
            }
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ExtractBtn.IsEnabled = true;
            }));
            printlog("Finished!");
        }

        // To analyze the chunkN.bin
        private void analyze(string filename)
        {
            if (!File.Exists(filename)) { printlog("Error: file does not exist."); return; }

            try
            {
                using (BinaryReader Reader = new BinaryReader(File.Open(filename, FileMode.Open))) MagicInputFile = Reader.ReadInt32();
                if (MagicInputFile == MagicChunk)
                {
                    printlog("Chunk detected，now analyzing...", true);
                    if (CombineChecked) {
                        printlog("Combine mode on. The program will combine all the chunk files.");
                    }
                    if (!File.Exists($"{AppDomain.CurrentDomain.BaseDirectory}\\oo2core_5_win64.dll")) { printlog("Error: oo2core_5_win64.dll not found. Copy the file from root path where your MHW game install at."); Console.Read(); return; }
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        CombineCheckBox.IsEnabled = false;
                    }));
                    if (CombineChecked)
                    {
                        FileInfo chosenChunkFileInfo = new FileInfo(filename);
                        string[] chunkfiles = Directory.GetFiles(chosenChunkFileInfo.DirectoryName, "chunk*.bin");
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
                printlog("Error info is as follows:");
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
                fbd.Description = "Select the path where you want to export the files to and the program will create a chunkN directory.";

                if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    if (CombineChecked) { output_directory = fbd.SelectedPath + "\\chunk"; }
                    else output_directory = fbd.SelectedPath + "\\" + System.IO.Path.GetFileNameWithoutExtension(chunkfilename);
                }
                else
                {
                    printlog("Canceled.");
                    return;
                }
            }
            ExtractBtn.IsEnabled = false;
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

        public Chunk getChunk(string chunkfile) {
            if(chunkMap.ContainsKey(chunkfile))return chunkMap[chunkfile];
            return mainChunk;
        }
    }
}
