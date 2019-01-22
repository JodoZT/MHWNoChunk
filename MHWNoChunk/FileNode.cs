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
            if (IsFile) { setNameWithSize(Name, Size); return Size; }
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
            string sizestr = "";
            if (_size < 1024)
            {
                sizestr = $"{_size} B";
            }
            else if (_size >= 1024 && _size <= 1048576)
            {
                sizestr = $"{_size / 1024f:F2} KB";
            }
            else if (_size <= 1073741824 && _size >= 1048576)
            {
                sizestr = $"{_size / 1048576f:F2} MB";
            }
            else {
                sizestr = $"{_size / 1073741824f:F2} GB";
            }
            NameWithSize = $"{Name} ({sizestr})";
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public FileNode()
        {
            Name = "";
            NameWithSize = "";
            Icon = AppDomain.CurrentDomain.BaseDirectory + "\\file.png";
            Childern = new List<FileNode>();
            IsSelected = false;
            IsFile = false;
        }

        public FileNode(string name)
        {
            Name = name;
            NameWithSize = "";
            Icon = AppDomain.CurrentDomain.BaseDirectory + "\\file.png";
            Childern = new List<FileNode>();
            IsSelected = false;
            IsFile = false;
        }

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
        }

        public FileNode(string name, List<FileNode> children)
        {
            Name = name;
            NameWithSize = "";
            Icon = AppDomain.CurrentDomain.BaseDirectory + "\\file.png";
            Childern = children;
            IsSelected = false;
            IsFile = false;
        }
    }
}
