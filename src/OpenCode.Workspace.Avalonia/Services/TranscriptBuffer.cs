using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenCode.Workspace.AppSupport;

namespace OpenCode.Workspace.Avalonia.Services;

internal sealed class TranscriptBuffer : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly Func<OperationTranscriptLine, string> _formatter;
    private readonly Queue<BufferedTranscriptLine> _pendingUiLines = new();
    private readonly StreamWriter _writer;
    private bool _disposed;
    private string? _latestStatusText;

    public TranscriptBuffer(string operationName, string workspaceName, Func<OperationTranscriptLine, string> formatter)
    {
        _formatter = formatter;
        TranscriptFilePath = CreateTranscriptPath(operationName, workspaceName);

        var directoryPath = Path.GetDirectoryName(TranscriptFilePath)!;
        Directory.CreateDirectory(directoryPath);

        _writer = new StreamWriter(new FileStream(TranscriptFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };
    }

    public string TranscriptFilePath { get; }

    public int PendingLineCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _pendingUiLines.Count;
            }
        }
    }

    public void Append(OperationTranscriptLine line, string? statusText = null)
    {
        var formattedLine = _formatter(line);

        lock (_syncRoot)
        {
            ThrowIfDisposed();
            _writer.WriteLine(formattedLine);
            _pendingUiLines.Enqueue(new BufferedTranscriptLine(line, formattedLine));

            if (!string.IsNullOrWhiteSpace(statusText))
            {
                _latestStatusText = statusText;
            }
        }
    }

    public TranscriptBufferBatch DrainPendingLines(int maxLines)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();

            var drainedLines = new List<BufferedTranscriptLine>(Math.Min(maxLines, _pendingUiLines.Count));
            while (drainedLines.Count < maxLines && _pendingUiLines.Count > 0)
            {
                drainedLines.Add(_pendingUiLines.Dequeue());
            }

            var latestStatusText = _latestStatusText;
            _latestStatusText = null;
            return new TranscriptBufferBatch(drainedLines, latestStatusText, _pendingUiLines.Count);
        }
    }

    public string ReadAllText()
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            _writer.Flush();
        }

        if (!File.Exists(TranscriptFilePath))
        {
            return string.Empty;
        }

        using var stream = new FileStream(TranscriptFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public async Task<string> ReadAllTextAsync(CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            _writer.Flush();
        }

        if (!File.Exists(TranscriptFilePath))
        {
            return string.Empty;
        }

        await using var stream = new FileStream(TranscriptFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 4096, useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(cancellationToken);
        return content;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _writer.Dispose();
            _disposed = true;
        }
    }

    public void DeleteFile()
    {
        Dispose();

        if (File.Exists(TranscriptFilePath))
        {
            File.Delete(TranscriptFilePath);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static string CreateTranscriptPath(string operationName, string workspaceName)
    {
        var transcriptsRoot = Path.Combine(WorkspaceAppDataPaths.GetWorkspaceManagerDataRoot(), "operation-transcripts");
        var operationSegment = SanitizePathSegment(operationName);
        var workspaceSegment = SanitizePathSegment(workspaceName);
        var fileName = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{operationSegment}-{workspaceSegment}-{Guid.NewGuid():N}.log";
        return Path.Combine(transcriptsRoot, fileName);
    }

    private static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "workspace";
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-');
        }

        var sanitized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "workspace" : sanitized;
    }
}

internal sealed record BufferedTranscriptLine(OperationTranscriptLine Line, string FormattedText);

internal sealed record TranscriptBufferBatch(IReadOnlyList<BufferedTranscriptLine> Lines, string? LatestStatusText, int RemainingPendingLineCount);
