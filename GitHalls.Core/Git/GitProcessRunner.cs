using System.Diagnostics;
using System.Text;
using GitHalls.Core.Models;

namespace GitHalls.Core.Git;

public class GitProcessResult
{
    public int ExitCode { get; }
    public string StandardOutput { get; }
    public string StandardError { get; }

    public GitProcessResult(int exitCode, string standardOutput, string standardError)
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }
}

public interface IGitProcessRunner
{
    Task<GitProcessResult> RunAsync(string workingDirectory, IEnumerable<string> arguments, string? stdinData = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same run with stdout left as bytes, and the exit code instead of an
    /// exception: a missing path at a revision is a normal answer here.
    /// </summary>
    Task<(byte[] Output, int ExitCode)> RunBytesAsync(string workingDirectory, IEnumerable<string> arguments, CancellationToken cancellationToken = default);
}

public class GitProcessRunner : IGitProcessRunner
{
    private static readonly string GitExecutablePath = LocateGit();

    private static string LocateGit()
    {
        // Probe common installation path first
        var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var probePath = Path.Combine(progFiles, "Git", "cmd", "git.exe");
        if (File.Exists(probePath))
        {
            return probePath;
        }

        // Fallback to expecting it in PATH
        return "git.exe";
    }

    /// <summary>The prelude, environment and redirection every git call shares.</summary>
    private static ProcessStartInfo StartInfoFor(string workingDirectory, IEnumerable<string> arguments, bool redirectStdin)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = GitExecutablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectStdin,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        // Without this .NET writes stdin in the console's code page, so a patch
        // carrying an accent reaches "git apply" corrupted. No BOM: git reads
        // the first bytes as part of the patch.
        if (redirectStdin) startInfo.StandardInputEncoding = new UTF8Encoding(false);

        startInfo.Environment["LC_ALL"] = "C";
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("core.longpaths=true");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("i18n.logOutputEncoding=UTF-8");

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    /// <summary>
    /// Reads stdout as bytes rather than text. Decoding a PNG as UTF-8 does not
    /// fail loudly — it replaces every invalid byte with U+FFFD — so anything
    /// that wants the file itself has to come through here.
    /// </summary>
    public async Task<(byte[] Output, int ExitCode)> RunBytesAsync(string workingDirectory, IEnumerable<string> arguments, CancellationToken cancellationToken = default)
    {
        var startInfo = StartInfoFor(workingDirectory, arguments, redirectStdin: false);

        // No encoding is set: setting one would install a StreamReader over the
        // stream this method is about to read raw.
        startInfo.StandardOutputEncoding = null;

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new GitException($"Failed to start Git process. Please ensure Git for Windows is installed. ({ex.Message})", string.Empty);
        }

        using var buffer = new MemoryStream();

        // stderr is drained too, and at the same time: it is redirected, so
        // leaving it unread lets a full pipe block git before it finishes
        // writing the file.
        var stdout = process.StandardOutput.BaseStream.CopyToAsync(buffer, cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        await Task.WhenAll(stdout, stderr);
        await process.WaitForExitAsync(cancellationToken);

        return (buffer.ToArray(), process.ExitCode);
    }

    public async Task<GitProcessResult> RunAsync(string workingDirectory, IEnumerable<string> arguments, string? stdinData = null, CancellationToken cancellationToken = default)
    {
        var processStartInfo = StartInfoFor(workingDirectory, arguments, redirectStdin: stdinData != null);

        using var process = new Process { StartInfo = processStartInfo };
        
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new GitException($"Failed to start Git process. Please ensure Git for Windows is installed. ({ex.Message})", string.Empty);
        }

        if (stdinData != null)
        {
            await process.StandardInput.WriteAsync(stdinData);
            process.StandardInput.Close();
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var stdout = (await stdoutTask).Replace("\r\n", "\n");
        var stderr = (await stderrTask).Replace("\r\n", "\n");

        if (process.ExitCode != 0)
        {
            throw GitException.Parse(stderr);
        }

        return new GitProcessResult(process.ExitCode, stdout, stderr);
    }
}
