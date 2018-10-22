using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MHWNoChunk
{
    class Chunk
    {
        static Dictionary<long, long> MetaChunk;
        static Dictionary<int, long> ChunkOffsetDict;
        static BinaryReader Reader;
        static byte[] ChunkDecompressed, NextChunkDecompressed;
        static int cur_pointer = 0;
        static int cur_loc = 0;
        static int DictCount = 0;
        static string fileinput;
        static Dictionary<int, byte[]> ChunkCache;

        public static List<FileNode> AnalyzeChunk(String FileInput, MainWindow mainwindow)
        {
            fileinput = FileInput;
            ChunkCache = new Dictionary<int, byte[]>();
            List<FileNode> filelist = new List<FileNode>();
            MetaChunk = new Dictionary<long, long>();
            ChunkOffsetDict = new Dictionary<int, long>();
            string NamePKG = $"{Environment.CurrentDirectory}\\{Path.GetFileNameWithoutExtension(FileInput)}.pkg";
            Reader = new BinaryReader(File.Open(FileInput, FileMode.Open));

            // Key = ChunkOffset, Value = ChunkSize

            // Read header
            Reader.BaseStream.Seek(4, SeekOrigin.Begin);
            int ChunkCount = Reader.ReadInt32(); int ChunkPadding = ChunkCount.ToString().Length;
            mainwindow.printlog($"识别到{ChunkCount}个子chunk");

            // Read file list
            DictCount = 0;
            for (int i = 0; i < ChunkCount; i++)
            {
                // Process file size
                byte[] ArrayTmp1 = new byte[8];
                byte[] ArrayChunkSize = Reader.ReadBytes(3);
                //int Low = ArrayChunkSize[0] & 0x0F;
                int High = ArrayChunkSize[0] >> 4;
                ArrayChunkSize[0] = BitConverter.GetBytes(High)[0];
                Array.Copy(ArrayChunkSize, ArrayTmp1, ArrayChunkSize.Length);
                long ChunkSize = BitConverter.ToInt64(ArrayTmp1, 0);
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

            // Write decompressed chunks to pkg
            //BinaryWriter Writer = new BinaryWriter(File.Create(NamePKG));
            
            //StreamWriter LogWriter = new StreamWriter($"{Path.GetFileNameWithoutExtension(FileInput)}.csv", false);
            //LogWriter.WriteLine("Chunkid, Offset, CompressedSize, DecompressedSize");
            cur_loc = 0;
            long cur_offset = ChunkOffsetDict[cur_loc];
            long cur_size = MetaChunk[cur_offset];
            
            ChunkDecompressed = getDecompressedChunk(cur_offset, cur_size, Reader);
            if (cur_loc + 1 < DictCount)
            {
                NextChunkDecompressed = getDecompressedChunk(ChunkOffsetDict[cur_loc + 1], MetaChunk[ChunkOffsetDict[cur_loc + 1]], Reader);
            }
            else {
                NextChunkDecompressed = new byte[0];
            }
            cur_pointer = 0x0C;
            int TotalParentCount = BitConverter.ToInt32(ChunkDecompressed, cur_pointer);
            cur_pointer += 4;
            int TotalChildrenCount = BitConverter.ToInt32(ChunkDecompressed, cur_pointer);
            cur_pointer = 0x100;
            FileNode root_node = null;
            for (int i = 0; i < TotalParentCount; i++) {
                string StringNameParent = getName(0x3C);
                long FileSize = getInt64();
                long FileOffset = getInt64();
                int EntryType = getInt32();
                int CountChildren = getInt32();
                
                if (i == 0) {
                    root_node = new FileNode(StringNameParent, false);
                    root_node.EntireName = root_node.Name;
                    filelist.Add(root_node);
                }
                for (int j = 0; j < CountChildren; j++)
                {
                    int origin_pointer = cur_pointer;
                    int origin_loc = cur_loc;
                    if (!ChunkCache.ContainsKey(cur_loc)) ChunkCache.Add(cur_loc, ChunkDecompressed);
                    if (!ChunkCache.ContainsKey(cur_loc + 1)) ChunkCache.Add(cur_loc + 1, NextChunkDecompressed);


                    string StringNameChild = getName(0xA0);
                    FileSize = getInt64();
                    FileOffset = getInt64();
                    EntryType = getInt32();
                    int Unknown = getInt32();

                    if (EntryType == 0x02)
                    {
                        cur_pointer = origin_pointer;
                        if (cur_loc != origin_loc) {cur_loc = origin_loc;
                            ChunkDecompressed = ChunkCache[cur_loc];
                            NextChunkDecompressed = ChunkCache[cur_loc + 1];
                            ChunkCache.Remove(cur_loc);
                            ChunkCache.Remove(cur_loc + 1);
                        }
                        StringNameChild = getName(0x50);
                        getOnLength(0x68);
                    }
                    string[] fathernodes = StringNameChild.Split('\\');
                    bool isFile = false;
                    if (EntryType == 0x02 || EntryType == 0x00) isFile = true;
                    FileNode child_node = new FileNode(fathernodes[fathernodes.Length - 1], isFile);
                    if (isFile) {
                        child_node.Size = FileSize;
                        child_node.Offset = FileOffset;
                        child_node.ChunkLoc = (int)(FileOffset / 0x40000);
                        child_node.ChunkPointer = (int)(FileOffset % 0x40000);
                    }
                    child_node.EntireName = StringNameChild;
                    FileNode target_node = root_node;
                    foreach (string node_name in fathernodes) {
                        if (node_name.Equals("")) continue;
                        foreach (FileNode node in target_node.Childern) {
                            if (node.Name == node_name) {
                                target_node = node;
                                break;
                            }
                        }
                    }
                    target_node.Childern.Add(child_node);
                }
            }
            ChunkCache.Clear();
            return filelist;
        }

        public static void ExtractSelected(List<FileNode> itemlist, string BaseLocation, MainWindow mainWindow) {
            foreach (FileNode node in itemlist) {
                if (node.Childern.Count > 0)
                {
                    ExtractSelected(node.Childern, BaseLocation, mainWindow);
                }
                else if (node.IsSelected) {
                    cur_loc = node.ChunkLoc;
                    cur_pointer = node.ChunkPointer;
                    long size = node.Size;
                    if (ChunkCache.ContainsKey(cur_loc))
                    {
                        ChunkDecompressed = ChunkCache[cur_loc];
                    }
                    else {
                        if (ChunkCache.Count > 100) ChunkCache.Clear();
                        ChunkDecompressed = getDecompressedChunk(ChunkOffsetDict[cur_loc], MetaChunk[ChunkOffsetDict[cur_loc]], Reader);
                        ChunkCache.Add(cur_loc, ChunkDecompressed);
                    }
                    if (ChunkCache.ContainsKey(cur_loc + 1))
                    {
                        NextChunkDecompressed = ChunkCache[cur_loc + 1];
                    }
                    else
                    {
                        if (ChunkCache.Count > 100) ChunkCache.Clear();
                        if (cur_loc + 1 < DictCount) { NextChunkDecompressed = getDecompressedChunk(ChunkOffsetDict[cur_loc + 1], MetaChunk[ChunkOffsetDict[cur_loc + 1]], Reader); }
                        else { NextChunkDecompressed = new byte[0]; }
                        ChunkCache.Add(cur_loc + 1, NextChunkDecompressed);
                    }
                    if(!node.IsFile) new FileInfo(BaseLocation + node.EntireName + "\\").Directory.Create();
                    else new FileInfo(BaseLocation + node.EntireName).Directory.Create();
                    if(node.IsFile)File.WriteAllBytes(BaseLocation + node.EntireName, getOnLength(size));
                    //mainWindow.printlog("已输出：" + BaseLocation + node.EntireName);
                }
            }
        }

        private static byte[] getDecompressedChunk(long offset, long size, BinaryReader reader) {
            
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

        private static string getName(int targetlength) {
            return Convert.ToString(System.Text.Encoding.ASCII.GetString(getOnLength(targetlength).Where(b => b != 0x00).ToArray()));
        }

        private static long getInt64() {
            return BitConverter.ToInt64(getOnLength(8), 0);
        }

        private static int getInt32()
        {
            return BitConverter.ToInt32(getOnLength(4), 0);
        }
        
        private static byte[] getOnLength(long targetlength) {
            byte[] tmp = new byte[targetlength];
            if (cur_pointer + targetlength < 0x40000)
            {
                Array.Copy(ChunkDecompressed, cur_pointer, tmp, 0, targetlength);
                cur_pointer += (int)targetlength;
            }
            else {
                int tmp_target_short = 0x40000 - cur_pointer;
                long tmp_targetlength = targetlength - tmp_target_short;
                Array.Copy(ChunkDecompressed, cur_pointer, tmp, 0, tmp_target_short);
                cur_pointer = 0;
                ChunkDecompressed = NextChunkDecompressed;
                cur_loc += 1;
                if (cur_loc + 1 < DictCount) { NextChunkDecompressed = getDecompressedChunk(ChunkOffsetDict[cur_loc + 1], MetaChunk[ChunkOffsetDict[cur_loc + 1]], Reader); }
                else
                {
                    NextChunkDecompressed = new byte[0];
                }
                byte[] tmpmore = getOnLength(tmp_targetlength);
                Array.Copy(tmpmore,0,tmp, tmp_target_short, tmp_targetlength);
            }
            return tmp;
        }
    }
}
