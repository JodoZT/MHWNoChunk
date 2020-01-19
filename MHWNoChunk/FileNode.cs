using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace MHWNoChunk
{
    public class FileNode : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public List<FileNode> Childern { get; set; }
        public string Icon { get; set; }
        public string EntireName { get; set; }
        public long Offset { get; set; }
        public long Size { get; set; }
        public int ChunkIndex { get; set; }
        public bool IsFile { get; set; }
        public int ChunkPointer { get; set; }
        public string NameWithSize { get; set; }
        public string FromChunk { get; set; }
        public string FromChunkName { get; set; }
        public int CrossChunkCnt { get; set; }
        public bool IsCorrect { get; set; }

        private bool isSelected;

        public bool IsSelected
        {
            get { return isSelected; }
            set
            {
                isSelected = value;
                setChilrenSelected(value);
                if (this.PropertyChanged != null)
                {
                    this.PropertyChanged.Invoke(this, new PropertyChangedEventArgs("IsSelected"));
                }
            }
        }

        public int getSelectedCount() {
            int count = 0;
            foreach (FileNode node in Childern) {
                count += node.getSelectedCount();
            }
            if (IsFile && IsSelected) {
                count++;
            }
            return count;
        }

        public void setChilrenSelected(bool selected)
        {
            foreach (FileNode child in Childern)
            {
                child.IsSelected = selected;
            }
        }

        public long getSize() {
            if (IsFile) {
                int crossChunkCnt = 1;
                if (Size > 0x40000 - ChunkPointer)
                {
                    crossChunkCnt = (int)((Size - (0x40000 - ChunkPointer)) / 0x40000) + 2;
                }
                CrossChunkCnt = crossChunkCnt;
                FromChunkName += $"(Idx {ChunkIndex}(Offset{ChunkPointer}) - Idx {ChunkIndex + crossChunkCnt - 1}(Offset{(Size + ChunkPointer - 1) % 0x40000}))";
                for (int i = 0; i < CrossChunkCnt; i++) {
                    if (Chunk.chunkKeyPattern[ChunkIndex + i] > 0xF) { IsCorrect = false; break; }
                }
                if (IsCorrect) FromChunkName += " √";
                else FromChunkName += " ×";
                setNameWithSize(Name, Size); return Size; }
            else
            {
                long _size = 0;
                foreach (FileNode child in Childern)
                {
                    _size += child.getSize();
                }
                Size = _size;
                setNameWithSize(Name, Size);
                return _size;
            }
        }

        private void setNameWithSize(string name, long _size) {
            NameWithSize = $"{Name} ({getSizeStr(_size)})";
        }

        public string getSizeStr(long _size) {
            string sizestr = "";
            if (_size < 1024)
            {
                sizestr = $"{_size} B";
            }
            else if (_size >= 1024 && _size < 1048576)
            {
                sizestr = $"{_size / 1024f:F2} KB({_size}B)";
            }
            else if (_size < 1073741824 && _size >= 1048576)
            {
                sizestr = $"{(_size >> 10) / 1024f:F2} MB({_size}B)";
            }
            else
            {
                sizestr = $"{(_size >> 20) / 1024f:F2} GB({_size}B)";
            }
            return sizestr;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public FileNode(string name, bool isFile, string fromChunk)
        {
            Name = name;
            NameWithSize = "";
            IsFile = isFile;
            if (isFile) Icon = AppDomain.CurrentDomain.BaseDirectory + "\\file.png";
            else Icon = AppDomain.CurrentDomain.BaseDirectory + "\\dir.png";
            Childern = new List<FileNode>();
            IsSelected = false;
            FromChunk = fromChunk;
            FromChunkName = $"({System.IO.Path.GetFileNameWithoutExtension(fromChunk)})";
            CrossChunkCnt = 1;
            IsCorrect = true;
        }
    }
}
