using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace PhoneRomFlashTool.ViewModels
{
    public class HexEditorViewModel : INotifyPropertyChanged
    {
        #region Constants
        private const int DefaultBytesPerLine = 16;
        private const int MaxUndoHistory = 100;
        private const int ChunkSize = 1024 * 1024; // 1MB chunks for large files
        #endregion

        #region Private Fields
        private byte[]? _fileData;
        private string? _filePath;
        private long _currentOffset;
        private long _selectionStart;
        private long _selectionLength;
        private int _bytesPerLine = DefaultBytesPerLine;
        private bool _isModified;
        private bool _isLittleEndian = true;
        private bool _showDataInspector = true;
        private bool _showBookmarks = true;
        private bool _showStructureView = true;
        private bool _highlightModified = true;
        private bool _showOffsetsInHex = true;
        private bool _isInsertMode;
        private string _statusMessage = "Ready";
        private string _searchText = "";
        private string _quickGoToOffset = "";
        private int _currentSearchIndex = -1;

        private readonly Stack<UndoAction> _undoStack = new();
        private readonly Stack<UndoAction> _redoStack = new();
        private readonly HashSet<long> _modifiedOffsets = new();
        private readonly List<string> _recentFiles = new();
        #endregion

        #region Collections
        public ObservableCollection<HexDisplayLine> DisplayLines { get; } = new();
        public ObservableCollection<HexBookmark> Bookmarks { get; } = new();
        public ObservableCollection<FileStructureNode> FileStructure { get; } = new();
        public ObservableCollection<HexSearchResult> SearchResults { get; } = new();
        public ObservableCollection<string> RecentFiles { get; } = new();
        #endregion

        #region Properties
        public string? FilePath
        {
            get => _filePath;
            set { _filePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(WindowTitle)); }
        }

        public string WindowTitle => string.IsNullOrEmpty(_filePath)
            ? "Hex Editor"
            : $"Hex Editor - {Path.GetFileName(_filePath)}{(_isModified ? " *" : "")}";

        public long CurrentOffset
        {
            get => _currentOffset;
            set
            {
                _currentOffset = Math.Max(0, Math.Min(value, (_fileData?.Length ?? 1) - 1));
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentOffsetHex));
                OnPropertyChanged(nameof(CurrentOffsetDec));
                OnPropertyChanged(nameof(SelectedOffsetText));
                UpdateDataInspector();
            }
        }

        public string CurrentOffsetHex => $"0x{_currentOffset:X8}";
        public string CurrentOffsetDec => _currentOffset.ToString("N0");
        public string SelectedOffsetText => $"Offset: 0x{_currentOffset:X8} ({_currentOffset:N0})";

        public long SelectionStart
        {
            get => _selectionStart;
            set { _selectionStart = value; OnPropertyChanged(); }
        }

        public long SelectionLength
        {
            get => _selectionLength;
            set { _selectionLength = value; OnPropertyChanged(); }
        }

        public int BytesPerLine
        {
            get => _bytesPerLine;
            set
            {
                _bytesPerLine = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Is16BytesPerLine));
                RefreshDisplay();
            }
        }

        public bool Is16BytesPerLine => _bytesPerLine == 16;

        public bool IsModified
        {
            get => _isModified;
            set { _isModified = value; OnPropertyChanged(); OnPropertyChanged(nameof(ModifiedIndicator)); OnPropertyChanged(nameof(WindowTitle)); }
        }

        public string ModifiedIndicator => _isModified ? "Modified" : "";

        public bool IsLittleEndian
        {
            get => _isLittleEndian;
            set { _isLittleEndian = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsBigEndian)); UpdateDataInspector(); }
        }

        public bool IsBigEndian
        {
            get => !_isLittleEndian;
            set { _isLittleEndian = !value; OnPropertyChanged(); OnPropertyChanged(nameof(IsLittleEndian)); UpdateDataInspector(); }
        }

        public bool ShowDataInspector
        {
            get => _showDataInspector;
            set { _showDataInspector = value; OnPropertyChanged(); OnPropertyChanged(nameof(RightPanelVisible)); }
        }

        public bool ShowBookmarks
        {
            get => _showBookmarks;
            set { _showBookmarks = value; OnPropertyChanged(); OnPropertyChanged(nameof(RightPanelVisible)); }
        }

        public bool ShowStructureView
        {
            get => _showStructureView;
            set { _showStructureView = value; OnPropertyChanged(); OnPropertyChanged(nameof(RightPanelVisible)); }
        }

        public bool RightPanelVisible => _showDataInspector || _showBookmarks || _showStructureView;

        public bool HighlightModified
        {
            get => _highlightModified;
            set { _highlightModified = value; OnPropertyChanged(); RefreshDisplay(); }
        }

        public bool ShowOffsetsInHex
        {
            get => _showOffsetsInHex;
            set { _showOffsetsInHex = value; OnPropertyChanged(); RefreshDisplay(); }
        }

        public bool IsInsertMode
        {
            get => _isInsertMode;
            set { _isInsertMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(InsertModeColor)); }
        }

        public Brush InsertModeColor => _isInsertMode ? Brushes.LimeGreen : Brushes.Gray;

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public string FileSizeText => _fileData != null ? FormatFileSize(_fileData.Length) : "No file";

        public string EncodingName => "UTF-8";

        public string QuickGoToOffset
        {
            get => _quickGoToOffset;
            set { _quickGoToOffset = value; OnPropertyChanged(); }
        }

        public int SearchResultsCount => SearchResults.Count;

        public HexBookmark? SelectedBookmark { get; set; }
        public HexSearchResult? SelectedSearchResult { get; set; }
        #endregion

        #region Data Inspector Properties
        public string DataInt8 => GetDataValue<sbyte>();
        public string DataInt16 => GetDataValue<short>();
        public string DataInt32 => GetDataValue<int>();
        public string DataInt64 => GetDataValue<long>();
        public string DataUInt8 => GetDataValue<byte>();
        public string DataUInt16 => GetDataValue<ushort>();
        public string DataUInt32 => GetDataValue<uint>();
        public string DataUInt64 => GetDataValue<ulong>();
        public string DataFloat => GetDataValue<float>();
        public string DataDouble => GetDataValue<double>();
        public string DataString => GetStringValue();
        public string DataBinary => GetBinaryValue();
        #endregion

        #region Commands
        public ICommand OpenFileCommand { get; }
        public ICommand SaveFileCommand { get; }
        public ICommand SaveAsCommand { get; }
        public ICommand CloseCommand { get; }
        public ICommand ExportHexDumpCommand { get; }
        public ICommand ExportSelectionCommand { get; }
        public ICommand ExportAnalysisCommand { get; }
        public ICommand OpenRecentCommand { get; }

        public ICommand UndoCommand { get; }
        public ICommand RedoCommand { get; }
        public ICommand CutCommand { get; }
        public ICommand CopyCommand { get; }
        public ICommand PasteCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand SelectBlockCommand { get; }

        public ICommand ShowSearchCommand { get; }
        public ICommand FindNextCommand { get; }
        public ICommand FindPreviousCommand { get; }
        public ICommand ShowReplaceCommand { get; }
        public ICommand ReplaceAllCommand { get; }
        public ICommand ShowGoToCommand { get; }
        public ICommand QuickGoToCommand { get; }

        public ICommand SetBytesPerLineCommand { get; }
        public ICommand CompareFilesCommand { get; }
        public ICommand ShowChecksumCommand { get; }
        public ICommand FillSelectionCommand { get; }
        public ICommand InsertBytesCommand { get; }
        public ICommand AnalyzeFileCommand { get; }
        public ICommand FindPatternsCommand { get; }
        public ICommand FindStringsCommand { get; }

        public ICommand AddBookmarkCommand { get; }
        public ICommand ManageBookmarksCommand { get; }
        public ICommand GoToBookmarkCommand { get; }
        public ICommand DeleteBookmarkCommand { get; }
        public ICommand PrevBookmarkCommand { get; }
        public ICommand NextBookmarkCommand { get; }

        public ICommand ShowShortcutsCommand { get; }
        public ICommand ShowAboutCommand { get; }
        #endregion

        #region Constructor
        public HexEditorViewModel()
        {
            // File commands
            OpenFileCommand = new HexRelayCommand(_ => OpenFile());
            SaveFileCommand = new HexRelayCommand(_ => SaveFile(), _ => _isModified && _fileData != null);
            SaveAsCommand = new HexRelayCommand(_ => SaveFileAs(), _ => _fileData != null);
            CloseCommand = new HexRelayCommand(_ => CloseWindow());
            ExportHexDumpCommand = new HexRelayCommand(_ => ExportHexDump(), _ => _fileData != null);
            ExportSelectionCommand = new HexRelayCommand(_ => ExportSelection(), _ => _fileData != null && _selectionLength > 0);
            ExportAnalysisCommand = new HexRelayCommand(_ => ExportAnalysis(), _ => _fileData != null);
            OpenRecentCommand = new HexRelayCommand(path => OpenFileFromPath(path?.ToString()));

            // Edit commands
            UndoCommand = new HexRelayCommand(_ => Undo(), _ => _undoStack.Count > 0);
            RedoCommand = new HexRelayCommand(_ => Redo(), _ => _redoStack.Count > 0);
            CutCommand = new HexRelayCommand(_ => Cut(), _ => _selectionLength > 0);
            CopyCommand = new HexRelayCommand(_ => Copy(), _ => _selectionLength > 0);
            PasteCommand = new HexRelayCommand(_ => Paste());
            DeleteCommand = new HexRelayCommand(_ => Delete(), _ => _selectionLength > 0);
            SelectAllCommand = new HexRelayCommand(_ => SelectAll(), _ => _fileData != null);
            SelectBlockCommand = new HexRelayCommand(_ => ShowSelectBlockDialog());

            // Search commands
            ShowSearchCommand = new HexRelayCommand(_ => ShowSearchDialog());
            FindNextCommand = new HexRelayCommand(_ => FindNext(), _ => SearchResults.Count > 0);
            FindPreviousCommand = new HexRelayCommand(_ => FindPrevious(), _ => SearchResults.Count > 0);
            ShowReplaceCommand = new HexRelayCommand(_ => ShowReplaceDialog());
            ReplaceAllCommand = new HexRelayCommand(_ => ReplaceAll());
            ShowGoToCommand = new HexRelayCommand(_ => ShowGoToDialog());
            QuickGoToCommand = new HexRelayCommand(_ => QuickGoTo());

            // View commands
            SetBytesPerLineCommand = new HexRelayCommand(param => SetBytesPerLine(param));

            // Tools commands
            CompareFilesCommand = new HexRelayCommand(_ => CompareFiles());
            ShowChecksumCommand = new HexRelayCommand(_ => ShowChecksum(), _ => _fileData != null);
            FillSelectionCommand = new HexRelayCommand(_ => FillSelection(), _ => _selectionLength > 0);
            InsertBytesCommand = new HexRelayCommand(_ => InsertBytes(), _ => _fileData != null);
            AnalyzeFileCommand = new HexRelayCommand(_ => AnalyzeFile(), _ => _fileData != null);
            FindPatternsCommand = new HexRelayCommand(_ => FindPatterns(), _ => _fileData != null);
            FindStringsCommand = new HexRelayCommand(_ => FindStrings(), _ => _fileData != null);

            // Bookmark commands
            AddBookmarkCommand = new HexRelayCommand(_ => AddBookmark(), _ => _fileData != null);
            ManageBookmarksCommand = new HexRelayCommand(_ => ManageBookmarks());
            GoToBookmarkCommand = new HexRelayCommand(_ => GoToBookmark(), _ => SelectedBookmark != null);
            DeleteBookmarkCommand = new HexRelayCommand(_ => DeleteBookmark(), _ => SelectedBookmark != null);
            PrevBookmarkCommand = new HexRelayCommand(_ => PrevBookmark(), _ => Bookmarks.Count > 0);
            NextBookmarkCommand = new HexRelayCommand(_ => NextBookmark(), _ => Bookmarks.Count > 0);

            // Help commands
            ShowShortcutsCommand = new HexRelayCommand(_ => ShowShortcuts());
            ShowAboutCommand = new HexRelayCommand(_ => ShowAbout());

            LoadRecentFiles();
        }
        #endregion

        #region File Operations
        public void OpenFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "All Files (*.*)|*.*|Binary Files (*.bin;*.img;*.dat)|*.bin;*.img;*.dat|ROM Files (*.rom;*.fw)|*.rom;*.fw",
                Title = "Open File for Hex Editing"
            };

            if (dialog.ShowDialog() == true)
            {
                OpenFileFromPath(dialog.FileName);
            }
        }

        public async void OpenFileFromPath(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                StatusMessage = "File not found";
                return;
            }

            try
            {
                StatusMessage = $"Loading {Path.GetFileName(path)}...";

                var fileInfo = new FileInfo(path);
                if (fileInfo.Length > 500 * 1024 * 1024) // 500MB warning
                {
                    var result = MessageBox.Show(
                        $"This file is {FormatFileSize(fileInfo.Length)}. Loading large files may use significant memory. Continue?",
                        "Large File Warning",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result != MessageBoxResult.Yes)
                        return;
                }

                _fileData = await Task.Run(() => File.ReadAllBytes(path));
                FilePath = path;
                _isModified = false;
                _modifiedOffsets.Clear();
                _undoStack.Clear();
                _redoStack.Clear();
                CurrentOffset = 0;

                AddToRecentFiles(path);
                RefreshDisplay();
                AnalyzeFileStructure();

                StatusMessage = $"Loaded {Path.GetFileName(path)} ({FormatFileSize(_fileData.Length)})";
                OnPropertyChanged(nameof(FileSizeText));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "Error loading file";
            }
        }

        private void SaveFile()
        {
            if (string.IsNullOrEmpty(_filePath) || _fileData == null) return;

            try
            {
                File.WriteAllBytes(_filePath, _fileData);
                IsModified = false;
                _modifiedOffsets.Clear();
                RefreshDisplay();
                StatusMessage = "File saved successfully";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveFileAs()
        {
            if (_fileData == null) return;

            var dialog = new SaveFileDialog
            {
                Filter = "All Files (*.*)|*.*|Binary Files (*.bin)|*.bin",
                Title = "Save File As",
                FileName = Path.GetFileName(_filePath ?? "untitled.bin")
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllBytes(dialog.FileName, _fileData);
                    FilePath = dialog.FileName;
                    IsModified = false;
                    _modifiedOffsets.Clear();
                    AddToRecentFiles(dialog.FileName);
                    StatusMessage = "File saved successfully";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CloseWindow()
        {
            if (_isModified)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Do you want to save before closing?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                    SaveFile();
                else if (result == MessageBoxResult.Cancel)
                    return;
            }

            Application.Current.Windows.OfType<Window>()
                .FirstOrDefault(w => w.DataContext == this)?.Close();
        }
        #endregion

        #region Display
        private void RefreshDisplay()
        {
            if (_fileData == null)
            {
                DisplayLines.Clear();
                return;
            }

            DisplayLines.Clear();
            var lineCount = (_fileData.Length + _bytesPerLine - 1) / _bytesPerLine;

            for (int i = 0; i < Math.Min(lineCount, 10000); i++) // Limit for performance
            {
                var offset = (long)i * _bytesPerLine;
                var line = CreateHexLine(offset);
                DisplayLines.Add(line);
            }
        }

        private HexDisplayLine CreateHexLine(long offset)
        {
            var line = new HexDisplayLine
            {
                Offset = offset,
                OffsetText = _showOffsetsInHex ? $"{offset:X8}" : $"{offset:D8}"
            };

            var hexBuilder = new StringBuilder();
            var asciiBuilder = new StringBuilder();
            var bytesInLine = (int)Math.Min(_bytesPerLine, _fileData!.Length - offset);

            for (int i = 0; i < bytesInLine; i++)
            {
                var b = _fileData[offset + i];
                hexBuilder.Append($"{b:X2} ");
                asciiBuilder.Append(b >= 32 && b < 127 ? (char)b : '.');
            }

            // Pad if necessary
            for (int i = bytesInLine; i < _bytesPerLine; i++)
            {
                hexBuilder.Append("   ");
            }

            line.HexText = hexBuilder.ToString();
            line.AsciiText = asciiBuilder.ToString();

            // Check if line contains modified bytes
            if (_highlightModified)
            {
                for (int i = 0; i < bytesInLine; i++)
                {
                    if (_modifiedOffsets.Contains(offset + i))
                    {
                        line.Background = new SolidColorBrush(Color.FromRgb(0x6B, 0x4C, 0x00));
                        break;
                    }
                }
            }

            return line;
        }
        #endregion

        #region Edit Operations
        public void ModifyByte(long offset, byte newValue)
        {
            if (_fileData == null || offset < 0 || offset >= _fileData.Length) return;

            var oldValue = _fileData[offset];
            if (oldValue == newValue) return;

            // Record undo action
            _undoStack.Push(new UndoAction
            {
                Offset = offset,
                OldData = new[] { oldValue },
                NewData = new[] { newValue },
                Type = UndoActionType.Modify
            });

            if (_undoStack.Count > MaxUndoHistory)
            {
                // Remove oldest
                var temp = _undoStack.ToArray().Take(MaxUndoHistory).ToArray();
                _undoStack.Clear();
                foreach (var item in temp.Reverse())
                    _undoStack.Push(item);
            }

            _redoStack.Clear();

            _fileData[offset] = newValue;
            _modifiedOffsets.Add(offset);
            IsModified = true;

            RefreshLineAt(offset);
            UpdateDataInspector();
        }

        private void Undo()
        {
            if (_undoStack.Count == 0 || _fileData == null) return;

            var action = _undoStack.Pop();
            _redoStack.Push(action);

            switch (action.Type)
            {
                case UndoActionType.Modify:
                    for (int i = 0; i < action.OldData.Length; i++)
                    {
                        _fileData[action.Offset + i] = action.OldData[i];
                    }
                    break;
            }

            CurrentOffset = action.Offset;
            RefreshDisplay();
            UpdateDataInspector();
            StatusMessage = "Undo completed";
        }

        private void Redo()
        {
            if (_redoStack.Count == 0 || _fileData == null) return;

            var action = _redoStack.Pop();
            _undoStack.Push(action);

            switch (action.Type)
            {
                case UndoActionType.Modify:
                    for (int i = 0; i < action.NewData.Length; i++)
                    {
                        _fileData[action.Offset + i] = action.NewData[i];
                        _modifiedOffsets.Add(action.Offset + i);
                    }
                    break;
            }

            IsModified = true;
            CurrentOffset = action.Offset;
            RefreshDisplay();
            UpdateDataInspector();
            StatusMessage = "Redo completed";
        }

        private void Cut()
        {
            Copy();
            Delete();
        }

        private void Copy()
        {
            if (_fileData == null || _selectionLength <= 0) return;

            var selectedBytes = new byte[_selectionLength];
            Array.Copy(_fileData, _selectionStart, selectedBytes, 0, (int)_selectionLength);

            var hexString = BitConverter.ToString(selectedBytes).Replace("-", " ");
            Clipboard.SetText(hexString);
            StatusMessage = $"Copied {_selectionLength} bytes to clipboard";
        }

        private void Paste()
        {
            if (_fileData == null) return;

            var clipText = Clipboard.GetText();
            if (string.IsNullOrEmpty(clipText)) return;

            try
            {
                // Try to parse as hex string
                var hexString = clipText.Replace(" ", "").Replace("-", "");
                if (hexString.Length % 2 != 0)
                {
                    MessageBox.Show("Invalid hex string", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var bytes = new byte[hexString.Length / 2];
                for (int i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = Convert.ToByte(hexString.Substring(i * 2, 2), 16);
                }

                // Record undo
                var oldData = new byte[bytes.Length];
                Array.Copy(_fileData, _currentOffset, oldData, 0, Math.Min(bytes.Length, (int)(_fileData.Length - _currentOffset)));

                _undoStack.Push(new UndoAction
                {
                    Offset = _currentOffset,
                    OldData = oldData,
                    NewData = bytes,
                    Type = UndoActionType.Modify
                });
                _redoStack.Clear();

                // Apply paste
                for (int i = 0; i < bytes.Length && _currentOffset + i < _fileData.Length; i++)
                {
                    _fileData[_currentOffset + i] = bytes[i];
                    _modifiedOffsets.Add(_currentOffset + i);
                }

                IsModified = true;
                RefreshDisplay();
                StatusMessage = $"Pasted {bytes.Length} bytes";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error parsing clipboard data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Delete()
        {
            if (_fileData == null || _selectionLength <= 0) return;

            // Fill selection with zeros
            var oldData = new byte[_selectionLength];
            Array.Copy(_fileData, _selectionStart, oldData, 0, (int)_selectionLength);

            _undoStack.Push(new UndoAction
            {
                Offset = _selectionStart,
                OldData = oldData,
                NewData = new byte[_selectionLength],
                Type = UndoActionType.Modify
            });
            _redoStack.Clear();

            for (long i = 0; i < _selectionLength; i++)
            {
                _fileData[_selectionStart + i] = 0;
                _modifiedOffsets.Add(_selectionStart + i);
            }

            IsModified = true;
            RefreshDisplay();
            StatusMessage = $"Deleted {_selectionLength} bytes (filled with zeros)";
        }

        private void SelectAll()
        {
            if (_fileData == null) return;
            _selectionStart = 0;
            SelectionLength = _fileData.Length;
            StatusMessage = $"Selected all {_fileData.Length} bytes";
        }

        private void ShowSelectBlockDialog()
        {
            // TODO: Implement select block dialog
            StatusMessage = "Select block dialog not implemented yet";
        }
        #endregion

        #region Search Operations
        private void ShowSearchDialog()
        {
            var searchText = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter hex bytes (e.g., 'FF 00 AB') or text to search:",
                "Find",
                _searchText);

            if (string.IsNullOrEmpty(searchText)) return;

            _searchText = searchText;
            PerformSearch(searchText);
        }

        private void PerformSearch(string searchText)
        {
            if (_fileData == null) return;

            SearchResults.Clear();
            _currentSearchIndex = -1;

            byte[] searchBytes;
            bool isHexSearch = searchText.All(c => "0123456789ABCDEFabcdef ".Contains(c));

            if (isHexSearch && searchText.Contains(' '))
            {
                // Hex search
                try
                {
                    searchBytes = searchText.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => Convert.ToByte(s, 16))
                        .ToArray();
                }
                catch
                {
                    // Fallback to text search
                    searchBytes = Encoding.UTF8.GetBytes(searchText);
                }
            }
            else
            {
                searchBytes = Encoding.UTF8.GetBytes(searchText);
            }

            // Search
            for (long i = 0; i <= _fileData.Length - searchBytes.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < searchBytes.Length; j++)
                {
                    if (_fileData[i + j] != searchBytes[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    var result = new HexSearchResult
                    {
                        Offset = i,
                        OffsetText = $"0x{i:X8}",
                        Length = searchBytes.Length,
                        PreviewText = GetPreviewText(i, 16)
                    };
                    SearchResults.Add(result);

                    if (SearchResults.Count >= 1000)
                    {
                        StatusMessage = $"Found {SearchResults.Count}+ matches (showing first 1000)";
                        break;
                    }
                }
            }

            OnPropertyChanged(nameof(SearchResultsCount));
            StatusMessage = $"Found {SearchResults.Count} matches";

            if (SearchResults.Count > 0)
            {
                FindNext();
            }
        }

        private void FindNext()
        {
            if (SearchResults.Count == 0) return;

            _currentSearchIndex = (_currentSearchIndex + 1) % SearchResults.Count;
            var result = SearchResults[_currentSearchIndex];
            CurrentOffset = result.Offset;
            SelectionStart = result.Offset;
            SelectionLength = result.Length;
            StatusMessage = $"Match {_currentSearchIndex + 1} of {SearchResults.Count}";
        }

        private void FindPrevious()
        {
            if (SearchResults.Count == 0) return;

            _currentSearchIndex = _currentSearchIndex <= 0 ? SearchResults.Count - 1 : _currentSearchIndex - 1;
            var result = SearchResults[_currentSearchIndex];
            CurrentOffset = result.Offset;
            SelectionStart = result.Offset;
            SelectionLength = result.Length;
            StatusMessage = $"Match {_currentSearchIndex + 1} of {SearchResults.Count}";
        }

        private void ShowReplaceDialog()
        {
            StatusMessage = "Replace dialog - use Find first, then Replace All";
        }

        private void ReplaceAll()
        {
            if (SearchResults.Count == 0 || _fileData == null)
            {
                StatusMessage = "No search results to replace";
                return;
            }

            var replaceText = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter replacement hex bytes (e.g., 'FF 00 AB'):",
                "Replace All",
                "");

            if (string.IsNullOrEmpty(replaceText)) return;

            try
            {
                var replaceBytes = replaceText.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToByte(s, 16))
                    .ToArray();

                int replacedCount = 0;
                foreach (var result in SearchResults.OrderByDescending(r => r.Offset))
                {
                    if (replaceBytes.Length == result.Length)
                    {
                        for (int i = 0; i < replaceBytes.Length; i++)
                        {
                            _fileData[result.Offset + i] = replaceBytes[i];
                            _modifiedOffsets.Add(result.Offset + i);
                        }
                        replacedCount++;
                    }
                }

                IsModified = true;
                RefreshDisplay();
                SearchResults.Clear();
                OnPropertyChanged(nameof(SearchResultsCount));
                StatusMessage = $"Replaced {replacedCount} occurrences";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Replace Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowGoToDialog()
        {
            var input = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter offset (hex with 0x prefix, or decimal):",
                "Go to Offset",
                $"0x{_currentOffset:X}");

            if (string.IsNullOrEmpty(input)) return;

            try
            {
                long offset;
                if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    offset = Convert.ToInt64(input[2..], 16);
                }
                else
                {
                    offset = long.Parse(input);
                }

                CurrentOffset = offset;
                StatusMessage = $"Jumped to offset 0x{offset:X8}";
            }
            catch
            {
                MessageBox.Show("Invalid offset format", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void QuickGoTo()
        {
            if (string.IsNullOrEmpty(_quickGoToOffset)) return;

            try
            {
                long offset;
                var input = _quickGoToOffset.Trim();
                if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    offset = Convert.ToInt64(input[2..], 16);
                }
                else
                {
                    offset = long.Parse(input);
                }

                CurrentOffset = offset;
                StatusMessage = $"Jumped to offset 0x{offset:X8}";
            }
            catch
            {
                StatusMessage = "Invalid offset format";
            }
        }
        #endregion

        #region Tools
        private void CompareFiles()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "All Files (*.*)|*.*",
                Title = "Select file to compare with"
            };

            if (dialog.ShowDialog() != true || _fileData == null) return;

            try
            {
                var compareData = File.ReadAllBytes(dialog.FileName);
                var differences = new List<long>();

                var maxLength = Math.Min(_fileData.Length, compareData.Length);
                for (long i = 0; i < maxLength; i++)
                {
                    if (_fileData[i] != compareData[i])
                    {
                        differences.Add(i);
                    }
                }

                if (_fileData.Length != compareData.Length)
                {
                    MessageBox.Show(
                        $"Files have different sizes:\nCurrent: {FormatFileSize(_fileData.Length)}\nCompare: {FormatFileSize(compareData.Length)}\n\nFound {differences.Count} differences in common area.",
                        "Compare Results",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        $"Found {differences.Count} differences",
                        "Compare Results",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                if (differences.Count > 0)
                {
                    CurrentOffset = differences[0];
                    StatusMessage = $"First difference at offset 0x{differences[0]:X8}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error comparing files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowChecksum()
        {
            if (_fileData == null) return;

            using var md5 = MD5.Create();
            using var sha1 = SHA1.Create();
            using var sha256 = SHA256.Create();

            var md5Hash = BitConverter.ToString(md5.ComputeHash(_fileData)).Replace("-", "");
            var sha1Hash = BitConverter.ToString(sha1.ComputeHash(_fileData)).Replace("-", "");
            var sha256Hash = BitConverter.ToString(sha256.ComputeHash(_fileData)).Replace("-", "");

            // CRC32
            uint crc32 = CalculateCrc32(_fileData);

            var message = $"File: {Path.GetFileName(_filePath)}\n" +
                         $"Size: {FormatFileSize(_fileData.Length)}\n\n" +
                         $"CRC32:  {crc32:X8}\n" +
                         $"MD5:    {md5Hash}\n" +
                         $"SHA1:   {sha1Hash}\n" +
                         $"SHA256: {sha256Hash}";

            MessageBox.Show(message, "Checksum Calculator", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void FillSelection()
        {
            if (_fileData == null || _selectionLength <= 0) return;

            var input = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter fill byte (hex, e.g., 'FF' or '00'):",
                "Fill Selection",
                "00");

            if (string.IsNullOrEmpty(input)) return;

            try
            {
                var fillByte = Convert.ToByte(input, 16);

                var oldData = new byte[_selectionLength];
                Array.Copy(_fileData, _selectionStart, oldData, 0, (int)_selectionLength);

                _undoStack.Push(new UndoAction
                {
                    Offset = _selectionStart,
                    OldData = oldData,
                    NewData = Enumerable.Repeat(fillByte, (int)_selectionLength).ToArray(),
                    Type = UndoActionType.Modify
                });
                _redoStack.Clear();

                for (long i = 0; i < _selectionLength; i++)
                {
                    _fileData[_selectionStart + i] = fillByte;
                    _modifiedOffsets.Add(_selectionStart + i);
                }

                IsModified = true;
                RefreshDisplay();
                StatusMessage = $"Filled {_selectionLength} bytes with 0x{fillByte:X2}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Fill Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InsertBytes()
        {
            StatusMessage = "Insert bytes - not supported (would change file size)";
            MessageBox.Show(
                "Inserting bytes would change the file size, which is not supported in this hex editor.\n\nYou can modify existing bytes or use Fill to overwrite sections.",
                "Insert Not Supported",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private async void AnalyzeFile()
        {
            if (_fileData == null) return;

            StatusMessage = "Analyzing file...";
            await Task.Run(() => AnalyzeFileStructure());
            StatusMessage = "Analysis complete";
        }

        private void AnalyzeFileStructure()
        {
            if (_fileData == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                FileStructure.Clear();

                // Detect file type
                var fileType = DetectFileType();
                var rootNode = new FileStructureNode
                {
                    Name = fileType,
                    Icon = "📄",
                    OffsetInfo = $"0x0 - 0x{_fileData.Length - 1:X}"
                };

                // Add detected structures
                AddDetectedStructures(rootNode);

                FileStructure.Add(rootNode);
            });
        }

        private string DetectFileType()
        {
            if (_fileData == null || _fileData.Length < 4) return "Unknown";

            // Check magic bytes
            if (_fileData.Length >= 8)
            {
                // Android boot image
                if (_fileData[0] == 'A' && _fileData[1] == 'N' && _fileData[2] == 'D' &&
                    _fileData[3] == 'R' && _fileData[4] == 'O' && _fileData[5] == 'I' &&
                    _fileData[6] == 'D' && _fileData[7] == '!')
                    return "Android Boot Image";

                // ELF
                if (_fileData[0] == 0x7F && _fileData[1] == 'E' && _fileData[2] == 'L' && _fileData[3] == 'F')
                    return "ELF Executable";

                // ZIP/APK
                if (_fileData[0] == 'P' && _fileData[1] == 'K' && _fileData[2] == 0x03 && _fileData[3] == 0x04)
                    return "ZIP Archive (possibly APK)";

                // Sparse image
                if (_fileData[0] == 0x3A && _fileData[1] == 0xFF && _fileData[2] == 0x26 && _fileData[3] == 0xED)
                    return "Android Sparse Image";

                // PNG
                if (_fileData[0] == 0x89 && _fileData[1] == 'P' && _fileData[2] == 'N' && _fileData[3] == 'G')
                    return "PNG Image";

                // JPEG
                if (_fileData[0] == 0xFF && _fileData[1] == 0xD8 && _fileData[2] == 0xFF)
                    return "JPEG Image";
            }

            // Check for text file
            bool isText = true;
            for (int i = 0; i < Math.Min(1000, _fileData.Length); i++)
            {
                if (_fileData[i] < 9 || (_fileData[i] > 13 && _fileData[i] < 32 && _fileData[i] != 27))
                {
                    isText = false;
                    break;
                }
            }
            if (isText) return "Text File";

            return "Binary File";
        }

        private void AddDetectedStructures(FileStructureNode root)
        {
            if (_fileData == null) return;

            // Look for common patterns
            var patterns = new[]
            {
                ("ANDROID!", "Android Boot Header"),
                ("EFI PART", "GPT Header"),
                ("MSDOS5.0", "FAT Boot Sector"),
                ("\x53\xEF", "EXT Superblock"),
                ("hsqs", "SquashFS"),
                ("\x1F\x8B", "GZIP Data"),
                ("\xFD\x37\x7A\x58\x5A\x00", "XZ Data"),
            };

            foreach (var (magic, name) in patterns)
            {
                var magicBytes = Encoding.ASCII.GetBytes(magic);
                for (long i = 0; i <= _fileData.Length - magicBytes.Length; i += 512)
                {
                    bool match = true;
                    for (int j = 0; j < magicBytes.Length; j++)
                    {
                        if (_fileData[i + j] != magicBytes[j])
                        {
                            match = false;
                            break;
                        }
                    }

                    if (match)
                    {
                        root.Children.Add(new FileStructureNode
                        {
                            Name = name,
                            Icon = "📦",
                            OffsetInfo = $"@ 0x{i:X8}"
                        });
                        break;
                    }
                }
            }
        }

        private void FindPatterns()
        {
            if (_fileData == null) return;

            SearchResults.Clear();

            // Find common patterns
            var patterns = new Dictionary<string, byte[]>
            {
                { "Android Boot Magic", Encoding.ASCII.GetBytes("ANDROID!") },
                { "ELF Magic", new byte[] { 0x7F, 0x45, 0x4C, 0x46 } },
                { "ZIP/APK", new byte[] { 0x50, 0x4B, 0x03, 0x04 } },
                { "PNG", new byte[] { 0x89, 0x50, 0x4E, 0x47 } },
                { "JPEG", new byte[] { 0xFF, 0xD8, 0xFF } },
                { "DEX", Encoding.ASCII.GetBytes("dex\n") },
                { "GZIP", new byte[] { 0x1F, 0x8B } },
            };

            foreach (var (name, pattern) in patterns)
            {
                for (long i = 0; i <= _fileData.Length - pattern.Length; i++)
                {
                    bool match = true;
                    for (int j = 0; j < pattern.Length; j++)
                    {
                        if (_fileData[i + j] != pattern[j])
                        {
                            match = false;
                            break;
                        }
                    }

                    if (match)
                    {
                        SearchResults.Add(new HexSearchResult
                        {
                            Offset = i,
                            OffsetText = $"0x{i:X8}",
                            Length = pattern.Length,
                            PreviewText = $"{name}"
                        });
                    }
                }
            }

            OnPropertyChanged(nameof(SearchResultsCount));
            StatusMessage = $"Found {SearchResults.Count} patterns";
        }

        private void FindStrings()
        {
            if (_fileData == null) return;

            SearchResults.Clear();
            const int minLength = 4;
            var currentString = new List<byte>();
            long stringStart = 0;

            for (long i = 0; i < _fileData.Length; i++)
            {
                var b = _fileData[i];
                if (b >= 32 && b < 127)
                {
                    if (currentString.Count == 0)
                        stringStart = i;
                    currentString.Add(b);
                }
                else
                {
                    if (currentString.Count >= minLength)
                    {
                        var str = Encoding.ASCII.GetString(currentString.ToArray());
                        SearchResults.Add(new HexSearchResult
                        {
                            Offset = stringStart,
                            OffsetText = $"0x{stringStart:X8}",
                            Length = currentString.Count,
                            PreviewText = str.Length > 50 ? str[..50] + "..." : str
                        });

                        if (SearchResults.Count >= 1000)
                        {
                            StatusMessage = $"Found {SearchResults.Count}+ strings (showing first 1000)";
                            OnPropertyChanged(nameof(SearchResultsCount));
                            return;
                        }
                    }
                    currentString.Clear();
                }
            }

            OnPropertyChanged(nameof(SearchResultsCount));
            StatusMessage = $"Found {SearchResults.Count} strings";
        }
        #endregion

        #region Bookmarks
        private void AddBookmark()
        {
            var name = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter bookmark name:",
                "Add Bookmark",
                $"Bookmark at 0x{_currentOffset:X8}");

            if (string.IsNullOrEmpty(name)) return;

            var bookmark = new HexBookmark
            {
                Name = name,
                Offset = _currentOffset,
                OffsetText = $"0x{_currentOffset:X8}",
                Description = $"Added at {DateTime.Now:HH:mm:ss}"
            };

            Bookmarks.Add(bookmark);
            StatusMessage = $"Bookmark added: {name}";
        }

        private void ManageBookmarks()
        {
            StatusMessage = "Bookmark management - use the Bookmarks panel";
        }

        private void GoToBookmark()
        {
            if (SelectedBookmark == null) return;
            CurrentOffset = SelectedBookmark.Offset;
            StatusMessage = $"Jumped to bookmark: {SelectedBookmark.Name}";
        }

        private void DeleteBookmark()
        {
            if (SelectedBookmark == null) return;
            var name = SelectedBookmark.Name;
            Bookmarks.Remove(SelectedBookmark);
            StatusMessage = $"Deleted bookmark: {name}";
        }

        private void PrevBookmark()
        {
            var prevBookmarks = Bookmarks.Where(b => b.Offset < _currentOffset).OrderByDescending(b => b.Offset).ToList();
            if (prevBookmarks.Count > 0)
            {
                CurrentOffset = prevBookmarks[0].Offset;
                StatusMessage = $"Jumped to: {prevBookmarks[0].Name}";
            }
        }

        private void NextBookmark()
        {
            var nextBookmarks = Bookmarks.Where(b => b.Offset > _currentOffset).OrderBy(b => b.Offset).ToList();
            if (nextBookmarks.Count > 0)
            {
                CurrentOffset = nextBookmarks[0].Offset;
                StatusMessage = $"Jumped to: {nextBookmarks[0].Name}";
            }
        }
        #endregion

        #region Export
        private void ExportHexDump()
        {
            if (_fileData == null) return;

            var dialog = new SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt",
                Title = "Export Hex Dump",
                FileName = Path.GetFileNameWithoutExtension(_filePath) + "_hexdump.txt"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                using var writer = new StreamWriter(dialog.FileName);
                writer.WriteLine($"Hex Dump of: {_filePath}");
                writer.WriteLine($"Size: {FormatFileSize(_fileData.Length)}");
                writer.WriteLine($"Generated: {DateTime.Now}");
                writer.WriteLine(new string('=', 80));
                writer.WriteLine();
                writer.WriteLine("Offset     00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F  ASCII");
                writer.WriteLine(new string('-', 80));

                for (long i = 0; i < _fileData.Length; i += 16)
                {
                    var hexPart = new StringBuilder();
                    var asciiPart = new StringBuilder();

                    for (int j = 0; j < 16; j++)
                    {
                        if (i + j < _fileData.Length)
                        {
                            var b = _fileData[i + j];
                            hexPart.Append($"{b:X2} ");
                            asciiPart.Append(b >= 32 && b < 127 ? (char)b : '.');
                        }
                        else
                        {
                            hexPart.Append("   ");
                        }
                    }

                    writer.WriteLine($"{i:X8}   {hexPart} {asciiPart}");
                }

                StatusMessage = $"Exported hex dump to {Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportSelection()
        {
            if (_fileData == null || _selectionLength <= 0) return;

            var dialog = new SaveFileDialog
            {
                Filter = "Binary Files (*.bin)|*.bin|All Files (*.*)|*.*",
                Title = "Export Selection",
                FileName = $"selection_0x{_selectionStart:X8}.bin"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                var selectedBytes = new byte[_selectionLength];
                Array.Copy(_fileData, _selectionStart, selectedBytes, 0, (int)_selectionLength);
                File.WriteAllBytes(dialog.FileName, selectedBytes);
                StatusMessage = $"Exported {_selectionLength} bytes";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportAnalysis()
        {
            if (_fileData == null) return;

            var dialog = new SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt",
                Title = "Export Analysis Report",
                FileName = Path.GetFileNameWithoutExtension(_filePath) + "_analysis.txt"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                using var md5 = MD5.Create();
                using var sha256 = SHA256.Create();

                using var writer = new StreamWriter(dialog.FileName);
                writer.WriteLine($"File Analysis Report");
                writer.WriteLine(new string('=', 60));
                writer.WriteLine($"File: {_filePath}");
                writer.WriteLine($"Size: {FormatFileSize(_fileData.Length)} ({_fileData.Length} bytes)");
                writer.WriteLine($"Generated: {DateTime.Now}");
                writer.WriteLine();
                writer.WriteLine("Checksums:");
                writer.WriteLine($"  CRC32:  {CalculateCrc32(_fileData):X8}");
                writer.WriteLine($"  MD5:    {BitConverter.ToString(md5.ComputeHash(_fileData)).Replace("-", "")}");
                writer.WriteLine($"  SHA256: {BitConverter.ToString(sha256.ComputeHash(_fileData)).Replace("-", "")}");
                writer.WriteLine();
                writer.WriteLine($"File Type: {DetectFileType()}");
                writer.WriteLine();
                writer.WriteLine("Byte Distribution:");

                var byteCounts = new int[256];
                foreach (var b in _fileData)
                    byteCounts[b]++;

                var nonZeroBytes = byteCounts.Select((count, index) => (index, count)).Where(x => x.count > 0).OrderByDescending(x => x.count).Take(20);
                foreach (var (index, count) in nonZeroBytes)
                {
                    var percentage = (count * 100.0) / _fileData.Length;
                    writer.WriteLine($"  0x{index:X2}: {count,10} ({percentage:F2}%)");
                }

                StatusMessage = $"Exported analysis to {Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion

        #region Help
        private void ShowShortcuts()
        {
            var shortcuts = @"Keyboard Shortcuts:

File Operations:
  Ctrl+O    Open file
  Ctrl+S    Save file
  Ctrl+Shift+S    Save as

Edit Operations:
  Ctrl+Z    Undo
  Ctrl+Y    Redo
  Ctrl+X    Cut
  Ctrl+C    Copy
  Ctrl+V    Paste
  Ctrl+A    Select all
  Del       Delete selection

Navigation:
  Ctrl+G    Go to offset
  Ctrl+F    Find
  F3        Find next
  Shift+F3  Find previous
  Ctrl+H    Replace

Bookmarks:
  Ctrl+B    Add bookmark
  Ctrl+Up   Previous bookmark
  Ctrl+Down Next bookmark";

            MessageBox.Show(shortcuts, "Keyboard Shortcuts", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShowAbout()
        {
            MessageBox.Show(
                "Hex Editor\nPart of PhoneX Manager\n\nVersion 1.0\nby Xman Studio\n\nFeatures:\n- View and edit binary files\n- Search hex/text patterns\n- Data inspector\n- Bookmarks\n- File comparison\n- Checksum calculator\n\n(c) 2024-2025 Xman Studio",
                "About Hex Editor",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        #endregion

        #region Data Inspector
        private void UpdateDataInspector()
        {
            OnPropertyChanged(nameof(DataInt8));
            OnPropertyChanged(nameof(DataInt16));
            OnPropertyChanged(nameof(DataInt32));
            OnPropertyChanged(nameof(DataInt64));
            OnPropertyChanged(nameof(DataUInt8));
            OnPropertyChanged(nameof(DataUInt16));
            OnPropertyChanged(nameof(DataUInt32));
            OnPropertyChanged(nameof(DataUInt64));
            OnPropertyChanged(nameof(DataFloat));
            OnPropertyChanged(nameof(DataDouble));
            OnPropertyChanged(nameof(DataString));
            OnPropertyChanged(nameof(DataBinary));
        }

        private string GetDataValue<T>() where T : struct
        {
            if (_fileData == null || _currentOffset >= _fileData.Length)
                return "-";

            try
            {
                var size = System.Runtime.InteropServices.Marshal.SizeOf<T>();
                if (_currentOffset + size > _fileData.Length)
                    return "-";

                var bytes = new byte[size];
                Array.Copy(_fileData, _currentOffset, bytes, 0, size);

                if (!_isLittleEndian && size > 1)
                    Array.Reverse(bytes);

                if (typeof(T) == typeof(sbyte)) return ((sbyte)bytes[0]).ToString();
                if (typeof(T) == typeof(byte)) return bytes[0].ToString();
                if (typeof(T) == typeof(short)) return BitConverter.ToInt16(bytes, 0).ToString();
                if (typeof(T) == typeof(ushort)) return BitConverter.ToUInt16(bytes, 0).ToString();
                if (typeof(T) == typeof(int)) return BitConverter.ToInt32(bytes, 0).ToString();
                if (typeof(T) == typeof(uint)) return BitConverter.ToUInt32(bytes, 0).ToString();
                if (typeof(T) == typeof(long)) return BitConverter.ToInt64(bytes, 0).ToString();
                if (typeof(T) == typeof(ulong)) return BitConverter.ToUInt64(bytes, 0).ToString();
                if (typeof(T) == typeof(float)) return BitConverter.ToSingle(bytes, 0).ToString("G");
                if (typeof(T) == typeof(double)) return BitConverter.ToDouble(bytes, 0).ToString("G");

                return "-";
            }
            catch
            {
                return "-";
            }
        }

        private string GetStringValue()
        {
            if (_fileData == null || _currentOffset >= _fileData.Length)
                return "";

            var length = Math.Min(64, (int)(_fileData.Length - _currentOffset));
            var bytes = new byte[length];
            Array.Copy(_fileData, _currentOffset, bytes, 0, length);

            // Find null terminator
            var nullIndex = Array.IndexOf(bytes, (byte)0);
            if (nullIndex >= 0)
                length = nullIndex;

            try
            {
                return Encoding.UTF8.GetString(bytes, 0, length);
            }
            catch
            {
                return Encoding.ASCII.GetString(bytes, 0, length);
            }
        }

        private string GetBinaryValue()
        {
            if (_fileData == null || _currentOffset >= _fileData.Length)
                return "";

            return Convert.ToString(_fileData[_currentOffset], 2).PadLeft(8, '0');
        }
        #endregion

        #region Helpers
        private void RefreshLineAt(long offset)
        {
            var lineIndex = (int)(offset / _bytesPerLine);
            if (lineIndex >= 0 && lineIndex < DisplayLines.Count)
            {
                var lineOffset = (long)lineIndex * _bytesPerLine;
                DisplayLines[lineIndex] = CreateHexLine(lineOffset);
            }
        }

        private string GetPreviewText(long offset, int length)
        {
            if (_fileData == null) return "";
            var previewLength = Math.Min(length, (int)(_fileData.Length - offset));
            var bytes = new byte[previewLength];
            Array.Copy(_fileData, offset, bytes, 0, previewLength);
            return BitConverter.ToString(bytes).Replace("-", " ");
        }

        private static string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double size = bytes;
            while (size >= 1024 && i < suffixes.Length - 1)
            {
                size /= 1024;
                i++;
            }
            return $"{size:F2} {suffixes[i]}";
        }

        private static uint CalculateCrc32(byte[] data)
        {
            uint[] table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 0; j < 8; j++)
                    crc = (crc & 1) == 1 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
                table[i] = crc;
            }

            uint result = 0xFFFFFFFF;
            foreach (var b in data)
                result = table[(result ^ b) & 0xFF] ^ (result >> 8);
            return result ^ 0xFFFFFFFF;
        }

        private void SetBytesPerLine(object? param)
        {
            if (param != null && int.TryParse(param.ToString(), out var value))
            {
                BytesPerLine = value;
            }
        }

        private void AddToRecentFiles(string path)
        {
            if (RecentFiles.Contains(path))
                RecentFiles.Remove(path);

            RecentFiles.Insert(0, path);

            while (RecentFiles.Count > 10)
                RecentFiles.RemoveAt(RecentFiles.Count - 1);

            SaveRecentFiles();
        }

        private void LoadRecentFiles()
        {
            var recentPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhoneRomFlashTool",
                "recent_hex_files.txt");

            if (File.Exists(recentPath))
            {
                foreach (var line in File.ReadAllLines(recentPath).Take(10))
                {
                    if (File.Exists(line))
                        RecentFiles.Add(line);
                }
            }
        }

        private void SaveRecentFiles()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhoneRomFlashTool");

            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, "recent_hex_files.txt"), RecentFiles);
        }
        #endregion

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        #endregion
    }

    #region Supporting Classes
    public class HexDisplayLine
    {
        public long Offset { get; set; }
        public string OffsetText { get; set; } = "";
        public string HexText { get; set; } = "";
        public string AsciiText { get; set; } = "";
        public Brush? Background { get; set; }
    }

    public class HexBookmark
    {
        public string Name { get; set; } = "";
        public long Offset { get; set; }
        public string OffsetText { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public class HexSearchResult
    {
        public long Offset { get; set; }
        public string OffsetText { get; set; } = "";
        public int Length { get; set; }
        public string PreviewText { get; set; } = "";
    }

    public class FileStructureNode
    {
        public string Name { get; set; } = "";
        public string Icon { get; set; } = "";
        public string OffsetInfo { get; set; } = "";
        public ObservableCollection<FileStructureNode> Children { get; } = new();
    }

    public class UndoAction
    {
        public long Offset { get; set; }
        public byte[] OldData { get; set; } = Array.Empty<byte>();
        public byte[] NewData { get; set; } = Array.Empty<byte>();
        public UndoActionType Type { get; set; }
    }

    public enum UndoActionType
    {
        Modify,
        Insert,
        Delete
    }

    public class HexRelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public HexRelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => _execute(parameter);
    }
    #endregion
}
