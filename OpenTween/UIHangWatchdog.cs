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

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

namespace OpenTween
{
    /// <summary>
    /// UI スレッドの応答停止を検出し、停止中の UI スレッドの状態をログに記録するデバッグ用の監視クラス
    /// </summary>
    /// <remarks>
    /// 監視スレッドから UI スレッドのメッセージ専用ウィンドウへ定期的に PostMessage し、
    /// 一定時間以内に処理されなかった場合を応答停止とみなす。
    /// 応答停止中は UI スレッドのスタックトレース・待機チェーン (WCT)・CPU 時間などを定期的に採取し、
    /// 長時間続いた場合は別プロセスからミニダンプを書き出す。
    /// </remarks>
    internal sealed class UIHangWatchdog : IDisposable
    {
        /// <summary>ダンプ書き出し用の子プロセスとして起動された事を示すコマンドライン引数</summary>
        public const string DumpCommandArg = "--ot-write-hang-dump";

        private const int WmPing = 0x0400 + 0x0b0b; // WM_USER + α (自前のメッセージ専用ウィンドウ宛てなので衝突しない)
        private const int MaxMarks = 40;
        private const int MaxStackFrames = 120;

        private static readonly IntPtr HwndMessage = new(-3);
        private static readonly ConcurrentQueue<string> Marks = new();

        private readonly Settings config;
        private readonly Thread uiThread;
        private readonly int uiNativeThreadId;
        private readonly PingWindow pingWindow;
        private readonly LastMessageFilter messageFilter;
        private readonly Thread watchThread;
        private readonly ManualResetEventSlim stopEvent = new(false);

        private int pingOutstanding;
        private long pingSentTicks;
        private long pingAckTicks;
        private HangRecord? currentHang;
        private int hangCount;
        private bool disposed;

        public UIHangWatchdog(Settings config)
        {
            this.config = config;
            this.uiThread = Thread.CurrentThread;
            this.uiNativeThreadId = GetCurrentThreadId();
            this.pingWindow = new PingWindow(this);
            this.messageFilter = new LastMessageFilter();
            Application.AddMessageFilter(this.messageFilter);

            this.watchThread = new Thread(this.WatchLoop)
            {
                Name = "UIHangWatchdog",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
            };
            this.watchThread.Start();
        }

        /// <summary>直近に記録されたログファイルのパス</summary>
        public string LogFilePath
            => Path.Combine(this.config.LogDirectory, $"UIHang-{DateTime.Now:yyyyMMdd}.log");

        /// <summary>検出した応答停止の回数</summary>
        public int HangCount
            => Volatile.Read(ref this.hangCount);

        /// <summary>
        /// 応答停止の前後関係を追うための目印を記録します。応答停止を検出した時に直近の目印がログに出力されます
        /// </summary>
        public static void Mark(string text)
        {
            Marks.Enqueue($"{DateTime.Now:HH:mm:ss.fff} [T{GetCurrentThreadId()}] {text}");
            while (Marks.Count > MaxMarks)
                Marks.TryDequeue(out _);
        }

        public static string GetDefaultLogDirectory()
        {
            try
            {
                var dir = Path.Combine(MyCommon.GetErrorLogPath(), "UIHang");
                Directory.CreateDirectory(dir);

                // 書き込み可能か確認する
                var probe = Path.Combine(dir, ".probe");
                File.WriteAllText(probe, "");
                File.Delete(probe);

                return dir;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ApplicationSettings.AssemblyName, "UIHang");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public void Dispose()
        {
            if (this.disposed)
                return;

            this.disposed = true;
            this.stopEvent.Set();
            this.watchThread.Join(TimeSpan.FromSeconds(3));
            Application.RemoveMessageFilter(this.messageFilter);
            this.pingWindow.DestroyHandle();
            this.stopEvent.Dispose();
        }

        private void OnPingReceived()
        {
            Volatile.Write(ref this.pingAckTicks, Stopwatch.GetTimestamp());
            Volatile.Write(ref this.pingOutstanding, 0);
        }

        private void WatchLoop()
        {
            var lastLoopTicks = Stopwatch.GetTimestamp();

            while (!this.stopEvent.Wait(this.config.PollInterval))
            {
                try
                {
                    this.Poll(ref lastLoopTicks);
                }
                catch (Exception ex)
                {
                    this.WriteLog($"!!! UIHangWatchdog 内部エラー: {ex}");
                }
            }

            // 終了時点で応答停止中だった場合も記録を締める
            if (this.currentHang != null)
                this.EndHang(Stopwatch.GetTimestamp(), aborted: true);
        }

        private void Poll(ref long lastLoopTicks)
        {
            var now = Stopwatch.GetTimestamp();
            var loopGap = ElapsedBetween(lastLoopTicks, now);
            lastLoopTicks = now;

            // 監視スレッド自体が長時間止まっていた (スリープからの復帰、またはプロセス全体の停止)
            if (loopGap >= this.config.WatchdogGapThreshold)
            {
                var gapText = $"監視スレッド自体が {loopGap.TotalSeconds:F1} 秒停止していました (システムのスリープ復帰、またはプロセス全体の停止・ページング)";
                if (this.currentHang != null)
                {
                    this.currentHang.Log.AppendLine($"  !!! {gapText}");
                    this.FlushHangLog();
                }
                else
                {
                    this.WriteLog($"{DateTime.Now:yyyy/MM/dd HH:mm:ss.fff} {gapText}");

                    // スリープ等で止まっていた時間を応答停止と誤検出しないよう待ち時間を数え直す
                    if (Volatile.Read(ref this.pingOutstanding) != 0)
                        Volatile.Write(ref this.pingSentTicks, now);
                    return;
                }
            }

            if (Volatile.Read(ref this.pingOutstanding) == 0)
            {
                if (this.currentHang != null)
                    this.EndHang(Volatile.Read(ref this.pingAckTicks), aborted: false);

                this.SendPing(now);
                return;
            }

            var waited = ElapsedBetween(Volatile.Read(ref this.pingSentTicks), now);

            if (this.currentHang == null)
            {
                if (waited >= this.config.HangThreshold && !Debugger.IsAttached)
                    this.BeginHang(Volatile.Read(ref this.pingSentTicks), now);

                return;
            }

            if (ElapsedBetween(this.currentHang.LastSampleTicks, now) >= this.config.SampleInterval)
                this.TakeSample(now);

            if (this.config.DumpThreshold != null && !this.currentHang.DumpRequested &&
                ElapsedBetween(this.currentHang.StartTicks, now) >= this.config.DumpThreshold.Value)
            {
                this.RequestDump();
            }
        }

        private void SendPing(long now)
        {
            Volatile.Write(ref this.pingSentTicks, now);
            Volatile.Write(ref this.pingOutstanding, 1);

            if (!PostMessage(this.pingWindow.Handle, WmPing, IntPtr.Zero, IntPtr.Zero))
            {
                // ウィンドウが破棄された等。次回のポーリングで送り直す
                Volatile.Write(ref this.pingOutstanding, 0);
            }
        }

        private void BeginHang(long startTicks, long now)
        {
            Interlocked.Increment(ref this.hangCount);

            using var proc = Process.GetCurrentProcess();
            var hang = new HangRecord(startTicks, DateTime.Now - ElapsedBetween(startTicks, now))
            {
                StartGcCounts = GetGcCounts(),
                StartPageFaults = GetPageFaultCount(proc),
                StartProcessCpu = proc.TotalProcessorTime,
                StartUIThreadCpu = this.GetUIProcessThread(proc)?.TotalProcessorTime,
            };
            this.currentHang = hang;

            var log = hang.Log;
            log.AppendLine();
            log.AppendLine("====================================================================");
            log.AppendLine($"UI スレッド応答停止を検出: 開始 {hang.StartTime:yyyy/MM/dd HH:mm:ss.fff} (検出閾値 {this.config.HangThreshold.TotalSeconds:F1} 秒)");
            log.AppendLine($"OpenTween {MyCommon.FileVersion} / {Environment.OSVersion.VersionString} / CLR {Environment.Version} / {(Environment.Is64BitProcess ? "x64" : "x86")}");
            log.AppendLine($"UI スレッド: managed id={this.uiThread.ManagedThreadId}, native id={this.uiNativeThreadId}");
            log.AppendLine($"プロセス: 起動から {DateTime.Now - proc.StartTime:d\\.hh\\:mm\\:ss}, スレッド数 {proc.Threads.Count}, ハンドル数 {proc.HandleCount}");
            log.AppendLine($"メモリ: WorkingSet {proc.WorkingSet64 / 1024 / 1024} MB, Private {proc.PrivateMemorySize64 / 1024 / 1024} MB, GC ヒープ {GC.GetTotalMemory(false) / 1024 / 1024} MB, GC 回数 {FormatGcCounts(hang.StartGcCounts)}");

            ThreadPool.GetAvailableThreads(out var availWorker, out var availIo);
            ThreadPool.GetMaxThreads(out var maxWorker, out var maxIo);
            ThreadPool.GetMinThreads(out var minWorker, out var minIo);
            log.AppendLine($"ThreadPool: worker 使用中 {maxWorker - availWorker} (min {minWorker}), IO 使用中 {maxIo - availIo} (min {minIo})");

            var foreground = GetForegroundWindow();
            GetWindowThreadProcessId(foreground, out var fgPid);
            log.AppendLine($"前面ウィンドウ: hwnd=0x{foreground.ToInt64():X} class=\"{GetClassNameSafe(foreground)}\" pid={fgPid}{(fgPid == proc.Id ? " (自プロセス)" : "")}");

            log.AppendLine($"メインループで最後に取り出したメッセージ: {this.messageFilter.Describe()}");

            log.AppendLine("直近の目印 (Mark):");
            foreach (var mark in Marks.ToArray())
                log.AppendLine($"  {mark}");

            log.AppendLine("--- サンプル (経過時間 / UI スレッド状態 / UI スレッドの CPU 消費 / プロセス CPU / ページフォールト / GC) ---");
            log.AppendLine("    ※PF (ページフォールト) は監視処理自体でも 1 サンプルあたり数千程度増える。数万以上ならページングを疑う");

            this.TakeSample(now);
        }

        private void TakeSample(long now)
        {
            var hang = this.currentHang!;
            hang.LastSampleTicks = now;
            hang.SampleCount++;

            // 1. UI スレッドのマネージドスタック (最優先で採取する)
            var stackText = this.CaptureUIStack();

            // 2. スレッドの状態と CPU 時間
            using var proc = Process.GetCurrentProcess();
            var uiProcThread = this.GetUIProcessThread(proc);
            var elapsed = ElapsedBetween(hang.StartTicks, now);

            var stateText = "UI スレッドの状態取得失敗";
            if (uiProcThread != null)
            {
                stateText = uiProcThread.ThreadState == System.Diagnostics.ThreadState.Wait
                    ? $"Wait/{uiProcThread.WaitReason}"
                    : uiProcThread.ThreadState.ToString();

                if (hang.StartUIThreadCpu != null)
                {
                    var uiCpu = uiProcThread.TotalProcessorTime - hang.StartUIThreadCpu.Value;
                    var ratio = elapsed.TotalSeconds > 0 ? uiCpu.TotalSeconds / elapsed.TotalSeconds * 100 : 0;
                    stateText += $" | UI CPU +{uiCpu.TotalSeconds:F2}s ({ratio:F0}%)";
                }
            }

            var procCpu = proc.TotalProcessorTime - hang.StartProcessCpu;
            var pageFaults = GetPageFaultCount(proc) - hang.StartPageFaults;
            var gcCounts = GetGcCounts();
            var gcDelta = string.Join("/", gcCounts.Zip(hang.StartGcCounts, (x, y) => $"+{x - y}"));

            hang.Log.AppendLine($"[+{elapsed.TotalSeconds,5:F1}s] {stateText} | proc CPU +{procCpu.TotalSeconds:F2}s | PF +{pageFaults} | WS {proc.WorkingSet64 / 1024 / 1024} MB | GC {gcDelta}");

            // 3. 待機チェーン (UI スレッドが何を待っているか: SendMessage 先のスレッド・プロセス、ロック、ALPC 等)
            var waitChain = this.DescribeWaitChain();
            if (waitChain != hang.LastWaitChain)
            {
                hang.Log.AppendLine($"    WCT: {waitChain}");
                hang.LastWaitChain = waitChain;
            }

            // 4. スタックトレース (前回と同じなら番号だけ)
            if (!hang.StackIndex.TryGetValue(stackText, out var stackNo))
            {
                stackNo = hang.StackIndex.Count + 1;
                hang.StackIndex[stackText] = stackNo;
                hang.StackCounts[stackNo] = 0;
                hang.Log.AppendLine($"    stack #{stackNo} (新規):");
                foreach (var line in stackText.Split('\n'))
                    hang.Log.AppendLine($"      {line.TrimEnd('\r')}");
            }
            else if (stackNo != hang.LastStackNo)
            {
                hang.Log.AppendLine($"    stack #{stackNo} に変化");
            }

            hang.StackCounts[stackNo]++;
            hang.LastStackNo = stackNo;

            this.FlushHangLog();
        }

#pragma warning disable CS0618 // Thread.Suspend / Resume は非推奨だが、.NET Framework で他スレッドのスタックを得る手段が他にない
        private string CaptureUIStack()
        {
            StackTrace? stackTrace = null;
            Exception? error = null;
            var suspended = false;

            try
            {
                // サスペンド中は監視スレッド側でもロックやメモリ確保を極力行わない (デッドロック防止)
                this.uiThread.Suspend();
                suspended = true;
                stackTrace = new StackTrace(this.uiThread, needFileInfo: false);
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                if (suspended)
                {
                    try
                    {
                        this.uiThread.Resume();
                    }
                    catch (ThreadStateException)
                    {
                    }
                }
            }

            if (stackTrace == null)
                return $"(スタック取得失敗: {error?.GetType().Name}: {error?.Message})";

            return FormatStackTrace(stackTrace);
        }
#pragma warning restore CS0618

        private static string FormatStackTrace(StackTrace stackTrace)
        {
            var sb = new StringBuilder();
            var frames = stackTrace.GetFrames() ?? Array.Empty<StackFrame>();

            if (frames.Length == 0)
                return "(マネージドフレームなし: ネイティブコードのみで停止中。ダンプを参照)";

            foreach (var frame in frames.Take(MaxStackFrames))
            {
                var method = frame.GetMethod();
                if (method == null)
                {
                    sb.Append("at <unknown>\n");
                    continue;
                }

                var typeName = method.DeclaringType?.FullName ?? "<global>";
                var parameters = string.Join(", ", method.GetParameters().Select(x => x.ParameterType.Name));
                var ilOffset = frame.GetILOffset();
                sb.Append($"at {typeName}.{method.Name}({parameters})");
                if (ilOffset != StackFrame.OFFSET_UNKNOWN)
                    sb.Append($" +IL_{ilOffset:X4}");
                sb.Append('\n');
            }

            if (frames.Length > MaxStackFrames)
                sb.Append($"... (残り {frames.Length - MaxStackFrames} フレーム省略)\n");

            return sb.ToString().TrimEnd('\n');
        }

        private string DescribeWaitChain()
        {
            var session = OpenThreadWaitChainSession(0, IntPtr.Zero);
            if (session == IntPtr.Zero)
                return $"(OpenThreadWaitChainSession 失敗: {Marshal.GetLastWin32Error()})";

            const int MaxNodes = 16; // WCT_MAX_NODE_COUNT
            const int NodeSize = 0x118; // sizeof(WAITCHAIN_NODE_INFO)
            var buffer = Marshal.AllocHGlobal(MaxNodes * NodeSize);

            try
            {
                var count = MaxNodes;
                if (!GetThreadWaitChain(session, IntPtr.Zero, 0x7 /* WCTP_GETINFO_ALL_FLAGS */, this.uiNativeThreadId, ref count, buffer, out var isCycle))
                    return $"(GetThreadWaitChain 失敗: {Marshal.GetLastWin32Error()})";

                var parts = new List<string>();
                for (var i = 0; i < Math.Min(count, MaxNodes); i++)
                {
                    var node = buffer + (i * NodeSize);
                    var type = (WctObjectType)Marshal.ReadInt32(node, 0);
                    var status = (WctObjectStatus)Marshal.ReadInt32(node, 4);

                    if (type == WctObjectType.Thread)
                    {
                        var pid = Marshal.ReadInt32(node, 8);
                        var tid = Marshal.ReadInt32(node, 12);
                        var contextSwitches = Marshal.ReadInt32(node, 20);
                        parts.Add($"[Thread pid={pid}{DescribeProcess(pid)} tid={tid}{(tid == this.uiNativeThreadId ? "(UI)" : "")} {status} cs={contextSwitches}]");
                    }
                    else
                    {
                        var name = Marshal.PtrToStringUni(node + 8, 128).TrimEnd('\0');
                        var timeout = Marshal.ReadInt64(node, 264);
                        var alertable = Marshal.ReadInt32(node, 272) != 0;
                        var nameText = MyCommon.IsNullOrEmpty(name) ? "" : $" \"{name}\"";
                        var timeoutText = timeout == -1 || timeout == long.MaxValue ? "INFINITE" : $"{timeout}";
                        parts.Add($"[{type}{nameText} {status} timeout={timeoutText}{(alertable ? " alertable" : "")}]");
                    }
                }

                return string.Join(" -> ", parts) + (isCycle != 0 ? " (デッドロック: 循環あり)" : "");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
                CloseThreadWaitChainSession(session);
            }
        }

        private static string DescribeProcess(int pid)
        {
            if (pid == 0)
                return "";

            try
            {
                using var proc = Process.GetProcessById(pid);
                return $"({proc.ProcessName})";
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return "";
            }
        }

        private void RequestDump()
        {
            var hang = this.currentHang!;
            hang.DumpRequested = true;

            try
            {
                // 古いダンプを削除して件数を制限する
                var oldDumps = new DirectoryInfo(this.config.LogDirectory).GetFiles("UIHang-*.dmp")
                    .OrderByDescending(x => x.LastWriteTimeUtc)
                    .Skip(Math.Max(0, this.config.MaxDumpFiles - 1));
                foreach (var oldDump in oldDumps)
                    oldDump.Delete();

                var dumpPath = Path.Combine(this.config.LogDirectory, $"UIHang-{DateTime.Now:yyyyMMdd-HHmmss}.dmp");
                using var proc = Process.GetCurrentProcess();
                var startInfo = new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    Arguments = $"{DumpCommandArg} {proc.Id} \"{dumpPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                hang.DumpProcess = Process.Start(startInfo);
                hang.DumpPath = dumpPath;
                hang.Log.AppendLine($"    >>> ミニダンプの書き出しを開始: {dumpPath}");
            }
            catch (Exception ex)
            {
                hang.Log.AppendLine($"    >>> ミニダンプの書き出しに失敗: {ex.GetType().Name}: {ex.Message}");
            }

            this.FlushHangLog();
        }

        private void EndHang(long endTicks, bool aborted)
        {
            var hang = this.currentHang!;
            this.currentHang = null;

            var duration = ElapsedBetween(hang.StartTicks, Math.Max(endTicks, hang.StartTicks));
            var log = hang.Log;

            log.AppendLine("--- 集計 ---");
            log.AppendLine(aborted
                ? $"監視終了時点でも応答停止中 (少なくとも {duration.TotalSeconds:F1} 秒)"
                : $"応答再開: 停止時間 約 {duration.TotalSeconds:F1} 秒 (終了 {DateTime.Now:HH:mm:ss.fff})");

            using (var proc = Process.GetCurrentProcess())
            {
                var uiProcThread = this.GetUIProcessThread(proc);
                if (uiProcThread != null && hang.StartUIThreadCpu != null)
                {
                    var uiCpu = uiProcThread.TotalProcessorTime - hang.StartUIThreadCpu.Value;
                    log.AppendLine($"UI スレッドの CPU 消費 {uiCpu.TotalSeconds:F2} 秒 / 停止時間 {duration.TotalSeconds:F1} 秒" +
                        (uiCpu.TotalSeconds < duration.TotalSeconds * 0.2 ? " → 主に「待ち」(ロック・I/O・SendMessage・ページング等)" : " → 主に「計算」(UI スレッド上の重い処理)"));
                }

                log.AppendLine($"プロセス CPU +{(proc.TotalProcessorTime - hang.StartProcessCpu).TotalSeconds:F2}s, ページフォールト +{GetPageFaultCount(proc) - hang.StartPageFaults}, GC {string.Join("/", GetGcCounts().Zip(hang.StartGcCounts, (x, y) => $"+{x - y}"))}");
            }

            log.AppendLine($"サンプル数 {hang.SampleCount}, スタック別の出現回数:");
            foreach (var pair in hang.StackCounts.OrderByDescending(x => x.Value))
                log.AppendLine($"  stack #{pair.Key}: {pair.Value} 回");

            if (hang.DumpPath != null)
            {
                var dumpState = hang.DumpProcess switch
                {
                    null => "不明",
                    { HasExited: false } => "書き出し中",
                    var p => p.ExitCode == 0 ? "完了" : $"失敗 (exit code {p.ExitCode})",
                };
                log.AppendLine($"ミニダンプ: {hang.DumpPath} ({dumpState})");
                log.AppendLine("  ※ダンプにはメモリ内容 (認証トークン・Cookie 等) が含まれます。他人に渡したりリポジトリに含めたりしないでください");
                hang.DumpProcess?.Dispose();
            }

            log.AppendLine("====================================================================");
            this.FlushHangLog(hang);
        }

        private void FlushHangLog()
            => this.FlushHangLog(this.currentHang!);

        private void FlushHangLog(HangRecord hang)
        {
            if (hang.Log.Length == 0)
                return;

            this.WriteLog(hang.Log.ToString().TrimEnd('\r', '\n'));
            hang.Log.Clear();
        }

        private void WriteLog(string text)
        {
            try
            {
                Directory.CreateDirectory(this.config.LogDirectory);
                File.AppendAllText(this.LogFilePath, text + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        private ProcessThread? GetUIProcessThread(Process proc)
        {
            try
            {
                foreach (ProcessThread thread in proc.Threads)
                {
                    if (thread.Id == this.uiNativeThreadId)
                        return thread;
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
            {
            }

            return null;
        }

        private static int[] GetGcCounts()
            => Enumerable.Range(0, GC.MaxGeneration + 1).Select(x => GC.CollectionCount(x)).ToArray();

        private static string FormatGcCounts(int[] counts)
            => string.Join("/", counts);

        private static long GetPageFaultCount(Process proc)
        {
            var counters = new ProcessMemoryCounters { Cb = Marshal.SizeOf<ProcessMemoryCounters>() };
            return GetProcessMemoryInfo(proc.Handle, ref counters, counters.Cb) ? counters.PageFaultCount : 0;
        }

        private static string GetClassNameSafe(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return "";

            var sb = new StringBuilder(256);
            return GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
        }

        private static TimeSpan ElapsedBetween(long fromTicks, long toTicks)
            => TimeSpan.FromSeconds((double)(toTicks - fromTicks) / Stopwatch.Frequency);

        /// <summary>
        /// ダンプ書き出し用の子プロセスのエントリポイント (OpenTween.exe --ot-write-hang-dump &lt;pid&gt; &lt;path&gt;)
        /// </summary>
        /// <remarks>
        /// MiniDumpWriteDump を応答停止中のプロセス自身から呼ぶとデッドロックする恐れがあるため、別プロセスから書き出す
        /// </remarks>
        public static int WriteDumpMain(string[] args)
        {
            if (args.Length < 3 || !int.TryParse(args[1], out var pid))
                return 2;

            var dumpPath = args[2];
            try
            {
                using var target = Process.GetProcessById(pid);
                using var file = new FileStream(dumpPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

                // procdump -mp 相当: マネージドヒープを含むプライベートメモリ + スレッド・ハンドル情報
                const int DumpType =
                    0x00000001 | // MiniDumpWithDataSegs
                    0x00000004 | // MiniDumpWithHandleData
                    0x00000020 | // MiniDumpWithUnloadedModules
                    0x00000200 | // MiniDumpWithPrivateReadWriteMemory
                    0x00000800 | // MiniDumpWithFullMemoryInfo
                    0x00001000; // MiniDumpWithThreadInfo

                if (!MiniDumpWriteDump(target.Handle, pid, file.SafeFileHandle, DumpType, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero))
                {
                    var error = Marshal.GetHRForLastWin32Error();
                    file.Dispose();
                    File.WriteAllText(dumpPath + ".error.txt", $"MiniDumpWriteDump failed: 0x{error:X8}");
                    return 1;
                }

                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    File.WriteAllText(dumpPath + ".error.txt", ex.ToString());
                }
                catch (Exception ex2) when (ex2 is IOException or UnauthorizedAccessException)
                {
                }

                return 1;
            }
        }

        public sealed record Settings(string LogDirectory)
        {
            /// <summary>この時間以上 UI スレッドがメッセージを処理しなかった場合に応答停止とみなす</summary>
            public TimeSpan HangThreshold { get; init; } = TimeSpan.FromSeconds(1.5);

            /// <summary>応答停止中にスタック等を採取する間隔</summary>
            public TimeSpan SampleInterval { get; init; } = TimeSpan.FromMilliseconds(500);

            /// <summary>応答停止がこの時間以上続いた場合にミニダンプを書き出す (null で無効)</summary>
            public TimeSpan? DumpThreshold { get; init; } = TimeSpan.FromSeconds(8);

            /// <summary>保持するミニダンプの最大数</summary>
            public int MaxDumpFiles { get; init; } = 3;

            public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(100);

            /// <summary>監視スレッド自身がこの時間以上止まっていた場合はスリープ復帰等とみなす</summary>
            public TimeSpan WatchdogGapThreshold { get; init; } = TimeSpan.FromSeconds(3);
        }

        private sealed class HangRecord
        {
            public HangRecord(long startTicks, DateTime startTime)
            {
                this.StartTicks = startTicks;
                this.StartTime = startTime;
                this.LastSampleTicks = startTicks;
            }

            public long StartTicks { get; }

            public DateTime StartTime { get; }

            public long LastSampleTicks { get; set; }

            public int SampleCount { get; set; }

            public StringBuilder Log { get; } = new();

            public Dictionary<string, int> StackIndex { get; } = new();

            public Dictionary<int, int> StackCounts { get; } = new();

            public int LastStackNo { get; set; }

            public string? LastWaitChain { get; set; }

            public int[] StartGcCounts { get; init; } = Array.Empty<int>();

            public long StartPageFaults { get; init; }

            public TimeSpan StartProcessCpu { get; init; }

            public TimeSpan? StartUIThreadCpu { get; init; }

            public bool DumpRequested { get; set; }

            public string? DumpPath { get; set; }

            public Process? DumpProcess { get; set; }
        }

        private sealed class PingWindow : NativeWindow
        {
            private readonly UIHangWatchdog owner;

            public PingWindow(UIHangWatchdog owner)
            {
                this.owner = owner;
                this.CreateHandle(new CreateParams { Parent = HwndMessage });
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WmPing)
                {
                    this.owner.OnPingReceived();
                    return;
                }

                base.WndProc(ref m);
            }
        }

        /// <summary>
        /// メインのメッセージループで最後に取り出されたメッセージを記録する
        /// </summary>
        private sealed class LastMessageFilter : IMessageFilter
        {
            private IntPtr hwnd;
            private int msg;
            private IntPtr wParam;
            private long ticks;

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg == WmPing)
                    return false;

                this.hwnd = m.HWnd;
                this.msg = m.Msg;
                this.wParam = m.WParam;
                Volatile.Write(ref this.ticks, Stopwatch.GetTimestamp());
                return false;
            }

            public string Describe()
            {
                var ticks = Volatile.Read(ref this.ticks);
                if (ticks == 0)
                    return "(なし)";

                var ago = ElapsedBetween(ticks, Stopwatch.GetTimestamp());
                return $"{DescribeMessage(this.msg)} hwnd=0x{this.hwnd.ToInt64():X} class=\"{GetClassNameSafe(this.hwnd)}\" wParam=0x{this.wParam.ToInt64():X} ({ago.TotalSeconds:F1} 秒前)";
            }

            private static string DescribeMessage(int msg)
                => msg switch
                {
                    0x0006 => "WM_ACTIVATE",
                    0x0007 => "WM_SETFOCUS",
                    0x0008 => "WM_KILLFOCUS",
                    0x000F => "WM_PAINT",
                    0x001C => "WM_ACTIVATEAPP",
                    0x0021 => "WM_MOUSEACTIVATE",
                    0x0086 => "WM_NCACTIVATE",
                    0x00A0 => "WM_NCMOUSEMOVE",
                    0x00A1 => "WM_NCLBUTTONDOWN",
                    0x0100 => "WM_KEYDOWN",
                    0x0101 => "WM_KEYUP",
                    0x0102 => "WM_CHAR",
                    0x0104 => "WM_SYSKEYDOWN",
                    0x0113 => "WM_TIMER",
                    0x0200 => "WM_MOUSEMOVE",
                    0x0201 => "WM_LBUTTONDOWN",
                    0x0202 => "WM_LBUTTONUP",
                    0x0204 => "WM_RBUTTONDOWN",
                    0x020A => "WM_MOUSEWHEEL",
                    0x0281 => "WM_IME_SETCONTEXT",
                    0x0282 => "WM_IME_NOTIFY",
                    0x02A3 => "WM_MOUSELEAVE",
                    0x0312 => "WM_HOTKEY",
                    >= 0xC000 and <= 0xFFFF => $"RegisteredMessage(0x{msg:X4})",
                    >= 0x8000 and < 0xC000 => $"WM_APP+0x{msg - 0x8000:X}",
                    >= 0x0400 and < 0x8000 => $"WM_USER+0x{msg - 0x0400:X}",
                    _ => $"0x{msg:X4}",
                };
        }

        private enum WctObjectType
        {
            CriticalSection = 1,
            SendMessage,
            Mutex,
            Alpc,
            Com,
            ThreadWait,
            ProcessWait,
            Thread,
            ComActivation,
            Unknown,
            SocketIo,
            SmbIo,
        }

        private enum WctObjectStatus
        {
            NoAccess = 1,
            Running,
            Blocked,
            PidOnly,
            PidOnlyRpcss,
            Owned,
            NotOwned,
            Abandoned,
            Unknown,
            Error,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessMemoryCounters
        {
            public int Cb;
            public int PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
            public UIntPtr QuotaPeakPagedPoolUsage;
            public UIntPtr QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            public UIntPtr QuotaNonPagedPoolUsage;
            public UIntPtr PagefileUsage;
            public UIntPtr PeakPagefileUsage;
        }

        [DllImport("kernel32.dll")]
        private static extern int GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessMemoryInfo(IntPtr process, ref ProcessMemoryCounters counters, int size);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern IntPtr OpenThreadWaitChainSession(int flags, IntPtr callback);

        [DllImport("advapi32.dll")]
        private static extern void CloseThreadWaitChainSession(IntPtr session);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetThreadWaitChain(IntPtr session, IntPtr context, int flags, int threadId, ref int nodeCount, IntPtr nodeInfoArray, out int isCycle);

        [DllImport("dbghelp.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MiniDumpWriteDump(IntPtr process, int processId, SafeFileHandle file, int dumpType, IntPtr exceptionParam, IntPtr userStreamParam, IntPtr callbackParam);
    }
}
