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

    public async Task<GitProcessResult> RunAsync(string workingDirectory, IEnumerable<string> arguments, string? stdinData = null, CancellationToken cancellationToken = default)
    {
        var argsList = new List<string>
        {
            "-c", "core.longpaths=true",
            "-c", "i18n.logOutputEncoding=UTF-8"
        };
        argsList.AddRange(arguments);

        var processStartInfo = new ProcessStartInfo
        {
            FileName = GitExecutablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdinData != null,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        // Environment variables
        processStartInfo.Environment["LC_ALL"] = "C";
        processStartInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

        foreach (var arg in argsList)
        {
            processStartInfo.ArgumentList.Add(arg);
        }

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
