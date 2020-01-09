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
            long totalChunkSize = 0;
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
                totalChunkSize += ChunkSize;

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

            ChunkDecompressed = getDecompressedChunk(cur_offset, cur_size, Reader, cur_index);
            if (cur_index + 1 < DictCount)
            {
                NextChunkDecompressed = getDecompressedChunk(ChunkOffsetDict[cur_index + 1], MetaChunk[ChunkOffsetDict[cur_index + 1]], Reader, cur_index + 1);
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
                            CurNodeChunk.ChunkDecompressed = CurNodeChunk.getDecompressedChunk(CurNodeChunk.ChunkOffsetDict[CurNodeChunk.cur_index], CurNodeChunk.MetaChunk[CurNodeChunk.ChunkOffsetDict[CurNodeChunk.cur_index]], CurNodeChunk.Reader, CurNodeChunk.cur_index);
                            CurNodeChunk.ChunkCache.Add(CurNodeChunk.cur_index, CurNodeChunk.ChunkDecompressed);
                        }
                        if (CurNodeChunk.ChunkCache.ContainsKey(CurNodeChunk.cur_index + 1))
                        {
                            CurNodeChunk.NextChunkDecompressed = CurNodeChunk.ChunkCache[CurNodeChunk.cur_index + 1];
                        }
                        else
                        {
                            if (CurNodeChunk.ChunkCache.Count > 20) CurNodeChunk.ChunkCache.Clear();
                            if (CurNodeChunk.cur_index + 1 < CurNodeChunk.DictCount) { CurNodeChunk.NextChunkDecompressed = CurNodeChunk.getDecompressedChunk(CurNodeChunk.ChunkOffsetDict[CurNodeChunk.cur_index + 1], CurNodeChunk.MetaChunk[CurNodeChunk.ChunkOffsetDict[CurNodeChunk.cur_index + 1]], CurNodeChunk.Reader, CurNodeChunk.cur_index + 1); }
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
        private byte[] getDecompressedChunk(long offset, long size, BinaryReader reader, int chunkNum)
        {

            if (size != 0)
            {
                reader.BaseStream.Seek(offset, SeekOrigin.Begin);
                byte[] ChunkCompressed = reader.ReadBytes((int)size); // Unsafe cast
                return DecryptChunk(Utils.Decompress(ChunkCompressed, ChunkCompressed.Length, 0x40000), GetChunkKey(chunkNum));
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
                if (cur_index + 1 < DictCount) { NextChunkDecompressed = getDecompressedChunk(ChunkOffsetDict[cur_index + 1], MetaChunk[ChunkOffsetDict[cur_index + 1]], Reader, cur_index + 1); }
                else
                {
                    NextChunkDecompressed = new byte[0];
                }
                getOnLength(tmp_remain_length, tmp, startAddr + tmp_can_read_length);
            }
            return tmp;
        }

        // Get right chunk encryption key for iteration. Copy from WorldChunkTool.
        public static byte[] GetChunkKey(int i)
        {
            List<byte[]> chunkKeys = new List<byte[]>
            {
                // 1f6d31c883dd716d7e8f598ce23f1929
                new byte[] { 0x1f, 0x6d, 0x31, 0xc8, 0x83, 0xdd, 0x71, 0x6d, 0x7e, 0x8f, 0x59, 0x8c, 0xe2, 0x3f, 0x19, 0x29 },
                // 25099c1f911a26e5ce9172f07a82c80a
                new byte[] { 0x25, 0x09, 0x9c, 0x1f, 0x91, 0x1a, 0x26, 0xe5, 0xce, 0x91, 0x72, 0xf0, 0x7a, 0x82, 0xc8, 0x0a },
                // 41d055b3dd6015167e8f598ce23f1929
                new byte[] { 0x41, 0xd0, 0x55, 0xb3, 0xdd, 0x60, 0x15, 0x16, 0x7e, 0x8f, 0x59, 0x8c, 0xe2, 0x3f, 0x19, 0x29 },
                // 4bb0de04e4e0856980ccb2942f9ce9f9
                new byte[] { 0x4b, 0xb0, 0xde, 0x04, 0xe4, 0xe0, 0x85, 0x69, 0x80, 0xcc, 0xb2, 0x94, 0x2f, 0x9c, 0xe9, 0xf9 },
                // 6bb5c956e44d00bc305233cfbfaafa25
                new byte[] { 0x6b, 0xb5, 0xc9, 0x56, 0xe4, 0x4d, 0x00, 0xbc, 0x30, 0x52, 0x33, 0xcf, 0xbf, 0xaa, 0xfa, 0x25 },
                // 7eb268373b5d361ed6d313e2933c4dcb
                new byte[] { 0x7e, 0xb2, 0x68, 0x37, 0x3b, 0x5d, 0x36, 0x1e, 0xd6, 0xd3, 0x13, 0xe2, 0x93, 0x3c, 0x4d, 0xcb },
                // 82a43b2108797c6a440089a2ceddcee9
                new byte[] { 0x82, 0xa4, 0x3b, 0x21, 0x08, 0x79, 0x7c, 0x6a, 0x44, 0x00, 0x89, 0xa2, 0xce, 0xdd, 0xce, 0xe9 },
                // 8bce54dc4c11139a7875bd63bfaafa25
                new byte[] { 0x8b, 0xce, 0x54, 0xdc, 0x4c, 0x11, 0x13, 0x9a, 0x78, 0x75, 0xbd, 0x63, 0xbf, 0xaa, 0xfa, 0x25 },
                // 8f021dccb0f2787206fbdee2390bbb5c
                new byte[] { 0x8f, 0x02, 0x1d, 0xcc, 0xb0, 0xf2, 0x78, 0x72, 0x06, 0xfb, 0xde, 0xe2, 0x39, 0x0b, 0xbb, 0x5c },
                // a1c7d2ea661895ac7875bd63bfaafa25
                new byte[] { 0xa1, 0xc7, 0xd2, 0xea, 0x66, 0x18, 0x95, 0xac, 0x78, 0x75, 0xbd, 0x63, 0xbf, 0xaa, 0xfa, 0x25 },
                // a492fc9033949c15a033ac223735cca7
                new byte[] { 0xa4, 0x92, 0xfc, 0x90, 0x33, 0x94, 0x9c, 0x15, 0xa0, 0x33, 0xac, 0x22, 0x37, 0x35, 0xcc, 0xa7 },
                // ac76cb97ec7500133a81038e7a82c80a
                new byte[] { 0xac, 0x76, 0xcb, 0x97, 0xec, 0x75, 0x00, 0x13, 0x3a, 0x81, 0x03, 0x8e, 0x7a, 0x82, 0xc8, 0x0a },
                // d1d29d7446d4fdf1a033ac223735cca7
                new byte[] { 0xd1, 0xd2, 0x9d, 0x74, 0x46, 0xd4, 0xfd, 0xf1, 0xa0, 0x33, 0xac, 0x22, 0x37, 0x35, 0xcc, 0xa7 },
                // da5c1e531d8359157875bd63bfaafa25
                new byte[] { 0xda, 0x5c, 0x1e, 0x53, 0x1d, 0x83, 0x59, 0x15, 0x78, 0x75, 0xbd, 0x63, 0xbf, 0xaa, 0xfa, 0x25 },
                // e4662c709c753a039a2c0f5ae23f1929
                new byte[] { 0xe4, 0x66, 0x2c, 0x70, 0x9c, 0x75, 0x3a, 0x03, 0x9a, 0x2c, 0x0f, 0x5a, 0xe2, 0x3f, 0x19, 0x29 },
                // ec13345966ce7312440089a2ceddcee9
                new byte[] { 0xec, 0x13, 0x34, 0x59, 0x66, 0xce, 0x73, 0x12, 0x44, 0x00, 0x89, 0xa2, 0xce, 0xdd, 0xce, 0xe9 }
            };

            List<int> chunkKeyPattern = new List<int> { 11, 4, 8, 13, 0, 3, 7, 11, 15, 14, 10, 1, 4, 12, 13, 5, 3, 7, 9, 15, 12, 13, 0, 3, 7, 9, 15, 14, 10, 1, 6, 12, 13, 5, 3, 2, 9, 15, 8, 13, 5, 3, 7, 9, 15, 8, 10, 1, 6, 12, 11, 5, 3, 2, 9, 4, 8, 10, 5, 3, 2, 9, 15, 8, 10, 0, 6, 12, 11, 5, 14, 2, 9, 4, 8, 13, 0, 3, 2, 9, 4, 8, 13, 0, 6, 7, 11, 15, 14, 2, 1, 4, 12, 13, 0, 3, 2, 1, 4, 8, 13, 0, 3, 7, 11, 15, 14, 10, 1, 4, 12, 13, 5, 3, 7, 1, 4, 12, 13, 0, 3, 7, 9, 15, 14, 10, 1, 6, 12, 13, 5, 3, 2, 9, 4, 12, 13, 5, 3, 7, 9, 15, 8, 10, 1, 6, 12, 11, 5, 3, 2, 9, 4, 12, 13, 5, 3, 2, 9, 4, 8, 10, 0, 6, 7, 11, 5, 14, 2, 1, 4, 8, 11, 5, 14, 2, 9, 4, 8, 13, 0, 6, 7, 11, 15, 14, 2, 1, 4, 12, 13, 5, 14, 2, 1, 4, 8, 13, 0, 3, 7, 11, 15, 14, 10, 1, 4, 12, 13, 5, 3, 2, 1, 4, 12, 13, 0, 3, 7, 9, 15, 14, 10, 1, 6, 12, 13, 5, 3, 2, 1, 4, 12, 13, 5, 3, 2, 9, 15, 8, 10, 0, 6, 12, 11, 5, 14, 2, 9, 6, 12, 11, 5, 3, 2, 9, 4, 8, 10, 0, 6, 7, 11, 5, 14, 2, 1, 4, 12, 11, 5, 14, 2, 9, 4, 8, 13, 0, 6, 7, 11, 15, 14, 2, 1, 4, 12, 11, 5, 14, 2, 1, 4, 8, 13, 0, 3, 7, 11, 15, 14, 10, 1, 4, 12, 13, 15, 14, 2, 1, 4, 12, 13, 0, 3, 7, 9, 15, 8, 10, 1, 6, 12, 11, 5, 14, 10, 1, 6, 12, 13, 5, 3, 2, 9, 15, 8, 10, 0, 6, 12, 11, 5, 14, 10, 1, 6, 12, 11, 5, 3, 2, 9, 4, 8, 10, 0, 6, 7, 11, 5, 14, 2, 0, 6, 12, 11, 5, 14, 2, 9, 4, 8, 13, 0, 6, 7, 11, 15, 14, 2, 1, 6, 7, 11, 5, 14, 2, 1, 4, 8, 13, 0, 3, 7, 9, 15, 14, 10, 1, 6, 12, 11, 15, 14, 10, 1, 4, 12, 13, 5, 3, 7, 9, 15, 8, 10, 1, 6, 12, 11, 15, 14, 10, 1, 6, 12, 13, 5, 3, 2, 9, 15, 8, 10, 0, 6, 12, 11, 5, 8, 10, 1, 6, 12, 11, 5, 3, 2, 9, 4, 8, 10, 0, 6, 7, 11, 5, 14, 10, 0, 6, 12, 11, 5, 14, 2, 9, 4, 8, 13, 0, 3, 7, 11, 15, 14, 10, 0, 6, 7, 11, 15, 14, 2, 1, 4, 12, 13, 0, 3, 7, 9, 15, 14, 10, 1, 3, 7, 11, 15, 14, 10, 1, 4, 12, 13, 5, 3, 7, 9, 15, 8, 10, 1, 6, 7, 9, 15, 14, 10, 1, 6, 12, 13, 5, 3, 2, 9, 15, 8, 10, 0, 6, 12, 9, 15, 8, 10, 1, 6, 12, 11, 5, 3, 2, 9, 4, 8, 10, 0, 6, 7, 11, 4, 8, 10, 0, 6, 7, 11, 5, 14, 2, 1, 4, 8, 13, 0, 3, 7, 11, 15, 8, 13, 0, 6, 7, 11, 15, 14, 2, 1, 4, 12, 13, 0, 3, 7, 9, 15, 14, 13, 0, 3, 7, 11, 15, 14, 10, 1, 4, 12, 13, 5, 3, 7, 9, 15, 8, 10, 1, 3, 7, 9, 15, 14, 10, 1, 6, 12, 13, 5, 3, 2, 9, 15, 8, 10, 0, 6, 2, 9, 15, 8, 10, 0, 6, 12, 11, 5, 14, 2, 9, 4, 8, 13, 0, 6, 7, 9, 4, 8, 10, 0, 6, 7, 11, 5, 14, 2, 1, 4, 8, 13, 0, 3, 7, 11, 4, 8, 13, 0, 6, 7, 11, 15, 14, 2, 1, 4, 12, 13, 0, 3, 7, 9, 15, 8, 13, 0, 3, 7, 11, 15, 14, 10, 1, 4, 12, 13, 5, 3, 7, 9, 15, 8, 13, 5, 3, 7, 9, 15, 8, 10, 1, 6, 12, 11, 5, 3, 2, 9, 4, 8, 10, 5, 3, 2, 9, 15, 8, 10, 0, 6, 12, 11, 5, 14, 2, 9, 4, 8, 13, 0, 3, 2, 9, 4, 8, 10, 0, 6, 7, 11, 5, 14, 2, 1, 4, 8, 13, 0, 3, 2, 9, 4, 8, 13, 0, 6, 7, 11, 15, 14, 2, 1, 4, 12, 13, 0, 3, 7, 1, 4, 12, 13, 0, 3, 7, 9, 15, 14, 10, 1, 6, 12, 13, 5, 3, 2, 9, 15, 12, 13, 5, 3, 7, 9, 15, 8, 10, 1, 6, 12, 11, 5, 3, 2, 9, 4, 8, 13, 5, 3, 2, 9, 15, 8, 10, 0, 6, 12, 11, 5, 14, 2, 9, 4, 8, 13, 5, 3, 2, 9, 4, 8, 10, 0, 6, 7, 11, 5, 14, 2, 1, 4, 8, 13, 0, 14, 2, 9, 4, 8, 13, 0, 3, 7, 11, 15, 14, 10, 1, 4, 12, 13, 5, 3, 2, 1, 4, 12, 13, 0, 3, 7, 9, 15, 14, 10, 1, 6, 12, 13, 5, 3, 2, 1, 4, 12, 13, 5, 3, 7, 9, 15, 8, 10, 1, 6, 12, 11, 5, 3, 2, 9, 6, 12, 13, 5, 3, 2, 9, 15, 8, 10, 0, 6, 12, 11, 5, 14, 2, 9, 4, 12, 11, 5, 3, 2, 9, 4, 8, 13, 0, 6, 7, 11, 15, 14, 2, 1, 4, 12, 11, 5, 14, 2, 1, 4, 8, 13, 0, 3, 7, 11, 15, 14, 10, 1, 4, 12, 13, 15, 14, 2, 1, 4, 12, 13, 0, 3, 7, 9, 15, 14, 10, 1, 6, 12, 13, 5, 14, 10, 1, 4, 12, 13, 5, 3, 7, 9, 15, 8, 10, 1, 6, 12, 11, 5, 3, 2, 1, 6, 12, 13, 5, 3, 2, 9, 4, 8, 10, 0, 6, 7, 11, 5, 14, 2, 1, 6, 12, 11, 5, 14, 2, 9, 4, 8, 13, 0, 6, 7, 11, 15, 14, 2, 1, 4, 7, 11, 5, 14, 2, 1, 4, 8, 13, 0, 3, 7, 11, 15, 14, 10, 1, 4, 12, 11, 15, 14, 2, 1, 4, 12, 13, 0, 3, 7, 9, 15, 14, 10, 1, 6, 12, 13, 15, 14, 10, 1, 4, 12, 13, 5, 3, 7, 9, 15, 8, 10, 0, 6, 12, 11, 5, 8, 10, 1, 6, 12, 11, 5, 3, 2, 9, 4, 8, 10, 0, 6, 7, 11, 5, 14, 10, 0, 6, 12, 11, 5, 14, 2, 9, 4, 8, 13, 0, 6, 7, 11, 15, 14, 2, 0, 6, 7, 11, 5, 14, 2, 1, 4, 8, 13, 0, 3, 7, 11, 15, 14, 10, 1, 6, 7, 11, 15, 14, 2, 1, 4, 12, 13, 0, 3, 7, 9, 15, 8, 10, 1, 6, 7, 9, 15, 14, 10, 1, 6, 12, 13, 5, 3, 2, 9, 15, 8, 10, 0, 6, 12, 11, 15, 8, 10, 1, 6, 12, 11, 5, 3, 2, 9, 4, 8, 10, 0, 6, 7, 11, 5, 8, 10, 0, 6, 12, 11, 5, 14, 2, 9, 4, 8, 13, 0, 6, 7, 11, 15, 14, 10, 0, 6, 7, 11, 5, 14, 2, 1, 4, 8, 13, 0, 3, 7, 9, 15, 14, 10, 0, 3, 7, 11, 15, 14, 10, 1, 4, 12, 13, 5, 3, 7, 9, 15, 8, 10, 1, 3, 7, 9, 15, 14, 10, 1, 6, 12, 13, 5, 3, 2, 9, 15, 8, 10, 0, 6, 7, 9, 15, 8, 10, 1, 6, 12, 11, 5, 3, 2, 9, 4, 8, 10, 0, 6, 7, 9, 15, 8, 10, 0, 6, 12, 11, 5, 14, 2, 9, 4, 8, 13, 0, 6, 7, 11, 4, 8, 13, 0, 6, 7, 11, 15, 14, 2, 1, 4, 12, 13, 0, 3, 7, 9, 15, 8, 13, 0, 3, 7, 11, 15, 14, 10, 1, 4, 12, 13, 5, 3, 7, 9, 15, 8, 13, 0, 3, 7, 9, 15, 14, 10, 1, 6, 12, 13, 5, 3, 2, 9, 15, 8, 10, 5, 3, 7, 9, 15, 8, 10, 1, 6, 12, 11, 5, 3, 2, 9, 4, 8, 10, 0, 6, 2, 9, 4, 8, 10, 0, 6, 7, 11, 5, 14, 2, 1, 4, 8, 13, 0, 3, 7, 9, 4, 8, 13, 0, 6, 7, 11, 15, 14, 2, 1, 4, 12, 13, 0, 3, 7, 9, 4, 8, 13, 0, 3, 7, 11, 15, 14, 10, 1, 4, 12, 13, 5, 3, 7, 9, 15, 12, 13, 0, 3, 7, 9, 15, 14, 10, 1, 6, 12, 13, 5, 3, 2, 9, 15, 8, 13, 5, 3, 2, 9, 15, 8, 10, 0, 6, 12, 11, 5, 14, 2, 9, 4, 8, 13, 5, 3, 2, 9, 4, 8, 10, 0, 6, 7, 11, 5, 14, 2, 1, 4, 8, 13, 0, 14, 2, 9, 4, 8, 13, 0, 6, 7, 11, 15, 14, 2, 1, 4, 12, 13, 0, 3, 2, 1, 4, 8, 13, 0, 3, 7, 11, 15, 14, 10, 1, 4, 12, 13, 5, 3, 7, 1, 4, 12, 13, 5, 3, 7, 9, 15, 8, 10, 1, 6, 12, 11, 5, 3, 2, 9, 6, 12, 13, 5, 3, 2, 9, 15, 8, 10, 0, 6, 12, 11, 5, 14, 2, 9, 4, 8, 11, 5, 3, 2, 9, 4, 8, 10, 0, 6, 7, 11, 5, 14, 2, 1, 4, 8, 13, 5, 14, 2, 9, 4, 8, 13, 0, 6, 7, 11, 15, 14, 2, 1, 4, 12, 13, 0, 14, 2, 1, 4, 8, 13, 0, 3, 7, 9, 15, 14, 10, 1, 6, 12, 13, 5, 3, 10, 1, 4, 12, 13, 5, 3, 7, 9, 15, 8, 10, 1, 6, 12, 11, 5, 3, 2, 1, 6, 12, 13, 5, 3, 2, 9, 15, 8, 10, 0, 6, 12, 11, 5, 14, 2, 9, 6, 12, 11, 5, 3, 2, 9, 4, 8, 10, 0, 6, 7, 11, 5, 14, 2, 1, 4, 12, 11, 5, 14, 2, 9, 4, 8, 13, 0, 3, 7, 11, 15, 14, 10, 1, 4, 12, 11, 15, 14, 2, 1, 4, 12, 13, 0, 3, 7, 9, 15, 14, 10, 1, 6, 12, 13, 15, 14, 10, 1, 4, 12, 13, 5, 3, 7, 9, 15, 8, 10, 1, 6, 12, 11, 5, 14, 10, 1, 6, 12, 13, 5, 3, 2, 9, 15, 8, 10, 0, 6, 12, 11, 5, 14, 10, 1, 6, 12, 11, 5, 3, 2, 9, 4, 8, 13, 0, 6, 7, 11, 15, 14, 2, 1, 6, 7, 11, 5, 14, 2, 1, 4, 8, 13, 0, 3, 7, 11, 15, 14, 10, 1, 4, 7, 11, 15, 14, 2, 1, 4, 12, 13, 0, 3, 7, 9, 15, 14, 10, 1, 6, 12, 11, 15, 14, 10, 1, 4, 12, 13, 5, 3, 7, 9, 15, 8, 10, 1, 6, 12, 11, 15, 14, 10, 1, 6, 12, 13, 5, 3, 2, 9, 15, 8, 10, 0, 6, 7, 11, 5, 8, 10, 0, 6, 12, 11, 5, 14, 2, 9, 4, 8, 13, 0, 6, 7, 11, 15, 14, 10, 0, 6, 7, 11, 5, 14, 2, 1, 4, 8, 13, 0, 3, 7, 11, 15, 14, 10, 0, 6, 7, 11, 15, 14, 2, 1, 4, 12, 13, 0, 3, 7, 9, 15, 14, 10, 1, 3, 7, 11, 15, 14, 10, 1, 4, 12, 13, 5, 3, 7, 9, 15, 8, 10, 0, 6, 7, 9, 15, 8, 10, 1, 6, 12, 11, 5, 3, 2, 9, 4, 8, 10, 0, 6, 7, 9, 15, 8, 10, 0, 6, 12, 11, 5, 14, 2, 9, 4, 8, 13, 0, 6, 7, 11, 15, 8, 10, 0, 6, 7, 11, 5, 14, 2, 1, 4, 8, 13, 0, 3, 7, 11, 15, 14, 13, 0, 6, 7, 11, 15, 14, 2, 1, 4, 12, 13, 0, 3, 7, 9, 15, 8, 10, 0, 3, 7, 9, 15, 14, 10, 1, 6, 12, 13, 5, 3, 2, 9, 15 };
            int keyPos = chunkKeyPattern[i];
            byte[] chunkKey = chunkKeys[keyPos];
            return chunkKey;
        }


        // Decrypt Iceborne PKG chunks. Copy from WorldChunkTool.
        public static byte[] DecryptChunk(byte[] data, byte[] chunkKey)
        {
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(chunkKey[i % chunkKey.Length] ^ data[i]);
            }
            return data;
        }
    }
}
