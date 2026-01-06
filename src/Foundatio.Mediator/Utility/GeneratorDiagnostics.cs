using System.Diagnostics;

namespace Foundatio.Mediator.Utility;

/// <summary>
/// Diagnostic logging for the MediatorGenerator. Enable by setting the environment variable
/// FOUNDATIO_MEDIATOR_DIAGNOSTICS=1 or to a file path to log to a specific location.
/// 
/// When enabled, logs generator execution details to help diagnose performance issues.
/// Useful for understanding how often the generator runs during IDE typing.
/// 
/// Usage:
///   - Set env var FOUNDATIO_MEDIATOR_DIAGNOSTICS=1 to log to temp folder
///   - Set env var FOUNDATIO_MEDIATOR_DIAGNOSTICS=C:\logs\mediator.log to log to specific file
///   - Leave unset to disable all logging (no performance impact)
/// </summary>
// Suppress analyzer warnings - this is diagnostic code that intentionally uses banned APIs
#pragma warning disable RS1035 // Do not use APIs banned for analyzers
#pragma warning disable RS1036 // Specify analyzer banned API enforcement setting
internal static class GeneratorDiagnostics
{
    private static readonly string? LogPath;
    private static readonly bool IsEnabled;
    private static readonly object Lock = new();
    private static int _executeCount;
    private static int _predicateCallCount;
    private static readonly Stopwatch SessionStopwatch = Stopwatch.StartNew();

    static GeneratorDiagnostics()
    {
        var envValue = Environment.GetEnvironmentVariable("FOUNDATIO_MEDIATOR_DIAGNOSTICS");
        
        if (string.IsNullOrEmpty(envValue))
        {
            IsEnabled = false;
            return;
        }

        IsEnabled = true;

        if (envValue == "1" || envValue.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            // Default location
            var tempDir = Path.Combine(Path.GetTempPath(), "FoundatioMediatorDiagnostics");
            Directory.CreateDirectory(tempDir);
            LogPath = Path.Combine(tempDir, $"mediator-generator-{DateTime.Now:yyyyMMdd-HHmmss}-{Process.GetCurrentProcess().Id}.log");
        }
        else
        {
            // Custom path specified
            LogPath = envValue;
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }

        // Write header
        WriteLogRaw($"""
            ================================================================================
            Foundatio.Mediator Generator Diagnostics
            Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}
            Process: {Process.GetCurrentProcess().ProcessName} (PID: {Process.GetCurrentProcess().Id})
            Log File: {LogPath}
            ================================================================================
            
            """);
    }

    /// <summary>
    /// Log that the Execute method was called.
    /// </summary>
    public static void LogExecute(
        string assemblyName,
        int handlerCount,
        int middlewareCount,
        int callSiteCount,
        int crossAssemblyHandlerCount,
        long elapsedMs)
    {
        if (!IsEnabled) return;

        var count = Interlocked.Increment(ref _executeCount);
        var sessionTime = SessionStopwatch.Elapsed;

        WriteLog($"""
            [EXECUTE #{count}] {DateTime.Now:HH:mm:ss.fff} (Session: {sessionTime.TotalSeconds:F1}s)
              Assembly: {assemblyName}
              Handlers: {handlerCount}, Middleware: {middlewareCount}, CallSites: {callSiteCount}
              Cross-Assembly Handlers: {crossAssemblyHandlerCount}
              Duration: {elapsedMs}ms
            """);
    }

    /// <summary>
    /// Log predicate evaluation (syntax filtering). Call sparingly as this is called frequently.
    /// </summary>
    public static void LogPredicateCall(string analyzerName)
    {
        if (!IsEnabled) return;
        
        // Only log every 1000th call to avoid overwhelming the log
        var count = Interlocked.Increment(ref _predicateCallCount);
        if (count % 1000 == 0)
        {
            WriteLog($"[PREDICATE] {analyzerName} evaluated {count} times total");
        }
    }

    /// <summary>
    /// Log when a transform produces a result.
    /// </summary>
    public static void LogTransformResult(string analyzerName, string resultSummary)
    {
        if (!IsEnabled) return;
        WriteLog($"[TRANSFORM] {analyzerName}: {resultSummary}");
    }

    /// <summary>
    /// Log a custom message.
    /// </summary>
    public static void Log(string message)
    {
        if (!IsEnabled) return;
        WriteLog($"[INFO] {message}");
    }

    /// <summary>
    /// Get a summary of execution statistics.
    /// </summary>
    public static string GetSummary()
    {
        if (!IsEnabled) return "Diagnostics disabled";
        
        return $"""
            Execute calls: {_executeCount}
            Predicate evaluations: {_predicateCallCount}
            Session duration: {SessionStopwatch.Elapsed.TotalSeconds:F1}s
            Log file: {LogPath}
            """;
    }

    /// <summary>
    /// Write a summary to the log (call periodically or at end of session).
    /// </summary>
    public static void WriteSummary()
    {
        if (!IsEnabled) return;
        
        WriteLog($"""
            
            ================================================================================
            SUMMARY at {DateTime.Now:HH:mm:ss.fff}
            {GetSummary()}
            ================================================================================
            """);
    }

    private static void WriteLog(string message)
    {
        WriteLogRaw($"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
    }

    private static void WriteLogRaw(string message)
    {
        if (LogPath == null) return;
        
        lock (Lock)
        {
            try
            {
                File.AppendAllText(LogPath, message);
            }
            catch
            {
                // Ignore write failures - don't crash the generator
            }
        }
    }
}
#pragma warning restore RS1035
#pragma warning restore RS1036
