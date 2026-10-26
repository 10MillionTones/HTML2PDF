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

[assembly: AssemblyTitle("HTML2PDF - Batch HTML to PDF")]
[assembly: AssemblyDescription("Batch convert local HTML files to PDF")]
[assembly: AssemblyProduct("HTML2PDF")]
[assembly: AssemblyCompany("10MillionTones")]
[assembly: AssemblyCopyright("Copyright (c) 2026 10MillionTones")]
[assembly: AssemblyVersion("1.1.2.0")]
[assembly: AssemblyFileVersion("1.1.2.0")]

namespace Html2Pdf
{
    // 版本号与界面语言。改版本时同时改 AssemblyVersion / AssemblyFileVersion / README。
    internal static class L
    {
        public const string Version = "1.1.2";
        public const string RegPath = @"Software\10MillionTones\HTML2PDF";

        // 0 中文，1 English，2 日本語，3 한국어
        public static int Lang;

        public static string T(string zh, string en, string ja, string ko)
        {
            switch (Lang)
            {
                case 1: return en;
                case 2: return ja;
                case 3: return ko;
                default: return zh;
            }
        }

        public static int LoadLang()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RegPath))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("Language");
                        if (v is int && (int)v >= 0 && (int)v <= 3) return (int)v;
                    }
                }
            }
            catch { }
            return 0;
        }

        public static void SaveLang(int lang)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RegPath))
                {
                    if (k != null) k.SetValue("Language", lang, RegistryValueKind.DWord);
                }
            }
            catch { }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Diag.Write("启动 v" + L.Version + "  OS=" + Environment.OSVersion.VersionString + "  CLR=" + Environment.Version + "  TEMP=" + Path.GetTempPath());
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                Diag.Write("未处理异常：" + e.Exception);
                MessageBox.Show(e.Exception.Message,
                    L.T("出错了", "Error", "エラー", "오류"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Application.Run(new MainForm(args));
        }
    }

    // 排障日志，写在 exe 旁边，超过 512 KB 自动重开
    internal static class Diag
    {
        private static readonly object Gate = new object();
        public static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HTML2PDF.log");

        public static void Write(string line)
        {
            try
            {
                lock (Gate)
                {
                    FileInfo fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 512 * 1024) fi.Delete();
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly ListView _list = new ListView();
        private readonly TextBox _txtOut = new TextBox();
        private readonly TextBox _txtBrowser = new TextBox();
        private readonly RadioButton _rbSource = new RadioButton();
        private readonly RadioButton _rbCustom = new RadioButton();
        private readonly ComboBox _cboPaper = new ComboBox();
        private readonly ComboBox _cboOrient = new ComboBox();
        private readonly ComboBox _cboMargin = new ComboBox();
        private readonly ComboBox _cboLang = new ComboBox();
        private readonly CheckBox _chkSkip = new CheckBox();
        private readonly NumericUpDown _numWorkers = new NumericUpDown();
        private readonly NumericUpDown _numWait = new NumericUpDown();
        private readonly Button _btnOutBrowse = new Button();
        private readonly Button _btnStart = new Button();
        private readonly Button _btnStop = new Button();
        private readonly Button _btnOpen = new Button();
        private readonly Button _btnAddFiles = new Button();
        private readonly Button _btnAddFolder = new Button();
        private readonly Button _btnRemove = new Button();
        private readonly Button _btnClear = new Button();
        private readonly Button _btnPick = new Button();
        private readonly ProgressBar _bar = new ProgressBar();
        private readonly Label _lblStatus = new Label();
        private readonly Label _lblHint = new Label();
        private readonly Label _lblBrowser = new Label();
        private readonly Label _lblLang = new Label();
        private readonly Label _lblCopy = new Label();
        private readonly Label _lblPaper = new Label();
        private readonly Label _lblOrient = new Label();
        private readonly Label _lblMargin = new Label();
        private readonly Label _lblWorkers = new Label();
        private readonly Label _lblWait = new Label();
        private readonly Label _lblMs = new Label();
        private readonly GroupBox _grpOut = new GroupBox();
        private readonly GroupBox _grpPage = new GroupBox();
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
            public int State; // 0 待转换 1 转换中 2 跳过 3 完成 4 失败
        }

        public MainForm(string[] args)
        {
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1000, 730);
            MinimumSize = new Size(880, 640);
            Font = new Font("Microsoft YaHei", 9F);
            AllowDrop = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            L.Lang = L.LoadLang();
            BuildUi();
            ApplyLanguage();

            _cboPaper.SelectedIndex = 1; // 默认 A4（浏览器无头模式默认是 Letter）
            _cboOrient.SelectedIndex = 0;
            _cboMargin.SelectedIndex = 0;

            string browser = DetectBrowser();
            if (browser != null)
            {
                _txtBrowser.Text = browser;
            }
            else
            {
                AppendLog(L.T("没有自动找到 Edge 或 Chrome，请点“选择...”手动指定 msedge.exe / chrome.exe 的路径。",
                    "Edge or Chrome was not found automatically. Click \"Select...\" to point to msedge.exe / chrome.exe.",
                    "Edge または Chrome が見つかりません。「選択...」から msedge.exe / chrome.exe を指定してください。",
                    "Edge 또는 Chrome을 자동으로 찾지 못했습니다. \"선택...\"에서 msedge.exe / chrome.exe 경로를 지정하세요."));
            }

            AppendLog(L.T("支持 .html / .htm，可多选文件、添加整个文件夹（含子目录），也可把文件或文件夹直接拖进列表。",
                "Supports .html / .htm. Pick multiple files, add a whole folder (including subfolders), or drag files and folders into the list.",
                ".html / .htm に対応。複数ファイルの選択、フォルダ全体（サブフォルダを含む）の追加、一覧へのドラッグ＆ドロップもできます。",
                ".html / .htm 지원. 여러 파일 선택, 폴더 전체(하위 폴더 포함) 추가, 목록으로 파일/폴더 끌어다 놓기를 지원합니다."));
            AppendLog(L.T("转换使用本机浏览器的打印引擎，输出矢量 PDF，文字可复制、可搜索。",
                "Conversion uses the local browser's print engine, producing vector PDFs with selectable, searchable text.",
                "本機ブラウザの印刷エンジンを使うため、文字をコピー・検索できるベクター PDF を出力します。",
                "로컬 브라우저의 인쇄 엔진을 사용해 텍스트를 복사·검색할 수 있는 벡터 PDF를 만듭니다."));

            if (args != null && args.Length > 0) AddPaths(args, true);
        }

        // ---------------- UI ----------------

        private static string[] PaperNames()
        {
            return new string[]
            {
                L.T("跟随原文件", "Same as source", "元ファイルと同じ", "원본과 동일"),
                "A4", "A3", "A5", "Letter", "Legal"
            };
        }

        private static string[] OrientNames()
        {
            return new string[]
            {
                L.T("自动", "Auto", "自動", "자동"),
                L.T("纵向", "Portrait", "縦", "세로"),
                L.T("横向", "Landscape", "横", "가로")
            };
        }

        private static string[] MarginNames()
        {
            return new string[]
            {
                L.T("跟随原文件", "Same as source", "元ファイルと同じ", "원본과 동일"),
                "0mm", "5mm", "10mm", "15mm", "20mm", "25mm"
            };
        }

        private void BuildUi()
        {
            Label[] autoLabels = { _lblHint, _lblLang, _lblPaper, _lblOrient, _lblMargin, _lblWorkers, _lblWait, _lblMs };
            foreach (Label l in autoLabels) l.AutoSize = true;
            _lblPaper.Margin = new Padding(6, 10, 4, 0);
            _lblOrient.Margin = new Padding(6, 10, 4, 0);
            _lblMargin.Margin = new Padding(6, 10, 4, 0);
            _lblWorkers.Margin = new Padding(6, 10, 4, 0);
            _lblWait.Margin = new Padding(6, 10, 4, 0);
            _lblMs.Margin = new Padding(6, 10, 4, 0);

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

            // 1. 工具栏（左：文件操作，右：语言切换）
            TableLayoutPanel tTop = new TableLayoutPanel();
            tTop.Dock = DockStyle.Fill;
            tTop.ColumnCount = 2;
            tTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tTop.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200F));
            tTop.RowCount = 1;
            tTop.Margin = new Padding(0, 0, 0, 4);
            root.Controls.Add(tTop, 0, 0);

            FlowLayoutPanel toolbar = new FlowLayoutPanel();
            toolbar.Dock = DockStyle.Fill;
            toolbar.WrapContents = false;
            toolbar.Margin = new Padding(0);
            tTop.Controls.Add(toolbar, 0, 0);

            toolbar.Controls.Add(NewButton(_btnAddFiles, 96, AddFilesClick));
            toolbar.Controls.Add(NewButton(_btnAddFolder, 106, AddFolderClick));
            toolbar.Controls.Add(NewButton(_btnRemove, 96, RemoveSelectedClick));
            toolbar.Controls.Add(NewButton(_btnClear, 76, ClearClick));
            _lblHint.AutoSize = true;
            _lblHint.ForeColor = Color.Gray;
            _lblHint.Margin = new Padding(8, 8, 0, 0);
            toolbar.Controls.Add(_lblHint);

            FlowLayoutPanel langFlow = new FlowLayoutPanel();
            langFlow.Dock = DockStyle.Fill;
            langFlow.WrapContents = false;
            langFlow.Margin = new Padding(0);
            tTop.Controls.Add(langFlow, 1, 0);

            _lblLang.AutoSize = true;
            _lblLang.Margin = new Padding(0, 8, 4, 0);
            langFlow.Controls.Add(_lblLang);

            _cboLang.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboLang.Width = 118;
            _cboLang.Margin = new Padding(0, 4, 0, 0);
            _cboLang.Items.AddRange(new object[] { "中文", "English", "日本語", "한국어" });
            _cboLang.SelectedIndex = L.Lang;
            _cboLang.SelectedIndexChanged += LangChanged;
            langFlow.Controls.Add(_cboLang);

            // 2. 浏览器
            TableLayoutPanel tBrowser = new TableLayoutPanel();
            tBrowser.Dock = DockStyle.Fill;
            tBrowser.ColumnCount = 3;
            tBrowser.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108F));
            tBrowser.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tBrowser.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
            tBrowser.RowCount = 1;
            tBrowser.Margin = new Padding(0, 0, 0, 6);
            root.Controls.Add(tBrowser, 0, 1);

            _lblBrowser.Dock = DockStyle.Fill;
            _lblBrowser.TextAlign = ContentAlignment.MiddleLeft;
            tBrowser.Controls.Add(_lblBrowser, 0, 0);

            _txtBrowser.Dock = DockStyle.Fill;
            _txtBrowser.ReadOnly = true;
            _txtBrowser.Margin = new Padding(0, 3, 6, 3);
            tBrowser.Controls.Add(_txtBrowser, 1, 0);

            _btnPick.Dock = DockStyle.Fill;
            _btnPick.Height = 28;
            _btnPick.Margin = new Padding(0, 2, 0, 2);
            _btnPick.Click += PickBrowserClick;
            _editButtons.Add(_btnPick);
            tBrowser.Controls.Add(_btnPick, 2, 0);

            // 3. 文件列表
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = true;
            _list.HideSelection = false;
            _list.Margin = new Padding(0, 0, 0, 8);
            _list.Columns.Add("", 240);
            _list.Columns.Add("", 84);
            _list.Columns.Add("", -2);
            _list.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Delete) RemoveSelected();
            };
            _list.AllowDrop = true;
            _list.DragEnter += ListDragEnter;
            _list.DragDrop += ListDragDrop;
            root.Controls.Add(_list, 0, 2);

            // 4. 输出位置
            _grpOut.Dock = DockStyle.Fill;
            _grpOut.Margin = new Padding(0, 0, 0, 8);
            root.Controls.Add(_grpOut, 0, 3);

            TableLayoutPanel tOut = new TableLayoutPanel();
            tOut.Dock = DockStyle.Fill;
            tOut.ColumnCount = 3;
            tOut.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190F));
            tOut.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tOut.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));
            tOut.RowCount = 2;
            tOut.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            tOut.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            tOut.Padding = new Padding(6, 4, 6, 2);
            _grpOut.Controls.Add(tOut);

            _rbSource.Dock = DockStyle.Fill;
            _rbSource.Checked = true;
            _rbSource.CheckedChanged += OutputModeChanged;
            tOut.Controls.Add(_rbSource, 0, 0);
            tOut.SetColumnSpan(_rbSource, 3);

            _rbCustom.Dock = DockStyle.Fill;
            _rbCustom.CheckedChanged += OutputModeChanged;
            tOut.Controls.Add(_rbCustom, 0, 1);

            _txtOut.Dock = DockStyle.Fill;
            _txtOut.Margin = new Padding(0, 2, 6, 2);
            _txtOut.TextChanged += delegate { RefreshOutputPreview(); };
            tOut.Controls.Add(_txtOut, 1, 1);

            _btnOutBrowse.Dock = DockStyle.Fill;
            _btnOutBrowse.Margin = new Padding(0, 1, 0, 1);
            _btnOutBrowse.Click += BrowseOutputClick;
            tOut.Controls.Add(_btnOutBrowse, 2, 1);

            // 5. 页面设置
            _grpPage.Dock = DockStyle.Fill;
            _grpPage.Margin = new Padding(0, 0, 0, 8);
            root.Controls.Add(_grpPage, 0, 4);

            FlowLayoutPanel pageFlow = new FlowLayoutPanel();
            pageFlow.Dock = DockStyle.Fill;
            pageFlow.WrapContents = false;
            pageFlow.Padding = new Padding(6, 4, 6, 0);
            _grpPage.Controls.Add(pageFlow);

            pageFlow.Controls.Add(_lblPaper);
            pageFlow.Controls.Add(NewCombo(_cboPaper, 104));
            pageFlow.Controls.Add(_lblOrient);
            pageFlow.Controls.Add(NewCombo(_cboOrient, 84));
            pageFlow.Controls.Add(_lblMargin);
            pageFlow.Controls.Add(NewCombo(_cboMargin, 112));

            _chkSkip.AutoSize = true;
            _chkSkip.Checked = true;
            _chkSkip.Margin = new Padding(10, 8, 0, 0);
            pageFlow.Controls.Add(_chkSkip);

            // 6. 运行控制
            FlowLayoutPanel runFlow = new FlowLayoutPanel();
            runFlow.Dock = DockStyle.Fill;
            runFlow.WrapContents = false;
            runFlow.Margin = new Padding(0, 0, 0, 6);
            root.Controls.Add(runFlow, 0, 5);

            runFlow.Controls.Add(_lblWorkers);
            _numWorkers.Minimum = 1;
            _numWorkers.Maximum = 4;
            _numWorkers.Value = 1;
            _numWorkers.Width = 48;
            _numWorkers.Margin = new Padding(0, 7, 10, 0);
            runFlow.Controls.Add(_numWorkers);

            runFlow.Controls.Add(_lblWait);
            _numWait.Minimum = 0;
            _numWait.Maximum = 20000;
            _numWait.Increment = 500;
            _numWait.Value = 0;
            _numWait.Width = 66;
            _numWait.Margin = new Padding(0, 7, 4, 0);
            runFlow.Controls.Add(_numWait);
            _lblMs.ForeColor = Color.Gray;
            runFlow.Controls.Add(_lblMs);

            _btnStart.Width = 120;
            _btnStart.Height = 32;
            _btnStart.Margin = new Padding(18, 3, 8, 0);
            _btnStart.Click += StartClick;
            runFlow.Controls.Add(_btnStart);

            _btnStop.Width = 86;
            _btnStop.Height = 32;
            _btnStop.Margin = new Padding(0, 3, 8, 0);
            _btnStop.Enabled = false;
            _btnStop.Click += delegate
            {
                _cancel = true;
                _btnStop.Enabled = false;
                AppendLog(L.T("已请求停止，正在结束当前文件…", "Stop requested, finishing the current file…",
                    "停止を要求しました。現在のファイルを終了しています…", "중지 요청됨. 현재 파일을 마무리하는 중…"));
            };
            runFlow.Controls.Add(_btnStop);

            _btnOpen.Width = 136;
            _btnOpen.Height = 32;
            _btnOpen.Margin = new Padding(0, 3, 0, 0);
            _btnOpen.Click += OpenOutputClick;
            runFlow.Controls.Add(_btnOpen);

            // 7. 进度
            TableLayoutPanel tProg = new TableLayoutPanel();
            tProg.Dock = DockStyle.Fill;
            tProg.ColumnCount = 3;
            tProg.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280F));
            tProg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tProg.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230F));
            tProg.RowCount = 1;
            tProg.Margin = new Padding(0, 0, 0, 4);
            root.Controls.Add(tProg, 0, 6);

            _lblStatus.Dock = DockStyle.Fill;
            _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            tProg.Controls.Add(_lblStatus, 0, 0);

            _bar.Dock = DockStyle.Fill;
            _bar.Margin = new Padding(0, 4, 0, 4);
            tProg.Controls.Add(_bar, 1, 0);

            _lblCopy.Text = "© 2026 10MillionTones · MIT · v" + L.Version;
            _lblCopy.Dock = DockStyle.Fill;
            _lblCopy.TextAlign = ContentAlignment.MiddleRight;
            _lblCopy.ForeColor = Color.Gray;
            _lblCopy.Margin = new Padding(0);
            tProg.Controls.Add(_lblCopy, 2, 0);

            // 8. 日志
            _log.Dock = DockStyle.Fill;
            _log.Multiline = true;
            _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.Vertical;
            _log.BackColor = Color.White;
            _log.Margin = new Padding(0);
            root.Controls.Add(_log, 0, 7);
        }

        // 切换语言后重设所有界面文案，列表里已有的状态也跟着换
        private void ApplyLanguage()
        {
            Text = L.T("HTML 批量转 PDF", "HTML to PDF", "HTML 一括 PDF 変換", "HTML 일괄 PDF 변환") + "  v" + L.Version;

            _btnAddFiles.Text = L.T("添加文件", "Add Files", "ファイル追加", "파일 추가");
            _btnAddFolder.Text = L.T("添加文件夹", "Add Folder", "フォルダ追加", "폴더 추가");
            _btnRemove.Text = L.T("移除选中", "Remove", "選択を削除", "선택 제거");
            _btnClear.Text = L.T("清空", "Clear", "クリア", "비우기");
            _lblHint.Text = L.T("（可拖拽文件或文件夹到此处）", "(drag files or folders here)",
                "（ファイルやフォルダをここにドラッグ）", "(파일이나 폴더를 여기로 끌어오세요)");
            _lblLang.Text = L.T("语言", "Language", "言語", "언어");

            _lblBrowser.Text = L.T("打印引擎：", "Print engine:", "印刷エンジン：", "인쇄 엔진:");
            _btnPick.Text = L.T("选择...", "Select...", "選択...", "선택...");

            _list.Columns[0].Text = L.T("文件", "File", "ファイル", "파일");
            _list.Columns[1].Text = L.T("状态", "Status", "状態", "상태");
            _list.Columns[2].Text = L.T("输出到", "Output", "出力先", "출력 위치");

            _grpOut.Text = L.T("保存位置", "Output location", "保存先", "저장 위치");
            _rbSource.Text = L.T("存到源文件旁边", "Same folder as source", "元ファイルと同じ場所", "원본 파일과 같은 폴더");
            _rbCustom.Text = L.T("指定目录", "Custom folder", "指定フォルダ", "지정 폴더");
            _btnOutBrowse.Text = L.T("浏览...", "Browse...", "参照...", "찾아보기...");

            _grpPage.Text = L.T("页面设置（选“跟随原文件”则不改动 HTML 原有样式）",
                "Page setup (\"Same as source\" keeps the HTML's original styling)",
                "ページ設定（「元ファイルと同じ」を選ぶと HTML の元のスタイルを変更しません）",
                "페이지 설정 (\"원본과 동일\"을 선택하면 HTML 원래 스타일을 유지합니다)");
            _lblPaper.Text = L.T("纸张", "Paper", "用紙", "용지");
            _lblOrient.Text = L.T("方向", "Orientation", "向き", "방향");
            _lblMargin.Text = L.T("页边距", "Margin", "余白", "여백");
            _chkSkip.Text = L.T("跳过已存在的 PDF", "Skip existing PDFs", "既存の PDF をスキップ", "기존 PDF 건너뛰기");

            _lblWorkers.Text = L.T("并发数", "Workers", "同時実行", "동시 작업");
            _lblWait.Text = L.T("渲染等待", "Render delay", "レンダリング待ち", "렌더링 대기");
            _lblMs.Text = L.T("毫秒", "ms", "ミリ秒", "밀리초");
            _btnStart.Text = L.T("开始转换", "Start", "変換開始", "변환 시작");
            _btnStop.Text = L.T("停止", "Stop", "停止", "중지");
            _btnOpen.Text = L.T("打开输出文件夹", "Open output folder", "出力フォルダを開く", "출력 폴더 열기");

            if (!_running) _lblStatus.Text = L.T("就绪", "Ready", "準備完了", "준비");

            FillCombo(_cboPaper, PaperNames());
            FillCombo(_cboOrient, OrientNames());
            FillCombo(_cboMargin, MarginNames());

            _tips.SetToolTip(_numWait, L.T(
                "页面内容靠脚本异步生成时才需要填，例如 2000。留 0 表示加载完就打印，速度最快。",
                "Only needed when the page fills itself in with scripts, e.g. 2000. Leave 0 to print right after load — fastest.",
                "スクリプトで非同期に内容を生成するページのみ指定します（例：2000）。0 なら読み込み後すぐ印刷し、最速です。",
                "스크립트로 내용을 비동기 생성하는 페이지만 입력하세요(예: 2000). 0이면 로드 후 바로 인쇄해 가장 빠릅니다."));
            _tips.SetToolTip(_lblCopy, L.T(
                "MIT License：可自由使用、修改、分发和商用，保留版权声明即可。https://github.com/10MillionTones/HTML2PDF",
                "MIT License: free to use, modify, distribute and sell, just keep the copyright notice. https://github.com/10MillionTones/HTML2PDF",
                "MIT License：使用・改変・配布・商用利用は自由です。著作権表示を残してください。https://github.com/10MillionTones/HTML2PDF",
                "MIT License: 사용, 수정, 배포, 상업적 이용이 자유롭습니다. 저작권 표시만 유지하세요. https://github.com/10MillionTones/HTML2PDF"));

            RefreshStates();
        }

        private void LangChanged(object sender, EventArgs e)
        {
            int idx = _cboLang.SelectedIndex;
            if (idx < 0 || idx == L.Lang) return;
            L.Lang = idx;
            L.SaveLang(idx);
            ApplyLanguage();
        }

        private static void FillCombo(ComboBox cbo, string[] items)
        {
            int idx = cbo.SelectedIndex;
            cbo.BeginUpdate();
            cbo.Items.Clear();
            cbo.Items.AddRange(items);
            cbo.EndUpdate();
            if (idx >= items.Length) idx = items.Length - 1;
            if (idx >= 0) cbo.SelectedIndex = idx;
        }

        private static string StateText(int state)
        {
            switch (state)
            {
                case 1: return L.T("转换中…", "Converting…", "変換中…", "변환 중…");
                case 2: return L.T("已存在，跳过", "Exists, skipped", "既存のためスキップ", "이미 존재하여 건너뜀");
                case 3: return L.T("完成", "Done", "完了", "완료");
                case 4: return L.T("失败", "Failed", "失敗", "실패");
                default: return L.T("待转换", "Pending", "待機", "대기");
            }
        }

        private void RefreshStates()
        {
            foreach (ListViewItem it in _list.Items)
            {
                Job j = it.Tag as Job;
                it.SubItems[1].Text = StateText(j == null ? 0 : j.State);
            }
        }

        private Button NewButton(Button b, int width, EventHandler onClick)
        {
            b.Height = 30;
            b.Width = width;
            b.Margin = new Padding(0, 1, 8, 0);
            b.Click += onClick;
            _editButtons.Add(b);
            return b;
        }

        private static ComboBox NewCombo(ComboBox cbo, int width)
        {
            cbo.DropDownStyle = ComboBoxStyle.DropDownList;
            cbo.Width = width;
            cbo.Margin = new Padding(0, 6, 6, 0);
            cbo.SelectedIndex = -1;
            return cbo;
        }

        // ---------------- 文件收集 ----------------

        private void AddFilesClick(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = L.T("选择 HTML 文件", "Select HTML files", "HTML ファイルを選択", "HTML 파일 선택");
                dlg.Filter = L.T("网页文件 (*.html;*.htm)|*.html;*.htm|所有文件 (*.*)|*.*",
                    "Web pages (*.html;*.htm)|*.html;*.htm|All files (*.*)|*.*",
                    "Web ページ (*.html;*.htm)|*.html;*.htm|すべてのファイル (*.*)|*.*",
                    "웹 페이지 (*.html;*.htm)|*.html;*.htm|모든 파일 (*.*)|*.*");
                dlg.Multiselect = true;
                if (dlg.ShowDialog(this) == DialogResult.OK) AddPaths(dlg.FileNames, false);
            }
        }

        private void AddFolderClick(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = L.T("选择包含 HTML 文件的文件夹（含所有子目录）",
                    "Select a folder containing HTML files (including all subfolders)",
                    "HTML ファイルを含むフォルダを選択（すべてのサブフォルダを含む）",
                    "HTML 파일이 있는 폴더 선택 (모든 하위 폴더 포함)");
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
                    AppendLog(L.T("读取失败：", "Read failed: ", "読み取り失敗：", "읽기 실패: ") + p + "（" + ex.Message + "）");
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
                job.State = 0;
                ListViewItem item = new ListViewItem(Path.GetFileName(full));
                item.SubItems.Add(StateText(0));
                item.SubItems.Add("");
                item.Tag = job;
                _list.Items.Add(item);
                added++;
            }

            RefreshOutputPreview();
            AppendLog(L.T("添加 " + added + " 个文件，列表共 " + _list.Items.Count + " 个。",
                "Added " + added + " file(s), " + _list.Items.Count + " in the list.",
                added + " 件追加しました。一覧は合計 " + _list.Items.Count + " 件です。",
                added + "개 추가. 목록에 총 " + _list.Items.Count + "개."));
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
            AppendLog(L.T("列表已清空。", "List cleared.", "一覧をクリアしました。", "목록을 비웠습니다."));
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
                dlg.Title = L.T("选择 msedge.exe 或 chrome.exe", "Select msedge.exe or chrome.exe",
                    "msedge.exe または chrome.exe を選択", "msedge.exe 또는 chrome.exe 선택");
                dlg.Filter = L.T("浏览器 (msedge.exe;chrome.exe)|msedge.exe;chrome.exe|可执行文件 (*.exe)|*.exe",
                    "Browser (msedge.exe;chrome.exe)|msedge.exe;chrome.exe|Executable (*.exe)|*.exe",
                    "ブラウザ (msedge.exe;chrome.exe)|msedge.exe;chrome.exe|実行ファイル (*.exe)|*.exe",
                    "브라우저 (msedge.exe;chrome.exe)|msedge.exe;chrome.exe|실행 파일 (*.exe)|*.exe");
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
                dlg.Description = L.T("选择 PDF 输出目录", "Select the PDF output folder",
                    "PDF の出力先フォルダを選択", "PDF 출력 폴더 선택");
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
                MessageBox.Show(this, L.T("还没有可打开的输出文件夹。", "There is no output folder to open yet.",
                        "開ける出力フォルダがまだありません。", "열 수 있는 출력 폴더가 아직 없습니다."),
                    L.T("提示", "Notice", "お知らせ", "알림"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try { Process.Start("explorer.exe", "\"" + dir + "\""); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, L.T("无法打开", "Cannot open", "開けません", "열 수 없음"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ---------------- 转换 ----------------

        private void StartClick(object sender, EventArgs e)
        {
            if (_running) return;

            string browser = _txtBrowser.Text.Trim();
            if (browser.Length == 0 || !File.Exists(browser))
            {
                MessageBox.Show(this, L.T(
                        "没有可用的打印引擎。\n\n本程序调用本机 Edge 或 Chrome 的打印引擎来生成 PDF，请先在“打印引擎”里点“选择...”指定 msedge.exe 或 chrome.exe。",
                        "No print engine available.\n\nThis tool uses the local Edge or Chrome print engine to build PDFs. Click \"Select...\" next to \"Print engine\" and point to msedge.exe or chrome.exe.",
                        "印刷エンジンが見つかりません。\n\n本ツールは本機の Edge または Chrome の印刷エンジンで PDF を作ります。「印刷エンジン」の「選択...」から msedge.exe または chrome.exe を指定してください。",
                        "사용 가능한 인쇄 엔진이 없습니다.\n\n이 도구는 로컬 Edge 또는 Chrome 인쇄 엔진으로 PDF를 만듭니다. \"인쇄 엔진\" 옆의 \"선택...\"에서 msedge.exe 또는 chrome.exe를 지정하세요."),
                    L.T("缺少浏览器", "Browser missing", "ブラウザがありません", "브라우저 없음"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_list.Items.Count == 0)
            {
                MessageBox.Show(this, L.T("请先添加要转换的 HTML 文件。", "Add the HTML files you want to convert first.",
                        "変換する HTML ファイルを先に追加してください。", "먼저 변환할 HTML 파일을 추가하세요."),
                    L.T("提示", "Notice", "お知らせ", "알림"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_rbCustom.Checked && _txtOut.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, L.T("请选择输出目录，或改回“存到源文件旁边”。", "Choose an output folder, or switch back to \"Same folder as source\".",
                        "出力先フォルダを選ぶか、「元ファイルと同じ場所」に戻してください。", "출력 폴더를 선택하거나 \"원본 파일과 같은 폴더\"로 되돌리세요."),
                    L.T("提示", "Notice", "お知らせ", "알림"), MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                    AppendLog(L.T("跳过（路径无效）：", "Skipped (invalid path): ", "スキップ（無効なパス）：", "건너뜀(잘못된 경로): ")
                        + src.Src + "（" + ex.Message + "）");
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
                Job j = _list.Items[i].Tag as Job;
                if (j != null) j.State = 0;
                _list.Items[i].SubItems[1].Text = StateText(0);
            }

            _tempRoot = Path.Combine(Path.GetTempPath(), "html2pdf_" + Process.GetCurrentProcess().Id);
            try { Directory.CreateDirectory(_tempRoot); }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.T("无法创建临时目录：", "Cannot create the temp folder: ",
                        "一時フォルダを作成できません：", "임시 폴더를 만들 수 없습니다: ") + ex.Message,
                    L.T("错误", "Error", "エラー", "오류"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _workers = Math.Min((int)_numWorkers.Value, _jobs.Count);
            _running = true;
            SetBusy(true);
            _watch.Reset();
            _watch.Start();
            AppendLog(L.T("开始转换 " + _jobs.Count + " 个文件，并发 " + _workers + "，引擎：",
                "Converting " + _jobs.Count + " file(s), workers: " + _workers + ", engine: ",
                _jobs.Count + " 件の変換を開始。同時実行 " + _workers + "、エンジン：",
                _jobs.Count + "개 파일 변환 시작, 동시 작업 " + _workers + ", 엔진: ") + Path.GetFileName(browser));

            Diag.Write("批次开始：任务 " + _jobs.Count + "，并发 " + _workers + "，引擎=" + browser + "（" + FileVersion(browser) + "）"
                + "，临时目录=" + _tempRoot + "，纸张=" + PaperNames()[_cboPaper.SelectedIndex] + "，方向=" + OrientNames()[_cboOrient.SelectedIndex]
                + "，边距=" + MarginNames()[_cboMargin.SelectedIndex] + "，渲染等待=" + (int)_numWait.Value);

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
                            MarkItem(i, 2, Color.Gray);
                            AdvanceProgress();
                        });
                        continue;
                    }

                    RunOnUi(delegate { MarkItem(i, 1, Color.DarkOrange); });

                    string error;
                    bool ok = ConvertOne(job.Src, job.Dst, profile, out error);
                    if (ok)
                    {
                        Interlocked.Increment(ref _ok);
                        RunOnUi(delegate
                        {
                            MarkItem(i, 3, Color.ForestGreen);
                            AppendLog(L.T("完成：", "Done: ", "完了：", "완료: ") + Path.GetFileName(job.Dst));
                            AdvanceProgress();
                        });
                    }
                    else
                    {
                        Interlocked.Increment(ref _fail);
                        RunOnUi(delegate
                        {
                            MarkItem(i, 4, Color.Firebrick);
                            AppendLog(L.T("失败：", "Failed: ", "失敗：", "실패: ") + Path.GetFileName(job.Src) + " —— " + error);
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
            // 先写到临时目录，成功后再搬到目标位置。这样目标目录的权限问题不会表现为“浏览器没生成 PDF”
            string tempPdf = Path.Combine(_tempRoot, "print-" + Guid.NewGuid().ToString("N") + ".pdf");
            try
            {
                if (dst.Length > 250)
                {
                    error = L.T("输出路径太长，请换一个短一点的保存目录：", "The output path is too long, choose a shorter folder: ",
                        "出力パスが長すぎます。短いフォルダを選んでください：", "출력 경로가 너무 깁니다. 더 짧은 폴더를 선택하세요: ") + dst;
                    return false;
                }

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
                if (!string.IsNullOrEmpty(dir))
                {
                    try { Directory.CreateDirectory(dir); }
                    catch { }
                    if (!CanWrite(dir))
                    {
                        error = L.T("目标目录不可写，请换一个保存位置或检查权限：", "The output folder is not writable, choose another location or check permissions: ",
                            "出力先フォルダに書き込めません。保存先を変えるか権限を確認してください：",
                            "출력 폴더에 쓸 수 없습니다. 저장 위치를 바꾸거나 권한을 확인하세요: ") + dir;
                        return false;
                    }
                }

                int exitCode;
                string log;
                bool timeout;
                long elapsed;
                if (!RunPrint(target, profile, tempPdf, false, "first", out exitCode, out log, out timeout, out elapsed))
                {
                    // 第一次没产出：换最小参数、全新配置目录再试一次
                    int exit2;
                    string log2;
                    bool timeout2;
                    long elapsed2;
                    if (!RunPrint(target, profile + "-retry", tempPdf, true, "retry", out exit2, out log2, out timeout2, out elapsed2))
                    {
                        error = BuildFailure(timeout2, exit2, log2, log, elapsed2);
                        Diag.Write("失败：" + src + " -> " + dst + "  " + error);
                        return false;
                    }
                }

                if (File.Exists(dst))
                {
                    try { File.Delete(dst); } catch { }
                }
                try
                {
                    File.Move(tempPdf, dst);
                }
                catch (IOException)
                {
                    File.Copy(tempPdf, dst, true);
                    try { File.Delete(tempPdf); } catch { }
                }
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

        private bool RunPrint(string target, string profile, string tempPdf, bool minimal, string round,
            out int exitCode, out string log, out bool timeout, out long elapsedMs)
        {
            exitCode = 0;
            timeout = false;
            log = "";
            elapsedMs = 0;
            Stopwatch sw = Stopwatch.StartNew();
            try { Directory.CreateDirectory(profile); }
            catch { }
            try { if (File.Exists(tempPdf)) File.Delete(tempPdf); }
            catch { }

            StringBuilder args = new StringBuilder();
            args.Append("--headless --disable-gpu --hide-scrollbars ");
            if (!minimal)
            {
                args.Append("--disable-extensions --no-first-run --no-default-browser-check ");
                args.Append("--allow-file-access-from-files --disable-background-networking --disable-sync ");
                args.Append("--disable-component-update --no-pings --disable-breakpad --disable-crash-reporter ");
            }
            args.Append("--user-data-dir=").Append(Quote(profile)).Append(' ');
            if (_numWait.Value > 0) args.Append("--virtual-time-budget=").Append((int)_numWait.Value).Append(' ');
            if (!minimal) args.Append("--no-pdf-header-footer --print-to-pdf-no-header ");
            args.Append("--print-to-pdf=").Append(Quote(tempPdf)).Append(' ');
            args.Append(Quote(new Uri(target).AbsoluteUri));

            ProcessStartInfo psi = new ProcessStartInfo(_txtBrowser.Text.Trim(), args.ToString());
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.WorkingDirectory = Path.GetDirectoryName(target);
            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;

            StringBuilder buf = new StringBuilder();
            using (Process p = Process.Start(psi))
            {
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    lock (buf) { if (buf.Length < 4000) buf.AppendLine(e.Data); }
                };
                p.BeginErrorReadLine();
                p.BeginOutputReadLine();

                if (!p.WaitForExit(180000))
                {
                    try { p.Kill(); } catch { }
                    timeout = true;
                    elapsedMs = sw.ElapsedMilliseconds;
                    Diag.Write("[" + round + "] 超时 pid=" + p.Id + " 参数=" + args);
                    return false;
                }
                exitCode = p.ExitCode;
            }
            elapsedMs = sw.ElapsedMilliseconds;

            log = Tail(buf.ToString(), 500);
            bool ok;
            long size = 0;
            try
            {
                ok = File.Exists(tempPdf);
                if (ok) size = new FileInfo(tempPdf).Length;
            }
            catch { ok = false; }
            Diag.Write("[" + round + "] exit=" + exitCode + " 用时=" + elapsedMs + "ms pdf=" + ok + " 大小=" + size
                + " 浏览器=" + _txtBrowser.Text.Trim() + " 参数=" + args + (log.Length > 0 ? " stderr=" + log : ""));
            return ok && size > 0;
        }

        private string BuildFailure(bool timeout, int exitCode, string log, string firstLog, long elapsedMs)
        {
            if (timeout)
            {
                return L.T("超时（180 秒），页面可能卡住了", "Timed out (180 s), the page may be stuck",
                    "タイムアウト（180 秒）。ページが固まっている可能性があります",
                    "시간 초과(180초). 페이지가 멈췄을 수 있습니다");
            }
            StringBuilder sb = new StringBuilder();
            if (exitCode != 0)
            {
                sb.Append(L.T("浏览器退出码 ", "Browser exit code ", "ブラウザ終了コード ", "브라우저 종료 코드 ")).Append(exitCode).Append("。 ");
            }
            else if (elapsedMs < 800)
            {
                sb.Append(L.T("浏览器启动后不到 1 秒就退出了，没有执行打印。多数是被安全软件拦截，或者浏览器安装不完整。",
                    "The browser quit less than a second after start without printing. Most likely security software blocked it, or the browser install is broken.",
                    "ブラウザが起動から 1 秒未満で終了し、印刷を実行していません。セキュリティソフトに遮断されたか、ブラウザのインストールが不完全な可能性が高いです。",
                    "브라우저가 시작한 지 1초도 안 되어 종료되었고 인쇄를 실행하지 않았습니다. 보안 프로그램이 차단했거나 브라우저 설치가 불완전할 가능성이 큽니다。"));
            }
            else
            {
                sb.Append(L.T("浏览器没有生成 PDF。可能的原因：页面脚本阻止了打印、被安全软件拦截、浏览器版本不支持无头打印。",
                    "The browser did not produce a PDF. Possible causes: the page script blocks printing, security software blocked it, or the browser build does not support headless printing.",
                    "ブラウザが PDF を生成しませんでした。原因の候補：ページのスクリプトが印刷を妨げている、セキュリティソフトに遮断された、ブラウザがヘッドレス印刷に対応していない。",
                    "브라우저가 PDF를 만들지 못했습니다. 원인 후보: 페이지 스크립트가 인쇄를 막음, 보안 프로그램이 차단, 브라우저가 헤드리스 인쇄를 지원하지 않음。"));
            }
            string extra = log.Length > 0 ? log : firstLog;
            if (extra.Length > 0) sb.Append(" [").Append(extra).Append(']');
            return sb.ToString();
        }

        private static string Tail(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\r", " ").Replace("\n", " | ").Trim();
            while (text.Contains(" |  | ")) text = text.Replace(" |  | ", " | ");
            if (text.Length > max) text = text.Substring(text.Length - max);
            return text;
        }

        private static string FileVersion(string path)
        {
            try { return FileVersionInfo.GetVersionInfo(path).FileVersion; }
            catch { return "?"; }
        }

        private static bool CanWrite(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".h2p_probe_" + Guid.NewGuid().ToString("N").Substring(0, 6));
                using (FileStream fs = new FileStream(probe, FileMode.CreateNew, FileAccess.Write)) { }
                File.Delete(probe);
                return true;
            }
            catch { return false; }
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
                css.Append("size: ").Append(PaperNames()[paper]);
                if (orient == 2) css.Append(" landscape");
                css.Append(';');
            }
            else if (orient == 2)
            {
                css.Append("size: landscape;");
            }
            if (margin > 0) css.Append("margin: ").Append(MarginNames()[margin]).Append(';');

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

        private void MarkItem(int index, int state, Color color)
        {
            if (index < 0 || index >= _list.Items.Count) return;
            ListViewItem it = _list.Items[index];
            Job job = it.Tag as Job;
            if (job != null) job.State = state;
            it.SubItems[1].Text = StateText(state);
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

            string fmt = L.T("完成 {0} 个，失败 {1} 个，跳过 {2} 个，用时 {3:0.0} 秒",
                "Done {0}, failed {1}, skipped {2}, {3:0.0} s",
                "完了 {0} 件、失敗 {1} 件、スキップ {2} 件、所要 {3:0.0} 秒",
                "완료 {0}개, 실패 {1}개, 건너뜀 {2}개, 소요 {3:0.0}초");
            string summary = string.Format(fmt, _ok, _fail, _skipped, _watch.Elapsed.TotalSeconds);

            _lblStatus.Text = summary;
            AppendLog(_cancel ? L.T("已停止。", "Stopped. ", "停止しました。", "중지되었습니다. ") + summary : summary);
            MessageBox.Show(this, summary,
                _fail > 0
                    ? L.T("转换结束（有失败）", "Finished with errors", "変換終了（失敗あり）", "변환 종료 (실패 있음)")
                    : L.T("转换结束", "Finished", "変換終了", "변환 종료"),
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
                DialogResult r = MessageBox.Show(this,
                    L.T("还在转换中，确定要退出吗？", "Conversion is still running. Quit anyway?",
                        "変換中です。終了しますか？", "아직 변환 중입니다. 종료할까요?"),
                    L.T("确认退出", "Confirm exit", "終了の確認", "종료 확인"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) { e.Cancel = true; return; }
                _cancel = true;
            }
            base.OnFormClosing(e);
        }
    }
}
