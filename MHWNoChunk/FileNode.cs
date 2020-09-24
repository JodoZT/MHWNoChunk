using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;

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

        private bool? isSelected;

        public bool? IsSelected
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

        private bool visible;

        public bool Visible
        {
            get { return visible; }
            set
            {
                visible = value;
                if (this.PropertyChanged != null)
                {
                    this.PropertyChanged.Invoke(this, new PropertyChangedEventArgs("Visible"));
                }
            }
        }

        public int getSelectedCount()
        {
            int count = 0;
            foreach (FileNode node in Childern)
            {
                count += node.getSelectedCount();
            }
            if (IsFile && IsSelected != false)
            {
                count++;
            }
            return count;
        }

        public void setChilrenSelected(bool? selected)
        {
            foreach (FileNode child in Childern)
            {
                if (child.Visible) child.IsSelected = selected;
            }
        }

        public long getSize()
        {
            if (IsFile)
            {
                setNameWithSize(Name, Size); return Size;
            }
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

        private void setNameWithSize(string name, long _size)
        {
            NameWithSize = $"{Name} ({getSizeStr(_size)})";
        }

        public string getSizeStr(long _size)
        {
            string sizestr = "";
            if (_size < 1024)
            {
                sizestr = $"{_size} B";
            }
            else if (_size >= 1024 && _size < 1048576)
            {
                sizestr = $"{_size / 1024f:F2} KB";
            }
            else if (_size < 1073741824 && _size >= 1048576)
            {
                sizestr = $"{(_size >> 10) / 1024f:F2} MB";
            }
            else
            {
                sizestr = $"{(_size >> 20) / 1024f:F2} GB";
            }
            return sizestr;
        }

        public string getPreviewInfo()
        {
            if (!MainWindow.CNMode) return $"Path: {EntireName}\nType: {(IsFile ? "file" : $"folder\nChildren: {Childern.Count}")}\nSize: {getSizeStr(Size)}\nFrom: {FromChunk}\n";
            else { return $"路径: {EntireName}\n类型: {(IsFile ? "文件" : $"文件夹\n子项: {Childern.Count}")}\n尺寸: {getSizeStr(Size)}\n来自: {FromChunk}\n"; }
        }

        public void sortChildren()
        {
            if (!IsFile && Childern.Count > 0)
            {
                foreach (FileNode child in Childern)
                {
                    child.sortChildren();
                }
                Childern.Sort((x, y) => x.IsFile == y.IsFile ? StringComparer.CurrentCultureIgnoreCase.Compare(x.Name, y.Name) : x.IsFile ? 1 : -1);
            }
        }

        public bool filterChildren(Regex filterRegex)
        {
            bool TmpVisible;
            if (filterRegex is null) TmpVisible = true;
            else TmpVisible = filterRegex.IsMatch(EntireName);
            foreach (FileNode child in Childern)
            {
                bool childVisible = child.filterChildren(filterRegex);
                TmpVisible |= childVisible;
            }
            Visible = TmpVisible;
            return Visible;
        }

        public bool filterChildren(string filterText)
        {
            bool TmpVisible = EntireName.Contains(filterText);
            foreach (FileNode child in Childern)
            {
                bool childVisible = child.filterChildren(filterText);
                TmpVisible |= childVisible;
            }
            Visible = TmpVisible;
            return Visible;
        }

        public void resetVisibility()
        {
            Visible = true;
            foreach (FileNode child in Childern)
            {
                child.resetVisibility();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public FileNode(string name, bool isFile, string fromChunk)
        {
            Name = name;
            NameWithSize = "";
            IsFile = isFile;
            if (IsFile) Icon = "pack://application:,,,/Resources/file.png";
            else Icon = "pack://application:,,,/Resources/dir.png";
            Childern = new List<FileNode>();
            IsSelected = false;
            FromChunk = fromChunk;
            FromChunkName = $"({Path.GetFileNameWithoutExtension(fromChunk)})";
            Visible = true;
        }
    }
}
