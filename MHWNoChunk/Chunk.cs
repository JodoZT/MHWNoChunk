using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MHWNoChunk
{
    public class Chunk
    {
        Dictionary<long, long> MetaChunk;
        Dictionary<int, long> ChunkOffsetDict;
        BinaryReader Reader;
        byte[] ChunkDecompressed, NextChunkDecompressed;
        int cur_pointer = 0;
        int cur_index = 0;
        int DictCount = 0;
        string fileinput;
        Dictionary<int, byte[]> ChunkCache;

        // Learns from WorldChunkTool by MHVuze https://github.com/mhvuze/WorldChunkTool
        public List<FileNode> AnalyzeChunk(String FileInput, MainWindow mainwindow, List<FileNode> inputFileList)
        {
            fileinput = FileInput;
            FileInfo fileinputInfo = new FileInfo(fileinput);
            if(!MainWindow.CNMode)mainwindow.printlog($"Now analyzing {fileinputInfo.Name}");
            else mainwindow.printlog($"正在解析 {fileinputInfo.Name}");
            ChunkCache = new Dictionary<int, byte[]>();

            List<FileNode> filelist = inputFileList;
            MetaChunk = new Dictionary<long, long>();
            ChunkOffsetDict = new Dictionary<int, long>();
            string NamePKG = $"{Environment.CurrentDirectory}\\{Path.GetFileNameWithoutExtension(FileInput)}.pkg";
            Reader = new BinaryReader(File.Open(FileInput, FileMode.Open));

            // Read header
            Reader.BaseStream.Seek(4, SeekOrigin.Begin);
            int ChunkCount = Reader.ReadInt32(); int ChunkPadding = ChunkCount.ToString().Length;
            if (!MainWindow.CNMode) mainwindow.printlog($"{ChunkCount} subchunks detected.");
            else mainwindow.printlog($"解析到{ChunkCount}个子chunk ");

            // Read file list
            DictCount = 0;
            for (int i = 0; i < ChunkCount; i++)
            {
                // Process file size
                byte[] ArrayTmp1 = new byte[8];
                byte[] ArrayChunkSize = Reader.ReadBytes(3);
                int High = ArrayChunkSize[0] >> 4;
                ArrayChunkSize[0] = BitConverter.GetBytes(High)[0];
                Array.Copy(ArrayChunkSize, ArrayTmp1, ArrayChunkSize.Length);
                long ChunkSize = BitConverter.ToInt64(ArrayTmp1, 0);
                // Fixes the original code's error on ChunkSize
                ChunkSize = (ChunkSize >> 4) + (ChunkSize & 0xF);

                // Process offset
                byte[] ArrayTmp2 = new byte[8];
                byte[] ArrayChunkOffset = Reader.ReadBytes(5);
                Array.Copy(ArrayChunkOffset, ArrayTmp2, ArrayChunkOffset.Length);
                long ChunkOffset = BitConverter.ToInt64(ArrayTmp2, 0);

                MetaChunk.Add(ChunkOffset, ChunkSize);
                ChunkOffsetDict.Add(i, ChunkOffset);
                DictCount = i + 1;
            }

            cur_index = 0;
            long cur_offset = ChunkOffsetDict[cur_index];
            long cur_size = MetaChunk[cur_offset];

            ChunkDecompressed = getDecompressedChunk(cur_offset, cur_size, Reader);
            if (cur_index + 1 < DictCount)
            {
                NextChunkDecompressed = getDecompressedChunk(ChunkOffsetDict[cur_index + 1], MetaChunk[ChunkOffsetDict[cur_index + 1]], Reader);
            }
            else
            {
                NextChunkDecompressed = new byte[0];
            }
            cur_pointer = 0x0C;
            int TotalParentCount = BitConverter.ToInt32(ChunkDecompressed, cur_pointer);
            cur_pointer += 4;
            int TotalChildrenCount = BitConverter.ToInt32(ChunkDecompressed, cur_pointer);
            cur_pointer = 0x100;
            FileNode root_node = null;
            for (int i = 0; i < TotalParentCount; i++)
            {
                string StringNameParent = getName(0x3C);
                long FileSize = getInt64();
                long FileOffset = getInt64();
                int EntryType = getInt32();
                int CountChildren = getInt32();

                if (filelist.Count == 0)
                {
                    root_node = new FileNode(StringNameParent, false, FileInput);
                    root_node.EntireName = root_node.Name;
                    filelist.Add(root_node);
                }
                else
                {
                    root_node = filelist[0];
                    root_node.FromChunk = fileinput;
                    root_node.FromChunkName = $"({System.IO.Path.GetFileNameWithoutExtension(fileinput)})";
                }
                for (int j = 0; j < CountChildren; j++)
                {
                    int origin_pointer = cur_pointer;
                    int origin_loc = cur_index;
                    if (!ChunkCache.ContainsKey(cur_index)) ChunkCache.Add(cur_index, ChunkDecompressed);
                    if (!ChunkCache.ContainsKey(cur_index + 1)) ChunkCache.Add(cur_index + 1, NextChunkDecompressed);

                    string StringNameChild = getName(0xA0);
                    FileSize = getInt64();
                    FileOffset = getInt64();
                    EntryType = getInt32();
                    int Unknown = getInt32();

                    if (EntryType == 0x02)
                    {
                        cur_pointer = origin_pointer;
                        if (cur_index != origin_loc)
                        {
                            cur_index = origin_loc;
                            ChunkDecompressed = ChunkCache[cur_index];
                            NextChunkDecompressed = ChunkCache[cur_index + 1];
                            ChunkCache.Remove(cur_index);
                            ChunkCache.Remove(cur_index + 1);
                        }
                        StringNameChild = getName(0x50);
                        getOnLength(0x68, new byte[0x68], 0);
                    }
                    string[] fathernodes = StringNameChild.Split('\\');
                    bool isFile = false;
                    if (EntryType == 0x02 || EntryType == 0x00) isFile = true;
                    FileNode child_node = new FileNode(fathernodes[fathernodes.Length - 1], isFile, FileInput);
                    if (isFile)
                    {
                        child_node.Size = FileSize;
                        child_node.Offset = FileOffset;
                        child_node.ChunkIndex = (int)(FileOffset / 0x40000);
                        child_node.ChunkPointer = (int)(FileOffset % 0x40000);
                    }
                    child_node.EntireName = StringNameChild;
                    FileNode target_node = root_node;
                    foreach (string node_name in fathernodes)
                    {
                        if (node_name.Equals("")) continue;
                        foreach (FileNode node in target_node.Childern)
                        {
                            if (node.Name == node_name)
                            {
                                if (node.Name == child_node.Name) { break; }
                                target_node = node;
                                break;
                            }
                        }
                    }
                    bool need_add = true;
                    foreach (FileNode tmp_node in target_node.Childern)
                    {
                        if (tmp_node.Name == child_node.Name)
                        {
                            if (child_node.IsFile) target_node.Childern.Remove(tmp_node);
                            else
                            {
                                tmp_node.FromChunk = child_node.FromChunk; tmp_node.FromChunkName = child_node.FromChunkName;
                                need_add = false;
                            }
                            break;
                        }
                    }
                    if (need_add) target_node.Childern.Add(child_node);
                }
                mainwindow.setProgressbar(i + 1, TotalParentCount);
            }
            ChunkCache.Clear();
            if (filelist.Count > 0)
            {
                filelist[0].getSize();
            }
            return filelist;
        }

        //Extact function
        public int ExtractSelected(List<FileNode> itemlist, string BaseLocation, MainWindow mainWindow)
        {
            int failed = 0;
            foreach (FileNode node in itemlist)
            {
                try
                {
                    if (node.Childern.Count > 0)
                    {
                        failed += ExtractSelected(node.Childern, BaseLocation, mainWindow);
                    }
                    else if (node.IsSelected)
                    {
                        Chunk CurNodeChunk = mainWindow.getChunk(node.FromChunk);
                        CurNodeChunk.cur_index = node.ChunkIndex;
                        CurNodeChunk.cur_pointer = node.ChunkPointer;
                        long size = node.Size;
                        if (CurNodeChunk.ChunkCache.ContainsKey(CurNodeChunk.cur_index))
                        {
                            CurNodeChunk.ChunkDecompressed = CurNodeChunk.ChunkCache[CurNodeChunk.cur_index];
                        }
                        else
                        {
                            if (CurNodeChunk.ChunkCache.Count > 20) CurNodeChunk.ChunkCache.Clear();
                            CurNodeChunk.ChunkDecompressed = CurNodeChunk.getDecompressedChunk(CurNodeChunk.ChunkOffsetDict[CurNodeChunk.cur_index], CurNodeChunk.MetaChunk[CurNodeChunk.ChunkOffsetDict[CurNodeChunk.cur_index]], CurNodeChunk.Reader);
                            CurNodeChunk.ChunkCache.Add(CurNodeChunk.cur_index, CurNodeChunk.ChunkDecompressed);
                        }
                        if (CurNodeChunk.ChunkCache.ContainsKey(CurNodeChunk.cur_index + 1))
                        {
                            CurNodeChunk.NextChunkDecompressed = CurNodeChunk.ChunkCache[CurNodeChunk.cur_index + 1];
                        }
                        else
                        {
                            if (CurNodeChunk.ChunkCache.Count > 20) CurNodeChunk.ChunkCache.Clear();
                            if (CurNodeChunk.cur_index + 1 < CurNodeChunk.DictCount) { CurNodeChunk.NextChunkDecompressed = CurNodeChunk.getDecompressedChunk(CurNodeChunk.ChunkOffsetDict[CurNodeChunk.cur_index + 1], CurNodeChunk.MetaChunk[CurNodeChunk.ChunkOffsetDict[CurNodeChunk.cur_index + 1]], CurNodeChunk.Reader); }
                            else { CurNodeChunk.NextChunkDecompressed = new byte[0]; }
                            CurNodeChunk.ChunkCache.Add(CurNodeChunk.cur_index + 1, CurNodeChunk.NextChunkDecompressed);
                        }
                        if (!node.IsFile) new FileInfo(BaseLocation + node.EntireName + "\\").Directory.Create();
                        else new FileInfo(BaseLocation + node.EntireName).Directory.Create();
                        if (node.IsFile)
                        {
                            File.WriteAllBytes(BaseLocation + node.EntireName, CurNodeChunk.getOnLength(size, new byte[size], 0));
                            mainWindow.updateExtractProgress();
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!MainWindow.CNMode) mainWindow.printlog($"Error occured while extracting {node.EntireName}{node.FromChunkName}, skipped. Please try again later.");
                    else mainWindow.printlog($"提取 {node.EntireName}{node.FromChunkName} 时发生错误，已跳过，请稍后重试");
                    Console.WriteLine(ex.StackTrace);
                    failed += 1;
                }
            }
            return failed;
        }

        //To get decompressed chunk
        private byte[] getDecompressedChunk(long offset, long size, BinaryReader reader)
        {

            if (size != 0)
            {
                reader.BaseStream.Seek(offset, SeekOrigin.Begin);
                byte[] ChunkCompressed = reader.ReadBytes((int)size); // Unsafe cast
                return Utils.Decompress(ChunkCompressed, ChunkCompressed.Length, 0x40000);
            }
            else
            {
                reader.BaseStream.Seek(offset, SeekOrigin.Begin);
                return reader.ReadBytes(0x40000);
            }
        }

        //To read an ASCII string from chunk bytes
        private string getName(int targetlength)
        {
            return Convert.ToString(System.Text.Encoding.ASCII.GetString(getOnLength(targetlength, new byte[targetlength], 0).Where(b => b != 0x00).ToArray()));
        }

        //To read int64 from chunk bytes
        private long getInt64()
        {
            return BitConverter.ToInt64(getOnLength(8, new byte[8], 0), 0);
        }

        //To read int64 from chunk bytes
        private int getInt32()
        {
            return BitConverter.ToInt32(getOnLength(4, new byte[4], 0), 0);
        }

        //To read a byte array at length of targetlength
        private byte[] getOnLength(long targetlength, byte[] tmp, long startAddr)
        {
            if (cur_pointer + targetlength < 0x40000)
            {
                Array.Copy(ChunkDecompressed, cur_pointer, tmp, startAddr, targetlength);
                cur_pointer += (int)targetlength;
            }
            else
            {
                int tmp_can_read_length = 0x40000 - cur_pointer;
                long tmp_remain_length = targetlength - tmp_can_read_length;
                Array.Copy(ChunkDecompressed, cur_pointer, tmp, startAddr, tmp_can_read_length);
                cur_pointer = 0;
                ChunkDecompressed = NextChunkDecompressed;
                cur_index += 1;
                if (cur_index + 1 < DictCount) { NextChunkDecompressed = getDecompressedChunk(ChunkOffsetDict[cur_index + 1], MetaChunk[ChunkOffsetDict[cur_index + 1]], Reader); }
                else
                {
                    NextChunkDecompressed = new byte[0];
                }
                getOnLength(tmp_remain_length, tmp, startAddr + tmp_can_read_length);
            }
            return tmp;
        }
    }
}
