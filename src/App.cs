using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DailyReflection
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            bool verification = args.Length > 0 && (args[0] == "--self-test" || args[0] == "--preview");
            try
            {
                Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DailyReflection.SharedStyles.xaml"))
                    app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
                if (verification)
                {
                    if (args.Length != 2) throw new ArgumentException("验证模式需要指定输出文件夹。");
                    return args[0] == "--self-test" ? SelfTests.Run(args[1]) : SelfTests.Preview(args[1]);
                }

                string identity;
                using (SHA256 hash = SHA256.Create())
                    identity = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(AppDomain.CurrentDomain.BaseDirectory.ToUpperInvariant()))).Replace("-", "").Substring(0, 20);
                bool created;
                using (Mutex mutex = new Mutex(true, @"Local\DailyReflection-" + identity, out created))
                using (EventWaitHandle activation = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\DailyReflection-Activate-" + identity))
                {
                    if (!created) { activation.Set(); return 0; }
                    JournalStore store = new JournalStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "数据"));
                    ShutdownCountdown countdown = new ShutdownCountdown(new WindowsShutdownService(), WindowsShutdownService.KeepAwake,
                        () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
                    MainController main = new MainController(store, countdown, DateTime.Today);
                    app.MainWindow = main.Window;
                    app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                    RegisteredWaitHandle listener = ThreadPool.RegisterWaitForSingleObject(activation, delegate
                    {
                        if (!app.Dispatcher.HasShutdownStarted)
                        {
                            try { app.Dispatcher.BeginInvoke(new Action(main.Activate)); }
                            catch (InvalidOperationException) { }
                        }
                    }, null, Timeout.Infinite, false);
                    app.SessionEnding += delegate { main.CancelShutdown(); };
                    try { app.Run(main.Window); }
                    finally { listener.Unregister(null); mutex.ReleaseMutex(); }
                }
                return 0;
            }
            catch (Exception error)
            {
                if (verification)
                {
                    Directory.CreateDirectory(args[1]);
                    File.WriteAllText(Path.Combine(args[1], "failure.txt"), error.ToString(), Encoding.UTF8);
                }
                else MessageBox.Show("程序未能启动：\n" + error.Message, "每日一记吧", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }

        internal static Window LoadWindow(string name)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DailyReflection." + name + ".xaml"))
            {
                Window window = (Window)XamlReader.Load(stream);
                Rect area = SystemParameters.WorkArea;
                window.MinHeight = Math.Min(window.MinHeight, Math.Max(420, area.Height - 20));
                window.MinWidth = Math.Min(window.MinWidth, Math.Max(420, area.Width - 20));
                window.Height = Math.Min(window.Height, area.Height - 20);
                window.Width = Math.Min(window.Width, area.Width - 20);
                return window;
            }
        }
    }

    internal sealed class MainController
    {
        public Window Window { get; private set; }
        internal readonly TextBox Input;
        internal readonly Button SaveShutdown;
        internal readonly Button SaveOnly;
        internal readonly ShutdownCountdown Countdown;
        private readonly JournalStore store;
        private DateTime date;
        private readonly Func<DateTime> currentDate;
        private readonly TextBlock status;
        private readonly TextBlock hint;
        private readonly DispatcherTimer timer;
        private string savedText = "";
        private bool readError;
        private bool requestedShutdown;
        private HistoryController history;

        internal MainController(JournalStore journal, ShutdownCountdown countdown, DateTime entryDate, Func<DateTime> dateProvider = null)
        {
            store = journal; Countdown = countdown; date = entryDate.Date;
            currentDate = dateProvider ?? (() => DateTime.Today);
            Window = Program.LoadWindow("MainWindow");
            RefreshPoem();
            Input = (TextBox)Window.FindName("SummaryInput");
            SaveShutdown = (Button)Window.FindName("SaveShutdownButton");
            SaveOnly = (Button)Window.FindName("SaveOnlyButton");
            status = (TextBlock)Window.FindName("StatusLabel");
            hint = (TextBlock)Window.FindName("EntryHint");
            ((TextBlock)Window.FindName("DateLabel")).Text = date.ToString("M 月 d 日  ·  dddd", CultureInfo.GetCultureInfo("zh-CN"));
            hint.Text = "每天留下一点点，就很好。";
            try
            {
                Entry entry = store.Load().FirstOrDefault(e => e.Date == DateKey);
                // A new launch starts a fresh composer. Saved entries live in History.
                if (entry != null) hint.Text = "今天已有小记，再次保存会更新它。";
            }
            catch (Exception error)
            {
                readError = true;
                Input.IsReadOnly = true;
                SaveShutdown.IsEnabled = SaveOnly.IsEnabled = false;
                SetStatus(error.Message + "\n记录位置：" + store.FilePath, true);
            }
            Input.TextChanged += delegate { UpdateInput(); };
            UpdateInput();
            SaveOnly.Click += delegate { TrySave(); };
            SaveShutdown.Click += delegate { SaveAndShutdown(); };
            ((Button)Window.FindName("CancelShutdownButton")).Click += delegate { CancelShutdown(); };
            ((Button)Window.FindName("HistoryButton")).Click += delegate { OpenHistory(); };
            Window.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { SaveAndShutdown(); e.Handled = true; }
                else if (e.Key == Key.Escape && Countdown.IsArmed) { CancelShutdown(); e.Handled = true; }
            };
            Window.Closing += OnClosing;
            Window.Activated += delegate { RefreshToday(); };
            Window.Loaded += delegate { Input.Focus(); };
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            timer.Tick += delegate { Tick(); };
        }

        private string DateKey { get { return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); } }
        private bool IsDirty { get { return !readError && JournalStore.Normalize(Input.Text) != savedText; } }

        private void UpdateInput()
        {
            TextBlock counter = (TextBlock)Window.FindName("Counter");
            counter.Text = Input.Text.Length + " 字";
            counter.Visibility = Input.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void SetStatus(string text, bool error)
        {
            status.Text = text;
            status.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(error ? "#A65353" : "#637B9D"));
        }

        internal bool TrySave()
        {
            if (readError || Countdown.IsArmed) return false;
            try
            {
                if (!RefreshToday()) return false;
                Entry entry = store.Save(date, Input.Text);
                savedText = entry.Summary;
                Input.Text = savedText;
                hint.Text = "今天的小记已保存，还可以改一改。";
                SetStatus("已保存  ·  " + DateTime.Now.ToString("HH:mm") + "  ·  今天辛苦了。", false);
                return true;
            }
            catch (Exception error)
            {
                SetStatus("保存失败，关机未启动：" + error.Message, true);
                Input.Focus();
                return false;
            }
        }

        private bool RefreshToday()
        {
            DateTime today = currentDate().Date;
            if (today == date || Countdown.IsArmed || readError) return !readError;
            try
            {
                string draft = IsDirty ? Input.Text : null;
                string key = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                Entry entry = store.Load().FirstOrDefault(e => e.Date == key);
                date = today;
                savedText = "";
                Input.Text = draft ?? "";
                ((TextBlock)Window.FindName("DateLabel")).Text = date.ToString("M 月 d 日  ·  dddd", CultureInfo.GetCultureInfo("zh-CN"));
                hint.Text = entry == null ? "每天留下一点点，就很好。" : "今天已有小记，再次保存会更新它。";
                SetStatus("已切换到 " + key + (draft == null ? "。" : "，输入内容已保留。"), false);
                return true;
            }
            catch (Exception error) { SetStatus("无法切换日期：" + error.Message, true); return false; }
        }

        internal void SaveAndShutdown()
        {
            if (Countdown.IsArmed || !TrySave()) return;
            if (history != null) { history.Window.Activate(); SetStatus("请先关闭历史记录窗口，再点击保存并关机。", false); return; }
            try
            {
                Countdown.Begin();
                Input.IsEnabled = false;
                ((UIElement)Window.FindName("ActionPanel")).Visibility = Visibility.Collapsed;
                ((UIElement)Window.FindName("CountdownPanel")).Visibility = Visibility.Visible;
                ((Button)Window.FindName("HistoryButton")).IsEnabled = false;
                UpdateCountdown();
                Window.Topmost = true;
                timer.Start();
                ((Button)Window.FindName("CancelShutdownButton")).Focus();
                SetStatus("想再待一会儿，可以取消；Esc 或关闭窗口也可以。", false);
            }
            catch (Exception error) { ResetCountdownUI(); SetStatus("小记已保存，关机未启动：" + error.Message, true); }
        }

        private void UpdateCountdown()
        {
            ((TextBlock)Window.FindName("CountdownTitle")).Text = Countdown.SecondsRemaining + " 秒后关机";
        }

        internal void Tick()
        {
            if (!Countdown.IsArmed) return;
            try
            {
                if (Countdown.Tick())
                {
                    requestedShutdown = true;
                    timer.Stop();
                    Window.Close();
                }
                else UpdateCountdown();
            }
            catch (Exception error)
            {
                ResetCountdownUI();
                SetStatus("小记已保存，但 Windows 未接受关机请求：" + error.Message, true);
            }
        }

        internal void CancelShutdown()
        {
            bool armed = Countdown.IsArmed;
            Countdown.Cancel();
            ResetCountdownUI();
            if (armed) SetStatus("已取消关机，今天的小记已经保存。", false);
        }

        private void ResetCountdownUI()
        {
            timer.Stop();
            Window.Topmost = false;
            Input.IsEnabled = true;
            ((UIElement)Window.FindName("ActionPanel")).Visibility = Visibility.Visible;
            ((UIElement)Window.FindName("CountdownPanel")).Visibility = Visibility.Collapsed;
            ((Button)Window.FindName("HistoryButton")).IsEnabled = true;
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            CancelShutdown();
            if (requestedShutdown || !IsDirty) return;
            MessageBoxResult answer = MessageBox.Show(Window, "这段小记还没有保存。是否保存后退出？\n这里的保存不会关机。", "每日一记吧", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !TrySave())) e.Cancel = true;
        }

        private void OpenHistory()
        {
            if (history != null) { history.Window.Activate(); return; }
            history = new HistoryController(store);
            history.Window.Owner = Window;
            history.Window.ShowInTaskbar = false;
            history.Window.Closed += delegate
            {
                history = null;
                if (!IsDirty && !readError)
                {
                    try
                    {
                        Entry entry = store.Load().FirstOrDefault(e => e.Date == DateKey);
                        // Refresh text only when this session has already saved a sentence.
                        // Opening History must not fill an otherwise empty composer.
                        if (entry != null && savedText.Length > 0) Input.Text = savedText = entry.Summary;
                        hint.Text = entry == null ? "每天留下一点点，就很好。" : "今天已有小记，再次保存会更新它。";
                    }
                    catch (Exception error) { SetStatus(error.Message, true); }
                }
            };
            history.Window.Show();
        }

        private void RefreshPoem()
        {
            PoemQuote quote = PoemCatalog.Pick();
            TextBlock label = (TextBlock)Window.FindName("PoemLabel");
            label.Inlines.Clear();
            label.Inlines.Add(new Run(quote.Text));
            label.Inlines.Add(new Run("　— " + quote.Author) { FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"), FontSize = 10.5 });
            label.ToolTip = quote.Author + "《" + quote.Work + "》";
        }

        internal void Activate()
        {
            RefreshPoem();
            if (Window.WindowState == WindowState.Minimized) Window.WindowState = WindowState.Normal;
            Window.Show(); Window.Activate();
            bool previous = Window.Topmost; Window.Topmost = true; Window.Topmost = previous;
        }
    }

    internal sealed class HistoryController
    {
        public Window Window { get; private set; }
        private readonly JournalStore store;
        private readonly ListBox list;
        private readonly TextBox editor;
        private readonly TextBox search;
        private readonly TextBlock status;
        private Entry selected;
        private bool updating;
        private string query = "";

        internal HistoryController(JournalStore journal)
        {
            store = journal;
            Window = Program.LoadWindow("HistoryWindow");
            list = (ListBox)Window.FindName("EntryList");
            editor = (TextBox)Window.FindName("HistoryInput");
            search = (TextBox)Window.FindName("SearchInput");
            status = (TextBlock)Window.FindName("HistoryStatus");
            search.TextChanged += delegate
            {
                if (updating) return;
                if (!ConfirmDirty()) { updating = true; search.Text = query; updating = false; return; }
                query = search.Text;
                ((TextBlock)Window.FindName("SearchPlaceholder")).Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                Refresh(null);
            };
            list.SelectionChanged += delegate
            {
                if (updating) return;
                Entry next = list.SelectedItem as Entry;
                if (!ConfirmDirty()) { updating = true; list.SelectedItem = selected; updating = false; return; }
                selected = next;
                ShowSelected();
            };
            ((Button)Window.FindName("SaveEditButton")).Click += delegate
            {
                if (SaveSelected()) { string key = selected.Date; Refresh(key); }
            };
            ((Button)Window.FindName("ExportButton")).Click += delegate { Export(); };
            Window.Closing += delegate(object sender, CancelEventArgs e) { if (!ConfirmDirty()) e.Cancel = true; };
            Refresh(null);
        }

        private bool ConfirmDirty()
        {
            if (selected == null || JournalStore.Normalize(editor.Text) == selected.Summary) return true;
            MessageBoxResult answer = MessageBox.Show(Window, "这条记录的修改尚未保存。是否保存？", "历史记录", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            return answer == MessageBoxResult.No || (answer == MessageBoxResult.Yes && SaveSelected());
        }

        private bool SaveSelected()
        {
            if (selected == null) return false;
            try
            {
                selected = store.Save(DateTime.ParseExact(selected.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture), editor.Text);
                editor.Text = selected.Summary;
                status.Text = "已保存修改。";
                return true;
            }
            catch (Exception error) { status.Text = "保存失败：" + error.Message; return false; }
        }

        private void Refresh(string keepDate)
        {
            try
            {
                var all = store.Load();
                var filtered = all.Where(e => e.Date.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || e.Summary.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                updating = true;
                list.ItemsSource = filtered;
                selected = filtered.FirstOrDefault(e => e.Date == keepDate) ?? filtered.FirstOrDefault();
                list.SelectedItem = selected;
                updating = false;
                ((TextBlock)Window.FindName("TotalLabel")).Text = "共留下 " + all.Count + " 天的记录" + (query.Length == 0 ? "" : "  ·  找到 " + filtered.Count + " 条");
                if (all.Count == 0) status.Text = "还没有记录。写下今天的第一句话吧。";
                ShowSelected();
            }
            catch (Exception error) { updating = false; status.Text = error.Message; }
        }

        private void ShowSelected()
        {
            editor.IsEnabled = selected != null;
            ((Button)Window.FindName("SaveEditButton")).IsEnabled = selected != null;
            ((TextBlock)Window.FindName("SelectedDate")).Text = selected == null ? "没有匹配的记录" : selected.FriendlyDate;
            editor.Text = selected == null ? "" : selected.Summary;
            DateTimeOffset updated;
            ((TextBlock)Window.FindName("UpdatedLabel")).Text = selected != null && DateTimeOffset.TryParse(selected.UpdatedAt, out updated)
                ? "最后保存于 " + updated.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "";
        }

        private void Export()
        {
            if (!ConfirmDirty()) return;
            SaveFileDialog dialog = new SaveFileDialog { Title = "导出全部小记", Filter = "Markdown 文件 (*.md)|*.md|文本文件 (*.txt)|*.txt", FileName = "每日一记吧-" + DateTime.Today.ToString("yyyyMMdd") + ".md", AddExtension = true };
            if (dialog.ShowDialog(Window) != true) return;
            try { File.WriteAllText(dialog.FileName, store.ExportMarkdown(), new UTF8Encoding(true)); status.Text = "已导出到：" + dialog.FileName; }
            catch (Exception error) { status.Text = "导出失败：" + error.Message; }
        }
    }
}
