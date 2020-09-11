using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace MHWNoChunk
{
    public class Chunk
    {
        public static int MagicChunk = 0x00504D43;

        Dictionary<int, byte[]> chunkCache;
        Dictionary<long, long> metaChunk;
        Dictionary<int, long> chunkOffsetDict;
        BinaryReader reader;
        byte[] curChunkDecompressed, nextChunkDecompressed;
        int curPointer = 0;
        int curIndex = 0;
        int dictCount = 0;
        string inputFilePath;
        MainWindow bindingWindow = null;


        // Learns from WorldChunkTool by MHVuze https://github.com/mhvuze/WorldChunkTool
        public List<FileNode> AnalyzeChunk(String inputFile, MainWindow mainWindow, List<FileNode> inputFileNodeList)
        {
            bindingWindow = mainWindow;
            inputFilePath = inputFile;
            FileInfo inputFileInfo = new FileInfo(inputFilePath);
            if(!MainWindow.CNMode)mainWindow.PrintLog($"Now analyzing {inputFileInfo.Name}");
            else mainWindow.PrintLog($"正在解析 {inputFileInfo.Name}");
            chunkCache = new Dictionary<int, byte[]>();

            List<FileNode> fileNodeList = inputFileNodeList;
            metaChunk = new Dictionary<long, long>();
            chunkOffsetDict = new Dictionary<int, long>();
            string NamePKG = $"{Environment.CurrentDirectory}\\{Path.GetFileNameWithoutExtension(inputFile)}.pkg";
            reader = new BinaryReader(File.OpenRead(inputFile));

            // Read header
            if (reader.ReadInt32() != MagicChunk) {
                reader.Close();
                return inputFileNodeList;
            }
            reader.BaseStream.Seek(4, SeekOrigin.Begin);
            int ChunkCount = reader.ReadInt32(); int ChunkPadding = ChunkCount.ToString().Length;
            if (!MainWindow.CNMode) mainWindow.PrintLog($"{ChunkCount} subchunks detected.");
            else mainWindow.PrintLog($"解析到{ChunkCount}个子chunk ");

            // Read file list
            dictCount = 0;
            long totalChunkSize = 0;
            for (int i = 0; i < ChunkCount; i++)
            {
                // Process file size
                byte[] arrayTmp1 = new byte[8];
                byte[] arrayChunkSize = reader.ReadBytes(3);
                int high = arrayChunkSize[0] >> 4;
                arrayChunkSize[0] = BitConverter.GetBytes(high)[0];
                Array.Copy(arrayChunkSize, arrayTmp1, arrayChunkSize.Length);
                long chunkSize = BitConverter.ToInt64(arrayTmp1, 0);
                // Fixes the original code's error on ChunkSize
                chunkSize = (chunkSize >> 4) + (chunkSize & 0xF);
                totalChunkSize += chunkSize;

                // Process offset
                byte[] arrayTmp2 = new byte[8];
                byte[] arrayChunkOffset = reader.ReadBytes(5);
                Array.Copy(arrayChunkOffset, arrayTmp2, arrayChunkOffset.Length);
                long chunkOffset = BitConverter.ToInt64(arrayTmp2, 0);

                metaChunk.Add(chunkOffset, chunkSize);
                chunkOffsetDict.Add(i, chunkOffset);
                dictCount = i + 1;
            }

            curIndex = 0;
            long curOffset = chunkOffsetDict[curIndex];
            long curSize = metaChunk[curOffset];
            curChunkDecompressed = GetDecompressedChunk(curOffset, curSize, reader, curIndex);
            if (curIndex + 1 < dictCount)
            {
                nextChunkDecompressed = GetDecompressedChunk(chunkOffsetDict[curIndex + 1], metaChunk[chunkOffsetDict[curIndex + 1]], reader, curIndex + 1);
            }
            else
            {
                nextChunkDecompressed = new byte[0];
            }
            curPointer = 0x0C;
            int totalParentCount = BitConverter.ToInt32(curChunkDecompressed, curPointer);
            curPointer += 4;
            int totalChildrenCount = BitConverter.ToInt32(curChunkDecompressed, curPointer);
            curPointer = 0x100;
            FileNode rootNode = null;
            for (int i = 0; i < totalParentCount; i++)
            {
                string stringNameParent = GetASCIIString(0x3C);
                long fileSize = GetInt64();
                long fileOffset = GetInt64();
                int entryType = GetInt32();
                int countChildren = GetInt32();

                if (fileNodeList.Count == 0)
                {
                    rootNode = new FileNode(stringNameParent, false, inputFile);
                    rootNode.EntireName = rootNode.Name;
                    fileNodeList.Add(rootNode);
                }
                else
                {
                    rootNode = fileNodeList[0];
                    rootNode.FromChunk = inputFilePath;
                    rootNode.FromChunkName = $"({Path.GetFileNameWithoutExtension(inputFilePath)})";
                }
                for (int j = 0; j < countChildren; j++)
                {
                    int originPointer = curPointer;
                    int originIndex = curIndex;
                    if (!chunkCache.ContainsKey(curIndex)) chunkCache.Add(curIndex, curChunkDecompressed);
                    if (!chunkCache.ContainsKey(curIndex + 1)) chunkCache.Add(curIndex + 1, nextChunkDecompressed);

                    string stringNameChild = GetASCIIString(0xA0);
                    fileSize = GetInt64();
                    fileOffset = GetInt64();
                    entryType = GetInt32();
                    int unknown = GetInt32();

                    if (entryType == 0x02)
                    {
                        curPointer = originPointer;
                        if (curIndex != originIndex)
                        {
                            curIndex = originIndex;
                            curChunkDecompressed = chunkCache[curIndex];
                            nextChunkDecompressed = chunkCache[curIndex + 1];
                            chunkCache.Remove(curIndex);
                            chunkCache.Remove(curIndex + 1);
                        }
                        stringNameChild = GetASCIIString(0x50);
                        GetByLength(0x68, new byte[0x68], 0);
                    }
                    string[] fatherNodes = stringNameChild.Split('\\');
                    bool isFile = false;
                    if (entryType == 0x02 || entryType == 0x00) isFile = true;
                    FileNode childNode = new FileNode(fatherNodes[fatherNodes.Length - 1], isFile, inputFile);
                    if (isFile)
                    {
                        childNode.Size = fileSize;
                        childNode.Offset = fileOffset;
                        childNode.ChunkIndex = (int)(fileOffset / 0x40000);
                        childNode.ChunkPointer = (int)(fileOffset % 0x40000);
                    }
                    childNode.EntireName = stringNameChild;
                    FileNode targetNode = rootNode;
                    foreach (string nodeName in fatherNodes)
                    {
                        if (nodeName.Equals("")) continue;
                        foreach (FileNode node in targetNode.Childern)
                        {
                            if (node.Name == nodeName)
                            {
                                if (node.Name == childNode.Name) { break; }
                                targetNode = node;
                                break;
                            }
                        }
                    }
                    bool needAdd = true;
                    foreach (FileNode tmpFileNode in targetNode.Childern)
                    {
                        if (tmpFileNode.Name == childNode.Name)
                        {
                            if (childNode.IsFile) targetNode.Childern.Remove(tmpFileNode);
                            else
                            {
                                tmpFileNode.FromChunk = childNode.FromChunk; tmpFileNode.FromChunkName = childNode.FromChunkName;
                                needAdd = false;
                            }
                            break;
                        }
                    }
                    if (needAdd) targetNode.Childern.Add(childNode);
                }
                mainWindow.SetProgressbar(i + 1, totalParentCount);
            }
            chunkCache.Clear();
            if (fileNodeList.Count > 0)
            {
                fileNodeList[0].getSize();
            }
            return fileNodeList;
        }

        //Extact function
        public int ExtractSelected(List<FileNode> itemlist, string BaseLocation, MainWindow mainWindow)
        {
            int failed = 0;
            foreach (FileNode node in itemlist)
            {

                while (mainWindow.PauseFlag) {Thread.Sleep(200); if (mainWindow.TerminateFlag) break; }
                if (mainWindow.TerminateFlag) break;
                try
                {
                    if (node.Childern.Count > 0)
                    {
                        failed += ExtractSelected(node.Childern, BaseLocation, mainWindow);
                    }
                    else if (node.IsSelected != false)
                    {
                        if (!node.IsFile) new FileInfo(BaseLocation + node.EntireName + "\\").Directory.Create();
                        else 
                        {
                            new FileInfo(BaseLocation + node.EntireName).Directory.Create();
                            File.WriteAllBytes(BaseLocation + node.EntireName, GetFileData(node));
                            mainWindow.updateExtractProgress();
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!MainWindow.CNMode) mainWindow.PrintLog($"Error occured while extracting {node.EntireName}{node.FromChunkName}, skipped. Please try again later.");
                    else mainWindow.PrintLog($"提取 {node.EntireName}{node.FromChunkName} 时发生错误，已跳过，请稍后重试");
                    Console.WriteLine(ex.StackTrace);
                    failed += 1;
                }
            }
            return failed;
        }

        public byte[] GetFileData(FileNode node) {
            Chunk curNodeChunk = bindingWindow.GetChunk(node.FromChunk);
            curNodeChunk.curIndex = node.ChunkIndex;
            curNodeChunk.curPointer = node.ChunkPointer;
            long size = node.Size;
            if (curNodeChunk.chunkCache.ContainsKey(curNodeChunk.curIndex))
            {
                curNodeChunk.curChunkDecompressed = curNodeChunk.chunkCache[curNodeChunk.curIndex];
            }
            else
            {
                if (curNodeChunk.chunkCache.Count > 20) curNodeChunk.chunkCache.Clear();
                curNodeChunk.curChunkDecompressed = curNodeChunk.GetDecompressedChunk(curNodeChunk.chunkOffsetDict[curNodeChunk.curIndex], curNodeChunk.metaChunk[curNodeChunk.chunkOffsetDict[curNodeChunk.curIndex]], curNodeChunk.reader, curNodeChunk.curIndex);
                curNodeChunk.chunkCache.Add(curNodeChunk.curIndex, curNodeChunk.curChunkDecompressed);
            }
            if (curNodeChunk.chunkCache.ContainsKey(curNodeChunk.curIndex + 1))
            {
                curNodeChunk.nextChunkDecompressed = curNodeChunk.chunkCache[curNodeChunk.curIndex + 1];
            }
            else
            {
                if (curNodeChunk.chunkCache.Count > 20) curNodeChunk.chunkCache.Clear();
                if (curNodeChunk.curIndex + 1 < curNodeChunk.dictCount) { curNodeChunk.nextChunkDecompressed = curNodeChunk.GetDecompressedChunk(curNodeChunk.chunkOffsetDict[curNodeChunk.curIndex + 1], curNodeChunk.metaChunk[curNodeChunk.chunkOffsetDict[curNodeChunk.curIndex + 1]], curNodeChunk.reader, curNodeChunk.curIndex + 1); }
                else { curNodeChunk.nextChunkDecompressed = new byte[0]; }
                curNodeChunk.chunkCache.Add(curNodeChunk.curIndex + 1, curNodeChunk.nextChunkDecompressed);
            }
            return curNodeChunk.GetByLength(size, new byte[size], 0);
        }

        //To get decompressed chunk
        private byte[] GetDecompressedChunk(long offset, long size, BinaryReader reader, int chunkNum)
        {
            try {if (size != 0)
            {
                reader.BaseStream.Seek(offset, SeekOrigin.Begin);
                byte[] ChunkCompressed = reader.ReadBytes((int)size); // Unsafe cast
                return DecryptChunk(Utils.Decompress(ChunkCompressed, ChunkCompressed.Length, 0x40000), GetChunkKey(chunkNum));
            }
            else
            {
                reader.BaseStream.Seek(offset, SeekOrigin.Begin);
                return DecryptChunk(reader.ReadBytes(0x40000), GetChunkKey(chunkNum));
            } }
            catch (Exception ex)
            {
                Console.WriteLine(ex.StackTrace);
                bindingWindow.PrintLog(ex.Message);
                return null;
            }
        }

        //To read an ASCII string from chunk bytes
        private string GetASCIIString(int targetlength)
        {
            return Convert.ToString(System.Text.Encoding.ASCII.GetString(GetByLength(targetlength, new byte[targetlength], 0).Where(b => b != 0x00).ToArray()));
        }

        //To read int64 from chunk bytes
        private long GetInt64()
        {
            return BitConverter.ToInt64(GetByLength(8, new byte[8], 0), 0);
        }

        //To read int64 from chunk bytes
        private int GetInt32()
        {
            return BitConverter.ToInt32(GetByLength(4, new byte[4], 0), 0);
        }

        //To read a byte array at length of targetlength
        private byte[] GetByLength(long targetlength, byte[] tmp, long startAddr)
        {
            if (curPointer + targetlength < 0x40000)
            {
                Array.Copy(curChunkDecompressed, curPointer, tmp, startAddr, targetlength);
                curPointer += (int)targetlength;
            }
            else
            {
                int tmpCanReadLength = 0x40000 - curPointer;
                long tmpRemainNeededLength = targetlength - tmpCanReadLength;
                Array.Copy(curChunkDecompressed, curPointer, tmp, startAddr, tmpCanReadLength);
                curPointer = 0;
                curChunkDecompressed = nextChunkDecompressed;
                curIndex += 1;
                if (curIndex + 1 < dictCount) { nextChunkDecompressed = GetDecompressedChunk(chunkOffsetDict[curIndex + 1], metaChunk[chunkOffsetDict[curIndex + 1]], reader, curIndex + 1); }
                else
                {
                    nextChunkDecompressed = new byte[0];
                }
                GetByLength(tmpRemainNeededLength, tmp, startAddr + tmpCanReadLength);
            }
            return tmp;
        }
        
        public static Dictionary<int, int> chunkKeyPattern = new Dictionary<int, int>();
        // Get right chunk encryption key for iteration. Copy from WorldChunkTool.
        public static byte[] GetChunkKey(int i)
        {
            if (chunkKeyPattern.Count == 0) {
                try
                {
                    BinaryReader keyReader = new BinaryReader(new MemoryStream(Properties.Resources.chunk));
                    int keyStart = keyReader.ReadInt32();
                    int keyEnd = keyReader.ReadInt32();
                    for (int keyIterator = keyStart; keyIterator <= keyEnd; keyIterator++)
                    {
                        int curKey = keyReader.ReadByte();
                        chunkKeyPattern.Add(keyIterator - 1, curKey);
                    }
                    keyReader.Close();
                }
                catch (Exception ex) {
                    MainWindow.ErrorsStack.Push(ex.Message);
                }
            }

            List<byte[]> chunkKeys = new List<byte[]>
            {
                //0 ac76cb97ec7500133a81038e7a82c80a
                new byte[] { 0xac, 0x76, 0xcb, 0x97, 0xec, 0x75, 0x00, 0x13, 0x3a, 0x81, 0x03, 0x8e, 0x7a, 0x82, 0xc8, 0x0a },
                //1 6bb5c956e44d00bc305233cfbfaafa25
                new byte[] { 0x6b, 0xb5, 0xc9, 0x56, 0xe4, 0x4d, 0x00, 0xbc, 0x30, 0x52, 0x33, 0xcf, 0xbf, 0xaa, 0xfa, 0x25 },
                //2 8f021dccb0f2787206fbdee2390bbb5c
                new byte[] { 0x8f, 0x02, 0x1d, 0xcc, 0xb0, 0xf2, 0x78, 0x72, 0x06, 0xfb, 0xde, 0xe2, 0x39, 0x0b, 0xbb, 0x5c },
                //3 da5c1e531d8359157875bd63bfaafa25
                new byte[] { 0xda, 0x5c, 0x1e, 0x53, 0x1d, 0x83, 0x59, 0x15, 0x78, 0x75, 0xbd, 0x63, 0xbf, 0xaa, 0xfa, 0x25 },
                //4 1f6d31c883dd716d7e8f598ce23f1929
                new byte[] { 0x1f, 0x6d, 0x31, 0xc8, 0x83, 0xdd, 0x71, 0x6d, 0x7e, 0x8f, 0x59, 0x8c, 0xe2, 0x3f, 0x19, 0x29 },
                //5 4bb0de04e4e0856980ccb2942f9ce9f9
                new byte[] { 0x4b, 0xb0, 0xde, 0x04, 0xe4, 0xe0, 0x85, 0x69, 0x80, 0xcc, 0xb2, 0x94, 0x2f, 0x9c, 0xe9, 0xf9 },
                //6 8bce54dc4c11139a7875bd63bfaafa25
                new byte[] { 0x8b, 0xce, 0x54, 0xdc, 0x4c, 0x11, 0x13, 0x9a, 0x78, 0x75, 0xbd, 0x63, 0xbf, 0xaa, 0xfa, 0x25 },
                //7 ec13345966ce7312440089a2ceddcee9
                new byte[] { 0xec, 0x13, 0x34, 0x59, 0x66, 0xce, 0x73, 0x12, 0x44, 0x00, 0x89, 0xa2, 0xce, 0xdd, 0xce, 0xe9 },
                //8 e4662c709c753a039a2c0f5ae23f1929
                new byte[] { 0xe4, 0x66, 0x2c, 0x70, 0x9c, 0x75, 0x3a, 0x03, 0x9a, 0x2c, 0x0f, 0x5a, 0xe2, 0x3f, 0x19, 0x29 },
                //9 a492fc9033949c15a033ac223735cca7
                new byte[] { 0xa4, 0x92, 0xfc, 0x90, 0x33, 0x94, 0x9c, 0x15, 0xa0, 0x33, 0xac, 0x22, 0x37, 0x35, 0xcc, 0xa7 },
                //10 25099c1f911a26e5ce9172f07a82c80a
                new byte[] { 0x25, 0x09, 0x9c, 0x1f, 0x91, 0x1a, 0x26, 0xe5, 0xce, 0x91, 0x72, 0xf0, 0x7a, 0x82, 0xc8, 0x0a },
                //11 d1d29d7446d4fdf1a033ac223735cca7
                new byte[] { 0xd1, 0xd2, 0x9d, 0x74, 0x46, 0xd4, 0xfd, 0xf1, 0xa0, 0x33, 0xac, 0x22, 0x37, 0x35, 0xcc, 0xa7 },
                //12 7eb268373b5d361ed6d313e2933c4dcb
                new byte[] { 0x7e, 0xb2, 0x68, 0x37, 0x3b, 0x5d, 0x36, 0x1e, 0xd6, 0xd3, 0x13, 0xe2, 0x93, 0x3c, 0x4d, 0xcb },
                //13 a1c7d2ea661895ac7875bd63bfaafa25
                new byte[] { 0xa1, 0xc7, 0xd2, 0xea, 0x66, 0x18, 0x95, 0xac, 0x78, 0x75, 0xbd, 0x63, 0xbf, 0xaa, 0xfa, 0x25 },
                //14 82a43b2108797c6a440089a2ceddcee9
                new byte[] { 0x82, 0xa4, 0x3b, 0x21, 0x08, 0x79, 0x7c, 0x6a, 0x44, 0x00, 0x89, 0xa2, 0xce, 0xdd, 0xce, 0xe9 },
                //15 41d055b3dd6015167e8f598ce23f1929
                new byte[] { 0x41, 0xd0, 0x55, 0xb3, 0xdd, 0x60, 0x15, 0x16, 0x7e, 0x8f, 0x59, 0x8c, 0xe2, 0x3f, 0x19, 0x29 },
            };
            int keyPos = chunkKeyPattern[i];
            if (keyPos > 0xF) keyPos = 0;
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
