using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DailyReflection
{
    internal sealed class FakeShutdownService : IShutdownService
    {
        internal int Calls;
        internal bool Fail;
        internal Action Verify;
        public void RequestShutdown()
        {
            Calls++;
            if (Verify != null) Verify();
            if (Fail) throw new InvalidOperationException("模拟 Windows 拒绝请求");
        }
    }

    internal static class SelfTests
    {
        private static readonly List<string> results = new List<string>();
        private static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + description);
            results.Add("PASS: " + description);
        }
        private static void ExpectFailure(Action operation, string description)
        {
            bool failed = false;
            try { operation(); } catch { failed = true; }
            Check(failed, description);
        }

        internal static int Run(string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);
            Random poemRandom = new Random(20261012);
            List<PoemQuote> draws = Enumerable.Range(0, 10000).Select(i => PoemCatalog.Pick(poemRandom)).ToList();
            int maoDraws = draws.Count(p => p.Author == "毛泽东");
            Check(maoDraws > 5500 && maoDraws < 6500, "随机抽样中毛泽东诗词约占 60%，高于其他作者合计");
            Check(draws.Where(p => p.Author == "毛泽东").Select(p => p.Text).Distinct().Count() > 1
                && draws.Where(p => p.Author != "毛泽东").Select(p => p.Author).Distinct().Count() > 1,
                "毛泽东组有多句可选，其他作者也能被抽到");
            Check(draws.All(p => !String.IsNullOrWhiteSpace(p.Text) && !String.IsNullOrWhiteSpace(p.Author) && !String.IsNullOrWhiteSpace(p.Work)),
                "随机诗句都有作者和作品来源");
            string sandbox = Path.Combine(outputDirectory, "run-" + Guid.NewGuid().ToString("N"));
            DateTime today = new DateTime(2026, 10, 12);
            JournalStore store = new JournalStore(Path.Combine(sandbox, "storage"));
            Check(store.Load().Count == 0, "首次使用没有虚构记录");
            Entry first = store.Save(today, "  完成了初稿，\r\n晚上散步 🚶。  ");
            Check(first.Summary == "完成了初稿， 晚上散步 🚶。", "中文、emoji 和多行粘贴正确保存");
            Check(new JournalStore(store.DirectoryPath).Load()[0].Summary == first.Summary, "重新打开后记录仍然存在");
            store.Save(today.AddDays(-1), "读了十页书。");
            string before = File.ReadAllText(store.FilePath);
            Entry changed = store.Save(today, "完成了初稿，晚上跑步。");
            Check(store.Load().Count == 2 && store.Load()[0].Summary == changed.Summary, "同一天更新并保留其他日期");
            Check(changed.CreatedAt == first.CreatedAt, "更新记录保留首次创建时间");
            Check(File.ReadAllText(store.BackupPath) == before, "原子写入保留上一版备份");
            Check(Directory.GetFiles(store.DirectoryPath, "*.tmp").Length == 0, "写入后没有残留临时文件");
            ExpectFailure(() => store.Save(today, "  \r\n "), "空白内容不能保存");
            ExpectFailure(() => store.Save(today, new string('字', 601)), "超长内容不能保存");
            using (FileStream locked = new FileStream(store.LockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                ExpectFailure(() => store.Save(today, "不能写入"), "并发写入被阻止");
            Check(store.Load()[0].Summary == changed.Summary, "失败的写入不改变原有记录");
            Check(store.ExportMarkdown().Contains("2026-10-12") && store.ExportMarkdown().Contains("读了十页书。"), "导出包含所有日期及中文内容");

            JournalStore corrupt = new JournalStore(Path.Combine(sandbox, "corrupt"));
            Directory.CreateDirectory(corrupt.DirectoryPath);
            File.WriteAllText(corrupt.FilePath, "{broken-json}", Encoding.UTF8);
            ExpectFailure(() => corrupt.Save(today, "新内容"), "损坏的记录不会被新内容覆盖");
            Check(File.ReadAllText(corrupt.FilePath) == "{broken-json}", "损坏文件保留供恢复");

            JournalStore reopenStore = new JournalStore(Path.Combine(sandbox, "reopen"));
            reopenStore.Save(today.AddDays(-1), "昨天的记录。");
            reopenStore.Save(today, "上一次关机时的总结。");
            string originalRecords = File.ReadAllText(reopenStore.FilePath);
            FakeShutdownService reopenShutdown = new FakeShutdownService();
            MainController reopened = new MainController(reopenStore, new ShutdownCountdown(reopenShutdown, delegate { }, () => 0), today, () => today);
            Check(reopened.Input.Text == "" && reopened.Window.FindName("Placeholder") == null,
                "同一天重新打开时输入框完全空白，没有提示文字或旧内容");
            Check(!reopened.TrySave() && File.ReadAllText(reopenStore.FilePath) == originalRecords,
                "空白输入不会覆盖之前保存的总结");
            reopened.SaveShutdown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!reopened.Countdown.IsArmed && reopenShutdown.Calls == 0, "已有记录时仍须重新填写才能启动关机");
            reopened.Window.Show(); Pump();
            ((Button)reopened.Window.FindName("HistoryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Window historyWindow = Application.Current.Windows.Cast<Window>().First(w => w.Owner == reopened.Window);
            Check(((ListBox)historyWindow.FindName("EntryList")).Items.Count == 2, "重新打开后原来的总结仍能在历史记录查看");
            historyWindow.Close();
            Check(reopened.Input.Text == "" && File.ReadAllText(reopenStore.FilePath) == originalRecords, "关闭历史记录不自动回填，也不改动记录文件");
            reopened.Window.Close();
            Check(File.ReadAllText(reopenStore.FilePath) == originalRecords, "关闭空白窗口不删除或改写既有记录");
            MainController replacement = new MainController(reopenStore, new ShutdownCountdown(new FakeShutdownService(), delegate { }, () => 0), today, () => today);
            replacement.Input.Text = "重新填写的今日总结。"; replacement.TrySave();
            Check(reopenStore.Load().Count == 2 && reopenStore.Load()[0].Summary == "重新填写的今日总结。", "重新填写仍更新当天记录并保留其他日期");
            replacement.Window.Close();

            DateTime composerDay = today;
            reopenStore.Save(today.AddDays(1), "已存在的明天记录。");
            MainController newDay = new MainController(reopenStore, new ShutdownCountdown(new FakeShutdownService(), delegate { }, () => 0), composerDay, () => composerDay);
            composerDay = today.AddDays(1); newDay.TrySave();
            Check(newDay.Input.Text == "" && reopenStore.Load()[0].Summary == "已存在的明天记录。", "跨日时也不自动回填目标日期的旧记录");
            newDay.Window.Close();

            double clock = 0;
            List<bool> awake = new List<bool>();
            FakeShutdownService fake = new FakeShutdownService();
            ShutdownCountdown countdown = new ShutdownCountdown(fake, awake.Add, () => clock);
            MainController ui = new MainController(new JournalStore(Path.Combine(sandbox, "ui")), countdown, today, () => today);
            ui.SaveShutdown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!countdown.IsArmed && fake.Calls == 0, "空白总结无法启动关机");
            ui.Input.Text = "测试只保存，不关机。";
            ui.SaveOnly.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!countdown.IsArmed && fake.Calls == 0, "只保存按钮不会触发关机");
            ui.SaveShutdown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(countdown.IsArmed && countdown.SecondsRemaining == 10, "保存成功后启动十秒倒计时");
            ui.CancelShutdown(); clock = 30; ui.Tick();
            Check(fake.Calls == 0 && !countdown.IsArmed && awake.Last() == false, "取消后不关机并解除保持唤醒");
            JournalStore uiStore = new JournalStore(Path.Combine(sandbox, "ui"));
            using (FileStream locked = new FileStream(uiStore.LockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                ui.Input.Text = "模拟保存失败。";
                ui.SaveShutdown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!countdown.IsArmed && fake.Calls == 0, "保存失败不会启动倒计时");
            }
            ui.Input.Text = "已经写入磁盘的总结。";
            fake.Verify = () => Check(uiStore.Load()[0].Summary == "已经写入磁盘的总结。", "请求关机时总结已经持久化");
            ui.SaveShutdown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            clock = 39.9; ui.Tick();
            Check(fake.Calls == 0, "十秒截止前不请求关机");
            clock = 40; ui.Tick(); ui.Tick();
            Check(fake.Calls == 1, "倒计时到期仅请求一次关机");

            FakeShutdownService denied = new FakeShutdownService { Fail = true };
            MainController deniedUi = new MainController(new JournalStore(Path.Combine(sandbox, "denied")), new ShutdownCountdown(denied, delegate { }, () => clock), today, () => today);
            deniedUi.Input.Text = "Windows 拒绝关机时也保留总结。";
            deniedUi.SaveShutdown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            clock += 10; deniedUi.Tick();
            Check(denied.Calls == 1 && !deniedUi.Countdown.IsArmed && deniedUi.Input.IsEnabled, "关机请求失败后恢复编辑并保留记录");
            Check(((TextBlock)deniedUi.Window.FindName("StatusLabel")).Text.Contains("未接受关机请求"), "Windows 错误展示给用户");
            deniedUi.Window.Close();

            FakeShutdownService onClose = new FakeShutdownService();
            MainController closeUi = new MainController(new JournalStore(Path.Combine(sandbox, "close")), new ShutdownCountdown(onClose, delegate { }, () => clock), today, () => today);
            closeUi.Input.Text = "关闭倒计时窗口取消关机。";
            closeUi.SaveShutdown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            closeUi.Window.Close(); clock += 20; closeUi.Tick();
            Check(onClose.Calls == 0 && !closeUi.Countdown.IsArmed, "关闭窗口取消关机");
            Check((WindowsShutdownService.PowerOffFlags & (0x04U | 0x10U)) == 0, "生产关机调用没有强制退出标志");

            DateTime day = today;
            JournalStore rolloverStore = new JournalStore(Path.Combine(sandbox, "rollover"));
            MainController rolloverUi = new MainController(rolloverStore, new ShutdownCountdown(new FakeShutdownService(), delegate { }, () => clock), day, () => day);
            rolloverUi.Input.Text = "昨天的总结。"; rolloverUi.TrySave();
            day = day.AddDays(1);
            rolloverUi.TrySave();
            Check(rolloverUi.Input.Text == "" && rolloverStore.Load().Count == 1, "跨日后不把昨天的总结重复记到今天");
            rolloverUi.Input.Text = "跨日时保留正在输入的草稿。";
            day = day.AddDays(1); rolloverUi.TrySave();
            Check(rolloverStore.Load()[0].Date == "2026-10-14" && rolloverStore.Load()[0].Summary == rolloverUi.Input.Text, "跨日草稿保留并保存到当前日期");
            rolloverUi.Window.Close();

            HistoryController history = new HistoryController(store);
            TextBox historySearch = (TextBox)history.Window.FindName("SearchInput");
            ListBox historyList = (ListBox)history.Window.FindName("EntryList");
            historySearch.Text = "读了";
            Check(historyList.Items.Count == 1 && ((Entry)historyList.Items[0]).Date == "2026-10-11", "历史记录按关键词筛选");
            historySearch.Text = "2026-10-12";
            Check(historyList.Items.Count == 1, "历史记录按日期筛选");
            ((TextBox)history.Window.FindName("HistoryInput")).Text = "修改后的历史记录。";
            ((Button)history.Window.FindName("SaveEditButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(store.Load()[0].Summary == "修改后的历史记录。", "历史记录修改写入磁盘");
            history.Window.Close();

            File.WriteAllLines(Path.Combine(outputDirectory, "test-results.txt"), results.Concat(new[] { "", "全部 " + results.Count + " 项通过；关机服务使用模拟实现，未执行真实关机。" }), new UTF8Encoding(true));
            return 0;
        }

        internal static int Preview(string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);
            JournalStore store = new JournalStore(Path.Combine(outputDirectory, "preview-data-" + Guid.NewGuid().ToString("N")));
            FakeShutdownService fake = new FakeShutdownService();
            double clock = 0;
            DateTime previewDate = new DateTime(2026, 10, 12);
            MainController main = new MainController(store, new ShutdownCountdown(fake, delegate { }, () => clock), previewDate, () => previewDate);
            main.Window.Show(); Pump();
            Capture(main.Window, Path.Combine(outputDirectory, "main-empty.png"));
            main.Input.Text = "今天有点累，但傍晚的风很舒服。\n和朋友聊了几句，心情慢慢好了。";
            main.SaveOnly.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Capture(main.Window, Path.Combine(outputDirectory, "main-saved.png"));
            main.SaveShutdown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Capture(main.Window, Path.Combine(outputDirectory, "countdown.png"));
            main.CancelShutdown();
            store.Save(new DateTime(2026, 10, 11), "读完了一篇论文，整理了研究笔记。");
            store.Save(new DateTime(2026, 10, 10), "把积压的工作处理完，和朋友吃了晚饭。");
            HistoryController history = new HistoryController(store);
            history.Window.Owner = main.Window;
            history.Window.Show(); Pump();
            Capture(history.Window, Path.Combine(outputDirectory, "history.png"));
            history.Window.Close();
            string savedPreview = main.Input.Text;
            main.Input.Text = new string('字', 600);
            using (FileStream locked = new FileStream(store.LockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                main.TrySave(); Pump();
                Capture(main.Window, Path.Combine(outputDirectory, "long-input-error.png"));
            }
            main.Input.Text = savedPreview;
            main.Window.Close();
            JournalStore corrupt = new JournalStore(Path.Combine(outputDirectory, "preview-corrupt-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(corrupt.DirectoryPath);
            File.WriteAllText(corrupt.FilePath, "{broken-json}", Encoding.UTF8);
            MainController readError = new MainController(corrupt, new ShutdownCountdown(fake, delegate { }, () => clock), previewDate, () => previewDate);
            readError.Window.Show(); Pump();
            Capture(readError.Window, Path.Combine(outputDirectory, "read-error.png"));
            readError.Window.Close();
            return 0;
        }

        private static void Pump()
        {
            DispatcherFrame frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        private static void Capture(Window window, string path)
        {
            window.UpdateLayout();
            FrameworkElement visual = (FrameworkElement)window.Content;
            // Include the window background and content's outer margin.
            double width = visual.ActualWidth + visual.Margin.Left + visual.Margin.Right;
            double height = visual.ActualHeight + visual.Margin.Top + visual.Margin.Bottom;
            DrawingVisual drawing = new DrawingVisual();
            using (DrawingContext context = drawing.RenderOpen())
            {
                context.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
                context.DrawRectangle(new VisualBrush(visual), null, new Rect(visual.Margin.Left, visual.Margin.Top, visual.ActualWidth, visual.ActualHeight));
            }
            RenderTargetBitmap bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * 1.5), (int)Math.Ceiling(height * 1.5), 144, 144, PixelFormats.Pbgra32);
            bitmap.Render(drawing);
            PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream file = File.Create(path)) encoder.Save(file);
        }
    }
}
