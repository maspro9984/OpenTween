// OpenTween - Client of Twitter
// Copyright (c) 2026 OpenTween contributors
// All rights reserved.
//
// This file is part of OpenTween.
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation; either version 3 of the License, or (at your option)
// any later version.
//
// This program is distributed in the hope that it will be useful, but
// WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
// or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License
// for more details.
//
// You should have received a copy of the GNU General Public License along
// with this program. If not, see <http://www.gnu.org/licenses/>, or write to
// the Free Software Foundation, Inc., 51 Franklin Street - Fifth Floor,
// Boston, MA 02110-1301, USA.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Xunit;
using Xunit.Abstractions;

namespace OpenTween
{
    public class UIHangWatchdogTest
    {
        private readonly ITestOutputHelper output;

        public UIHangWatchdogTest(ITestOutputHelper output)
            => this.output = output;

        [Fact]
        public void DetectHang_SleepTest()
        {
            var log = this.RunWithWatchdog(HangBySleep);

            Assert.Contains("UI スレッド応答停止を検出", log);
            Assert.Contains(nameof(HangBySleep), log);
            Assert.Contains("Wait/", log);
            Assert.Contains("主に「待ち」", log);
            Assert.Contains("応答再開", log);
        }

        [Fact]
        public void DetectHang_BusyLoopTest()
        {
            var log = this.RunWithWatchdog(HangByBusyLoop);

            Assert.Contains(nameof(HangByBusyLoop), log);
            Assert.Contains("主に「計算」", log);
            Assert.Contains("応答再開", log);
        }

        [Fact]
        public void DetectHang_SendMessageToBlockedThreadTest()
        {
            var log = this.RunWithWatchdog(HangBySendMessageToBlockedThread);

            Assert.Contains(nameof(HangBySendMessageToBlockedThread), log);
            Assert.Contains("-> [SendMessage", log);
            Assert.Contains("応答再開", log);
        }

        [Fact]
        public void NoHang_Test()
        {
            var log = this.RunWithWatchdog(() => Thread.Sleep(100));

            Assert.DoesNotContain("UI スレッド応答停止を検出", log);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void HangBySleep()
            => Thread.Sleep(2000);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void HangByBusyLoop()
        {
            var sw = Stopwatch.StartNew();
            var x = 0L;
            while (sw.ElapsedMilliseconds < 2000)
                x++;

            GC.KeepAlive(x);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void HangBySendMessageToBlockedThread()
        {
            var hwnd = IntPtr.Zero;
            using var ready = new ManualResetEventSlim(false);

            var otherThread = new Thread(() =>
            {
                using var control = new Control();
                hwnd = control.Handle;
                ready.Set();

                // メッセージを処理せずに停止する
                Thread.Sleep(2000);
                Application.DoEvents();
            });
            otherThread.SetApartmentState(ApartmentState.STA);
            otherThread.Start();
            ready.Wait();

            SendMessage(hwnd, 0x0000 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero);
            otherThread.Join();
        }

        private string RunWithWatchdog(Action hang)
        {
            var logDir = Path.Combine(Path.GetTempPath(), $"OpenTweenUIHangTest-{Guid.NewGuid():N}");
            var settings = new UIHangWatchdog.Settings(logDir)
            {
                HangThreshold = TimeSpan.FromMilliseconds(500),
                SampleInterval = TimeSpan.FromMilliseconds(200),
                DumpThreshold = null,
            };

            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using var control = new Control();
                    _ = control.Handle; // WindowsFormsSynchronizationContext を設定させる

                    var watchdog = new UIHangWatchdog(settings);
                    SynchronizationContext.Current!.Post(async _ =>
                    {
                        try
                        {
                            await Task.Delay(300);
                            hang();
                            await Task.Delay(800);
                        }
                        catch (Exception ex)
                        {
                            error = ex;
                        }
                        finally
                        {
                            watchdog.Dispose();
                            Application.ExitThread();
                        }
                    }, null);

                    Application.Run();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)));

            if (error != null)
                throw new Exception("UI スレッドでエラー", error);

            try
            {
                var files = Directory.Exists(logDir) ? Directory.GetFiles(logDir, "*.log") : Array.Empty<string>();
                var log = files.Length == 0 ? "" : File.ReadAllText(files[0]);
                this.output.WriteLine(log);
                return log;
            }
            finally
            {
                if (Directory.Exists(logDir))
                    Directory.Delete(logDir, recursive: true);
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
