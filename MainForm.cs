using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using Path = System.IO.Path;

namespace AwesomePDFSearch
{
    public partial class MainForm : Form
    {
        private TextBox txtDirectory;
        private Button btnBrowse;
        private Button btnSearch;
        private TextBox txtSearchText;
        private ListView lvResults;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel lblStatus;
        private ToolStripStatusLabel lblProgress;
        private ToolStripProgressBar progressBar;
        private BackgroundWorker bgWorker;
        private AutoCompleteStringCollection directoryHistory;
        private AutoCompleteStringCollection searchHistory;
        private int sortColumn = -1;
        private SortOrder sortOrder = SortOrder.Ascending;
        private long totalBytesScanned = 0;
        private DateTime searchStartTime;
        private int itemsFound = 0;

        public MainForm()
        {
            InitializeComponent();
            this.WindowState = FormWindowState.Maximized;
            LoadHistory();
        }

        private void InitializeComponent()
        {
            this.Text = "Awesome PDF Search";
            this.Size = new Size(800, 600);
            this.MinimumSize = new Size(600, 400);
            this.KeyPreview = true;
            this.KeyDown += MainForm_KeyDown;

            // Directory selection controls
            Label lblDir = new Label { Text = "Directory:", Location = new Point(10, 15), AutoSize = true };

            txtDirectory = new TextBox
            {
                Location = new Point(80, 12),
                Width = 500,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            txtDirectory.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            txtDirectory.AutoCompleteSource = AutoCompleteSource.CustomSource;
            directoryHistory = new AutoCompleteStringCollection();
            txtDirectory.AutoCompleteCustomSource = directoryHistory;
            txtDirectory.KeyDown += TextBox_KeyDown;

            btnBrowse = new Button
            {
                Text = "Bro&wse...",
                Location = new Point(590, 10),
                Width = 80,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnBrowse.Click += BtnBrowse_Click;

            // Search text controls
            Label lblSearch = new Label { Text = "Search:", Location = new Point(10, 45), AutoSize = true };

            txtSearchText = new TextBox
            {
                Location = new Point(80, 42),
                Width = 500,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            txtSearchText.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            txtSearchText.AutoCompleteSource = AutoCompleteSource.CustomSource;
            searchHistory = new AutoCompleteStringCollection();
            txtSearchText.AutoCompleteCustomSource = searchHistory;
            txtSearchText.KeyDown += TextBox_KeyDown;

            btnSearch = new Button
            {
                Text = "&Search",
                Location = new Point(590, 40),
                Width = 80,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnSearch.Click += BtnSearch_Click;

            // Results ListView
            lvResults = new ListView
            {
                Location = new Point(10, 75),
                Width = 760,
                Height = 450,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false,
                OwnerDraw = true
            };

            lvResults.Columns.Add("File Name", 150);
            lvResults.Columns.Add("Extract", 300);
            lvResults.Columns.Add("Size (bytes)", 100, HorizontalAlignment.Right);
            lvResults.Columns.Add("Last Modified", 130);
            lvResults.Columns.Add("Full Path", 400);
            lvResults.Columns.Add("", 80); // For Open button

            lvResults.ColumnClick += LvResults_ColumnClick;
            lvResults.DrawColumnHeader += LvResults_DrawColumnHeader;
            lvResults.DrawSubItem += LvResults_DrawSubItem;
            lvResults.MouseClick += LvResults_MouseClick;
            lvResults.MouseDoubleClick += LvResults_MouseDoubleClick;
            lvResults.SizeChanged += LvResults_SizeChanged;

            // Status bar
            statusStrip = new StatusStrip();
            lblStatus = new ToolStripStatusLabel { Text = "Ready", Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            lblProgress = new ToolStripStatusLabel { TextAlign = ContentAlignment.MiddleRight, Visible = false };
            progressBar = new ToolStripProgressBar { Visible = false, Width = 200 };
            statusStrip.Items.Add(lblStatus);
            statusStrip.Items.Add(lblProgress);
            statusStrip.Items.Add(progressBar);

            // Background worker
            bgWorker = new BackgroundWorker
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };
            bgWorker.DoWork += BgWorker_DoWork;
            bgWorker.ProgressChanged += BgWorker_ProgressChanged;
            bgWorker.RunWorkerCompleted += BgWorker_RunWorkerCompleted;

            // Add controls
            this.Controls.AddRange(new Control[] { lblDir, txtDirectory, btnBrowse, lblSearch, txtSearchText, btnSearch, lvResults, statusStrip });
        }

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && bgWorker.IsBusy)
            {
                bgWorker.CancelAsync();
                lblStatus.Text = "Cancelling search...";
            }
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                BtnSearch_Click(sender, e);
            }
        }

        private void LvResults_SizeChanged(object sender, EventArgs e)
        {
            int totalWidth = lvResults.ClientSize.Width;
            int fixedWidth = 150 + 100 + 130 + 80;
            int remainingWidth = totalWidth - fixedWidth;

            if (remainingWidth > 0)
            {
                lvResults.Columns[1].Width = (int)(remainingWidth * 0.4);
                lvResults.Columns[4].Width = (int)(remainingWidth * 0.6);
            }
        }

        private void LvResults_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (e.Column == 5) return; // Don't sort the Open button column

            if (sortColumn == e.Column)
            {
                sortOrder = sortOrder == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
            }
            else
            {
                sortColumn = e.Column;
                sortOrder = SortOrder.Ascending;
            }

            lvResults.ListViewItemSorter = new ListViewItemComparer(sortColumn, sortOrder);
            lvResults.Sort();
        }

        private void LvResults_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            e.DrawDefault = true;
        }

        private void LvResults_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            if (e.ColumnIndex == 5) // Open button column
            {
                e.DrawBackground();

                Rectangle btnRect = new Rectangle(e.Bounds.X + 5, e.Bounds.Y + 2, e.Bounds.Width - 10, e.Bounds.Height - 4);
                ButtonRenderer.DrawButton(e.Graphics, btnRect, "Open", this.Font, false, System.Windows.Forms.VisualStyles.PushButtonState.Normal);
            }
            else
            {
                e.DrawDefault = true;
            }
        }

        private void LvResults_MouseClick(object sender, MouseEventArgs e)
        {
            var hitTest = lvResults.HitTest(e.Location);
            if (hitTest.Item != null && hitTest.SubItem != null)
            {
                int columnIndex = hitTest.Item.SubItems.IndexOf(hitTest.SubItem);
                if (columnIndex == 5)
                {
                    OpenFile(hitTest.Item);
                }
            }
        }

        private void LvResults_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            var hitTest = lvResults.HitTest(e.Location);
            if (hitTest.Item != null)
            {
                OpenFile(hitTest.Item);
            }
        }

        private void OpenFile(ListViewItem item)
        {
            var result = (SearchResult)item.Tag;
            try
            {
                System.Diagnostics.Process.Start(result.FullPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.SelectedPath = txtDirectory.Text;
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtDirectory.Text = dialog.SelectedPath;
                }
            }
        }

        private void BtnSearch_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtDirectory.Text))
            {
                MessageBox.Show("Please select a directory.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!Directory.Exists(txtDirectory.Text))
            {
                MessageBox.Show("Directory does not exist.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(txtSearchText.Text))
            {
                MessageBox.Show("Please enter search text.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            AddToHistory(directoryHistory, txtDirectory.Text);
            AddToHistory(searchHistory, txtSearchText.Text);
            SaveHistory();

            lvResults.BeginUpdate();
            lvResults.Items.Clear();
            lvResults.EndUpdate();

            btnSearch.Enabled = false;
            btnBrowse.Enabled = false;
            txtDirectory.Enabled = false;
            txtSearchText.Enabled = false;
            progressBar.Visible = true;
            lblProgress.Visible = true;
            lblStatus.Text = "Searching... (Press ESC to cancel)";

            totalBytesScanned = 0;
            itemsFound = 0;
            searchStartTime = DateTime.Now;

            var searchParams = new SearchParams
            {
                Directory = txtDirectory.Text,
                SearchText = txtSearchText.Text
            };

            bgWorker.RunWorkerAsync(searchParams);
        }

        private void BgWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            var p = (SearchParams)e.Argument;
            var pdfFiles = Directory.GetFiles(p.Directory, "*.pdf", SearchOption.AllDirectories);

            int total = pdfFiles.Length;
            int current = 0;

            foreach (var file in pdfFiles)
            {
                if (bgWorker.CancellationPending)
                {
                    e.Cancel = true;
                    return;
                }

                current++;

                try
                {
                    var fileInfo = new FileInfo(file);
                    long fileSize = fileInfo.Length;

                    var progressData = new ProgressData
                    {
                        Percentage = (int)((current / (double)total) * 100),
                        CurrentFile = file,
                        BytesScanned = fileSize
                    };

                    var extract = SearchPdfContent(file, p.SearchText);
                    if (extract != null)
                    {
                        var result = new SearchResult
                        {
                            FileName = Path.GetFileName(file),
                            Extract = extract,
                            FileSize = fileSize,
                            LastModified = fileInfo.LastWriteTime,
                            FullPath = file
                        };
                        progressData.Result = result;
                    }

                    bgWorker.ReportProgress(progressData.Percentage, progressData);
                }
                catch
                {
                    // Skip files that can't be read
                }
            }
        }

        private string SearchPdfContent(string filePath, string searchText)
        {
            using (var reader = new PdfReader(filePath))
            {
                for (int i = 1; i <= reader.NumberOfPages; i++)
                {
                    string text = PdfTextExtractor.GetTextFromPage(reader, i);
                    int index = text.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);

                    if (index >= 0)
                    {
                        int start = Math.Max(0, index - 50);
                        int length = Math.Min(150, text.Length - start);
                        string extract = text.Substring(start, length);

                        extract = System.Text.RegularExpressions.Regex.Replace(extract, @"\s+", " ").Trim();

                        if (start > 0) extract = "..." + extract;
                        if (start + length < text.Length) extract = extract + "...";

                        return extract;
                    }
                }
            }
            return null;
        }

        private void BgWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            var data = (ProgressData)e.UserState;
            progressBar.Value = data.Percentage;

            totalBytesScanned += data.BytesScanned;

            TimeSpan elapsed = DateTime.Now - searchStartTime;
            double mbScanned = totalBytesScanned / (1024.0 * 1024.0);
            double mbPerSecond = elapsed.TotalSeconds > 0 ? mbScanned / elapsed.TotalSeconds : 0;

            lblStatus.Text = $"Scanning: {Path.GetFileName(data.CurrentFile)} (Press ESC to cancel)";
            lblProgress.Text = $"{mbPerSecond:F2} MB/s | {mbScanned:F2} MB scanned | Found: {itemsFound}";

            if (data.Result != null)
            {
                itemsFound++;

                lvResults.BeginUpdate();
                var item = new ListViewItem(new[]
                {
                    data.Result.FileName,
                    data.Result.Extract,
                    data.Result.FileSize.ToString("N0"),
                    data.Result.LastModified.ToString("yyyy-MM-dd HH:mm:ss"),
                    data.Result.FullPath,
                    ""
                });
                item.Tag = data.Result;
                lvResults.Items.Add(item);
                lvResults.EndUpdate();
            }
        }

        private void BgWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            progressBar.Visible = false;
            btnSearch.Enabled = true;
            btnBrowse.Enabled = true;
            txtDirectory.Enabled = true;
            txtSearchText.Enabled = true;

            TimeSpan elapsed = DateTime.Now - searchStartTime;
            double mbScanned = totalBytesScanned / (1024.0 * 1024.0);
            double mbPerSecond = elapsed.TotalSeconds > 0 ? mbScanned / elapsed.TotalSeconds : 0;

            if (e.Error != null)
            {
                lblStatus.Text = "Error occurred during search";
                lblProgress.Visible = false;
                MessageBox.Show($"Error: {e.Error.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else if (e.Cancelled)
            {
                lblStatus.Text = $"Search cancelled - Found {itemsFound} PDF(s)";
                lblProgress.Text = $"{mbScanned:F2} MB scanned in {elapsed.TotalSeconds:F1}s | Avg: {mbPerSecond:F2} MB/s";
            }
            else
            {
                lblStatus.Text = $"Search complete - Found {itemsFound} PDF(s) containing '{txtSearchText.Text}'";
                lblProgress.Text = $"{mbScanned:F2} MB scanned in {elapsed.TotalSeconds:F1}s | Avg: {mbPerSecond:F2} MB/s";
            }
        }

        private void AddToHistory(AutoCompleteStringCollection collection, string item)
        {
            if (!collection.Contains(item))
            {
                collection.Add(item);
            }
        }

        private void LoadHistory()
        {
            try
            {
                string historyFile = Path.Combine(Application.UserAppDataPath, "history.txt");
                if (File.Exists(historyFile))
                {
                    var lines = File.ReadAllLines(historyFile);
                    bool isDirectory = true;
                    foreach (var line in lines)
                    {
                        if (line == "[DIRECTORIES]") { isDirectory = true; continue; }
                        if (line == "[SEARCHES]") { isDirectory = false; continue; }

                        if (isDirectory)
                            directoryHistory.Add(line);
                        else
                            searchHistory.Add(line);
                    }

                    if (directoryHistory.Count > 0)
                        txtDirectory.Text = directoryHistory[directoryHistory.Count - 1];
                    if (searchHistory.Count > 0)
                        txtSearchText.Text = searchHistory[searchHistory.Count - 1];
                }
            }
            catch { }
        }

        private void SaveHistory()
        {
            try
            {
                string historyFile = Path.Combine(Application.UserAppDataPath, "history.txt");
                var lines = new List<string>();
                lines.Add("[DIRECTORIES]");
                foreach (string dir in directoryHistory)
                    lines.Add(dir);
                lines.Add("[SEARCHES]");
                foreach (string search in searchHistory)
                    lines.Add(search);
                File.WriteAllLines(historyFile, lines);
            }
            catch { }
        }

        private class SearchParams
        {
            public string Directory { get; set; }
            public string SearchText { get; set; }
        }

        private class SearchResult
        {
            public string FileName { get; set; }
            public string Extract { get; set; }
            public long FileSize { get; set; }
            public DateTime LastModified { get; set; }
            public string FullPath { get; set; }
        }

        private class ProgressData
        {
            public int Percentage { get; set; }
            public string CurrentFile { get; set; }
            public long BytesScanned { get; set; }
            public SearchResult Result { get; set; }
        }

        private class ListViewItemComparer : System.Collections.IComparer
        {
            private int col;
            private SortOrder order;

            public ListViewItemComparer(int column, SortOrder order)
            {
                this.col = column;
                this.order = order;
            }

            public int Compare(object x, object y)
            {
                int result;
                ListViewItem itemX = (ListViewItem)x;
                ListViewItem itemY = (ListViewItem)y;

                if (col == 2) // File size column
                {
                    long sizeX = ((SearchResult)itemX.Tag).FileSize;
                    long sizeY = ((SearchResult)itemY.Tag).FileSize;
                    result = sizeX.CompareTo(sizeY);
                }
                else if (col == 3) // Date column
                {
                    DateTime dateX = ((SearchResult)itemX.Tag).LastModified;
                    DateTime dateY = ((SearchResult)itemY.Tag).LastModified;
                    result = dateX.CompareTo(dateY);
                }
                else
                {
                    result = String.Compare(itemX.SubItems[col].Text, itemY.SubItems[col].Text);
                }

                return order == SortOrder.Ascending ? result : -result;
            }
        }
    }
}