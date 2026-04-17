using System;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace CheatEngine.Library;

public unsafe sealed class FileMapping : IDisposable
{
    private readonly FileStream _fileStream;
    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedViewAccessor _view;
    private byte* _fileContent;
    private bool _disposed;

    public FileMapping(string filename)
    {
        if (!File.Exists(filename))
        {
            throw new FileNotFoundException($"{filename} does not exist", filename);
        }

        try
        {
            _fileStream = new FileStream(filename, FileMode.Open, System.IO.FileAccess.ReadWrite, FileShare.ReadWrite);
        }
        catch (UnauthorizedAccessException)
        {
            _fileStream = new FileStream(filename, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite);
        }

        FileSize = checked((uint)_fileStream.Length);
        _mapping = MemoryMappedFile.CreateFromFile(_fileStream, null, 0, MemoryMappedFileAccess.CopyOnWrite, HandleInheritability.None, leaveOpen: false);
        _view = _mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.CopyOnWrite);

        byte* pointer = null;
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
        _fileContent = pointer;
    }

    public nint FileContent => (nint)_fileContent;

    public uint FileSize { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_fileContent != null)
        {
            _view.SafeMemoryMappedViewHandle.ReleasePointer();
            _fileContent = null;
        }

        _view.Dispose();
        _mapping.Dispose();
        _fileStream.Dispose();
        _disposed = true;
    }
}