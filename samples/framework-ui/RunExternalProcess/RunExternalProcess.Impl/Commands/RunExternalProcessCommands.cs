using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace AdSamples.RunExternalProcess
{
    /// <summary>
    /// Starts an external program from an extension command and waits for it to exit. This is plain .NET
    /// (System.Diagnostics.Process): no SDK call is needed to launch a program from Altium Designer.
    /// </summary>
    internal static class RunExternalProcessCommands
    {
        private const int DefaultTimeoutSeconds = 30;
        private const int MaxTimeoutSeconds = 600;

        // Captured output is cut to this many characters per stream in the returned text.
        private const int MaxCapturedChars = 4000;

        /// <summary>
        /// RunExternalProcess "Exe=&lt;path or name&gt;|Args=&lt;arguments&gt;|Timeout=&lt;seconds&gt;|WorkDir=&lt;folder&gt;":
        /// runs the program the caller names, waits for it (killing it when the timeout expires) and returns its
        /// exit code and captured output. It launches whatever Exe names: there is deliberately no default.
        /// </summary>
        [HotCommand("RunExternalProcess", "Run an external program and wait for it to exit")]
        public static string RunExternalProcess(CommandContext ctx)
        {
            string exe = ctx.Get("Exe");
            if (string.IsNullOrWhiteSpace(exe))
                throw new UserException(
                    "No program specified. Pass it as a parameter: Exe=<path or name>[|Args=<arguments>][|Timeout=<seconds>][|WorkDir=<folder>].");

            int timeoutSeconds = ParseTimeout(ctx.Get("Timeout"));

            string workDir = ctx.Get("WorkDir");
            if (!string.IsNullOrEmpty(workDir) && !Directory.Exists(workDir))
                throw new UserException($"WorkDir '{workDir}' does not exist.");

            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = ctx.Get("Args", string.Empty),
                WorkingDirectory = string.IsNullOrEmpty(workDir) ? string.Empty : workDir,
                // UseShellExecute = false is what allows redirecting the output; CreateNoWindow keeps console
                // programs from flashing a window over Altium Designer.
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            var stdout = new CappedBuffer(MaxCapturedChars);
            var stderr = new CappedBuffer(MaxCapturedChars);

            // Each stream signals its end of file (e.Data == null). They are deliberately not disposed: a reader
            // that has not finished when the command returns may still set its event later.
            var stdoutEnded = new ManualResetEventSlim(false);
            var stderrEnded = new ManualResetEventSlim(false);

            using var process = new Process { StartInfo = startInfo };
            // Read both streams asynchronously. Reading them synchronously after WaitForExit can deadlock: a child
            // that fills the pipe buffer blocks on its write and never exits.
            process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); else stdoutEnded.Set(); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); else stderrEnded.Set(); };

            var clock = Stopwatch.StartNew();
            try
            {
                process.Start();
            }
            catch (Win32Exception ex)
            {
                // For example "The system cannot find the file specified" when Exe is not found.
                throw new UserException($"Could not start '{exe}': {ex.Message}");
            }

            ctx.Log($"Started '{exe}' (process id {process.Id}); waiting up to {timeoutSeconds} s.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Not WaitForExit() with no limit: this command runs inside Altium Designer, so a hung
            // child would hang the command with it: wait a bounded time and kill the child when it expires.
            // The timed WaitForExit returns when the process itself ends. Unlike the parameterless WaitForExit(), it
            // does not wait for the output streams to reach end of file.
            bool exited = process.WaitForExit(TimeSpan.FromSeconds(timeoutSeconds));
            string killProblem = null;
            if (!exited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // It exited between the timeout and the kill.
                }
                catch (Exception ex) when (ex is Win32Exception || ex is AggregateException)
                {
                    // For example access denied, or a process of the tree that could not be terminated.
                    killProblem = ex.Message;
                }
            }

            // Everything after the timeout gets one short grace period, so the command never waits longer than
            // Timeout + GraceSeconds. The parameterless WaitForExit() is not used: it also waits for end of file on
            // the redirected streams, and a process the program started (for example with "start /b") inherits
            // those pipes and keeps them open after the program itself has exited, for as long as it runs.
            var grace = Stopwatch.StartNew();
            bool ended = exited || process.WaitForExit(Remaining(grace));
            // The reported time is when the process ended (or the kill wait gave up), not when the output drain
            // finished: a lingering descendant can add up to GraceSeconds to the drain.
            TimeSpan runTime = clock.Elapsed;
            bool drained = stdoutEnded.Wait(Remaining(grace)) & stderrEnded.Wait(Remaining(grace));

            var result = new StringBuilder();
            string elapsed = runTime.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);
            string exitCode = ended ? process.ExitCode.ToString(CultureInfo.InvariantCulture) : "unknown";
            if (exited)
                result.Append($"'{exe}' exited with code {exitCode} after {elapsed} s.");
            else if (ended)
                result.Append($"'{exe}' did not exit within {timeoutSeconds} s and was killed (after {elapsed} s, exit code {exitCode}).");
            else
                result.Append($"'{exe}' did not exit within {timeoutSeconds} s and was still running {GraceSeconds} s after the kill attempt (exit code unknown).");
            if (killProblem != null)
                result.AppendLine().Append($"(the kill did not complete: {killProblem})");
            if (!drained)
                result.AppendLine().Append(
                    "(output may be incomplete: a process the program started still holds the output pipes; it was left running)");

            AppendStream(result, "stdout", stdout);
            AppendStream(result, "stderr", stderr);
            return result.ToString();
        }

        // Upper bound for the wait after the timed wait: the end of the killed process and the end of both streams.
        private const int GraceSeconds = 3;

        private static TimeSpan Remaining(Stopwatch grace)
        {
            TimeSpan left = TimeSpan.FromSeconds(GraceSeconds) - grace.Elapsed;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }

        private static int ParseTimeout(string text)
        {
            if (string.IsNullOrEmpty(text))
                return DefaultTimeoutSeconds;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds)
                || seconds < 1 || seconds > MaxTimeoutSeconds)
                throw new UserException($"Timeout must be a whole number of seconds from 1 to {MaxTimeoutSeconds}, not '{text}'.");
            return seconds;
        }

        private static void AppendStream(StringBuilder result, string name, CappedBuffer buffer)
        {
            string text = buffer.ToString().TrimEnd();
            if (text.Length == 0)
                return;
            result.AppendLine().Append(name).Append(':').AppendLine().Append(text);
            if (buffer.Truncated)
                result.AppendLine().Append($"({name} cut to {MaxCapturedChars} characters)");
        }

        /// <summary>
        /// A thread-safe text buffer with a size limit. The Process events arrive on thread-pool threads, and a
        /// chatty program must not fill the memory or the returned message.
        /// </summary>
        private sealed class CappedBuffer
        {
            private readonly StringBuilder _text = new StringBuilder();
            private readonly int _max;

            public CappedBuffer(int max) => _max = max;

            public bool Truncated { get; private set; }

            public void AppendLine(string line)
            {
                lock (_text)
                {
                    if (_text.Length >= _max)
                    {
                        Truncated = true;
                        return;
                    }
                    int room = _max - _text.Length;
                    if (line.Length + 2 > room)
                    {
                        _text.Append(line, 0, Math.Min(line.Length, room));
                        Truncated = true;
                    }
                    else
                    {
                        _text.Append(line).Append("\r\n");
                    }
                }
            }

            public override string ToString()
            {
                lock (_text)
                    return _text.ToString();
            }
        }
    }
}
