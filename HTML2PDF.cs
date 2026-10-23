// HTML2PDF - 批量把本地 HTML 转成 PDF
// Copyright (c) 2026 10MillionTones
// Licensed under the MIT License.
// https://github.com/10MillionTones/HTML2PDF

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("HTML 批量转 PDF")]
[assembly: AssemblyDescription("批量把本地 HTML 文件转换为 PDF")]
[assembly: AssemblyProduct("HTML转PDF")]
[assembly: AssemblyCompany("10MillionTones")]
[assembly: AssemblyCopyright("Copyright (c) 2026 10MillionTones")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace Html2Pdf
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                MessageBox.Show(e.Exception.Message, "出错了", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Application.Run(new MainForm(args));
        }
    }

    internal sealed class MainForm : Form
    {
        private static readonly string[] PaperNames = { "跟随原文件", "A4", "A3", "A5", "Letter", "Legal" };
        private static readonly string[] OrientNames = { "自动", "纵向", "横向" };
        private static readonly string[] MarginNames = { "跟随原文件", "0mm", "5mm", "10mm", "15mm", "20mm", "25mm" };

        private readonly ListView _list = new ListView();
        private readonly TextBox _txtOut = new TextBox();
        private readonly TextBox _txtBrowser = new TextBox();
        private readonly RadioButton _rbSource = new RadioButton();
        private readonly RadioButton _rbCustom = new RadioButton();
        private readonly ComboBox _cboPaper = new ComboBox();
        private readonly ComboBox _cboOrient = new ComboBox();
        private readonly ComboBox _cboMargin = new ComboBox();
        private readonly CheckBox _chkSkip = new CheckBox();
        private readonly NumericUpDown _numWorkers = new NumericUpDown();
        private readonly NumericUpDown _numWait = new NumericUpDown();
        private readonly Button _btnOutBrowse = new Button();
        private readonly Button _btnStart = new Button();
        private readonly Button _btnStop = new Button();
        private readonly Button _btnOpen = new Button();
        private readonly ProgressBar _bar = new ProgressBar();
        private readonly Label _lblStatus = new Label();
        private readonly TextBox _log = new TextBox();
        private readonly List<Button> _editButtons = new List<Button>();
        private readonly ToolTip _tips = new ToolTip();

        private List<Job> _jobs = new List<Job>();
        private int _cursor = -1;
        private int _threadsDone;
        private int _workers = 1;
        private int _ok;
        private int _fail;
        private int _skipped;
        private volatile bool _running;
        private volatile bool _cancel;
        private string _tempRoot;
        private readonly Stopwatch _watch = new Stopwatch();

        private sealed class Job
        {
            public string Src;
            public string Dst;
            public bool Skip;
        }

        public MainForm(string[] args)
        {
            Text = "HTML 批量转 PDF";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1000, 730);
            MinimumSize = new Size(880, 640);
            Font = new Font("Microsoft YaHei", 9F);
            AllowDrop = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            BuildUi();

            string browser = DetectBrowser();
            if (browser != null)
            {
                _txtBrowser.Text = browser;
            }
            else
            {
                AppendLog("没有自动找到 Edge 或 Chrome，请点“选择...”手动指定 msedge.exe / chrome.exe 的路径。");
            }

            AppendLog("支持 .html / .htm，可多选文件、添加整个文件夹（含子目录），也可把文件或文件夹直接拖进列表。");
            AppendLog("转换使用本机浏览器的打印引擎，输出矢量 PDF，文字可复制、可搜索。");

            if (args != null && args.Length > 0) AddPaths(args, true);
        }

        // ---------------- UI ----------------

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowCount = 8;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 40F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 60F));
            root.Padding = new Padding(10, 8, 10, 10);
            Controls.Add(root);

            // 1. 工具栏
            FlowLayoutPanel toolbar = new FlowLayoutPanel();
            toolbar.Dock = DockStyle.Fill;
            toolbar.WrapContents = false;
            toolbar.Margin = new Padding(0, 0, 0, 4);
            root.Controls.Add(toolbar, 0, 0);

            toolbar.Controls.Add(NewButton("添加文件", 96, AddFilesClick));
            toolbar.Controls.Add(NewButton("添加文件夹", 106, AddFolderClick));
            toolbar.Controls.Add(NewButton("移除选中", 96, RemoveSelectedClick));
            toolbar.Controls.Add(NewButton("清空", 76, ClearClick));
            Label hint = new Label();
            hint.Text = "（可拖拽文件或文件夹到此处）";
            hint.AutoSize = true;
            hint.ForeColor = Color.Gray;
            hint.Margin = new Padding(8, 8, 0, 0);
            toolbar.Controls.Add(hint);

            // 2. 浏览器
            TableLayoutPanel tBrowser = new TableLayoutPanel();
            tBrowser.Dock = DockStyle.Fill;
            tBrowser.ColumnCount = 3;
            tBrowser.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72F));
            tBrowser.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tBrowser.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
            tBrowser.RowCount = 1;
            tBrowser.Margin = new Padding(0, 0, 0, 6);
            root.Controls.Add(tBrowser, 0, 1);

            Label lblBrowser = new Label();
            lblBrowser.Text = "打印引擎：";
            lblBrowser.Dock = DockStyle.Fill;
            lblBrowser.TextAlign = ContentAlignment.MiddleLeft;
            tBrowser.Controls.Add(lblBrowser, 0, 0);

            _txtBrowser.Dock = DockStyle.Fill;
            _txtBrowser.ReadOnly = true;
            _txtBrowser.Margin = new Padding(0, 3, 6, 3);
            tBrowser.Controls.Add(_txtBrowser, 1, 0);

            Button btnPick = NewButton("选择...", 84, PickBrowserClick);
            btnPick.Dock = DockStyle.Fill;
            btnPick.Margin = new Padding(0, 2, 0, 2);
            tBrowser.Controls.Add(btnPick, 2, 0);

            // 3. 文件列表
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = true;
            _list.HideSelection = false;
            _list.Margin = new Padding(0, 0, 0, 8);
            _list.Columns.Add("文件", 240);
            _list.Columns.Add("状态", 84);
            _list.Columns.Add("输出到", -2);
            _list.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Delete) RemoveSelected();
            };
            _list.AllowDrop = true;
            _list.DragEnter += ListDragEnter;
            _list.DragDrop += ListDragDrop;
            root.Controls.Add(_list, 0, 2);

            // 4. 输出位置
            GroupBox grpOut = new GroupBox();
            grpOut.Text = "保存位置";
            grpOut.Dock = DockStyle.Fill;
            grpOut.Margin = new Padding(0, 0, 0, 8);
            root.Controls.Add(grpOut, 0, 3);

            TableLayoutPanel tOut = new TableLayoutPanel();
            tOut.Dock = DockStyle.Fill;
            tOut.ColumnCount = 3;
            tOut.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
            tOut.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tOut.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));
            tOut.RowCount = 2;
            tOut.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            tOut.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            tOut.Padding = new Padding(6, 4, 6, 2);
            grpOut.Controls.Add(tOut);

            _rbSource.Text = "存到源文件旁边";
            _rbSource.Dock = DockStyle.Fill;
            _rbSource.Checked = true;
            _rbSource.CheckedChanged += OutputModeChanged;
            tOut.Controls.Add(_rbSource, 0, 0);
            tOut.SetColumnSpan(_rbSource, 3);

            _rbCustom.Text = "指定目录";
            _rbCustom.Dock = DockStyle.Fill;
            _rbCustom.CheckedChanged += OutputModeChanged;
            tOut.Controls.Add(_rbCustom, 0, 1);

            _txtOut.Dock = DockStyle.Fill;
            _txtOut.Margin = new Padding(0, 2, 6, 2);
            _txtOut.TextChanged += delegate { RefreshOutputPreview(); };
            tOut.Controls.Add(_txtOut, 1, 1);

            _btnOutBrowse.Text = "浏览...";
            _btnOutBrowse.Dock = DockStyle.Fill;
            _btnOutBrowse.Margin = new Padding(0, 1, 0, 1);
            _btnOutBrowse.Click += BrowseOutputClick;
            tOut.Controls.Add(_btnOutBrowse, 2, 1);

            // 5. 页面设置
            GroupBox grpPage = new GroupBox();
            grpPage.Text = "页面设置（选“跟随原文件”则不改动 HTML 原有样式）";
            grpPage.Dock = DockStyle.Fill;
            grpPage.Margin = new Padding(0, 0, 0, 8);
            root.Controls.Add(grpPage, 0, 4);

            FlowLayoutPanel pageFlow = new FlowLayoutPanel();
            pageFlow.Dock = DockStyle.Fill;
            pageFlow.WrapContents = false;
            pageFlow.Padding = new Padding(6, 4, 6, 0);
            grpPage.Controls.Add(pageFlow);

            pageFlow.Controls.Add(NewLabel("纸张"));
            pageFlow.Controls.Add(NewCombo(_cboPaper, PaperNames, 104));
            _cboPaper.SelectedIndex = 1; // 默认 A4（浏览器无头模式默认是 Letter）
            pageFlow.Controls.Add(NewLabel("方向"));
            pageFlow.Controls.Add(NewCombo(_cboOrient, OrientNames, 84));
            pageFlow.Controls.Add(NewLabel("页边距"));
            pageFlow.Controls.Add(NewCombo(_cboMargin, MarginNames, 112));

            _chkSkip.Text = "跳过已存在的 PDF";
            _chkSkip.AutoSize = true;
            _chkSkip.Checked = true;
            _chkSkip.Margin = new Padding(10, 4, 0, 0);
            pageFlow.Controls.Add(_chkSkip);

            // 6. 运行控制
            FlowLayoutPanel runFlow = new FlowLayoutPanel();
            runFlow.Dock = DockStyle.Fill;
            runFlow.WrapContents = false;
            runFlow.Margin = new Padding(0, 0, 0, 6);
            root.Controls.Add(runFlow, 0, 5);

            runFlow.Controls.Add(NewLabel("并发数"));
            _numWorkers.Minimum = 1;
            _numWorkers.Maximum = 4;
            _numWorkers.Value = 1;
            _numWorkers.Width = 48;
            _numWorkers.Margin = new Padding(0, 7, 10, 0);
            runFlow.Controls.Add(_numWorkers);

            runFlow.Controls.Add(NewLabel("渲染等待"));
            _numWait.Minimum = 0;
            _numWait.Maximum = 20000;
            _numWait.Increment = 500;
            _numWait.Value = 0;
            _numWait.Width = 66;
            _numWait.Margin = new Padding(0, 7, 4, 0);
            runFlow.Controls.Add(_numWait);
            Label lblMs = NewLabel("毫秒");
            lblMs.ForeColor = Color.Gray;
            runFlow.Controls.Add(lblMs);
            _tips.SetToolTip(_numWait, "页面内容靠脚本异步生成时才需要填，例如 2000。留 0 表示加载完就打印，速度最快。");

            _btnStart.Text = "开始转换";
            _btnStart.Width = 120;
            _btnStart.Height = 32;
            _btnStart.Margin = new Padding(18, 3, 8, 0);
            _btnStart.Click += StartClick;
            runFlow.Controls.Add(_btnStart);

            _btnStop.Text = "停止";
            _btnStop.Width = 80;
            _btnStop.Height = 32;
            _btnStop.Margin = new Padding(0, 3, 8, 0);
            _btnStop.Enabled = false;
            _btnStop.Click += delegate { _cancel = true; _btnStop.Enabled = false; AppendLog("已请求停止，正在结束当前文件…"); };
            runFlow.Controls.Add(_btnStop);

            _btnOpen.Text = "打开输出文件夹";
            _btnOpen.Width = 128;
            _btnOpen.Height = 32;
            _btnOpen.Margin = new Padding(0, 3, 0, 0);
            _btnOpen.Click += OpenOutputClick;
            runFlow.Controls.Add(_btnOpen);

            // 7. 进度
            TableLayoutPanel tProg = new TableLayoutPanel();
            tProg.Dock = DockStyle.Fill;
            tProg.ColumnCount = 3;
            tProg.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300F));
            tProg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tProg.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190F));
            tProg.RowCount = 1;
            tProg.Margin = new Padding(0, 0, 0, 4);
            root.Controls.Add(tProg, 0, 6);

            _lblStatus.Text = "就绪";
            _lblStatus.Dock = DockStyle.Fill;
            _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            tProg.Controls.Add(_lblStatus, 0, 0);

            _bar.Dock = DockStyle.Fill;
            _bar.Margin = new Padding(0, 4, 0, 4);
            tProg.Controls.Add(_bar, 1, 0);

            Label lblCopy = new Label();
            lblCopy.Text = "© 2026 10MillionTones · MIT";
            lblCopy.Dock = DockStyle.Fill;
            lblCopy.TextAlign = ContentAlignment.MiddleRight;
            lblCopy.ForeColor = Color.Gray;
            lblCopy.Margin = new Padding(0);
            _tips.SetToolTip(lblCopy, "MIT License：可自由使用、修改、分发和商用，保留版权声明即可。https://github.com/10MillionTones/HTML2PDF");
            tProg.Controls.Add(lblCopy, 2, 0);

            // 8. 日志
            _log.Dock = DockStyle.Fill;
            _log.Multiline = true;
            _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.Vertical;
            _log.BackColor = Color.White;
            _log.Margin = new Padding(0);
            root.Controls.Add(_log, 0, 7);
        }

        private Button NewButton(string text, int width, EventHandler onClick)
        {
            Button b = new Button();
            b.Text = text;
            b.Width = width;
            b.Height = 30;
            b.Margin = new Padding(0, 1, 8, 0);
            b.Click += onClick;
            _editButtons.Add(b);
            return b;
        }

        private static Label NewLabel(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Margin = new Padding(6, 10, 4, 0);
            return l;
        }

        private static ComboBox NewCombo(ComboBox cbo, string[] items, int width)
        {
            cbo.DropDownStyle = ComboBoxStyle.DropDownList;
            cbo.Width = width;
            cbo.Margin = new Padding(0, 6, 6, 0);
            cbo.Items.AddRange(items);
            cbo.SelectedIndex = 0;
            return cbo;
        }

        // ---------------- 文件收集 ----------------

        private void AddFilesClick(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择 HTML 文件";
                dlg.Filter = "网页文件 (*.html;*.htm)|*.html;*.htm|所有文件 (*.*)|*.*";
                dlg.Multiselect = true;
                if (dlg.ShowDialog(this) == DialogResult.OK) AddPaths(dlg.FileNames, false);
            }
        }

        private void AddFolderClick(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择包含 HTML 文件的文件夹（含所有子目录）";
                if (dlg.ShowDialog(this) == DialogResult.OK) AddPaths(new string[] { dlg.SelectedPath }, true);
            }
        }

        private void AddPaths(string[] paths, bool expandFolders)
        {
            List<string> files = new List<string>();
            foreach (string p in paths)
            {
                try
                {
                    if (Directory.Exists(p))
                    {
                        if (!expandFolders)
                        {
                            files.AddRange(Directory.GetFiles(p, "*.html", SearchOption.TopDirectoryOnly));
                            files.AddRange(Directory.GetFiles(p, "*.htm", SearchOption.TopDirectoryOnly));
                        }
                        else
                        {
                            files.AddRange(Directory.GetFiles(p, "*.html", SearchOption.AllDirectories));
                            files.AddRange(Directory.GetFiles(p, "*.htm", SearchOption.AllDirectories));
                        }
                    }
                    else if (File.Exists(p) && IsHtml(p))
                    {
                        files.Add(p);
                    }
                }
                catch (Exception ex)
                {
                    AppendLog("读取失败：" + p + "（" + ex.Message + "）");
                }
            }

            HashSet<string> exist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ListViewItem it in _list.Items) exist.Add(((Job)it.Tag).Src);

            int added = 0;
            foreach (string f in files)
            {
                string full;
                try { full = Path.GetFullPath(f); }
                catch { continue; }
                if (!exist.Add(full)) continue;

                Job job = new Job();
                job.Src = full;
                ListViewItem item = new ListViewItem(Path.GetFileName(full));
                item.SubItems.Add("待转换");
                item.SubItems.Add("");
                item.Tag = job;
                _list.Items.Add(item);
                added++;
            }

            RefreshOutputPreview();
            AppendLog("添加 " + added + " 个文件，列表共 " + _list.Items.Count + " 个。");
        }

        private static bool IsHtml(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".html" || ext == ".htm";
        }

        private void RemoveSelectedClick(object sender, EventArgs e) { RemoveSelected(); }

        private void RemoveSelected()
        {
            if (_running) return;
            foreach (ListViewItem it in _list.SelectedItems) _list.Items.Remove(it);
            RefreshOutputPreview();
        }

        private void ClearClick(object sender, EventArgs e)
        {
            if (_running) return;
            _list.Items.Clear();
            AppendLog("列表已清空。");
        }

        // ---------------- 拖拽 ----------------

        private void ListDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        private void ListDragDrop(object sender, DragEventArgs e)
        {
            if (_running) return;
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths != null) AddPaths(paths, true);
        }

        // ---------------- 浏览器 ----------------

        private void PickBrowserClick(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择 msedge.exe 或 chrome.exe";
                dlg.Filter = "浏览器 (msedge.exe;chrome.exe)|msedge.exe;chrome.exe|可执行文件 (*.exe)|*.exe";
                if (dlg.ShowDialog(this) == DialogResult.OK) _txtBrowser.Text = dlg.FileName;
            }
        }

        private static string DetectBrowser()
        {
            List<string> candidates = new List<string>();
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            if (pf86.Length > 0) candidates.Add(Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"));
            if (pf.Length > 0) candidates.Add(Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"));
            if (local.Length > 0) candidates.Add(Path.Combine(local, @"Google\Chrome\Application\chrome.exe"));
            if (pf86.Length > 0) candidates.Add(Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"));
            if (pf.Length > 0) candidates.Add(Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"));
            if (local.Length > 0) candidates.Add(Path.Combine(local, @"Microsoft\Edge\Application\msedge.exe"));

            foreach (string p in candidates)
            {
                try { if (File.Exists(p)) return p; }
                catch { }
            }

            string[] keys =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe"
            };
            RegistryHive[] hives = { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
            RegistryView[] views = { RegistryView.Registry64, RegistryView.Registry32 };
            foreach (RegistryHive hive in hives)
            {
                foreach (RegistryView view in views)
                {
                    foreach (string key in keys)
                    {
                        try
                        {
                            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                            using (RegistryKey sub = baseKey.OpenSubKey(key))
                            {
                                if (sub == null) continue;
                                string v = sub.GetValue("") as string;
                                if (!string.IsNullOrEmpty(v) && File.Exists(v)) return v;
                            }
                        }
                        catch { }
                    }
                }
            }
            return null;
        }

        // ---------------- 输出路径 ----------------

        private string PreviewOutput(string src)
        {
            if (_rbCustom.Checked)
            {
                string dir = _txtOut.Text.Trim();
                if (dir.Length == 0) dir = Path.GetDirectoryName(src);
                return Path.Combine(dir, Path.GetFileNameWithoutExtension(src) + ".pdf");
            }
            return Path.ChangeExtension(src, ".pdf");
        }

        private void OutputModeChanged(object sender, EventArgs e)
        {
            _txtOut.Enabled = _rbCustom.Checked;
            _btnOutBrowse.Enabled = _rbCustom.Checked;
            RefreshOutputPreview();
        }

        private void BrowseOutputClick(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择 PDF 输出目录";
                if (dlg.ShowDialog(this) == DialogResult.OK) _txtOut.Text = dlg.SelectedPath;
            }
        }

        private void RefreshOutputPreview()
        {
            foreach (ListViewItem it in _list.Items)
            {
                Job job = (Job)it.Tag;
                string dst;
                try { dst = PreviewOutput(job.Src); }
                catch { dst = ""; }
                it.SubItems[2].Text = dst;
            }
        }

        private void OpenOutputClick(object sender, EventArgs e)
        {
            string dir = null;
            if (_rbCustom.Checked && _txtOut.Text.Trim().Length > 0) dir = _txtOut.Text.Trim();
            else if (_list.Items.Count > 0) dir = Path.GetDirectoryName(((Job)_list.Items[0].Tag).Src);
            if (dir == null || !Directory.Exists(dir))
            {
                MessageBox.Show(this, "还没有可打开的输出文件夹。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try { Process.Start("explorer.exe", "\"" + dir + "\""); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        // ---------------- 转换 ----------------

        private void StartClick(object sender, EventArgs e)
        {
            if (_running) return;

            string browser = _txtBrowser.Text.Trim();
            if (browser.Length == 0 || !File.Exists(browser))
            {
                MessageBox.Show(this, "没有可用的打印引擎。\n\n本程序调用本机 Edge 或 Chrome 的打印引擎来生成 PDF，请先在“打印引擎”里点“选择...”指定 msedge.exe 或 chrome.exe。",
                    "缺少浏览器", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_list.Items.Count == 0)
            {
                MessageBox.Show(this, "请先添加要转换的 HTML 文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_rbCustom.Checked && _txtOut.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, "请选择输出目录，或改回“存到源文件旁边”。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 生成任务表（同一批内重名自动加序号）
            _jobs = new List<Job>();
            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ListViewItem it in _list.Items)
            {
                Job src = (Job)it.Tag;
                string dst;
                try { dst = PreviewOutput(src.Src); }
                catch (Exception ex)
                {
                    AppendLog("跳过（路径无效）：" + src.Src + "（" + ex.Message + "）");
                    continue;
                }

                string dir = Path.GetDirectoryName(dst);
                string name = Path.GetFileNameWithoutExtension(dst);
                string cand = dst;
                int n = 2;
                while (used.Contains(cand))
                {
                    cand = Path.Combine(dir, name + " (" + n + ").pdf");
                    n++;
                }
                used.Add(cand);

                Job job = new Job();
                job.Src = src.Src;
                job.Dst = cand;
                job.Skip = _chkSkip.Checked && File.Exists(cand);
                _jobs.Add(job);
            }
            if (_jobs.Count == 0) return;

            _ok = 0; _fail = 0; _skipped = 0; _cursor = -1; _threadsDone = 0;
            _cancel = false;
            _bar.Maximum = _jobs.Count;
            _bar.Value = 0;
            _lblStatus.Text = "0 / " + _jobs.Count;
            _log.Clear();
            for (int i = 0; i < _list.Items.Count; i++)
            {
                _list.Items[i].ForeColor = SystemColors.WindowText;
                _list.Items[i].SubItems[1].Text = "待转换";
            }

            _tempRoot = Path.Combine(Path.GetTempPath(), "html2pdf_" + Process.GetCurrentProcess().Id);
            try { Directory.CreateDirectory(_tempRoot); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法创建临时目录：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _workers = Math.Min((int)_numWorkers.Value, _jobs.Count);
            _running = true;
            SetBusy(true);
            _watch.Reset();
            _watch.Start();
            AppendLog("开始转换 " + _jobs.Count + " 个文件，并发 " + _workers + "，引擎：" + Path.GetFileName(browser));

            for (int i = 0; i < _workers; i++)
            {
                Thread t = new Thread(Worker);
                t.IsBackground = true;
                t.Name = "worker" + i;
                t.Start(i);
            }
        }

        private void Worker(object state)
        {
            int index = (int)state;
            string profile = Path.Combine(_tempRoot, "profile" + index);
            try
            {
                while (true)
                {
                    if (_cancel) break;
                    int i = Interlocked.Increment(ref _cursor);
                    if (i >= _jobs.Count) break;

                    Job job = _jobs[i];
                    if (job.Skip)
                    {
                        Interlocked.Increment(ref _skipped);
                        RunOnUi(delegate
                        {
                            MarkItem(i, "已存在，跳过", Color.Gray);
                            AdvanceProgress();
                        });
                        continue;
                    }

                    RunOnUi(delegate { MarkItem(i, "转换中…", Color.DarkOrange); });

                    string error;
                    bool ok = ConvertOne(job.Src, job.Dst, profile, out error);
                    if (ok)
                    {
                        Interlocked.Increment(ref _ok);
                        RunOnUi(delegate
                        {
                            MarkItem(i, "完成", Color.ForestGreen);
                            AppendLog("完成：" + Path.GetFileName(job.Dst));
                            AdvanceProgress();
                        });
                    }
                    else
                    {
                        Interlocked.Increment(ref _fail);
                        RunOnUi(delegate
                        {
                            MarkItem(i, "失败", Color.Firebrick);
                            AppendLog("失败：" + Path.GetFileName(job.Src) + " —— " + error);
                            AdvanceProgress();
                        });
                    }
                }
            }
            finally
            {
                int done = Interlocked.Increment(ref _threadsDone);
                if (done == _workers) RunOnUi(Finish);
            }
        }

        private bool ConvertOne(string src, string dst, string profile, out string error)
        {
            error = null;
            string tempHtml = null;
            string tempPdf = dst + ".h2p.pdf";
            try
            {
                string target = src;
                string css = BuildPageCss();
                if (css.Length > 0)
                {
                    try
                    {
                        tempHtml = CreateTempHtml(src, css);
                        target = tempHtml;
                    }
                    catch
                    {
                        target = src; // 源目录不可写时退回原始文件，只是页面设置不生效
                    }
                }

                string dir = Path.GetDirectoryName(dst);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(tempPdf))
                {
                    try { File.Delete(tempPdf); } catch { }
                }

                StringBuilder args = new StringBuilder();
                args.Append("--headless --disable-gpu --hide-scrollbars --disable-extensions --no-first-run ");
                args.Append("--no-default-browser-check --allow-file-access-from-files ");
                args.Append("--disable-background-networking --disable-sync --disable-component-update ");
                args.Append("--no-pings --disable-breakpad --disable-crash-reporter ");
                args.Append("--user-data-dir=").Append(Quote(profile)).Append(' ');
                if (_numWait.Value > 0) args.Append("--virtual-time-budget=").Append((int)_numWait.Value).Append(' ');
                args.Append("--no-pdf-header-footer --print-to-pdf-no-header ");
                args.Append("--print-to-pdf=").Append(Quote(tempPdf)).Append(' ');
                args.Append(Quote(new Uri(target).AbsoluteUri));

                ProcessStartInfo psi = new ProcessStartInfo(_txtBrowser.Text.Trim(), args.ToString());
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = Path.GetDirectoryName(target);

                using (Process p = Process.Start(psi))
                {
                    if (!p.WaitForExit(180000))
                    {
                        try { p.Kill(); } catch { }
                        error = "超时（180 秒），页面可能卡住了";
                        return false;
                    }
                    if (p.ExitCode != 0)
                    {
                        error = "浏览器退出码 " + p.ExitCode;
                        return false;
                    }
                }

                if (!File.Exists(tempPdf) || new FileInfo(tempPdf).Length == 0)
                {
                    error = "没有生成 PDF";
                    return false;
                }

                if (File.Exists(dst))
                {
                    try { File.Delete(dst); } catch { }
                }
                File.Move(tempPdf, dst);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (tempHtml != null) { try { File.Delete(tempHtml); } catch { } }
                if (File.Exists(tempPdf)) { try { File.Delete(tempPdf); } catch { } }
            }
        }

        private static string Quote(string s)
        {
            return "\"" + s.TrimEnd('\\') + "\"";
        }

        private string BuildPageCss()
        {
            int paper = _cboPaper.SelectedIndex;
            int orient = _cboOrient.SelectedIndex;
            int margin = _cboMargin.SelectedIndex;

            StringBuilder css = new StringBuilder();
            if (paper > 0)
            {
                css.Append("size: ").Append(PaperNames[paper]);
                if (orient == 2) css.Append(" landscape");
                css.Append(';');
            }
            else if (orient == 2)
            {
                css.Append("size: landscape;");
            }
            if (margin > 0) css.Append("margin: ").Append(MarginNames[margin]).Append(';');

            if (css.Length == 0) return "";
            return "@page { " + css + " }";
        }

        private static string CreateTempHtml(string src, string css)
        {
            byte[] data = File.ReadAllBytes(src);
            byte[] style = Encoding.UTF8.GetBytes("<style type=\"text/css\">" + css + "</style>");

            int pos = FindAscii(data, "</head>");
            if (pos < 0) pos = FindAscii(data, "<body");
            if (pos < 0) pos = data.Length;

            string dir = Path.GetDirectoryName(src);
            string name = Path.GetFileNameWithoutExtension(src) + ".h2p-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".html";
            string temp = Path.Combine(dir, name);

            using (FileStream fs = new FileStream(temp, FileMode.Create, FileAccess.Write))
            {
                fs.Write(data, 0, pos);
                fs.Write(style, 0, style.Length);
                fs.Write(data, pos, data.Length - pos);
            }
            return temp;
        }

        private static int FindAscii(byte[] data, string needle)
        {
            if (needle.Length == 0 || data.Length < needle.Length) return -1;
            for (int i = 0; i <= data.Length - needle.Length; i++)
            {
                int j = 0;
                while (j < needle.Length)
                {
                    byte a = data[i + j];
                    byte b = (byte)needle[j];
                    if (a >= 'A' && a <= 'Z') a = (byte)(a + 32);
                    if (b >= 'A' && b <= 'Z') b = (byte)(b + 32);
                    if (a != b) break;
                    j++;
                }
                if (j == needle.Length) return i;
            }
            return -1;
        }

        private void MarkItem(int index, string status, Color color)
        {
            if (index < 0 || index >= _list.Items.Count) return;
            ListViewItem it = _list.Items[index];
            it.SubItems[1].Text = status;
            it.ForeColor = color;
        }

        private void AdvanceProgress()
        {
            int value = _ok + _fail + _skipped;
            if (value > _bar.Maximum) value = _bar.Maximum;
            _bar.Value = value;
            _lblStatus.Text = value + " / " + _jobs.Count;
        }

        private void Finish()
        {
            _running = false;
            _watch.Stop();
            SetBusy(false);
            try { Directory.Delete(_tempRoot, true); }
            catch { }

            string summary = string.Format("完成 {0} 个，失败 {1} 个，跳过 {2} 个，用时 {3:0.0} 秒",
                _ok, _fail, _skipped, _watch.Elapsed.TotalSeconds);
            _lblStatus.Text = summary;
            AppendLog(_cancel ? "已停止。" + summary : summary);
            MessageBox.Show(this, summary, _fail > 0 ? "转换结束（有失败）" : "转换结束",
                MessageBoxButtons.OK, _fail > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        private void SetBusy(bool busy)
        {
            _btnStart.Enabled = !busy;
            _btnStop.Enabled = busy;
            _cboPaper.Enabled = !busy;
            _cboOrient.Enabled = !busy;
            _cboMargin.Enabled = !busy;
            _chkSkip.Enabled = !busy;
            _numWorkers.Enabled = !busy;
            _numWait.Enabled = !busy;
            _rbSource.Enabled = !busy;
            _rbCustom.Enabled = !busy;
            _txtOut.Enabled = !busy && _rbCustom.Checked;
            _btnOutBrowse.Enabled = !busy && _rbCustom.Checked;
            foreach (Button b in _editButtons) b.Enabled = !busy;
        }

        private void RunOnUi(Action action)
        {
            if (IsDisposed) return;
            try { BeginInvoke(action); }
            catch { }
        }

        private void AppendLog(string text)
        {
            _log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text + Environment.NewLine);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_running)
            {
                DialogResult r = MessageBox.Show(this, "还在转换中，确定要退出吗？", "确认退出",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) { e.Cancel = true; return; }
                _cancel = true;
            }
            base.OnFormClosing(e);
        }
    }
}
